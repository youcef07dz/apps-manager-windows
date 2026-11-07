using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AppsManager
{
    /// <summary>
    /// Update source. winget is used on Windows 10 1809+ and returns:
    /// normalized name -> [package id, available version, current version].
    /// </summary>
    internal static class WingetService
    {
        public const string Winget = "winget";

        public sealed class Result
        {
            public int ExitCode;
            public string Output;
        }

        public static string FindWinget()
        {
            WingetSetup.EnsureUnpacked();   // the bundled copy is packed until first use
            return FindOnPath("winget.exe") ??
                Existing(WingetSetup.ExePath) ??
                Existing(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\WindowsApps\winget.exe"));
        }

        // ---------- winget ----------

        public static Dictionary<string, string[]> ListUpgrades(string winget)
        {
            Result result = Run(winget, "upgrade --include-unknown --accept-source-agreements", 120000);
            return ParseUpgradeTable(result.Output);
        }

        public static Dictionary<string, string[]> ParseUpgradeTable(string output)
        {
            Dictionary<string, string[]> map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            string[] lines = output.Replace("\r", "").Split('\n');
            bool inTable = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();
                if (line.Length == 0) continue;
                if (IsSeparator(line))
                {
                    inTable = true;
                    continue;
                }
                if (!inTable) continue;

                // shortcut: columns are split on 2+ spaces; a program name containing double
                // spaces (or a wrapped line) is skipped, upgrade that one by hand.
                string[] cols = Regex.Split(line.Trim(), @"\s{2,}");
                if (cols.Length < 4) continue;
                string name = cols[0].Trim();
                string id = cols[1].Trim();
                string current = cols[2].Trim();
                string available = cols[3].Trim();
                string key = Normalize(name);
                if (key.Length == 0 || id.Length == 0) continue;
                if (!map.ContainsKey(key)) map[key] = new string[] { id, available, current };
            }
            return map;
        }

        /// <summary>
        /// Names of installed packages that winget tracks (so "no update" can be shown as
        /// "up to date"). ARP and MSIX entries are skipped: winget cannot upgrade those.
        /// </summary>
        public static HashSet<string> ListTracked(string winget)
        {
            Result result = Run(winget, "list --accept-source-agreements", 180000);
            return ParseTracked(result.Output);
        }

        /// <summary>
        /// Reads the Name column of "winget list" for rows with a real package id.
        /// shortcut: column offsets come from the English header line, so a localized
        /// winget build can leave some apps marked unknown.
        /// </summary>
        public static HashSet<string> ParseTracked(string output)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = output.Replace("\r", "").Split('\n');
            int nameAt = -1;
            int idAt = -1;
            int sourceAt = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();
                if (nameAt < 0)
                {
                    int nameCol = line.IndexOf("Name", StringComparison.OrdinalIgnoreCase);
                    int idCol = line.IndexOf("Id", StringComparison.OrdinalIgnoreCase);
                    int sourceCol = line.IndexOf("Source", StringComparison.OrdinalIgnoreCase);
                    if (nameCol >= 0 && idCol > nameCol && sourceCol > idCol)
                    {
                        nameAt = nameCol;
                        idAt = idCol;
                        sourceAt = sourceCol;
                    }
                    continue;
                }
                if (IsSeparator(line)) continue;
                if (line.Length <= idAt) continue;

                string name = line.Substring(nameAt, idAt - nameAt).Trim();
                int idEnd = sourceAt < line.Length ? sourceAt : line.Length;
                string id = line.Substring(idAt, idEnd - idAt).Trim();
                if (name.Length == 0 || id.Length == 0) continue;
                if (id.StartsWith("ARP\\", StringComparison.OrdinalIgnoreCase)) continue;
                if (id.StartsWith("MSIX\\", StringComparison.OrdinalIgnoreCase)) continue;
                set.Add(Normalize(name));
            }
            return set;
        }

        /// <summary>Searches the winget source. Rows: name, id, version, match, source.</summary>
        public static List<string[]> Search(string winget, string query)
        {
            Result result = Run(winget,
                "search \"" + Quote(query) + "\" --source winget --accept-source-agreements", 120000);
            return ParseSearchTable(result.Output);
        }

        /// <summary>
        /// Reads the search table from its English header line, so names and ids may contain
        /// spaces. shortcut: a localized winget header yields no rows.
        /// </summary>
        public static List<string[]> ParseSearchTable(string output)
        {
            List<string[]> list = new List<string[]>();
            string[] lines = output.Replace("\r", "").Split('\n');
            int nameAt = -1;
            int idAt = -1;
            int versionAt = -1;
            int matchAt = -1;
            int sourceAt = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();
                if (nameAt < 0)
                {
                    int nameCol = line.IndexOf("Name", StringComparison.OrdinalIgnoreCase);
                    int idCol = line.IndexOf("Id", StringComparison.OrdinalIgnoreCase);
                    int versionCol = line.IndexOf("Version", StringComparison.OrdinalIgnoreCase);
                    if (nameCol >= 0 && idCol > nameCol && versionCol > idCol)
                    {
                        nameAt = nameCol;
                        idAt = idCol;
                        versionAt = versionCol;
                        matchAt = line.IndexOf("Match", versionCol, StringComparison.OrdinalIgnoreCase);
                        sourceAt = line.IndexOf("Source", versionCol, StringComparison.OrdinalIgnoreCase);
                    }
                    continue;
                }
                if (IsSeparator(line)) continue;
                if (line.Length <= versionAt) continue;

                string name = line.Substring(nameAt, idAt - nameAt).Trim();
                string id = line.Substring(idAt, versionAt - idAt).Trim();

                int versionEnd = matchAt > versionAt ? matchAt : (sourceAt > versionAt ? sourceAt : line.Length);
                if (versionEnd > line.Length) versionEnd = line.Length;
                string version = line.Substring(versionAt, versionEnd - versionAt).Trim();

                string match = "";
                if (matchAt > versionAt && matchAt < line.Length)
                {
                    int matchEnd = sourceAt > matchAt ? sourceAt : line.Length;
                    if (matchEnd > line.Length) matchEnd = line.Length;
                    match = line.Substring(matchAt, matchEnd - matchAt).Trim();
                }

                string source = sourceAt > versionAt && sourceAt < line.Length
                    ? line.Substring(sourceAt).Trim()
                    : "";
                if (name.Length == 0 || id.Length == 0) continue;
                list.Add(new string[] { name, id, version, match, source });
            }
            return list;
        }

        /// <summary>
        /// Installs one package. When a progress callback is given, winget's own installer
        /// URL is resolved and the temp file it downloads is measured against that size.
        /// winget hides its progress when stdout is redirected, so the file is the only
        /// accurate source. Everything still runs hidden.
        /// </summary>
        public static Result Upgrade(string winget, string id, string version, Action<int> progress)
        {
            string safeId = Quote(id);
            long total = 0;
            if (progress != null)
            {
                string url = InstallerUrl(winget, id, version);
                if (url != null) total = Downloader.ContentLength(url);
            }

            ManualResetEvent done = new ManualResetEvent(false);
            Thread monitor = null;
            if (progress != null && total > 0)
            {
                monitor = new Thread(delegate() { MonitorDownload(id, version, total, progress, done); });
                monitor.IsBackground = true;
                monitor.Start();
            }

            Result result = Run(winget,
                "upgrade --id \"" + safeId + "\" -e --silent --accept-package-agreements --accept-source-agreements",
                900000);

            if (monitor != null)
            {
                done.Set();
                monitor.Join(1000);
            }
            return result;
        }

        /// <summary>Installs one package; same progress source as <see cref="Upgrade"/>.</summary>
        public static Result Install(string winget, string id, string version, Action<int> progress)
        {
            long total = 0;
            if (progress != null)
            {
                string url = InstallerUrl(winget, id, version);
                if (url != null) total = Downloader.ContentLength(url);
            }

            ManualResetEvent done = new ManualResetEvent(false);
            Thread monitor = null;
            if (progress != null && total > 0)
            {
                monitor = new Thread(delegate() { MonitorDownload(id, version, total, progress, done); });
                monitor.IsBackground = true;
                monitor.Start();
            }

            Result result = Run(winget,
                "install --id \"" + Quote(id) + "\" -e --silent --source winget " +
                "--accept-package-agreements --accept-source-agreements",
                900000);

            if (monitor != null)
            {
                done.Set();
                monitor.Join(1000);
            }
            return result;
        }

        /// <summary>The installer URL for the target version, from "winget show".</summary>
        private static string InstallerUrl(string winget, string id, string version)
        {
            string args = "show --id \"" + Quote(id) + "\" -e";
            if (!string.IsNullOrEmpty(version)) args += " --version \"" + Quote(version) + "\"";
            args += " --accept-source-agreements";

            Result result = Run(winget, args, 60000);
            string[] lines = result.Output.Replace("\r", "").Split('\n');
            const string label = "Installer Url:";
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                int at = line.IndexOf(label, StringComparison.OrdinalIgnoreCase);
                if (at >= 0) return line.Substring(at + label.Length).Trim();
            }
            return null;
        }

        /// <summary>Polls the temp file winget downloads into and reports 0-100%.</summary>
        private static void MonitorDownload(string id, string version, long total,
            Action<int> progress, ManualResetEvent done)
        {
            string root = Path.Combine(Path.GetTempPath(), @"WinGet\defaultState");
            string folder = Path.Combine(root, id + "." + version);
            DateTime start = DateTime.UtcNow;
            int last = -1;

            while (!done.WaitOne(150))
            {
                try
                {
                    if (!Directory.Exists(folder)) folder = NewestFolder(root, id, start);
                    if (folder == null) continue;

                    long size = LargestFile(folder);
                    if (size <= 0) continue;

                    int percent = (int)(size * 100 / total);
                    if (percent > 100) percent = 100;
                    if (percent == last) continue;
                    last = percent;
                    progress(percent);
                    if (percent >= 100) return;   // download done; install phase follows
                }
                catch (Exception)
                {
                    // winget creates and removes this folder; a miss is not an error
                }
            }
        }

        private static string NewestFolder(string root, string id, DateTime start)
        {
            if (!Directory.Exists(root)) return null;
            string[] dirs = Directory.GetDirectories(root, id + ".*");
            string best = null;
            DateTime bestTime = start;
            for (int i = 0; i < dirs.Length; i++)
            {
                DateTime time = Directory.GetLastWriteTimeUtc(dirs[i]);
                if (time >= bestTime)
                {
                    best = dirs[i];
                    bestTime = time;
                }
            }
            return best;
        }

        private static long LargestFile(string folder)
        {
            if (folder == null || !Directory.Exists(folder)) return 0;
            string[] files = Directory.GetFiles(folder);
            long best = 0;
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    long length = new FileInfo(files[i]).Length;
                    if (length > best) best = length;
                }
                catch (Exception) { }
            }
            return best;
        }

        // ---------- shared ----------

        public static string Summarize(string output)
        {
            if (string.IsNullOrEmpty(output)) return "the updater returned no details.";
            string[] lines = output.Replace("\r", "").Split('\n');
            StringBuilder sb = new StringBuilder();
            int taken = 0;
            for (int i = lines.Length - 1; i >= 0 && taken < 3; i--)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (sb.Length > 0) sb.Insert(0, " ");
                sb.Insert(0, line);
                taken++;
            }
            return sb.ToString();
        }

        public static string Normalize(string name)
        {
            if (name == null) return "";
            StringBuilder sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        private static string Quote(string value)
        {
            return (value ?? "").Replace("\"", "");
        }

        private static string FindOnPath(string exeName)
        {
            try
            {
                string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
                string[] parts = pathVar.Split(';');
                for (int i = 0; i < parts.Length; i++)
                {
                    string dir = parts[i].Trim().Trim('"');
                    if (dir.Length == 0) continue;
                    try
                    {
                        string candidate = Path.Combine(dir, exeName);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return null;
        }

        private static string Existing(string path)
        {
            try { return File.Exists(path) ? path : null; }
            catch (Exception) { return null; }
        }

        private static bool IsSeparator(string line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length < 3) return false;
            int dashes = 0;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (c == '-') dashes++;
                else if (c != ' ') return false;
            }
            return dashes >= 3;
        }

        private static Result Run(string file, string args, int timeoutMs)
        {
            ProcessStartInfo info = new ProcessStartInfo(file, args);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;

            StringBuilder output = new StringBuilder();
            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = info;
                    process.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) { lock (output) output.AppendLine(e.Data); }
                    };
                    process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) { lock (output) output.AppendLine(e.Data); }
                    };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(); } catch (Exception) { }
                        return new Result { ExitCode = -1, Output = "the package manager did not finish in time." };
                    }
                    process.WaitForExit();   // flush async readers

                    string text;
                    lock (output) text = output.ToString();
                    return new Result { ExitCode = process.ExitCode, Output = text };
                }
            }
            catch (Exception ex)
            {
                return new Result { ExitCode = -1, Output = ex.Message };
            }
        }
    }
}
