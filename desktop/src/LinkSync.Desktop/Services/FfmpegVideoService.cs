using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace LinkSync.Desktop.Services
{
    public class FfmpegVideoService
    {
        public static bool IsFfmpegAvailable()
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = "-version",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                });
                p?.WaitForExit(2000);
                return p?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> LosslessTrimAsync(
            string inputPath,
            string outputPath,
            TimeSpan start,
            TimeSpan duration,
            CancellationToken ct = default)
        {
            if (!File.Exists(inputPath)) throw new FileNotFoundException("Input video not found", inputPath);

            // Fast keyframe seek with -ss before -i and -c copy (completes in ~100-300ms, 0% CPU re-encode)
            string args = $"-y -ss {start:hh\\:mm\\:ss\\.fff} -i \"{inputPath}\" -t {duration:hh\\:mm\\:ss\\.fff} -c copy -avoid_negative_ts make_zero \"{outputPath}\"";

            return await RunFfmpegAsync(args, ct);
        }

        public static async Task<bool> ExportGifAsync(
            string inputPath,
            string outputPath,
            TimeSpan start,
            TimeSpan duration,
            int fps = 15,
            int width = 720,
            double speed = 1.0,
            CancellationToken ct = default)
        {
            if (!File.Exists(inputPath)) throw new FileNotFoundException("Input video not found", inputPath);

            // High-framerate 2-pass palette GIF pipeline with lanczos scaling and bayer dithering
            string speedFilter = Math.Abs(speed - 1.0) > 0.01 
                ? $"setpts={(1.0 / speed).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}*PTS," 
                : string.Empty;

            string filter = $"[0:v]{speedFilter}fps={fps},scale={width}:-1:flags=lanczos,split[s0][s1];[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5";

            string args = $"-y -ss {start:hh\\:mm\\:ss\\.fff} -i \"{inputPath}\" -t {duration:hh\\:mm\\:ss\\.fff} -filter_complex \"{filter}\" \"{outputPath}\"";

            return await RunFfmpegAsync(args, ct);
        }

        public static async Task<bool> AdjustSpeedAsync(
            string inputPath,
            string outputPath,
            TimeSpan start,
            TimeSpan duration,
            double speed,
            CancellationToken ct = default)
        {
            if (!File.Exists(inputPath)) throw new FileNotFoundException("Input video not found", inputPath);

            string ptsVal = (1.0 / speed).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            string atempo = speed.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

            // Hardware H.264 video speed multiplier
            string args = $"-y -ss {start:hh\\:mm\\:ss\\.fff} -i \"{inputPath}\" -t {duration:hh\\:mm\\:ss\\.fff} -filter:v \"setpts={ptsVal}*PTS\" -filter:a \"atempo={atempo}\" -c:v libx264 -preset veryfast -crf 22 \"{outputPath}\"";

            return await RunFfmpegAsync(args, ct);
        }

        public static void CopyFileToClipboard(string filePath)
        {
            if (!File.Exists(filePath)) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    var fileList = new StringCollection { filePath };
                    var dataObject = new DataObject();
                    dataObject.SetFileDropList(fileList);
                    Clipboard.SetDataObject(dataObject, true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Clipboard file copy error: {ex.Message}");
                }
            });
        }

        private static async Task<bool> RunFfmpegAsync(string arguments, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using var process = new Process { StartInfo = psi };
                try
                {
                    process.Start();
                    using var reg = ct.Register(() =>
                    {
                        try { process.Kill(); } catch { }
                    });

                    string errorOutput = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        Debug.WriteLine($"FFmpeg failed with code {process.ExitCode}: {errorOutput}");
                        return false;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"FFmpeg invocation error: {ex.Message}");
                    return false;
                }
            }, ct);
        }
    }
}
