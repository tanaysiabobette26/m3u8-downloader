using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace M3u8Downloader
{
    public class DownloadProgress
    {
        public double Percent;      // 0..100, or -1 for "status only"
        public string Status = "";  // Downloading / Merging / Done ...
        public string Detail = "";  // e.g. "45/181 segments"
    }

    public class DownloadResult
    {
        public bool Success;
        public string OutputFile;
        public string Message = "";
    }

    internal static class DownloadEngine
    {
        public static string BinDir => Path.Combine(Installer.InstallDir, "bin");
        public static string NreExe => Path.Combine(BinDir, "N_m3u8DL-RE.exe");
        public static string FfmpegExe => Path.Combine(BinDir, "ffmpeg.exe");

        public static void EnsureTools()
        {
            Directory.CreateDirectory(BinDir);
            ExtractResource("N_m3u8DL-RE.exe", NreExe);
            ExtractResource("ffmpeg.exe", FfmpegExe);
        }

        static void ExtractResource(string resourceName, string destPath)
        {
            try
            {
                if (File.Exists(destPath) && new FileInfo(destPath).Length > 0) return;
            }
            catch { }

            var asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(resourceName))
            {
                if (s == null)
                    throw new FileNotFoundException("Embedded tool missing: " + resourceName);
                string tmp = destPath + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    s.CopyTo(fs);
                if (File.Exists(destPath)) File.Delete(destPath);
                File.Move(tmp, destPath);
            }
        }

        static readonly Regex ProgressRx =
            new Regex(@"(\d+)/(\d+)\s+(\d+(?:\.\d+)?)%", RegexOptions.Compiled);

        public static async Task<DownloadResult> DownloadAsync(
            string url, string outDir, string saveName,
            IProgress<DownloadProgress> progress, CancellationToken ct)
        {
            EnsureTools();
            Directory.CreateDirectory(outDir);

            string args =
                "\"" + url + "\"" +
                " --save-dir \"" + outDir + "\"" +
                " --save-name \"" + saveName + "\"" +
                " --ffmpeg-binary-path \"" + FfmpegExe + "\"" +
                " --auto-select true" +
                " --no-ansi-color true" +
                " -M format=mp4";

            var psi = new ProcessStartInfo
            {
                FileName = NreExe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = BinDir
            };

            var tail = new StringBuilder();
            string lastStatus = "Starting";

            using (var proc = new Process { StartInfo = psi })
            {
                try { proc.Start(); }
                catch (Exception ex)
                {
                    return new DownloadResult { Success = false, Message = "Could not start downloader: " + ex.Message };
                }

                // drain stderr on the side so it can never deadlock stdout
                var errTask = Task.Run(async () =>
                {
                    string line;
                    while ((line = await proc.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
                        AppendTail(tail, line);
                });

                var buf = new char[8192];
                var sb = new StringBuilder();
                var stdout = proc.StandardOutput;
                int n;
                while ((n = await stdout.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
                {
                    if (ct.IsCancellationRequested) { TryKill(proc); break; }
                    for (int i = 0; i < n; i++)
                    {
                        char c = buf[i];
                        if (c == '\r' || c == '\n')
                        {
                            if (sb.Length > 0)
                            {
                                HandleLine(sb.ToString(), ref lastStatus, tail, progress);
                                sb.Clear();
                            }
                        }
                        else sb.Append(c);
                    }
                }
                if (sb.Length > 0) HandleLine(sb.ToString(), ref lastStatus, tail, progress);

                try { await errTask.ConfigureAwait(false); } catch { }

                if (ct.IsCancellationRequested)
                {
                    TryKill(proc);
                    return new DownloadResult { Success = false, Message = "Cancelled" };
                }

                try { await proc.WaitForExitAsync().ConfigureAwait(false); } catch { }

                string outFile = Path.Combine(outDir, saveName + ".mp4");
                if (proc.ExitCode == 0 && File.Exists(outFile))
                {
                    CleanupTemp(outDir, saveName);
                    progress?.Report(new DownloadProgress { Percent = 100, Status = "Done" });
                    return new DownloadResult { Success = true, OutputFile = outFile, Message = "Saved" };
                }

                string alt = FindProduced(outDir, saveName);
                if (proc.ExitCode == 0 && alt != null)
                {
                    progress?.Report(new DownloadProgress { Percent = 100, Status = "Done" });
                    return new DownloadResult { Success = true, OutputFile = alt, Message = "Saved" };
                }

                return new DownloadResult
                {
                    Success = false,
                    Message = "Download failed (exit " + proc.ExitCode + ").\n\n" + tail.ToString()
                };
            }
        }

        // remove the intermediate merged .ts that is left next to the final mp4
        static void CleanupTemp(string outDir, string saveName)
        {
            try
            {
                string ts = Path.Combine(outDir, saveName + ".ts");
                if (File.Exists(ts)) File.Delete(ts);
            }
            catch { }
        }

        static string FindProduced(string outDir, string saveName)
        {
            try
            {
                foreach (var ext in new[] { ".mp4", ".mkv", ".ts", ".m4a", ".mov" })
                {
                    string p = Path.Combine(outDir, saveName + ext);
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            return null;
        }

        static void HandleLine(string line, ref string lastStatus,
            StringBuilder tail, IProgress<DownloadProgress> progress)
        {
            AppendTail(tail, line);

            string status = lastStatus;
            if (Has(line, "Muxing") || Has(line, "Binary merg") || Has(line, "merging") || Has(line, "Merging"))
                status = "Merging";
            else if (Has(line, "Start downloading") || Has(line, "Downloading"))
                status = "Downloading";

            var m = ProgressRx.Match(line);
            if (m.Success)
            {
                double pct;
                double.TryParse(m.Groups[3].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out pct);
                lastStatus = status;
                progress?.Report(new DownloadProgress
                {
                    Percent = pct,
                    Status = status,
                    Detail = m.Groups[1].Value + "/" + m.Groups[2].Value + " segments"
                });
            }
            else if (!string.Equals(status, lastStatus, StringComparison.Ordinal))
            {
                lastStatus = status;
                progress?.Report(new DownloadProgress { Percent = -1, Status = status });
            }
        }

        static bool Has(string line, string token)
        {
            return line.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void AppendTail(StringBuilder tail, string line)
        {
            lock (tail)
            {
                tail.AppendLine(line);
                if (tail.Length > 8000) tail.Remove(0, tail.Length - 8000);
            }
        }

        static void TryKill(Process p)
        {
            try { if (!p.HasExited) p.Kill(true); } catch { }
        }
    }
}
