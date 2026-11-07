using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace AppsManager
{
    /// <summary>
    /// Self-update: version.json in the repo says which setup is current; the setup is
    /// downloaded with aria2c and only started after its SHA-256 matches.
    /// </summary>
    internal static class AppUpdate
    {
        public const string Version = "1.1";
        public const string FeedUrl =
            "https://raw.githubusercontent.com/youcef07dz/apps-manager-windows/main/version.json";

        public sealed class Info
        {
            public bool Available;
            public string Version = "";
            public string Url = "";
            public string Sha256 = "";
            public long Size;
            public string Error = "";
            public string FilePath = "";
        }

        // property names match the JSON keys in version.json
        private sealed class Feed
        {
            public string version { get; set; }
            public string url { get; set; }
            public string sha256 { get; set; }
            public long size { get; set; }
        }

        public static Info Check()
        {
            Info info = new Info();
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (WebClient client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "AppsManager");
                    client.Headers.Add("Cache-Control", "no-cache");
                    string json = client.DownloadString(FeedUrl + "?t=" + DateTime.UtcNow.Ticks);
                    Feed feed = new JavaScriptSerializer().Deserialize<Feed>(json);
                    if (feed == null || string.IsNullOrEmpty(feed.version) || string.IsNullOrEmpty(feed.url))
                    {
                        info.Error = "The update file is not valid.";
                        return info;
                    }
                    info.Version = CleanVersion(feed.version);
                    info.Url = feed.url.Trim();
                    info.Sha256 = (feed.sha256 ?? "").Trim().ToLowerInvariant();
                    info.Size = feed.size;
                    info.Available = info.Version.Length > 0 && IsNewer(info.Version, Version);
                }
            }
            catch (Exception ex)
            {
                info.Error = "Could not check for updates: " + ex.Message;
            }
            return info;
        }

        /// <summary>Downloads, verifies and keeps the setup. Returns "" or a message.</summary>
        public static string Download(Info info, Action<int> progress)
        {
            if (info == null || !info.Available) return "Check for updates first.";
            if (info.Url.IndexOf("https://", StringComparison.OrdinalIgnoreCase) != 0)
                return "The update link is not a secure HTTPS address.";
            if (info.Sha256.Length != 64) return "The update file has no SHA-256 hash.";

            string file = Path.Combine(Path.GetTempPath(), "AppsManager-Setup-" + info.Version + ".exe");
            try
            {
                if (File.Exists(file)) File.Delete(file);
                Downloader.Download(info.Url, file, progress);
            }
            catch (Exception ex)
            {
                return "The update download failed: " + ex.Message;
            }
            if (!File.Exists(file)) return "The update download failed.";

            if (!string.Equals(Sha256Of(file), info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(file); } catch (Exception) { }
                return "The downloaded file did not match its SHA-256 hash, so it was deleted.";
            }
            info.FilePath = file;
            return "";
        }

        public static string Sha256Of(string file)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(file))
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>Digits and dots only, so the value is safe in a file name.</summary>
        private static string CleanVersion(string raw)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < (raw ?? "").Length; i++)
            {
                char c = raw[i];
                if ((c >= '0' && c <= '9') || c == '.') sb.Append(c);
            }
            return sb.ToString().Trim('.');
        }

        private static bool IsNewer(string remote, string current)
        {
            string[] a = remote.Split('.');
            string[] b = current.Split('.');
            int count = a.Length > b.Length ? a.Length : b.Length;
            for (int i = 0; i < count; i++)
            {
                int x = i < a.Length ? Part(a[i]) : 0;
                int y = i < b.Length ? Part(b[i]) : 0;
                if (x != y) return x > y;
            }
            return false;
        }

        private static int Part(string value)
        {
            int number;
            return int.TryParse(value, out number) ? number : 0;
        }
    }
}
