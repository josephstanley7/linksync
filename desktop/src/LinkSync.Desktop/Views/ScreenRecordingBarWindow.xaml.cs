using System;
using System.Windows;
using System.Windows.Input;
using LinkSync.Desktop.Services;

namespace LinkSync.Desktop.Views
{
    public partial class ScreenRecordingBarWindow : Window
    {
        private readonly DesktopScreenRecorder _recorder;

        public ScreenRecordingBarWindow(DesktopScreenRecorder recorder, int width, int height)
        {
            InitializeComponent();
            _recorder = recorder;

            TxtResolution.Text = $"{width} × {height}";

            _recorder.Tick += OnRecorderTick;
            _recorder.StateChanged += OnRecorderStateChanged;

            // Position toolbar near bottom center of the screen
            double screenW = SystemParameters.WorkArea.Width;
            double screenH = SystemParameters.WorkArea.Height;
            Left = SystemParameters.WorkArea.Left + (screenW - 320) / 2;
            Top = SystemParameters.WorkArea.Top + screenH - 90;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void OnRecorderTick(TimeSpan duration)
        {
            Dispatcher.Invoke(() =>
            {
                TxtDuration.Text = $"{duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
            });
        }

        private void OnRecorderStateChanged(bool isRecording, bool isPaused)
        {
            Dispatcher.Invoke(() =>
            {
                if (isPaused)
                {
                    BtnPauseResume.Content = "▶ Resume";
                    DotRec.Fill = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#F59E0B")!;
                }
                else
                {
                    BtnPauseResume.Content = "⏸ Pause";
                    DotRec.Fill = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#EF4444")!;
                }
            });
        }

        private void BtnPauseResume_Click(object sender, RoutedEventArgs e)
        {
            _recorder.TogglePause();
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            BtnStop.IsEnabled = false;
            BtnPauseResume.IsEnabled = false;
            BtnAbort.IsEnabled = false;
            await _recorder.StopRecordingAsync();
            Close();
        }

        private void BtnAbort_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show(
                "Are you sure you want to abort and discard this recording?",
                "LinkSync Screen Recording",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                _recorder.AbortRecording();
                Close();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _recorder.Tick -= OnRecorderTick;
            _recorder.StateChanged -= OnRecorderStateChanged;
        }
    }
}
