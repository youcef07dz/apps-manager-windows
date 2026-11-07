using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace AppsManager
{
    /// <summary>Reads the Add/Remove Programs registry keys (64-bit, 32-bit and per-user).</summary>
    internal static class AppScanner
    {
        private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public static List<InstalledApp> Scan()
        {
            List<InstalledApp> list = new List<InstalledApp>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            if (Environment.Is64BitOperatingSystem)
            {
                ReadHive(RegistryHive.LocalMachine, RegistryView.Registry64, list, seen);
                ReadHive(RegistryHive.LocalMachine, RegistryView.Registry32, list, seen);
            }
            else
            {
                ReadHive(RegistryHive.LocalMachine, RegistryView.Registry32, list, seen);
            }
            ReadHive(RegistryHive.CurrentUser, RegistryView.Default, list, seen);

            list.Sort(delegate(InstalledApp a, InstalledApp b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        private static void ReadHive(RegistryHive hive, RegistryView view, List<InstalledApp> list, Dictionary<string, bool> seen)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey root = baseKey.OpenSubKey(UninstallPath))
                {
                    if (root == null) return;
                    string[] names = root.GetSubKeyNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        try
                        {
                            using (RegistryKey sub = root.OpenSubKey(names[i]))
                            {
                                if (sub == null) continue;
                                InstalledApp app = ReadApp(sub, hive, view, names[i]);
                                if (app == null) continue;
                                string id = app.Name + "|" + app.Version;
                                if (seen.ContainsKey(id)) continue;
                                seen[id] = true;
                                list.Add(app);
                            }
                        }
                        catch (Exception)
                        {
                            // One broken key must not stop the scan.
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private static InstalledApp ReadApp(RegistryKey key, RegistryHive hive, RegistryView view, string subName)
        {
            string name = Str(key.GetValue("DisplayName"));
            if (name.Length == 0) return null;
            if (Str(key.GetValue("ParentKeyName")).Length > 0) return null;   // update/patch of another product
            if (ToLong(key.GetValue("SystemComponent")) == 1) return null;

            string release = Str(key.GetValue("ReleaseType"));
            if (release.IndexOf("update", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            if (release.IndexOf("hotfix", StringComparison.OrdinalIgnoreCase) >= 0) return null;

            InstalledApp app = new InstalledApp();
            app.Name = name;
            app.Version = Str(key.GetValue("DisplayVersion"));
            app.Publisher = Str(key.GetValue("Publisher"));
            app.InstallDate = FormatDate(Str(key.GetValue("InstallDate")));
            app.SizeKb = ToLong(key.GetValue("EstimatedSize"));
            app.Location = Str(key.GetValue("InstallLocation")).Trim('"');
            app.DisplayIcon = Str(key.GetValue("DisplayIcon"));
            app.UninstallString = Str(key.GetValue("UninstallString"));
            if (app.UninstallString.Length == 0) app.UninstallString = Str(key.GetValue("QuietUninstallString"));
            app.IsMsi = ToLong(key.GetValue("WindowsInstaller")) == 1;
            app.Missing = app.Location.Length > 0 && !Directory.Exists(app.Location);
            app.ExePath = ResolveExe(app.DisplayIcon, app.Location);

            string hiveName = hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
            app.RegistryPath = hiveName + "\\" + UninstallPath + "\\" + subName;
            app.Key = hiveName + "|" + view + "|" + subName;
            return app;
        }

        /// <summary>DisplayIcon when it points at an exe, else the only exe in the install folder.</summary>
        private static string ResolveExe(string displayIcon, string location)
        {
            string icon = (displayIcon ?? "").Trim().Trim('"');
            int comma = icon.LastIndexOf(',');
            if (comma > 0) icon = icon.Substring(0, comma).Trim().Trim('"');
            if (icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(icon)) return icon;

            if (string.IsNullOrEmpty(location) || !Directory.Exists(location)) return "";
            try
            {
                string[] exes = Directory.GetFiles(location, "*.exe");
                return exes.Length == 1 ? exes[0] : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static string Str(object value)
        {
            string s = value as string;
            return s == null ? "" : s.Trim();
        }

        private static long ToLong(object value)
        {
            if (value == null) return 0;
            try
            {
                if (value is int) return (int)value;
                if (value is long) return (long)value;
                if (value is string)
                {
                    long parsed;
                    if (long.TryParse((string)value, out parsed)) return parsed;
                }
                return Convert.ToInt64(value);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static string FormatDate(string raw)
        {
            if (raw.Length != 8) return raw;
            int year, month, day;
            if (!int.TryParse(raw.Substring(0, 4), out year)) return raw;
            if (!int.TryParse(raw.Substring(4, 2), out month)) return raw;
            if (!int.TryParse(raw.Substring(6, 2), out day)) return raw;
            if (year < 1990 || month < 1 || month > 12 || day < 1 || day > 31) return raw;
            return new DateTime(year, month, day).ToString("yyyy-MM-dd");
        }

        /// <summary>Recursive folder size. Skips junctions and unreadable folders; -1 if the path is missing.</summary>
        public static long FolderSize(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return -1;
            long total = 0;
            Stack<string> pending = new Stack<string>();
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            pending.Push(path);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                if (!visited.Add(dir)) continue;
                try
                {
                    string[] files = Directory.GetFiles(dir);
                    for (int i = 0; i < files.Length; i++)
                    {
                        try { total += new FileInfo(files[i]).Length; }
                        catch (Exception) { }
                    }
                    string[] dirs = Directory.GetDirectories(dir);
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        try
                        {
                            if ((File.GetAttributes(dirs[i]) & FileAttributes.ReparsePoint) != 0) continue;
                            pending.Push(dirs[i]);
                        }
                        catch (Exception) { }
                    }
                }
                catch (Exception)
                {
                    // permissions: skip this folder, keep the rest
                }
            }
            return total;
        }
    }
}
