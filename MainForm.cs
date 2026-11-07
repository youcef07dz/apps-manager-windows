using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AppsManager
{
    internal sealed class MainForm : Form
    {
        private const string SettingsPath = @"Software\AppsManager";
        private const int ResizeBorder = 5;
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private readonly WebBrowser _browser;
        private readonly string _pagePath;

        public MainForm()
        {
            Text = "Apps Manager";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1120, 700);
            MinimumSize = new Size(940, 580);
            Font = new Font("Tahoma", 9f);
            BackColor = Color.FromArgb(15, 27, 38);   // blends with the page header
            Padding = new Padding(ResizeBorder);
            RestoreWindowBounds();
            _pagePath = Path.Combine(Application.StartupPath, "app.html");

            _browser = new WebBrowser();
            _browser.Dock = DockStyle.Fill;
            _browser.ScriptErrorsSuppressed = !DebugMode;
            _browser.IsWebBrowserContextMenuEnabled = false;
            _browser.AllowWebBrowserDrop = false;
            _browser.ScrollBarsEnabled = false;
            _browser.ObjectForScripting = new HostBridge(this);
            Controls.Add(_browser);

            Load += delegate
            {
                if (!File.Exists(_pagePath))
                {
                    MessageBox.Show(this, "app.html is missing next to the program:\r\n" + _pagePath,
                        "Apps Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Close();
                    return;
                }
                _browser.Navigate(new Uri(_pagePath).AbsoluteUri);
            };
        }

        private static bool DebugMode
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }

        private void RestoreWindowBounds()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsPath))
                {
                    if (key == null) return;
                    string saved = key.GetValue("WindowBounds") as string;
                    if (string.IsNullOrEmpty(saved)) return;

                    string[] parts = saved.Split(',');
                    int x, y, w, h;
                    if (parts.Length != 4) return;
                    if (!int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y) ||
                        !int.TryParse(parts[2], out w) || !int.TryParse(parts[3], out h)) return;
                    if (w < MinimumSize.Width || h < MinimumSize.Height) return;

                    Rectangle bounds = new Rectangle(x, y, w, h);
                    Screen[] screens = Screen.AllScreens;
                    bool visible = false;
                    for (int i = 0; i < screens.Length; i++)
                    {
                        if (screens[i].WorkingArea.IntersectsWith(bounds)) { visible = true; break; }
                    }
                    if (!visible) return;

                    StartPosition = FormStartPosition.Manual;
                    Bounds = bounds;
                }
            }
            catch (Exception)
            {
                // a bad stored value just keeps the default position
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_GETMINMAXINFO)
            {
                // a borderless window would otherwise maximize over the taskbar
                Rectangle area = Screen.FromHandle(Handle).WorkingArea;
                MINMAXINFO info = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
                info.ptMaxPosition.x = area.Left;
                info.ptMaxPosition.y = area.Top;
                info.ptMaxSize.x = area.Width;
                info.ptMaxSize.y = area.Height;
                info.ptMinTrackSize.x = MinimumSize.Width;
                info.ptMinTrackSize.y = MinimumSize.Height;
                Marshal.StructureToPtr(info, m.LParam, true);
                m.Result = IntPtr.Zero;
                return;
            }

            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                // the browser covers the client area; only the padding strip is reachable here
                int packed = m.LParam.ToInt32();
                Point p = PointToClient(new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF)));
                bool left = p.X < ResizeBorder;
                bool right = p.X >= ClientSize.Width - ResizeBorder;
                bool top = p.Y < ResizeBorder;
                bool bottom = p.Y >= ClientSize.Height - ResizeBorder;
                if (top && left) m.Result = (IntPtr)HTTOPLEFT;
                else if (top && right) m.Result = (IntPtr)HTTOPRIGHT;
                else if (bottom && left) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (bottom && right) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (left) m.Result = (IntPtr)HTLEFT;
                else if (right) m.Result = (IntPtr)HTRIGHT;
                else if (top) m.Result = (IntPtr)HTTOP;
                else if (bottom) m.Result = (IntPtr)HTBOTTOM;
                return;
            }

            base.WndProc(ref m);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Padding = WindowState == FormWindowState.Maximized ? Padding.Empty : new Padding(ResizeBorder);
            InvokeScript("onWindowState", WindowState == FormWindowState.Maximized ? "1" : "");
        }

        /// <summary>Starts the native window move loop (called from the page's title bar).</summary>
        public void BeginDrag()
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }

        public void ToggleMaximize()
        {
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsPath))
                {
                    if (key != null)
                    {
                        Rectangle r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                        key.SetValue("WindowBounds", r.X + "," + r.Y + "," + r.Width + "," + r.Height);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        /// <summary>Calls a JavaScript function on the UI thread (safe from worker threads).</summary>
        public void InvokeScript(string function, params object[] args)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (IsDisposed) return;
                    try
                    {
                        if (_browser.Document != null) _browser.Document.InvokeScript(function, args);
                    }
                    catch (Exception)
                    {
                        // the page may have navigated away; nothing to update
                    }
                }));
            }
            catch (Exception)
            {
                // window closing
            }
        }
    }
}
