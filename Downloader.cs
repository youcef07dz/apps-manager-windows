using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace AppsManager
{
    /// <summary>
    /// Downloads setup payloads with the bundled aria2c when it is present, falling back
    /// to WebClient. Neither path uses PowerShell, which keeps downloads independent of
    /// the system shell.
    /// </summary>
    internal static class Downloader
    {
        private const int TimeoutMs = 1800000;   // 30 minutes

        /// <summary>Bundled tools\aria2c.exe first, then a copy on PATH.</summary>
        public static string FindAria2c()
        {
            string bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"tools\aria2c.exe");
            if (File.Exists(bundled)) return bundled;

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
                        string candidate = Path.Combine(dir, "aria2c.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>Downloads url to file. Throws when both aria2c and WebClient fail.</summary>
        public static void Download(string url, string file, Action<int> progress)
        {
            string aria2c = FindAria2c();
            if (aria2c != null && TryAria2c(aria2c, url, file, progress)) return;
            DownloadWeb(url, file, progress);
        }

        private static bool TryAria2c(string aria2c, string url, string file, Action<int> progress)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(file));
            string name = Path.GetFileName(file);
            try
            {
                Directory.CreateDirectory(folder);
                if (File.Exists(file)) File.Delete(file);
            }
            catch (Exception) { return false; }

            long total = progress == null ? 0 : ContentLength(url);
            ProcessStartInfo info = new ProcessStartInfo(aria2c,
                "-x 4 -s 4 -k 1M --file-allocation=none --allow-overwrite=true " +
                "--auto-file-renaming=false --console-log-level=warn --summary-interval=0 " +
                "--show-console-readout=false --download-result=hide " +
                "--dir \"" + folder + "\" --out \"" + name + "\" \"" + url + "\"");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;

            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = info;
                    process.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { };
                    process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    DateTime start = DateTime.UtcNow;
                    int last = -1;
                    while (!process.WaitForExit(300))
                    {
                        if ((DateTime.UtcNow - start).TotalMilliseconds > TimeoutMs)
                        {
                            try { process.Kill(); } catch (Exception) { }
                            break;
                        }
                        if (total <= 0) continue;

                        long size = 0;
                        try { size = new FileInfo(file).Length; } catch (Exception) { }
                        int percent = (int)(size * 100 / total);
                        if (percent > 100) percent = 100;
                        if (percent == last) continue;
                        last = percent;
                        progress(percent);
                    }
                    process.WaitForExit();

                    if (process.ExitCode == 0 && File.Exists(file))
                    {
                        if (progress != null) progress(100);
                        return true;
                    }
                }
            }
            catch (Exception) { }

            try { if (File.Exists(file)) File.Delete(file); } catch (Exception) { }
            try { if (File.Exists(file + ".aria2")) File.Delete(file + ".aria2"); } catch (Exception) { }
            return false;
        }

        private static void DownloadWeb(string url, string file, Action<int> progress)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (WebClient client = new WebClient())
            {
                client.Headers.Add("User-Agent", "AppsManager");
                // DownloadFile is synchronous and never raises DownloadProgressChanged.
                client.DownloadFile(new Uri(url), file);
            }
            if (progress != null) progress(100);
        }

        /// <summary>Content length for progress; 0 when the server will not say.</summary>
        public static long ContentLength(string url)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "HEAD";
                request.AllowAutoRedirect = true;
                request.UserAgent = "AppsManager";
                request.Timeout = 20000;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    return response.ContentLength;
                }
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
