using System;
using System.Diagnostics;
using System.IO;

namespace M3u8Downloader
{
    // First-run self-install: drop a stable copy into %LOCALAPPDATA% and
    // put a shortcut on the Desktop + Start Menu, so the user never has to
    // hunt for / re-run the raw downloaded exe again.
    internal static class Installer
    {
        public static string InstallDir =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "M3u8Downloader");

        public static string InstalledExePath =>
            Path.Combine(InstallDir, "M3u8Downloader.exe");

        public static string CurrentExePath =>
            Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule.FileName;

        public static void EnsureInstalled()
        {
            Directory.CreateDirectory(InstallDir);

            string current = CurrentExePath;
            bool runningFromInstall = PathEquals(current, InstalledExePath);

            // keep a stable copy next to the extracted tools
            if (!runningFromInstall)
            {
                try { File.Copy(current, InstalledExePath, true); }
                catch { /* locked or already present — fine */ }
            }

            string target = File.Exists(InstalledExePath) ? InstalledExePath : current;

            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Program.AppName + ".lnk"),
                target);
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    Program.AppName + ".lnk"),
                target);
        }

        static bool PathEquals(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        static void CreateShortcut(string lnkPath, string targetExe)
        {
            try
            {
                if (File.Exists(lnkPath)) return; // don't clobber an existing one each launch
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                dynamic shell = Activator.CreateInstance(t);
                dynamic sc = shell.CreateShortcut(lnkPath);
                sc.TargetPath = targetExe;
                sc.WorkingDirectory = Path.GetDirectoryName(targetExe);
                sc.IconLocation = targetExe + ",0";
                sc.Description = "Download video from an .m3u8 link to MP4";
                sc.Save();
            }
            catch { /* shortcut is best-effort, never fatal */ }
        }
    }
}
