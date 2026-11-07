using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace AppsManager
{
    /// <summary>
    /// Turns the registry icon source ("C:\...\app.exe" or "...,0") into a 32x32 PNG
    /// data URI for the HTML grid. IE8 accepts data URIs well below its 32 KB limit here.
    /// </summary>
    internal static class IconCache
    {
        private static readonly Dictionary<string, string> _cache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string DataUri(string source)
        {
            string path = IconPath(source);
            if (path == null) return "";

            lock (_cache)
            {
                string cached;
                if (_cache.TryGetValue(path, out cached)) return cached;
            }

            string uri = "";
            try { uri = Render(path); }
            catch (Exception) { }

            lock (_cache) _cache[path] = uri;
            return uri;
        }

        /// <summary>Extracts the file name from a DisplayIcon value, dropping a trailing ,index.</summary>
        private static string IconPath(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;
            string value = source.Trim();
            if (value.Length == 0) return null;

            if (value[0] == '"')
            {
                int end = value.IndexOf('"', 1);
                if (end < 1) return null;
                value = value.Substring(1, end - 1);
            }
            else
            {
                int comma = value.LastIndexOf(',');
                int index;
                if (comma > 2 && int.TryParse(value.Substring(comma + 1).Trim(), out index))
                {
                    value = value.Substring(0, comma);
                }
            }

            value = value.Trim().Trim('"');
            string extension = Path.GetExtension(value).ToLowerInvariant();
            if (extension != ".exe" && extension != ".ico" && extension != ".dll") return null;
            return File.Exists(value) ? value : null;
        }

        private static string Render(string path)
        {
            Icon icon = Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase)
                ? new Icon(path)
                : Icon.ExtractAssociatedIcon(path);
            if (icon == null) return "";

            using (icon)
            using (Bitmap bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.DrawIcon(icon, new Rectangle(0, 0, 32, 32));
                }
                using (MemoryStream stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
                }
            }
        }
    }
}
