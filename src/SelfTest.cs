using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace M3u8Downloader
{
    // Headless verification helpers (no desktop needed). Output is teed to a
    // file in %TEMP% so it can be read back reliably even though a WinExe has
    // no console of its own.
    //   --selftest       fast offline wiring check  -> %TEMP%\m3u8dl_selftest.log
    //   --e2e <m3u8 url> real download, prints result -> %TEMP%\m3u8dl_e2e.log
    internal static class SelfTest
    {
        static StreamWriter _w;

        static void Open(string file)
        {
            try
            {
                _w = new StreamWriter(Path.Combine(Path.GetTempPath(), file), false) { AutoFlush = true };
            }
            catch { _w = null; }
        }

        static void Line(string s)
        {
            try { Console.WriteLine(s); } catch { }
            try { _w?.WriteLine(s); } catch { }
        }

        static void Close() { try { _w?.Flush(); _w?.Dispose(); } catch { } _w = null; }

        public static int Run()
        {
            Open("m3u8dl_selftest.log");
            int pass = 0, fail = 0;
            void Check(string name, bool ok)
            {
                if (ok) { pass++; Line("  OK   " + name); }
                else { fail++; Line(" FAIL  " + name); }
            }

            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var s1 = asm.GetManifestResourceStream("N_m3u8DL-RE.exe"))
                    Check("embedded N_m3u8DL-RE present", s1 != null && s1.Length > 1_000_000);
                using (var s2 = asm.GetManifestResourceStream("ffmpeg.exe"))
                    Check("embedded ffmpeg present", s2 != null && s2.Length > 1_000_000);

                try
                {
                    DownloadEngine.EnsureTools();
                    Check("extract N_m3u8DL-RE.exe",
                        File.Exists(DownloadEngine.NreExe) && new FileInfo(DownloadEngine.NreExe).Length > 0);
                    Check("extract ffmpeg.exe",
                        File.Exists(DownloadEngine.FfmpegExe) && new FileInfo(DownloadEngine.FfmpegExe).Length > 0);
                }
                catch (Exception ex) { Check("extract tools (" + ex.Message + ")", false); }

                Check("install dir resolves", !string.IsNullOrEmpty(Installer.InstallDir));
                Check("installed exe path", Installer.InstalledExePath.EndsWith("M3u8Downloader.exe"));

                Line("");
                Line(fail == 0
                    ? ("SELFTEST_OK (" + pass + " checks)")
                    : ("SELFTEST_FAIL (" + fail + " failed, " + pass + " passed)"));
            }
            catch (Exception ex) { Line("SELFTEST_EXCEPTION " + ex); fail++; }

            Close();
            return fail == 0 ? 0 : 1;
        }

        public static int RunE2E(string url)
        {
            Open("m3u8dl_e2e.log");
            if (string.IsNullOrWhiteSpace(url))
            {
                Line("E2E: no url given (usage: --e2e <m3u8 url>)");
                Close();
                return 2;
            }
            try
            {
                string outDir = Path.Combine(Path.GetTempPath(), "m3u8_e2e");
                string name = "e2e_test";
                int last = -1;
                var prog = new Progress<DownloadProgress>(p =>
                {
                    if (p.Percent < 0) { Line("  [" + p.Status + "]"); return; }
                    int ip = (int)p.Percent;
                    if (ip != last && ip % 10 == 0)
                    {
                        last = ip;
                        Line("  " + p.Status + " " + ip + "%  " + p.Detail);
                    }
                });

                var res = DownloadEngine
                    .DownloadAsync(url, outDir, name, prog, CancellationToken.None)
                    .GetAwaiter().GetResult();

                if (res.Success)
                {
                    long sz = new FileInfo(res.OutputFile).Length;
                    Line("E2E_OK file=" + res.OutputFile + " size=" + sz);
                    Close();
                    return 0;
                }
                Line("E2E_FAIL " + res.Message);
                Close();
                return 1;
            }
            catch (Exception ex)
            {
                Line("E2E_EXCEPTION " + ex.Message);
                Close();
                return 1;
            }
        }
    }
}
