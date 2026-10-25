using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LinkSync.Desktop.Services;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace LinkSync.Desktop
{
    public partial class VideoSuiteWindow : Window
    {
        private string _videoPath = string.Empty;
        private TimeSpan _duration = TimeSpan.Zero;
        private TimeSpan _startTime = TimeSpan.Zero;
        private TimeSpan _endTime = TimeSpan.Zero;
        private readonly DispatcherTimer _timer;
        private bool _isUserSeeking = false;
        private bool _isPlaying = false;
        private string _lastExportedGif = string.Empty;
        private readonly SyncServer? _server;
        private bool _isPreviewMode;

        public VideoSuiteWindow(string? videoPath = null, bool isPreviewMode = false, SyncServer? server = null)
        {
            InitializeComponent();
            _isPreviewMode = isPreviewMode;
            _server = server ?? MainWindow.Instance?.SyncServer;

            if (_isPreviewMode)
            {
                PanelPreviewBar.Visibility = Visibility.Visible;
            }

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _timer.Tick += Timer_Tick;

            if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
            {
                LoadVideo(videoPath);
            }

            Closed += (s, e) =>
            {
                _timer.Stop();
                Player.Close();
            };
        }

        private async void BtnSaveAndSyncPhone_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_videoPath) || !File.Exists(_videoPath)) return;
            BtnSaveAndSyncPhone.IsEnabled = false;

            string targetFile = _videoPath;
            if (_startTime > TimeSpan.FromMilliseconds(500) || (_duration - _endTime) > TimeSpan.FromMilliseconds(500))
            {
                TxtStatus.Text = "Trimming video...";
                string trimmedFile = Path.Combine(Path.GetDirectoryName(_videoPath)!, $"{Path.GetFileNameWithoutExtension(_videoPath)}_trimmed.mp4");
                var cutDuration = _endTime - _startTime;
                bool success = await FfmpegVideoService.LosslessTrimAsync(_videoPath, trimmedFile, _startTime, cutDuration);
                if (success && File.Exists(trimmedFile))
                {
                    try { File.Delete(_videoPath); } catch { }
                    targetFile = trimmedFile;
                }
            }

            FfmpegVideoService.CopyFileToClipboard(targetFile);

            MainWindow.Instance?.MediaItems.Insert(0, new MediaItemViewModel
            {
                FilePath = targetFile,
                FileName = Path.GetFileName(targetFile),
                TimestampText = DateTime.Now.ToString("MMM dd, HH:mm:ss")
            });

            if (_server != null)
            {
                TxtStatus.Text = "Syncing video to phone...";
                try
                {
                    byte[] bytes = await File.ReadAllBytesAsync(targetFile);
                    await _server.SendFileAsync(Path.GetFileName(targetFile), bytes, "video/mp4");
                    TxtStatus.Text = "✓ Saved and synced to Galaxy A17!";
                }
                catch (Exception ex)
                {
                    TxtStatus.Text = $"Sync error: {ex.Message}";
                }
            }
            else
            {
                TxtStatus.Text = "✓ Saved locally and copied to clipboard!";
            }

            PanelPreviewBar.Visibility = Visibility.Collapsed;
            MessageBox.Show("Video saved to PC and synced to your Galaxy A17!", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnSaveLocalOnly_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_videoPath) || !File.Exists(_videoPath)) return;
            BtnSaveLocalOnly.IsEnabled = false;

            string targetFile = _videoPath;
            if (_startTime > TimeSpan.FromMilliseconds(500) || (_duration - _endTime) > TimeSpan.FromMilliseconds(500))
            {
                string trimmedFile = Path.Combine(Path.GetDirectoryName(_videoPath)!, $"{Path.GetFileNameWithoutExtension(_videoPath)}_trimmed.mp4");
                var cutDuration = _endTime - _startTime;
                bool success = await FfmpegVideoService.LosslessTrimAsync(_videoPath, trimmedFile, _startTime, cutDuration);
                if (success && File.Exists(trimmedFile))
                {
                    try { File.Delete(_videoPath); } catch { }
                    targetFile = trimmedFile;
                }
            }

            FfmpegVideoService.CopyFileToClipboard(targetFile);
            MainWindow.Instance?.MediaItems.Insert(0, new MediaItemViewModel
            {
                FilePath = targetFile,
                FileName = Path.GetFileName(targetFile),
                TimestampText = DateTime.Now.ToString("MMM dd, HH:mm:ss")
            });

            PanelPreviewBar.Visibility = Visibility.Collapsed;
            TxtStatus.Text = "✓ Video saved locally and copied to clipboard!";
            MessageBox.Show("Video saved locally to your PC and copied to clipboard!", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnDiscardRecording_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show(
                "Are you sure you want to discard and permanently delete this recording?",
                "Discard Recording",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                Player.Close();
                try
                {
                    if (!string.IsNullOrEmpty(_videoPath) && File.Exists(_videoPath))
                    {
                        File.Delete(_videoPath);
                    }
                }
                catch { }
                Close();
            }
        }

        public void LoadVideo(string path)
        {
            if (!File.Exists(path)) return;

            _videoPath = path;
            TxtVideoFile.Text = $"{Path.GetFileName(path)} ({path})";
            Player.Source = new Uri(path);
            Player.Play();
            _isPlaying = true;
            _timer.Start();
            TxtStatus.Text = $"Loaded: {Path.GetFileName(path)}";
        }

        private void Player_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (Player.NaturalDuration.HasTimeSpan)
            {
                _duration = Player.NaturalDuration.TimeSpan;
                _startTime = TimeSpan.Zero;
                _endTime = _duration;

                SliderTimeline.Maximum = _duration.TotalMilliseconds;
                SliderTimeline.Value = 0;

                TxtStartTime.Text = FormatTime(_startTime);
                TxtEndTime.Text = FormatTime(_endTime);
                TxtSelectedDuration.Text = $"Selected Duration: {_duration.TotalSeconds:F2}s";
                UpdateTimeDisplay();
            }
        }

        private void Player_MediaEnded(object sender, RoutedEventArgs e)
        {
            Player.Position = _startTime;
            Player.Play();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (!_isUserSeeking && _isPlaying)
            {
                SliderTimeline.Value = Player.Position.TotalMilliseconds;
                UpdateTimeDisplay();

                // Loop playback inside selected trim range
                if (_endTime > _startTime && Player.Position >= _endTime)
                {
                    Player.Position = _startTime;
                }
            }
        }

        private void UpdateTimeDisplay()
        {
            TxtTimeReadout.Text = $"{FormatTime(Player.Position)} / {FormatTime(_duration)}";
        }

        private static string FormatTime(TimeSpan ts)
        {
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 100:D1}";
        }

        #region Transport Controls

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlaying)
            {
                Player.Pause();
                _isPlaying = false;
                BtnPlayPause.Content = "▶ Play";
            }
            else
            {
                Player.Play();
                _isPlaying = true;
                BtnPlayPause.Content = "⏸ Pause";
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            Player.Stop();
            Player.Position = _startTime;
            _isPlaying = false;
            BtnPlayPause.Content = "▶ Play";
            SliderTimeline.Value = _startTime.TotalMilliseconds;
            UpdateTimeDisplay();
        }

        private void SliderTimeline_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isUserSeeking = true;
        }

        private void SliderTimeline_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isUserSeeking = false;
            Player.Position = TimeSpan.FromMilliseconds(SliderTimeline.Value);
            UpdateTimeDisplay();
        }

        private void SliderTimeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUserSeeking)
            {
                Player.Position = TimeSpan.FromMilliseconds(SliderTimeline.Value);
                UpdateTimeDisplay();
            }
        }

        private void BtnSetStart_Click(object sender, RoutedEventArgs e)
        {
            _startTime = Player.Position;
            if (_startTime > _endTime) _endTime = _duration;
            TxtStartTime.Text = FormatTime(_startTime);
            TxtSelectedDuration.Text = $"Selected Duration: {(_endTime - _startTime).TotalSeconds:F2}s";
            TxtStatus.Text = $"Set Trim Start: {FormatTime(_startTime)}";
        }

        private void BtnSetEnd_Click(object sender, RoutedEventArgs e)
        {
            _endTime = Player.Position;
            if (_endTime < _startTime) _startTime = TimeSpan.Zero;
            TxtEndTime.Text = FormatTime(_endTime);
            TxtSelectedDuration.Text = $"Selected Duration: {(_endTime - _startTime).TotalSeconds:F2}s";
            TxtStatus.Text = $"Set Trim End: {FormatTime(_endTime)}";
        }

        private void BtnStepBack_Click(object sender, RoutedEventArgs e)
        {
            Player.Position = TimeSpan.FromMilliseconds(Math.Max(0, Player.Position.TotalMilliseconds - 100));
            SliderTimeline.Value = Player.Position.TotalMilliseconds;
            UpdateTimeDisplay();
        }

        private void BtnStepForward_Click(object sender, RoutedEventArgs e)
        {
            Player.Position = TimeSpan.FromMilliseconds(Math.Min(_duration.TotalMilliseconds, Player.Position.TotalMilliseconds + 100));
            SliderTimeline.Value = Player.Position.TotalMilliseconds;
            UpdateTimeDisplay();
        }

        private void BtnRecordNew_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.Dispatcher.Invoke(() =>
            {
                MainWindow.Instance.StartRegionScreenRecording();
            });
        }

        private void BtnOpenVideo_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "MP4 Video Files (*.mp4)|*.mp4|All Video Files (*.*)|*.*",
                Title = "Open Video for Trimming / GIF Export"
            };

            if (ofd.ShowDialog() == true)
            {
                LoadVideo(ofd.FileName);
            }
        }

        #endregion

        #region Trimming & GIF Export

        private async void BtnLosslessCut_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_videoPath) || !File.Exists(_videoPath))
            {
                MessageBox.Show("Please load a video first.", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TimeSpan sliceDuration = _endTime - _startTime;
            if (sliceDuration.TotalMilliseconds <= 100)
            {
                MessageBox.Show("Selected trim duration is too short. Please adjust Start and End points.", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string dir = Path.GetDirectoryName(_videoPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string baseName = Path.GetFileNameWithoutExtension(_videoPath);
            string outputPath = Path.Combine(dir, $"{baseName}_cut_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

            SetBusy(true, "Executing lossless keyframe cut with FFmpeg (-c copy)...");
            var sw = Stopwatch.StartNew();

            bool ok = await FfmpegVideoService.LosslessTrimAsync(_videoPath, outputPath, _startTime, sliceDuration);
            sw.Stop();
            SetBusy(false);

            if (ok && File.Exists(outputPath))
            {
                TxtStatus.Text = $"✅ Trimmed in {sw.ElapsedMilliseconds}ms: {Path.GetFileName(outputPath)}";
                var res = MessageBox.Show(
                    $"Lossless cut completed in {sw.ElapsedMilliseconds} ms (0% CPU re-encode)!\n\nSaved to:\n{outputPath}\n\nWould you like to open the folder?",
                    "LinkSync Video Slicer",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (res == MessageBoxResult.Yes)
                {
                    try { Process.Start("explorer.exe", $"/select,\"{outputPath}\""); } catch { }
                }
            }
            else
            {
                TxtStatus.Text = "❌ Lossless cut failed. Check FFmpeg installation.";
                MessageBox.Show("Lossless trimming failed. Ensure FFmpeg is available.", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnExportGif_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_videoPath) || !File.Exists(_videoPath))
            {
                MessageBox.Show("Please load a video first.", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TimeSpan sliceDuration = _endTime - _startTime;
            if (sliceDuration.TotalMilliseconds <= 100)
            {
                MessageBox.Show("Selected duration is too short for GIF export.", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int width = CmbResolution.SelectedIndex switch
            {
                0 => 720,
                1 => 480,
                2 => 1080,
                _ => 720
            };

            int fps = CmbFps.SelectedIndex switch
            {
                0 => 15,
                1 => 10,
                2 => 24,
                _ => 15
            };

            double speed = CmbSpeed.SelectedIndex switch
            {
                0 => 1.0,
                1 => 1.25,
                2 => 1.5,
                3 => 2.0,
                4 => 0.5,
                _ => 1.0
            };

            string dir = Path.GetDirectoryName(_videoPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string baseName = Path.GetFileNameWithoutExtension(_videoPath);
            string outputPath = Path.Combine(dir, $"{baseName}_{width}p_{fps}fps.gif");

            SetBusy(true, $"Generating high-framerate 2-pass palette GIF ({width}p @ {fps}fps, {speed}x)...");
            var sw = Stopwatch.StartNew();

            bool ok = await FfmpegVideoService.ExportGifAsync(_videoPath, outputPath, _startTime, sliceDuration, fps, width, speed);
            sw.Stop();
            SetBusy(false);

            if (ok && File.Exists(outputPath))
            {
                _lastExportedGif = outputPath;
                FfmpegVideoService.CopyFileToClipboard(outputPath);

                long bytes = new FileInfo(outputPath).Length;
                double mb = bytes / (1024.0 * 1024.0);

                TxtStatus.Text = $"✅ Exported GIF ({mb:F2} MB) in {sw.ElapsedMilliseconds}ms • COPIED TO CLIPBOARD!";
                MessageBox.Show(
                    $"Animated GIF exported successfully ({mb:F2} MB) in {sw.ElapsedMilliseconds} ms!\n\n" +
                    $"✅ Automatically copied to your Windows Clipboard!\n" +
                    $"You can now immediately paste (Ctrl+V) into Telegram, GitHub issues, Slack, or Discord.\n\nSaved to: {outputPath}",
                    "LinkSync GIF Suite",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                TxtStatus.Text = "❌ GIF generation failed.";
                MessageBox.Show("GIF generation failed. Please verify FFmpeg.", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCopyGifClipboard_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastExportedGif) || !File.Exists(_lastExportedGif))
            {
                MessageBox.Show("No GIF has been exported in this session yet.", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            FfmpegVideoService.CopyFileToClipboard(_lastExportedGif);
            TxtStatus.Text = $"📋 Copied {_lastExportedGif} to clipboard!";
            MessageBox.Show("GIF copied to Windows Clipboard!\nReady for Ctrl+V paste.", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnExportSpeedMp4_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_videoPath) || !File.Exists(_videoPath)) return;

            double speed = CmbSpeed.SelectedIndex switch
            {
                0 => 1.0,
                1 => 1.25,
                2 => 1.5,
                3 => 2.0,
                4 => 0.5,
                _ => 1.0
            };

            TimeSpan sliceDuration = _endTime - _startTime;
            string dir = Path.GetDirectoryName(_videoPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string baseName = Path.GetFileNameWithoutExtension(_videoPath);
            string outputPath = Path.Combine(dir, $"{baseName}_{speed:F2}x.mp4");

            SetBusy(true, $"Exporting MP4 at {speed}x speed...");
            bool ok = await FfmpegVideoService.AdjustSpeedAsync(_videoPath, outputPath, _startTime, sliceDuration, speed);
            SetBusy(false);

            if (ok && File.Exists(outputPath))
            {
                TxtStatus.Text = $"✅ Saved {speed}x MP4: {Path.GetFileName(outputPath)}";
                MessageBox.Show($"Exported speed-adjusted MP4:\n{outputPath}", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                TxtStatus.Text = "❌ Speed adjustment failed.";
            }
        }

        private async void BtnCaptureFrame_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_videoPath) || !File.Exists(_videoPath))
            {
                MessageBox.Show("Please load a video first.", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string dir = Path.GetDirectoryName(_videoPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string baseName = Path.GetFileNameWithoutExtension(_videoPath);
            string frameFile = Path.Combine(dir, $"{baseName}_frame_{DateTime.Now:yyyyMMdd_HHmmss}.png");

            SetBusy(true, "Extracting still frame with FFmpeg...");
            bool ok = await FfmpegVideoService.ExtractFrameAsync(_videoPath, frameFile, Player.Position);
            SetBusy(false);

            if (ok && File.Exists(frameFile))
            {
                FfmpegVideoService.CopyFileToClipboard(frameFile);
                TxtStatus.Text = $"📸 Frame copied to clipboard: {Path.GetFileName(frameFile)}";
                MessageBox.Show($"High-resolution frame extracted and copied to Windows Clipboard!\n\nSaved to:\n{frameFile}", "LinkSync Frame Extractor", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                TxtStatus.Text = "❌ Frame extraction failed.";
            }
        }

        private async void BtnAnnotateFrame_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_videoPath) || !File.Exists(_videoPath))
            {
                MessageBox.Show("Please load a video first.", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string dir = Path.GetDirectoryName(_videoPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string baseName = Path.GetFileNameWithoutExtension(_videoPath);
            string frameFile = Path.Combine(dir, $"{baseName}_frame_{DateTime.Now:yyyyMMdd_HHmmss}.png");

            SetBusy(true, "Extracting frame for annotation...");
            bool ok = await FfmpegVideoService.ExtractFrameAsync(_videoPath, frameFile, Player.Position);
            SetBusy(false);

            if (ok && File.Exists(frameFile))
            {
                var annotator = new ImageAnnotatorWindow(frameFile);
                annotator.Show();
                annotator.Activate();
            }
            else
            {
                TxtStatus.Text = "❌ Frame extraction failed.";
            }
        }

        private void SetBusy(bool busy, string? message = null)
        {
            ProgressWorking.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (message != null) TxtStatus.Text = message;
            IsEnabled = !busy;
        }

        #endregion
    }
}
