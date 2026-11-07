using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AppsManager
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            EnableBrowserEmulation();

            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                ShowError(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                ShowError(e.ExceptionObject as Exception);
            };

            Application.Run(new MainForm());
        }

        /// <summary>
        /// The WebBrowser control defaults to IE7 mode, where JSON does not exist. Windows 10
        /// and 11 provide IE11 emulation; the page itself still uses CSS 2.1 and ES3.
        /// </summary>
        private static void EnableBrowserEmulation()
        {
            const int mode = 11000;
            SetBrowserEmulation(RegistryView.Default, mode);
            if (Environment.Is64BitOperatingSystem)
            {
                // a 32-bit exe on 64-bit Windows writes the 32-bit view; cover both
                SetBrowserEmulation(RegistryView.Registry64, mode);
                SetBrowserEmulation(RegistryView.Registry32, mode);
            }
        }

        private static void SetBrowserEmulation(RegistryView view, int mode)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
                using (RegistryKey key = baseKey.CreateSubKey(
                    @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION"))
                {
                    if (key != null)
                    {
                        key.SetValue(Path.GetFileName(Application.ExecutablePath), mode, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception)
            {
                // locked-down machine: the page still works in whatever mode the control picks
            }
        }

        private static void ShowError(Exception ex)
        {
            if (ex == null) return;
            try
            {
                MessageBox.Show("Unexpected error:\r\n\r\n" + ex.Message + "\r\n\r\n" + ex.StackTrace,
                    "Apps Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception)
            {
            }
        }
    }
}
