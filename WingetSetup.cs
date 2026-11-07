using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace AppsManager
{
    /// <summary>
    /// Finds winget on Windows 10 1809+: the portable copy in tools\winget is used first;
    /// when that is missing, the official MSIX bundle is downloaded, the package for this
    /// architecture is pulled out and unpacked into tools\winget next to the exe. No AppX
    /// registration, so no admin and no PowerShell.
    /// </summary>
    internal static class WingetSetup
    {
        public const string Url =
            "https://github.com/microsoft/winget-cli/releases/download/v1.29.380/" +
            "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle";

        // Downloaded builds land where the bundled copy would live, so the lite and full
        // installers use the same lookup path. The lite installer makes tools\winget
        // writable for the user.
        private static readonly string Root = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, @"tools\winget\AppInstaller_x86");

        /// <summary>The portable copy shipped next to the exe (build.cmd copies it).</summary>
        private static string BundledExe
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    @"tools\winget\AppInstaller_x86\winget.exe");
            }
        }

        /// <summary>Bundled winget first, then a copy downloaded into AppData.</summary>
        public static string ExePath
        {
            get
            {
                string bundled = BundledExe;
                if (Supported && File.Exists(bundled)) return bundled;
                return Path.Combine(Root, "winget.exe");
            }
        }

        /// <summary>The packed winget tree shipped next to the exe.</summary>
        public static string ZipPath
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    @"tools\winget\winget.zip");
            }
        }

        /// <summary>True when a winget copy ships with the app, packed or already unpacked.</summary>
        public static bool HasBundled
        {
            get { return File.Exists(BundledExe) || File.Exists(ZipPath); }
        }

        private static readonly object UnpackLock = new object();

        /// <summary>
        /// Extracts tools\winget\winget.zip on first use, then deletes the archive so the
        /// installed folder keeps the size of the unpacked copy. Safe to call often.
        /// </summary>
        public static string EnsureUnpacked()
        {
            if (!Supported) return "";
            if (File.Exists(BundledExe)) return "";
            string zip = ZipPath;
            if (!File.Exists(zip)) return "";

            lock (UnpackLock)
            {
                if (File.Exists(BundledExe)) return "";
                // the archive root is AppInstaller_x86\, so unpack into tools\winget
                string target = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"tools\winget");
                string error;
                try { error = Unpack(zip, target); }
                catch (Exception ex) { return "The bundled winget could not be unpacked: " + ex.Message; }
                if (error.Length > 0) return error;
                try { File.Delete(zip); } catch (Exception) { }
                return "";
            }
        }

        public static bool Supported
        {
            get { return Environment.OSVersion.Version >= new Version(10, 0, 17763); }
        }

        public static bool Available
        {
            get { return File.Exists(ExePath); }
        }

        /// <summary>Downloads and unpacks winget. Returns "" on success, else a message.</summary>
        public static string Install(Action<int> progress)
        {
            string bundle = Path.Combine(Path.GetTempPath(),
                "AppsManager-winget-" + Guid.NewGuid().ToString("N") + ".msixbundle");
            try
            {
                Downloader.Download(Url, bundle, progress);
                return InstallFrom(bundle);
            }
            catch (Exception ex)
            {
                return "The winget download failed: " + ex.Message;
            }
            finally
            {
                try { File.Delete(bundle); } catch (Exception) { }
            }
        }

        /// <summary>Unpacks an already downloaded bundle. Split out so it can be tested offline.</summary>
        public static string InstallFrom(string bundle)
        {
            string appx = Path.Combine(Path.GetTempPath(),
                "AppsManager-winget-" + Guid.NewGuid().ToString("N") + ".appx");
            try
            {
                string arch = IntPtr.Size == 8 ? "_x64" : "_x86";
                using (ZipArchive zip = ZipFile.OpenRead(bundle))
                {
                    ZipArchiveEntry payload = null;
                    for (int i = 0; i < zip.Entries.Count; i++)
                    {
                        ZipArchiveEntry entry = zip.Entries[i];
                        if (entry.FullName.EndsWith(".appx", StringComparison.OrdinalIgnoreCase) &&
                            entry.FullName.IndexOf(arch, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            payload = entry;
                            break;
                        }
                    }
                    if (payload == null)
                    {
                        return "The downloaded package has no winget build for this PC (" +
                               (IntPtr.Size == 8 ? "64-bit" : "32-bit") + ").";
                    }
                    payload.ExtractToFile(appx, true);
                }

                string error = Unpack(appx, Root);
                if (error.Length > 0) return error;

                string version = Version();
                if (version == null)
                {
                    return "winget was unpacked to " + Root + " but does not start on this PC.";
                }
                return "";
            }
            catch (Exception ex)
            {
                return "The winget package could not be unpacked: " + ex.Message;
            }
            finally
            {
                try { File.Delete(appx); } catch (Exception) { }
            }
        }

        /// <summary>Returns the installed version, or null when the exe does not run.</summary>
        public static string Version()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(ExePath, "--version");
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.StandardOutputEncoding = Encoding.UTF8;
                info.StandardErrorEncoding = Encoding.UTF8;
                info.EnvironmentVariables["WINGET_DISABLE_INTERACTIVITY"] = "1";

                using (Process process = Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    if (!process.WaitForExit(30000))
                    {
                        try { process.Kill(); } catch (Exception) { }
                        return null;
                    }
                    if (process.ExitCode != 0 || output.Length == 0) return null;
                    return output;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Unpack(string appx, string folder)
        {
            Directory.CreateDirectory(folder);
            string full = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;

            using (ZipArchive zip = ZipFile.OpenRead(appx))
            {
                for (int i = 0; i < zip.Entries.Count; i++)
                {
                    ZipArchiveEntry entry = zip.Entries[i];
                    if (entry.FullName.EndsWith("/")) continue;
                    string target = Path.GetFullPath(Path.Combine(folder, entry.FullName));
                    if (!target.StartsWith(full, StringComparison.OrdinalIgnoreCase))
                    {
                        return "The winget package contains an unsafe path: " + entry.FullName;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
            }
            return "";
        }
    }
}
