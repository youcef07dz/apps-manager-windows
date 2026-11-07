using System;
using System.Globalization;

namespace AppsManager
{
    /// <summary>One entry from the uninstall registry keys.</summary>
    internal sealed class InstalledApp
    {
        public string Key;              // stable id: hive|view|subkey
        public string Name;
        public string Version;
        public string Publisher;
        public string InstallDate;      // display format, empty when unknown
        public long SizeKb;             // EstimatedSize from the registry
        public string Location;
        public string UninstallString;
        public bool IsMsi;
        public string RegistryPath;
        public string DisplayIcon;
        public string LatestVersion;    // from winget, null when no update
        public string WingetId;
        public bool Missing;            // install folder recorded but gone
        public string ExePath;          // runnable exe, empty when unknown

        public string SizeText
        {
            get { return SizeKb > 0 ? FormatSize(SizeKb * 1024L) : ""; }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 0) return "";
            if (bytes >= 1073741824L) return (bytes / 1073741824.0).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
            if (bytes >= 1048576L) return (bytes / 1048576.0).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
            if (bytes >= 1024L) return (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }
    }
}
