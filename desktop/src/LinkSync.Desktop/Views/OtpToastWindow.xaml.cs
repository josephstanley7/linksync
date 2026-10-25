using System;
using System.Media;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace LinkSync.Desktop.Views
{
    public partial class OtpToastWindow : Window
    {
        private readonly string _otpCode;
        private readonly DispatcherTimer _dismissTimer;

        public OtpToastWindow(string otpCode, string sender, string body)
        {
            InitializeComponent();
            _otpCode = otpCode;

            TxtMessageBody.Text = $"{sender}: {body}";
            TxtButtonLabel.Text = $"Copy Code: {otpCode}";

            Loaded += OnLoaded;

            _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _dismissTimer.Tick += (s, e) =>
            {
                _dismissTimer.Stop();
                Close();
            };
            _dismissTimer.Start();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var workingArea = SystemParameters.WorkArea;
            Left = workingArea.Right - Width - 16;
            Top = workingArea.Bottom - Height - 16;

            try
            {
                SystemSounds.Asterisk.Play();
            }
            catch { }
        }

        private void BtnCopyCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetDataObject(_otpCode, true);
                BtnCopyCode.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
                TxtButtonLabel.Text = "✓ Copied to Clipboard!";
                IconCopy.Text = "\uE73E"; // Checkmark icon

                var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
                closeTimer.Tick += (s, ev) =>
                {
                    closeTimer.Stop();
                    Close();
                };
                closeTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy code: {ex.Message}", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _dismissTimer.Stop();
            Close();
        }
    }
}
