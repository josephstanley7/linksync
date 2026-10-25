using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LinkSync.Desktop.Views;

namespace LinkSync.Desktop.Services
{
    public class DesktopScreenRecorder
    {
        [DllImport("ntdll.dll", EntryPoint = "NtSuspendProcess", SetLastError = true)]
        private static extern uint NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll", EntryPoint = "NtResumeProcess", SetLastError = true)]
        private static extern uint NtResumeProcess(IntPtr processHandle);

        private readonly SyncServer _server;
        private readonly SystemTrayManager _trayManager;
        private readonly string _saveDirectory;

        private Process? _ffmpegProcess;
        private DispatcherTimer? _recordTimer;
        private DateTime _recordStartTime;
        private TimeSpan _elapsedOffset = TimeSpan.Zero;
        private DateTime _pauseStartTime;
        private string? _currentOutputFile;
        private string? _currentFileName;

        private ScreenRecordingBarWindow? _barWindow;
        private RecordingBorderWindow? _borderWindow;

        public bool IsRecording { get; private set; }
        public bool IsPaused { get; private set; }

        public event Action<TimeSpan>? Tick;
        public event Action<bool, bool>? StateChanged; // (isRecording, isPaused)
        public event Action<string>? RecordingCompleted;

        public DesktopScreenRecorder(SyncServer server, SystemTrayManager trayManager)
        {
            _server = server;
            _trayManager = trayManager;
            _saveDirectory = server.SaveDirectory;
            Directory.CreateDirectory(_saveDirectory);
        }

        public bool StartRecording(Rect? bounds = null)
        {
            if (IsRecording) return false;

            if (!FfmpegVideoService.IsFfmpegAvailable())
            {
                MessageBox.Show(
                    "FFmpeg is not detected in your system PATH.\nPlease verify FFmpeg installation to record your screen.",
                    "LinkSync Screen Recording",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            int x, y, width, height;

            if (bounds.HasValue && bounds.Value.Width > 16 && bounds.Value.Height > 16)
            {
                x = (int)Math.Max(0, bounds.Value.Left);
                y = (int)Math.Max(0, bounds.Value.Top);
                width = (int)bounds.Value.Width;
                height = (int)bounds.Value.Height;
            }
            else
            {
                x = (int)Math.Max(0, SystemParameters.VirtualScreenLeft);
                y = (int)Math.Max(0, SystemParameters.VirtualScreenTop);
                width = (int)SystemParameters.VirtualScreenWidth;
                height = (int)SystemParameters.VirtualScreenHeight;
            }

            // H.264 / yuv420p requires even width and height
            width = (width / 2) * 2;
            height = (height / 2) * 2;
            if (width < 16) width = 16;
            if (height < 16) height = 16;

            _currentFileName = $"ScreenRecording_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
            _currentOutputFile = Path.Combine(_saveDirectory, _currentFileName);

            // Replicate ShareX gdigrab encoding pipeline optimized for Intel QuickSync / Ivy Bridge
            string ffmpegArgs = $"-y -rtbufsize 150M -f gdigrab -framerate 30 -offset_x {x} -offset_y {y} -video_size {width}x{height} -draw_mouse 1 -i desktop -c:v libx264 -preset ultrafast -tune zerolatency -pix_fmt yuv420p -movflags +faststart \"{_currentOutputFile}\"";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = ffmpegArgs,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                _ffmpegProcess = new Process { StartInfo = psi };
                _ffmpegProcess.ErrorDataReceived += (s, e) => { };
                _ffmpegProcess.OutputDataReceived += (s, e) => { };
                _ffmpegProcess.Start();
                _ffmpegProcess.BeginErrorReadLine();
                _ffmpegProcess.BeginOutputReadLine();

                IsRecording = true;
                IsPaused = false;
                _elapsedOffset = TimeSpan.Zero;
                _recordStartTime = DateTime.Now;

                // Start tick timer
                _recordTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _recordTimer.Tick += (s, e) =>
                {
                    if (IsRecording && !IsPaused)
                    {
                        var totalElapsed = (DateTime.Now - _recordStartTime) + _elapsedOffset;
                        Tick?.Invoke(totalElapsed);
                    }
                };
                _recordTimer.Start();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    // If capturing sub-region, display animated dashed border around region
                    if (bounds.HasValue && (width < SystemParameters.VirtualScreenWidth - 40 || height < SystemParameters.VirtualScreenHeight - 40))
                    {
                        _borderWindow = new RecordingBorderWindow(bounds.Value.Left, bounds.Value.Top, bounds.Value.Width, bounds.Value.Height);
                        _borderWindow.Show();
                    }

                    // Show ShareX-style floating recording toolbar
                    _barWindow = new ScreenRecordingBarWindow(this, width, height);
                    _barWindow.Show();
                });

                StateChanged?.Invoke(IsRecording, IsPaused);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to start desktop screen recording: {ex.Message}");
                MessageBox.Show($"Failed to start screen recording: {ex.Message}", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Error);
                Cleanup();
                return false;
            }
        }

