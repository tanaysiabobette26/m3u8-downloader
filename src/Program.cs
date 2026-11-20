using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace M3u8Downloader
{
    internal static class Program
    {
        public const string AppName = "M3u8 Downloader";
        public const string MutexName = "M3u8Downloader_SingleInstance_v1";

        // one shared message id (same string -> same id in every instance)
        public static readonly int WM_SHOWME =
            NativeMethods.RegisterWindowMessage("WM_M3U8DOWNLOADER_SHOWME");

        [STAThread]
        static int Main(string[] args)
        {
            // ---- headless / dev flags (no install, no UI loop) ----
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--selftest")
                {
                    NativeMethods.AttachConsole(-1);
                    return SelfTest.Run();
                }
                if (a == "--e2e")
                {
                    NativeMethods.AttachConsole(-1);
                    string url = (i + 1 < args.Length) ? args[i + 1] : null;
                    return SelfTest.RunE2E(url);
                }
                if (a == "--render")
                {
                    string path = (i + 1 < args.Length) ? args[i + 1] : "ui.png";
                    InitWinForms();
                    RenderToPng(path);
                    return 0;
                }
            }

            // ---- single instance ----
            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // ask the already-running window to come to the front, then quit
                    NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }

                InitWinForms();

                try { Installer.EnsureInstalled(); } catch { /* self-install is best-effort */ }

                Application.Run(new MainForm());
                GC.KeepAlive(mutex);
                return 0;
            }
        }

        static void InitWinForms()
        {
            try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }

        static void RenderToPng(string path)
        {
            using (var f = new MainForm())
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-5000, -5000);
                f.ShowInTaskbar = false;
                f.Show();
                Application.DoEvents();
                Thread.Sleep(450);
                Application.DoEvents();
                using (var bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(path, ImageFormat.Png);
                }
                f.Close();
            }
        }
    }

    internal static class NativeMethods
    {
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern bool AttachConsole(int dwProcessId);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
