using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AppsManager
{
    /// <summary>Bridge exposed to the HTML UI as window.external.</summary>
    [ComVisible(true)]
    public sealed class HostBridge
    {
        private readonly MainForm _form;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly object _sync = new object();
        private readonly Dictionary<string, InstalledApp> _byKey = new Dictionary<string, InstalledApp>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly HashSet<string> _sizePending = new HashSet<string>(StringComparer.Ordinal);
        private Dictionary<string, string> _cachedIcons = new Dictionary<string, string>(StringComparer.Ordinal);

        private List<InstalledApp> _apps = new List<InstalledApp>();
        private Dictionary<string, string[]> _updates;
        private HashSet<string> _current = new HashSet<string>(StringComparer.Ordinal);
        private string _updateProvider = "";
        private readonly StringBuilder _logHistory = new StringBuilder();
        private const int MaxLogChars = 128 * 1024;
        private bool _checkingUpdates;
        private bool _packageBusy;   // one winget install/update at a time
        private bool _measuringAll;
        private bool _sizesCancelled;
        private bool _installingWinget;
        private bool _searching;
        private bool _cacheLoaded;
        private bool _refreshing;
        private bool _servedFromCache;
        private bool _checkingAppUpdate;
        private bool _downloadingAppUpdate;
        private AppUpdate.Info _appUpdate;

        internal HostBridge(MainForm form)
        {
            _form = form;
            _json.MaxJsonLength = 16 * 1024 * 1024;
        }

        // ---------- list ----------

        public string GetApps()
        {
            bool fromCache;
            lock (_sync)
            {
                fromCache = !_cacheLoaded && LoadCache();
                _cacheLoaded = true;
                _servedFromCache = fromCache;
            }

            if (fromCache)
            {
                // the cached list is ready immediately; refresh it in the background
                List<InstalledApp> cached;
                Dictionary<string, string> icons;
                HashSet<string> current;
                string provider;
                lock (_sync)
                {
                    cached = _apps;
                    icons = new Dictionary<string, string>(_cachedIcons, StringComparer.Ordinal);
                    current = _current;
                    provider = _updateProvider;
                }
                string json = _json.Serialize(ToJson(cached, provider, current, icons));
                lock (_sync) _cachedIcons = icons;
                StartBackgroundRefresh();
                return json;
            }

            List<InstalledApp> apps = AppScanner.Scan();
            Dictionary<string, string> freshIcons;
            HashSet<string> currentKeys;
            string updateProvider;
            lock (_sync)
            {
                if (_updates != null)
                {
                    for (int i = 0; i < apps.Count; i++) ApplyUpdate(apps[i], _updates);
                }
                freshIcons = new Dictionary<string, string>(_cachedIcons, StringComparer.Ordinal);
                currentKeys = _current;
                updateProvider = _updateProvider;
            }
            string listJson = _json.Serialize(ToJson(apps, updateProvider, currentKeys, freshIcons));
            lock (_sync)
            {
                _apps = apps;
                _cachedIcons = freshIcons;
                RebuildIndex();
            }
            SaveCacheAsync();
            return listJson;
        }

        /// <summary>Reads the last scan; true when the list is usable.</summary>
        private bool LoadCache()
        {
            string json = AppCache.Load();
            if (json == null) return false;
            try
            {
                List<CachedApp> cached = _json.Deserialize<List<CachedApp>>(json);
                if (cached == null || cached.Count == 0) return false;

                List<InstalledApp> apps = new List<InstalledApp>(cached.Count);
                Dictionary<string, string> icons = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < cached.Count; i++)
                {
                    CachedApp item = cached[i];
                    if (string.IsNullOrEmpty(item.Key) || string.IsNullOrEmpty(item.Name)) continue;

                    InstalledApp app = new InstalledApp();
                    app.Key = item.Key;
                    app.Name = item.Name;
                    app.Version = item.Version ?? "";
                    app.Publisher = item.Publisher ?? "";
                    app.InstallDate = item.Installed ?? "";
                    app.SizeKb = item.SizeKb;
                    app.Location = item.Location ?? "";
                    app.UninstallString = item.Uninstall ?? "";
                    app.IsMsi = item.IsMsi;
                    app.RegistryPath = item.RegistryPath ?? "";
                    app.DisplayIcon = item.DisplayIcon ?? "";
                    app.LatestVersion = item.Latest ?? "";
                    app.WingetId = item.WingetId ?? "";
                    app.Missing = item.Missing;
                    app.ExePath = item.ExePath ?? "";
                    apps.Add(app);
                    icons[app.Key] = item.Icon ?? "";
                }
                if (apps.Count == 0) return false;

                _apps = apps;
                _cachedIcons = icons;
                RebuildIndex();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void StartBackgroundRefresh()
        {
            lock (_sync)
            {
                if (_refreshing) return;
                _refreshing = true;
            }

            Thread thread = new Thread(delegate()
            {
                try
                {
                    List<InstalledApp> apps = AppScanner.Scan();
                    Dictionary<string, string> icons = new Dictionary<string, string>(StringComparer.Ordinal);
                    HashSet<string> current;
                    string provider;
                    lock (_sync)
                    {
                        for (int i = 0; i < apps.Count; i++)
                        {
                            string icon;
                            if (_cachedIcons.TryGetValue(apps[i].Key, out icon)) icons[apps[i].Key] = icon;
                            if (_updates != null) ApplyUpdate(apps[i], _updates);
                        }
                        current = _current;
                        provider = _updateProvider;
                    }

                    string json = _json.Serialize(ToJson(apps, provider, current, icons));
                    lock (_sync)
                    {
                        _apps = apps;
                        _cachedIcons = icons;
                        RebuildIndex();
                    }
                    SaveCacheAsync();
                    _form.InvokeScript("onAppsUpdated", json);
                }
                catch (Exception)
                {
                    // keep the cached list when the scan fails
                }
                finally
                {
                    lock (_sync) _refreshing = false;
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void RebuildIndex()
        {
            _byKey.Clear();
            for (int i = 0; i < _apps.Count; i++) _byKey[_apps[i].Key] = _apps[i];
        }

        private void SaveCacheAsync()
        {
            string json;
            lock (_sync) json = _json.Serialize(ToCache(_apps, _cachedIcons));
            ThreadPool.QueueUserWorkItem(delegate { AppCache.Save(json); });
        }

        /// <summary>"1" when the last GetApps() was served from apps.json, else "".</summary>
        public string WasCached()
        {
            lock (_sync) return _servedFromCache ? "1" : "";
        }

        // ---------- details ----------

        public string Details(string key)
        {
            InstalledApp app = Find(key);
            Dictionary<string, object> data = new Dictionary<string, object>();
            if (app == null) return _json.Serialize(data);

            bool cached;
            long size;
            lock (_sync) cached = _sizes.TryGetValue(key, out size);

            bool canCompute = !cached && !string.IsNullOrEmpty(app.Location) && Directory.Exists(app.Location);
            data["registryPath"] = app.RegistryPath;
            data["uninstall"] = app.UninstallString ?? "";
            data["msi"] = app.IsMsi;
            data["sizePending"] = canCompute;
            data["realSize"] = cached ? InstalledApp.FormatSize(size) : "";
            data["startup"] = StartsWithWindows(app);

            if (canCompute) StartSize(key, app.Location);
            return _json.Serialize(data);
        }

        /// <summary>True when the app's exe or name is in a Run key or a Startup folder.</summary>
        private static bool StartsWithWindows(InstalledApp app)
        {
            string exe = (app.ExePath ?? "").Trim();
            if (exe.Length == 0) return false;
            string exeName = Path.GetFileName(exe);

            RegistryHive[] hives = { RegistryHive.CurrentUser, RegistryHive.LocalMachine };
            string[] runKeys =
            {
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                @"Software\Microsoft\Windows\CurrentVersion\RunOnce"
            };
            for (int h = 0; h < hives.Length; h++)
            {
                for (int k = 0; k < runKeys.Length; k++)
                {
                    try
                    {
                        using (RegistryKey run = RegistryKey.OpenBaseKey(hives[h], RegistryView.Default).OpenSubKey(runKeys[k]))
                        {
                            if (run == null) continue;
                            string[] names = run.GetValueNames();
                            for (int i = 0; i < names.Length; i++)
                            {
                                string value = run.GetValue(names[i]) as string;
                                if (string.IsNullOrEmpty(value)) continue;
                                if (value.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                                string dir = Path.GetDirectoryName(exe) ?? "";
                                if (dir.Length > 3 && value.IndexOf(exeName, StringComparison.OrdinalIgnoreCase) >= 0
                                    && value.IndexOf(dir, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // unreadable key: treat as no match
                    }
                }
            }

            string[] folders =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            };
            for (int f = 0; f < folders.Length; f++)
            {
                try
                {
                    if (!Directory.Exists(folders[f])) continue;
                    string[] files = Directory.GetFiles(folders[f]);
                    for (int i = 0; i < files.Length; i++)
                    {
                        string name = Path.GetFileNameWithoutExtension(files[i]);
                        if (string.Equals(Path.GetFileName(files[i]), exeName, StringComparison.OrdinalIgnoreCase)) return true;
                        if (name.Length > 2 && app.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    }
                }
                catch (Exception)
                {
                    // unreadable folder: treat as no match
                }
            }
            return false;
        }

        private void StartSize(string key, string path)
        {
            lock (_sync)
            {
                if (_sizes.ContainsKey(key) || !_sizePending.Add(key)) return;
            }

            Thread thread = new Thread(delegate() { ComputeSize(key, path); });
            thread.IsBackground = true;
            thread.Start();
        }

        private void ComputeSize(string key, string path)
        {
            long size = -1;
            try { size = AppScanner.FolderSize(path); }
            catch (Exception) { }
            lock (_sync)
            {
                _sizes[key] = size;
                _sizePending.Remove(key);
            }
            _form.InvokeScript("onSizeReady", key, size >= 0 ? InstalledApp.FormatSize(size) : "", size);
        }

        /// <summary>Measures the given keys one at a time. Keys are separated by newlines.</summary>
        public string MeasureSizes(string keys)
        {
            List<InstalledApp> queue = new List<InstalledApp>();
            string[] parts = (keys ?? "").Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                InstalledApp app = Find(parts[i].Trim());
                if (app == null || string.IsNullOrEmpty(app.Location)) continue;
                if (!Directory.Exists(app.Location)) continue;
                queue.Add(app);
            }
            if (queue.Count == 0) return "No install folders are available to measure.";

            lock (_sync)
            {
                if (_measuringAll) return "Sizes are already being measured.";
                _measuringAll = true;
                _sizesCancelled = false;
            }

            Thread thread = new Thread(delegate()
            {
                bool cancelled = false;
                for (int i = 0; i < queue.Count; i++)
                {
                    InstalledApp app = queue[i];
                    lock (_sync)
                    {
                        if (_sizesCancelled) { cancelled = true; break; }
                        if (_sizes.ContainsKey(app.Key) || !_sizePending.Add(app.Key)) continue;
                    }
                    ComputeSize(app.Key, app.Location);   // cancel stops after this folder finishes
                }
                lock (_sync) _measuringAll = false;
                _form.InvokeScript("onSizesDone", cancelled ? "1" : "");
            });
            thread.IsBackground = true;
            thread.Start();
            return "";
        }

        /// <summary>Stops a running MeasureSizes after the folder it is on.</summary>
        public string CancelSizes()
        {
            lock (_sync) _sizesCancelled = true;
            return "";
        }

        // ---------- uninstall ----------

        public string Uninstall(string key)
        {
            InstalledApp app = Find(key);
            if (app == null) return "This program is no longer in the list. Refresh and try again.";
            if (string.IsNullOrEmpty(app.UninstallString)) return "This program does not publish an uninstall command.";

            string file;
            string args;
            string guid = null;
            if (app.IsMsi || app.UninstallString.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                guid = ExtractGuid(app.UninstallString);
            }

            if (guid != null)
            {
                file = "msiexec.exe";
                args = "/x " + guid + " /qb";
            }
            else if (!SplitCommand(app.UninstallString, out file, out args))
            {
                return "Could not parse the uninstall command: " + app.UninstallString;
            }

            try
            {
                ProcessStartInfo info = new ProcessStartInfo(file, args);
                info.UseShellExecute = true;
                Process process = Process.Start(info);
                if (process != null)
                {
                    Thread thread = new Thread(delegate()
                    {
                        try { process.WaitForExit(); }
                        catch (Exception) { }
                        _form.InvokeScript("onUninstalled", "Uninstaller finished. Refreshing the list.");
                    });
                    thread.IsBackground = true;
                    thread.Start();
                }
                return "";
            }
            catch (Exception ex)
            {
                return "Could not start the uninstaller: " + ex.Message;
            }
        }

        public string OpenFolder(string key)
        {
            InstalledApp app = Find(key);
            if (app == null || string.IsNullOrEmpty(app.Location)) return "No install path is recorded for this program.";
            if (!Directory.Exists(app.Location)) return "The install folder no longer exists: " + app.Location;
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("explorer.exe", "\"" + app.Location + "\"");
                info.UseShellExecute = true;
                Process.Start(info);
                return "";
            }
            catch (Exception ex)
            {
                return "Could not open the folder: " + ex.Message;
            }
        }

        /// <summary>Starts the program's exe. Returns "" when started, else a message.</summary>
        public string Run(string key)
        {
            InstalledApp app = Find(key);
            if (app == null || string.IsNullOrEmpty(app.ExePath)) return "No program file is known for this app.";
            if (!File.Exists(app.ExePath)) return "The program file no longer exists: " + app.ExePath;
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(app.ExePath);
                info.UseShellExecute = true;
                info.WorkingDirectory = Path.GetDirectoryName(app.ExePath);
                Process.Start(info);
                return "";
            }
            catch (Exception ex)
            {
                return "Could not start the program: " + ex.Message;
            }
        }

        public string CopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "There is nothing to copy.";
            try
            {
                Clipboard.SetText(text);
                return "";
            }
            catch (Exception ex)
            {
                return "Could not copy to the clipboard: " + ex.Message;
            }
        }

        /// <summary>Opens regedit at the entry's key. The key is selected through LastKey.</summary>
        public string OpenRegistry(string key)
        {
            InstalledApp app = Find(key);
            if (app == null) return "This program is no longer in the list. Refresh and try again.";
            try
            {
                string path = ("Computer\\" + app.RegistryPath)
                    .Replace("HKLM\\", "HKEY_LOCAL_MACHINE\\")
                    .Replace("HKCU\\", "HKEY_CURRENT_USER\\");
                using (RegistryKey regedit = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit"))
                {
                    if (regedit != null) regedit.SetValue("LastKey", path);
                }
                Process.Start("regedit.exe");
                return "";
            }
            catch (Exception ex)
            {
                return "Could not open the registry editor: " + ex.Message;
            }
        }

        /// <summary>Writes the visible list to a CSV file chosen by the user. "" on success.</summary>
        public string SaveCsv(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return "There is nothing to export.";
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV file (*.csv)|*.csv|All files (*.*)|*.*";
                dialog.FileName = "installed-programs.csv";
                if (dialog.ShowDialog(_form) != DialogResult.OK) return "Export cancelled.";
                try
                {
                    File.WriteAllText(dialog.FileName, csv, new UTF8Encoding(true));
                    return "";
                }
                catch (Exception ex)
                {
                    return "Could not write the file: " + ex.Message;
                }
            }
        }

        // ---------- window chrome ----------

        public string WindowDrag()
        {
            _form.BeginDrag();
            return "";
        }

        public string WindowMinimize()
        {
            _form.WindowState = FormWindowState.Minimized;
            return "";
        }

        public string WindowToggleMaximize()
        {
            _form.ToggleMaximize();
            return "";
        }

        public string WindowClose()
        {
            // let the script call return before the form goes away
            _form.BeginInvoke(new Action(delegate { _form.Close(); }));
            return "";
        }

        public string GetWindowState()
        {
            return _form.WindowState == FormWindowState.Maximized ? "1" : "";
        }

        // ---------- small page settings ----------

        public string GetSetting(string name)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\AppsManager"))
                {
                    if (key == null) return "";
                    object value = key.GetValue(name);
                    return value == null ? "" : value.ToString();
                }
            }
            catch (Exception)
            {
                return "";
            }
        }

        public string SetSetting(string name, string value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\AppsManager"))
                {
                    if (key != null) key.SetValue(name, value ?? "");
                }
                return "";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>"light" or "dark" from the Windows personalization setting, "" when unknown.</summary>
        public string GetSystemTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key == null) return "";
                    object value = key.GetValue("AppsUseLightTheme");
                    if (value == null) return "";
                    return Convert.ToInt32(value) == 0 ? "dark" : "light";
                }
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>Contents of lang\&lt;code&gt;.txt, or "" when the code is invalid or missing.</summary>
        public string GetLanguage(string code)
        {
            if (code == null) return "";
            code = code.Trim().ToLowerInvariant();
            if (code.Length == 0 || code.Length > 12) return "";
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if ((c < 'a' || c > 'z') && c != '-') return "";
            }
            try
            {
                string path = Path.Combine(Application.StartupPath, Path.Combine("lang", code + ".txt"));
                return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>All check/update/install output from this session, for the "Show log" panel.</summary>
        public string GetLastLog()
        {
            lock (_sync) return _logHistory.ToString();
        }

        private void AddLog(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (_sync)
            {
                if (_logHistory.Length > 0) _logHistory.AppendLine();
                _logHistory.Append(text);
                if (_logHistory.Length > MaxLogChars)
                {
                    _logHistory.Remove(0, _logHistory.Length - MaxLogChars);
                    _logHistory.Insert(0, "[earlier output trimmed]\r\n");
                }
            }
        }

        // ---------- updates ----------

        /// <summary>
        /// Returns "winget" when a check started, "notfound" when winget is not installed,
        /// "busy" while a check is already running.
        /// </summary>
        public string CheckUpdates()
        {
            lock (_sync)
            {
                if (_checkingUpdates) return "busy";
                _checkingUpdates = true;
            }

            Thread thread = new Thread(delegate()
            {
                try
                {
                    string toolPath = WingetService.FindWinget();   // unpacks the bundled copy on first use
                    if (toolPath == null)
                    {
                        _form.InvokeScript("onUpdatesFailed", "winget is not installed on this PC.");
                        return;
                    }
                    Dictionary<string, string[]> map = WingetService.ListUpgrades(toolPath);
                    HashSet<string> tracked = WingetService.ListTracked(toolPath);
                    List<Dictionary<string, object>> payload = new List<Dictionary<string, object>>();
                    HashSet<string> current = new HashSet<string>(StringComparer.Ordinal);
                    lock (_sync)
                    {
                        _updates = map;
                        _updateProvider = WingetService.Winget;
                        for (int i = 0; i < _apps.Count; i++)
                        {
                            InstalledApp app = _apps[i];
                            ApplyUpdate(app, map);
                            if (app.LatestVersion != null && app.LatestVersion.Length > 0)
                            {
                                Dictionary<string, object> item = new Dictionary<string, object>();
                                item["key"] = app.Key;
                                item["latest"] = app.LatestVersion;
                                item["id"] = app.WingetId;
                                payload.Add(item);
                            }
                            else if (tracked.Contains(WingetService.Normalize(app.Name)))
                            {
                                current.Add(app.Key);   // checked by winget and no update
                            }
                        }
                        _current = current;
                    }
                    _form.InvokeScript("onUpdatesReady", _json.Serialize(payload), WingetService.Winget,
                        _json.Serialize(new List<string>(current)));
                }
                catch (Exception ex)
                {
                    AddLog(ex.ToString());
                    _form.InvokeScript("onUpdatesFailed", ex.Message);
                }
                finally
                {
                    lock (_sync) _checkingUpdates = false;
                }
            });
            thread.IsBackground = true;
            thread.Start();
            return WingetService.Winget;
        }

        /// <summary>Returns "" when the upgrade started, otherwise an error message for the UI.</summary>
        public string UpdateApp(string key)
        {
            InstalledApp app = Find(key);
            if (app == null) return "This program is no longer in the list.";
            if (string.IsNullOrEmpty(app.WingetId)) return "No update source was found for this program.";

            string winget = WingetService.FindWinget();
            if (winget == null) return "winget is not installed on this PC.";

            lock (_sync)
            {
                if (_packageBusy) return "Another install or update is already running.";
                _packageBusy = true;
            }

            string id = app.WingetId;
            string name = app.Name;
            string latest = app.LatestVersion;
            string appKey = app.Key;
            Thread thread = new Thread(delegate()
            {
                _form.InvokeScript("onUpdateProgress", appKey, -1);   // package started
                string ok = "0";
                string message;
                string log = "";
                try
                {
                    WingetService.Result result = WingetService.Upgrade(winget, id, latest,
                        delegate(int percent)
                        {
                            _form.InvokeScript("onUpdateProgress", appKey, percent);
                        });
                    log = result.Output ?? "";
                    if (result.ExitCode == 0)
                    {
                        ok = "1";
                        message = "Updated " + name + ". Refreshing the list.";
                    }
                    else
                    {
                        string detail = result.Output;
                        if (string.IsNullOrEmpty(detail)) detail = "exit code " + result.ExitCode;
                        message = "Update failed: " + WingetService.Summarize(detail);
                    }
                }
                catch (Exception ex)
                {
                    log = ex.ToString();
                    message = "Update failed: " + ex.Message;
                }
                AddLog(log);
                lock (_sync) _packageBusy = false;
                _form.InvokeScript("onUpdateFinished", key, ok, message);
            });
            thread.IsBackground = true;
            thread.Start();
            return "";
        }

        /// <summary>Starts one bulk update. Returns "" when started, else a message for the UI.</summary>
        public string UpdateAll(string skipKeys)
        {
            HashSet<string> skip = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(skipKeys))
            {
                string[] parts = skipKeys.Split('\n');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Length > 0) skip.Add(parts[i]);
                }
            }

            List<InstalledApp> targets = new List<InstalledApp>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (_sync)
            {
                for (int i = 0; i < _apps.Count; i++)
                {
                    InstalledApp app = _apps[i];
                    if (string.IsNullOrEmpty(app.WingetId)) continue;
                    if (skip.Contains(app.Key)) continue;   // update ignored by the user
                    if (!seen.Add(app.WingetId)) continue;   // one package per id
                    targets.Add(app);
                }
            }
            if (targets.Count == 0) return "There are no updates to install.";

            string winget = WingetService.FindWinget();
            if (winget == null) return "winget is not installed on this PC.";

            lock (_sync)
            {
                if (_packageBusy) return "Another install or update is already running.";
                _packageBusy = true;
            }

            int count = targets.Count;
            Thread thread = new Thread(delegate()
            {
                string ok = "0";
                string message;
                string log = "";
                try
                {
                    // one winget call per package so each row gets its own download progress
                    int updated = 0;
                    string lastError = "";
                    StringBuilder all = new StringBuilder();
                    for (int i = 0; i < targets.Count; i++)
                    {
                        InstalledApp app = targets[i];
                        string appKey = app.Key;
                        _form.InvokeScript("onUpdateProgress", appKey, -1);   // this package started
                        WingetService.Result result = WingetService.Upgrade(winget, app.WingetId, app.LatestVersion,
                            delegate(int percent)
                            {
                                _form.InvokeScript("onUpdateProgress", appKey, percent);
                            });
                        all.AppendLine(result.Output ?? "");
                        if (result.ExitCode == 0) updated++;
                        else lastError = result.Output;
                    }
                    log = all.ToString();
                    if (updated == count)
                    {
                        ok = "1";
                        message = "Updated " + count + " package(s) with winget. Refreshing the list.";
                    }
                    else
                    {
                        message = "Updated " + updated + " of " + count + " package(s).";
                        if (!string.IsNullOrEmpty(lastError))
                            message += " " + WingetService.Summarize(lastError);
                    }
                }
                catch (Exception ex)
                {
                    log = ex.ToString();
                    message = "Bulk update failed: " + ex.Message;
                }
                AddLog(log);
                lock (_sync) _packageBusy = false;
                _form.InvokeScript("onUpdateAllFinished", ok, message);
            });
            thread.IsBackground = true;
            thread.Start();
            return "";
        }

        // ---------- app installer ----------

        /// <summary>
        /// Starts a winget search. Returns "started", "notfound" when winget is missing,
        /// "empty" for a blank query, "busy" while a search is running, else an error.
        /// The UI gets onSearchResults(json, error).
        /// </summary>
        public string SearchWinget(string query)
        {
            if (query == null || query.Trim().Length == 0) return "empty";
            string winget = WingetService.FindWinget();
            if (winget == null) return "notfound";

            lock (_sync)
            {
                if (_searching) return "busy";
                _searching = true;
            }

            string tool = winget;
            string term = query.Trim();
            Thread thread = new Thread(delegate()
            {
                string raw = "[]";
                string error = "";
                try
                {
                    List<string[]> found = WingetService.Search(tool, term);
                    List<Dictionary<string, object>> results =
                        new List<Dictionary<string, object>>(found.Count);
                    for (int i = 0; i < found.Count; i++)
                    {
                        string[] row = found[i];
                        Dictionary<string, object> item = new Dictionary<string, object>();
                        item["name"] = row.Length > 0 ? row[0] : "";
                        item["id"] = row.Length > 1 ? row[1] : "";
                        item["version"] = row.Length > 2 ? row[2] : "";
                        item["match"] = row.Length > 3 ? row[3] : "";
                        item["source"] = row.Length > 4 ? row[4] : "";
                        results.Add(item);
                    }
                    raw = _json.Serialize(results);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                finally
                {
                    lock (_sync) _searching = false;
                }
                _form.InvokeScript("onSearchResults", raw, error);
            });
            thread.IsBackground = true;
            thread.Start();
            return "started";
        }

        /// <summary>
        /// Installs one winget package. Returns "" when started, else a message for the UI.
        /// The UI gets onInstallProgress(percent) and onInstallFinished(ok, message).
        /// </summary>
        public string InstallPackage(string id, string version)
        {
            if (string.IsNullOrEmpty(id)) return "Select a package first.";
            string winget = WingetService.FindWinget();
            if (winget == null) return "winget is not installed on this PC.";

            lock (_sync)
            {
                if (_packageBusy) return "Another install or update is already running.";
                _packageBusy = true;
            }

            string tool = winget;
            string package = id;
            string target = version ?? "";
            Thread thread = new Thread(delegate()
            {
                string ok = "0";
                string message;
                string log = "";
                try
                {
                    WingetService.Result result = WingetService.Install(tool, package, target,
                        delegate(int percent)
                        {
                            _form.InvokeScript("onInstallProgress", percent);
                        });
                    log = result.Output ?? "";
                    if (result.ExitCode == 0)
                    {
                        ok = "1";
                        message = "Installed " + package + " with winget. Refreshing the list.";
                    }
                    else
                    {
                        string detail = result.Output;
                        if (string.IsNullOrEmpty(detail)) detail = "exit code " + result.ExitCode;
                        message = "Install failed: " + WingetService.Summarize(detail);
                    }
                }
                catch (Exception ex)
                {
                    log = ex.ToString();
                    message = "Install failed: " + ex.Message;
                }
                finally
                {
                    lock (_sync) _packageBusy = false;
                }
                AddLog(log);
                _form.InvokeScript("onInstallFinished", ok, message);
            });
            thread.IsBackground = true;
            thread.Start();
            return "";
        }

        // ---------- winget ----------

        /// <summary>"1" when this PC can use winget but has none installed.</summary>
        public string CanInstallWinget()
        {
            if (!WingetSetup.Supported) return "";
            if (WingetSetup.HasBundled) return "";   // shipped copy unpacks on first use
            return WingetService.FindWinget() == null ? "1" : "";
        }

        // ---------- app update ----------

        /// <summary>Starts a check of version.json. The UI gets onAppUpdate(json). "" when started.</summary>
        public string CheckAppUpdate()
        {
            lock (_sync)
            {
                if (_checkingAppUpdate) return "An update check is already running.";
                _checkingAppUpdate = true;
            }

            Thread thread = new Thread(delegate()
            {
                AppUpdate.Info info = AppUpdate.Check();
                Dictionary<string, object> payload = new Dictionary<string, object>();
                payload["available"] = info.Available;
                payload["version"] = info.Version;
                payload["current"] = AppUpdate.Version;
                payload["error"] = info.Error;
                string json = _json.Serialize(payload);
                lock (_sync)
                {
                    _appUpdate = info;
                    _checkingAppUpdate = false;
                }
                _form.InvokeScript("onAppUpdate", json);
            });
            thread.IsBackground = true;
            thread.Start();
            return "";
        }

        /// <summary>Downloads the checked setup, verifies its hash and starts it.</summary>
        public string InstallAppUpdate()
        {
            AppUpdate.Info info;
            lock (_sync)
            {
                info = _appUpdate;
                if (info == null || !info.Available) return "Check for updates first.";
                if (_downloadingAppUpdate) return "The update is already downloading.";
                _downloadingAppUpdate = true;
            }

            Thread thread = new Thread(delegate()
            {
                string error = AppUpdate.Download(info, delegate(int percent)
                {
                    _form.InvokeScript("onAppUpdateProgress", percent);
                });
                lock (_sync) _downloadingAppUpdate = false;
                if (error.Length > 0)
                {
                    _form.InvokeScript("onAppUpdateReady", "0", error);
                    return;
                }
                _form.InvokeScript("onAppUpdateReady", "1", "");
                Thread.Sleep(600);   // let the status line paint before the setup closes the app
                try
                {
                    ProcessStartInfo start = new ProcessStartInfo(info.FilePath);
                    start.UseShellExecute = true;
                    Process.Start(start);
                }
                catch (Exception ex)
                {
                    _form.InvokeScript("onAppUpdateReady", "0", "Could not start the installer: " + ex.Message);
                }
            });
            thread.IsBackground = true;
            thread.Start();
            return "";
        }

        /// <summary>
        /// Downloads winget from the official release and unpacks it into the app's
        /// tools\winget folder. The UI gets onWingetProgress(percent) while downloading and
        /// onWingetInstalled(ok, message) at the end. Returns "" when it started, else a message.
        /// </summary>
        public string InstallWinget()
        {
            if (WingetService.FindWinget() != null) return "winget is already available.";
            if (!WingetSetup.Supported)
            {
                return "winget needs Windows 10 version 1809 or later.";
            }

            lock (_sync)
            {
                if (_installingWinget) return "The winget download is already running.";
                _installingWinget = true;
            }

            Thread thread = new Thread(delegate()
            {
                int last = -1;
                string error = WingetSetup.Install(delegate(int percent)
                {
                    if (percent / 5 == last) return;   // 5% steps keep the page calm
                    last = percent / 5;
                    _form.InvokeScript("onWingetProgress", percent);
                });
                lock (_sync) _installingWinget = false;
                _form.InvokeScript("onWingetInstalled",
                    error.Length == 0 ? "1" : "0",
                    error.Length == 0 ? "winget is ready. Checking for updates." : error);
            });
            thread.IsBackground = true;
            thread.Start();
            return "";
        }

        // ---------- helpers ----------
        private InstalledApp Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            lock (_sync)
            {
                InstalledApp app;
                return _byKey.TryGetValue(key, out app) ? app : null;
            }
        }

        private static void ApplyUpdate(InstalledApp app, Dictionary<string, string[]> map)
        {
            if (map == null) return;
            string[] match;
            if (!map.TryGetValue(WingetService.Normalize(app.Name), out match)) return;
            app.WingetId = match[0];
            app.LatestVersion = match.Length > 1 ? match[1] : "";
        }

        private static List<Dictionary<string, object>> ToJson(List<InstalledApp> apps, string provider,
            HashSet<string> current, Dictionary<string, string> icons)
        {
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>(apps.Count);
            for (int i = 0; i < apps.Count; i++)
            {
                InstalledApp app = apps[i];
                string icon;
                if (icons == null || !icons.TryGetValue(app.Key, out icon))
                {
                    icon = IconCache.DataUri(
                        string.IsNullOrEmpty(app.DisplayIcon) ? app.UninstallString : app.DisplayIcon);
                    if (icons != null) icons[app.Key] = icon;
                }

                Dictionary<string, object> item = new Dictionary<string, object>();
                item["key"] = app.Key;
                item["name"] = app.Name;
                item["version"] = app.Version;
                item["publisher"] = app.Publisher;
                item["installed"] = app.InstallDate;
                item["size"] = app.SizeText;
                item["sizeKb"] = app.SizeKb;
                item["location"] = app.Location;
                item["icon"] = icon;
                item["latest"] = app.LatestVersion ?? "";
                item["upToDate"] = current != null && current.Contains(app.Key);
                item["provider"] = provider ?? "";
                item["hasUninstall"] = !string.IsNullOrEmpty(app.UninstallString);
                item["missing"] = app.Missing;
                item["runnable"] = !string.IsNullOrEmpty(app.ExePath);
                list.Add(item);
            }
            return list;
        }

        private static List<CachedApp> ToCache(List<InstalledApp> apps, Dictionary<string, string> icons)
        {
            List<CachedApp> list = new List<CachedApp>(apps.Count);
            for (int i = 0; i < apps.Count; i++)
            {
                InstalledApp app = apps[i];
                string icon;
                CachedApp item = new CachedApp();
                item.Key = app.Key;
                item.Name = app.Name;
                item.Version = app.Version;
                item.Publisher = app.Publisher;
                item.Installed = app.InstallDate;
                item.SizeKb = app.SizeKb;
                item.Location = app.Location;
                item.Uninstall = app.UninstallString;
                item.IsMsi = app.IsMsi;
                item.RegistryPath = app.RegistryPath;
                item.DisplayIcon = app.DisplayIcon;
                item.Latest = app.LatestVersion;
                item.WingetId = app.WingetId;
                item.Missing = app.Missing;
                item.ExePath = app.ExePath;
                item.Icon = icons != null && icons.TryGetValue(app.Key, out icon) ? icon : "";
                list.Add(item);
            }
            return list;
        }

        private static string ExtractGuid(string command)
        {
            Match match = Regex.Match(command, "\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}");
            return match.Success ? match.Value : null;
        }

        private static bool SplitCommand(string command, out string file, out string args)
        {
            file = "";
            args = "";
            string cmd = command.Trim();
            if (cmd.Length == 0) return false;

            if (cmd[0] == '"')
            {
                int end = cmd.IndexOf('"', 1);
                if (end < 1) return false;
                file = cmd.Substring(1, end - 1);
                args = cmd.Substring(end + 1).Trim();
                return file.Length > 0;
            }

            int exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe > 0)
            {
                file = cmd.Substring(0, exe + 4).Trim();
                args = cmd.Substring(exe + 4).Trim();
                return true;
            }

            file = cmd;
            return true;
        }
    }
}