        public void TogglePause()
        {
            if (!IsRecording || _ffmpegProcess == null || _ffmpegProcess.HasExited) return;

            if (IsPaused)
            {
                // Resume
                try
                {
                    NtResumeProcess(_ffmpegProcess.Handle);
                }
                catch { }

                _recordStartTime = DateTime.Now;
                IsPaused = false;
            }
            else
            {
                // Pause
                _elapsedOffset += (DateTime.Now - _recordStartTime);
                _pauseStartTime = DateTime.Now;

                try
                {
                    NtSuspendProcess(_ffmpegProcess.Handle);
                }
                catch { }

                IsPaused = true;
            }

            StateChanged?.Invoke(IsRecording, IsPaused);
        }

        public async Task StopRecordingAsync()
        {
            if (!IsRecording) return;

            IsRecording = false;
            IsPaused = false;

            _recordTimer?.Stop();
            _recordTimer = null;

            CloseOverlays();

            string? finalFile = _currentOutputFile;
            string? fileName = _currentFileName;

            if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
            {
                try
                {
                    // Resume process if it was paused before writing 'q'
                    NtResumeProcess(_ffmpegProcess.Handle);
                }
                catch { }

                try
                {
                    // Send 'q' to FFmpeg standard input to flush moov atom and close cleanly
                    await _ffmpegProcess.StandardInput.WriteLineAsync("q");
                    await _ffmpegProcess.StandardInput.FlushAsync();
                    _ffmpegProcess.StandardInput.Close();

                    // Wait up to 10 seconds for graceful file finalization
                    await Task.Run(() => _ffmpegProcess.WaitForExit(10000));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error stopping FFmpeg cleanly: {ex.Message}");
                }

                if (!_ffmpegProcess.HasExited)
                {
                    try { _ffmpegProcess.Kill(); } catch { }
                }
            }

            Cleanup();

            if (!string.IsNullOrEmpty(finalFile) && File.Exists(finalFile))
            {
                var fileInfo = new FileInfo(finalFile);
                if (fileInfo.Length > 1024)
                {
                    // ShareX Pipeline:
                    // 1. Copy video file to Windows Clipboard (FileDropList)
                    FfmpegVideoService.CopyFileToClipboard(finalFile);

                    // 2. Automatically launch VideoSuiteWindow in Preview Mode so user can preview, trim, and sync
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var suite = new VideoSuiteWindow(finalFile, isPreviewMode: true, server: _server);
                        suite.Show();
                        suite.Activate();
                    });

                    _trayManager.ShowBalloon(
                        "🎥 Screen Recording Complete",
                        "Preview your video to trim or sync to phone.");

                    RecordingCompleted?.Invoke(finalFile);
                    return;
                }
            }

            MessageBox.Show("Screen recording could not be finalized. Please ensure the desktop is active and unlocked.", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void AbortRecording()
        {
            if (!IsRecording) return;

            IsRecording = false;
            IsPaused = false;

            _recordTimer?.Stop();
            _recordTimer = null;

            CloseOverlays();

            if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
            {
                try { _ffmpegProcess.Kill(); } catch { }
            }

            if (!string.IsNullOrEmpty(_currentOutputFile) && File.Exists(_currentOutputFile))
            {
                try { File.Delete(_currentOutputFile); } catch { }
            }

            Cleanup();
            _trayManager.ShowBalloon("Screen Recording Aborted", "Recording was discarded.");
        }

        private void CloseOverlays()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    _borderWindow?.Close();
                    _borderWindow = null;
                }
                catch { }

                try
                {
                    _barWindow?.Close();
                    _barWindow = null;
                }
                catch { }
            });
        }

        private void Cleanup()
        {
            try
            {
                _ffmpegProcess?.Dispose();
                _ffmpegProcess = null;
            }
            catch { }

            CloseOverlays();
            StateChanged?.Invoke(false, false);
        }
    }
}
