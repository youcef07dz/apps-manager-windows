using System;
using System.IO;
using System.Text;

namespace AppsManager
{
    /// <summary>One entry in apps.json. Public properties so the JavaScript serializer can read it back.</summary>
    internal sealed class CachedApp
    {
        public CachedApp() { }

        public string Key { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Publisher { get; set; }
        public string Installed { get; set; }
        public long SizeKb { get; set; }
        public string Location { get; set; }
        public string Uninstall { get; set; }
        public bool IsMsi { get; set; }
        public string RegistryPath { get; set; }
        public string DisplayIcon { get; set; }
        public string Latest { get; set; }
        public string WingetId { get; set; }
        public string Icon { get; set; }
        public bool Missing { get; set; }
        public string ExePath { get; set; }
    }

    /// <summary>Last scan on disk, so the list can be shown before the registry is read again.</summary>
    internal static class AppCache
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AppsManager", "apps.json");

        public static string Load()
        {
            try
            {
                return File.Exists(FilePath) ? File.ReadAllText(FilePath, Encoding.UTF8) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Save(string json)
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(temp, FilePath);
            }
            catch (Exception)
            {
                // no cache just means the next start scans again
            }
        }
    }
}
