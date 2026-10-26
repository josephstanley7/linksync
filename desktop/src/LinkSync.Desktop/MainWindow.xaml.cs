using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using LinkSync.Core;
using LinkSync.Desktop.Services;
using LinkSync.Desktop.Views;

namespace LinkSync.Desktop
{
    public partial class MainWindow : Window
    {
        private readonly SyncServer _server;
        private readonly Win32ClipboardWatcher _clipboardWatcher;
        private readonly AdbTunnelManager _adbManager;
        private readonly DiscoveryBeaconServer _discoveryServer;
        private readonly SystemTrayManager _trayManager = new();
        private readonly PhoneMirrorManager _mirrorManager;
        private ScreenCaptureService _captureService = null!;
        private bool _isExplicitExit = false;

        [DllImport("user32.dll")]
        public static extern bool LockWorkStation();

        private string? _lastReceivedFromPhoneText = null;
        private DateTime _lastReceivedFromPhoneTime = DateTime.MinValue;
        private string? _lastSentToPhoneText = null;
        private DateTime _lastSentToPhoneTime = DateTime.MinValue;
        private bool _autoPasteClipboard = true;

        private const string StartupRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegName = "LinkSync";

        public static MainWindow? Instance { get; private set; }
        public SyncServer SyncServer => _server;

        public ObservableCollection<MediaItemViewModel> MediaItems { get; } = new();
        public ObservableCollection<ClipboardItemViewModel> ClipboardItems { get; } = new();
        public ObservableCollection<NotificationItemViewModel> Notifications { get; } = new();

        public MainWindow()
        {
            Instance = this;
            App.LogStartup("MainWindow ctor entered");
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                App.LogStartup($"InitializeComponent EXCEPTION: {ex.GetType().Name}: {ex.Message} -> {ex.InnerException?.Message}");
                throw;
            }
            App.LogStartup("InitializeComponent finished");

            LstMedia.ItemsSource = MediaItems;
            LstClipboard.ItemsSource = ClipboardItems;
            LstNotifications.ItemsSource = Notifications;

            Closing += OnWindowClosing;

            _server = new SyncServer(54884);
            _server.DeviceConnected += OnDeviceConnected;
            _server.DeviceDisconnected += OnDeviceDisconnected;
            _server.ClipboardReceived += OnClipboardReceived;
            _server.MediaReceived += OnMediaReceived;
            _server.ContactsReceived += OnContactsReceived;
            _server.SmsListReceived += OnSmsListReceived;
            _server.SmsReceived += OnSmsReceived;
            _server.CallStateChanged += OnCallStateChanged;
            _server.LockPcRequested += () => Dispatcher.Invoke(() => LockWorkStation());
            _server.PhoneNotificationReceived += OnPhoneNotificationReceived;

            _clipboardWatcher = new Win32ClipboardWatcher();
            _clipboardWatcher.ClipboardTextChanged += OnLocalClipboardChanged;

            _adbManager = new AdbTunnelManager(54884);
            _adbManager.PortStatusChanged += OnPortStatusChanged;

            _mirrorManager = new PhoneMirrorManager(_adbManager);
            _mirrorManager.MirrorStatusChanged += OnMirrorStatusChanged;

            _discoveryServer = new DiscoveryBeaconServer(54884, "HP EliteBook Folio 9470M");

            Loaded += (s, e) =>
            {
                App.LogStartup($"Loaded event entered (StartMinimized={App.StartMinimized})");
                InitializeServices();
                SwitchTab(NavTab.Messages);
                App.LogStartup("Loaded event completed");
            };

            Closed += (s, e) =>
            {
                App.LogStartup("Closed event called");
                if (Instance == this) Instance = null;
                _trayManager.Dispose();
                _clipboardWatcher.Dispose();
                _mirrorManager.Dispose();
                _adbManager.Dispose();
                _discoveryServer.Dispose();
                _server.Stop();
            };
        }

        private bool _isServicesInitialized = false;

        public void InitializeServices()
        {
            if (_isServicesInitialized) return;
            _isServicesInitialized = true;

            App.LogStartup("InitializeServices starting");

            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.EnsureHandle();
            App.LogStartup($"Window HWND ensured: {hwnd}");

            _clipboardWatcher.Initialize(this);
            _captureService = new ScreenCaptureService(_server, _trayManager);
            _captureService.CaptureSaved += OnLocalScreenshotCaptured;
            _trayManager.Initialize(this, "LinkSync — Connected: Galaxy A17");
            _trayManager.DoubleClicked += RestoreWindow;
            _trayManager.LeftClicked += RestoreWindow;

            SetupTrayMenu();

            bool serverStarted = _server.Start();
            App.LogStartup($"SyncServer Start result: {serverStarted}");
            if (!serverStarted)
            {
                TxtDeviceName.Text = "Port 54884 In Use (Another instance is running)";
                DotStatus.Fill = (Brush)new BrushConverter().ConvertFrom("#EF4444")!;
            }

            _discoveryServer.Start();

            string wifiIp = AdbTunnelManager.GetLocalWifiIpAddress();
            TxtHeaderSub.Text = $"HP EliteBook Folio 9470M <—> Galaxy A17 | Auto-Discovery Active (Wi-Fi & USB)";

            LoadExistingMediaFiles();

            // Populate Settings Tab controls
            TxtSettingsSaveDir.Text = _server.SaveDirectory;
            TxtSettingsAdbPath.Text = AdbTunnelManager.ResolvedAdbPath ?? "Resolved via PATH";
            ChkRunOnStartup.IsChecked = IsRunOnStartupEnabled();
            ChkAutoPasteClipboard.IsChecked = _autoPasteClipboard;

            // Auto-establish ADB tunnel and watch for device connection
            _adbManager.StartWatchdog(() => _server.IsClientConnected, TimeSpan.FromSeconds(3));
            App.LogStartup("InitializeServices completed successfully");
        }

        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

        internal void TrimWorkingSet()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
            }
            catch { }
        }

        private void OnWindowClosing(object? sender, CancelEventArgs e)
        {
            if (!_isExplicitExit)
            {
                e.Cancel = true;
                Hide();
                TrimWorkingSet();
                _trayManager.ShowBalloon("LinkSync Running in Background", "LinkSync is still syncing your clipboard and media in the background. Double-click the tray icon anytime to reopen.");
            }
        }

        private void SetupTrayMenu()
        {
            var menu = new ContextMenu();

            var itemSnipRegion = new MenuItem { Header = "📸 Take Screenshot (Region)" };
            itemSnipRegion.Click += (s, e) => _captureService.CaptureRegion();
            menu.Items.Add(itemSnipRegion);

            var itemSnipFull = new MenuItem { Header = "🖥️ Take Screenshot (Full Screen)" };
            itemSnipFull.Click += (s, e) => _captureService.CaptureFullScreen();
            menu.Items.Add(itemSnipFull);

            var itemSnipWindow = new MenuItem { Header = "🪟 Take Screenshot (Active Window)" };
            itemSnipWindow.Click += (s, e) => _captureService.CaptureActiveWindow();
            menu.Items.Add(itemSnipWindow);

            menu.Items.Add(new Separator());

            var itemRecordRegion = new MenuItem { Header = "🎥 Record Screen (Region)" };
            itemRecordRegion.Click += (s, e) => _captureService.StartScreenRecordingRegion();
            menu.Items.Add(itemRecordRegion);

            var itemRecordFull = new MenuItem { Header = "🖥️ Record Screen (Full Screen)" };
            itemRecordFull.Click += (s, e) => _captureService.StartScreenRecordingFullScreen();
            menu.Items.Add(itemRecordFull);

            menu.Items.Add(new Separator());

            var itemFolder = new MenuItem { Header = "📂 Open Screenshots Folder" };
            itemFolder.Click += (s, e) => BtnOpenFolder_Click(s, e);
            menu.Items.Add(itemFolder);

            var itemAnnotator = new MenuItem { Header = "✏️ Open Image Annotator" };
            itemAnnotator.Click += (s, e) => BtnOpenAnnotator_Click(s, e);
            menu.Items.Add(itemAnnotator);

            var itemVideo = new MenuItem { Header = "🎬 Open Video Suite" };
            itemVideo.Click += (s, e) => BtnOpenVideoSuite_Click(s, e);
            menu.Items.Add(itemVideo);

            var itemMirror = new MenuItem { Header = "📱 Mirror Phone Screen (60 FPS)" };
            itemMirror.Click += async (s, e) => await StartPhoneMirroringAsync(turnScreenOff: false);
            menu.Items.Add(itemMirror);

            var itemMirrorScreenOff = new MenuItem { Header = "🌙 Mirror Screen-Off (Battery Saver)" };
            itemMirrorScreenOff.Click += async (s, e) => await StartPhoneMirroringAsync(turnScreenOff: true);
            menu.Items.Add(itemMirrorScreenOff);

            menu.Items.Add(new Separator());

            var itemOpen = new MenuItem { Header = "📱 Open LinkSync" };
            itemOpen.Click += (s, e) => RestoreWindow();
            menu.Items.Add(itemOpen);

            var itemStatus = new MenuItem { Header = "🟢 Status: Active (Port 54884)", IsEnabled = false };
            menu.Items.Add(itemStatus);

            menu.Items.Add(new Separator());

            var itemStartup = new MenuItem
            {
                Header = "Start with Windows",
                IsCheckable = true,
                IsChecked = IsRunOnStartupEnabled()
            };
            itemStartup.Click += (s, e) =>
            {
                SetRunOnStartup(itemStartup.IsChecked);
            };
            menu.Items.Add(itemStartup);

            menu.Items.Add(new Separator());

            var itemExit = new MenuItem { Header = "Exit LinkSync" };
            itemExit.Click += (s, e) =>
            {
                _isExplicitExit = true;
                Close();
                Application.Current.Shutdown();
            };
            menu.Items.Add(itemExit);

            _trayManager.SetContextMenu(menu);
        }

        private void OnLocalScreenshotCaptured(string path, byte[] bytes)
        {
            Dispatcher.Invoke(() =>
            {
                var item = new MediaItemViewModel
                {
                    FilePath = path,
                    FileName = Path.GetFileName(path),
                    TimestampText = DateTime.Now.ToString("MMM dd, HH:mm:ss")
                };
                MediaItems.Insert(0, item);
            });
        }

        private bool IsRunOnStartupEnabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupRegKey, false);
                return key?.GetValue(AppRegName) != null;
            }
            catch { return false; }
        }

        private void SetRunOnStartup(bool enable)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupRegKey, true);
                if (key != null)
                {
                    if (enable)
                    {
                        string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            key.SetValue(AppRegName, $"\"{exePath}\" --minimized");
                        }
                    }
                    else
                    {
                        key.DeleteValue(AppRegName, false);
                    }
                }
            }
            catch { }
        }

        private int _wmRestoreMessage;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                _wmRestoreMessage = App.RegisterWindowMessage(App.RestoreMessageName);
                var helper = new WindowInteropHelper(this);
                IntPtr hwnd = helper.EnsureHandle();
                var source = HwndSource.FromHwnd(hwnd);
                source?.AddHook(WndProcHook);
            }
            catch { }
        }

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == _wmRestoreMessage && _wmRestoreMessage != 0)
            {
                App.LogStartup("Received secondary instance restore signal! Restoring window to foreground.");
                RestoreWindow();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void RestoreWindow()
        {
            Dispatcher.Invoke(() =>
            {
                Show();
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }
                Activate();
                Topmost = true;
                Topmost = false;
                Focus();
            });
        }

        private void OnDeviceConnected(DeviceHandshake handshake)
        {
            Dispatcher.Invoke(() =>
            {
                DotStatus.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
                TxtDeviceName.Text = $"Connected: {handshake.DeviceManufacturer} {handshake.DeviceName}";
                TxtDeviceSub.Text = $"Android {handshake.AndroidVersion} • Instant Bridge Active";
                _trayManager.UpdateTooltip($"LinkSync — Connected: {handshake.DeviceManufacturer} {handshake.DeviceName}");

                TxtSidebarDeviceName.Text = $"{handshake.DeviceManufacturer} {handshake.DeviceName}";
                TxtSidebarStatus.Text = "Connected & Syncing";
                DotSidebarStatus.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                DotCompactStatus.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                TxtSidebarBattery.Text = "Connected";
                TxtSidebarNet.Text = "Wi-Fi / USB";

                _ = _server.RequestContactsAsync();
                _ = _server.RequestSmsListAsync();
            });
        }

        private void OnDeviceDisconnected()
        {
            Dispatcher.Invoke(() =>
            {
                DotStatus.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
                TxtDeviceName.Text = "Searching for Phone on Wi-Fi & USB...";
                TxtDeviceSub.Text = "Ensure phone is on same Wi-Fi or plug in USB cable.";
                _trayManager.UpdateTooltip("LinkSync — Searching for Phone on Wi-Fi & USB...");

                TxtSidebarStatus.Text = "Searching for Phone...";
                DotSidebarStatus.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                DotCompactStatus.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                TxtSidebarBattery.Text = "Waiting";
                TxtSidebarNet.Text = "Searching";
            });
        }

        private void OnPortStatusChanged(DevicePortStatus status)
        {
            if (_server.IsClientConnected) return;

            Dispatcher.Invoke(() =>
            {
                switch (status)
                {
                    case DevicePortStatus.ConnectedUnauthorized:
                        DotStatus.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
                        TxtDeviceName.Text = "⚠️ USB Cable Detected: Please Unlock Phone";
                        TxtDeviceSub.Text = "Unlock your phone and tap 'Allow USB debugging' to start syncing.";
                        TxtSidebarStatus.Text = "Unlock phone & tap Allow";
                        break;

                    case DevicePortStatus.ConnectedMtpOnly:
                        DotStatus.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
                        TxtDeviceName.Text = "⚠️ USB Cable Detected: File Transfer / Debugging Needed";
                        TxtDeviceSub.Text = "On your phone, enable File Transfer or USB Debugging in Settings.";
                        TxtSidebarStatus.Text = "Enable USB Debugging";
                        break;

                    case DevicePortStatus.ConnectedAuthorized:
                        DotStatus.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
                        TxtDeviceName.Text = "Searching for Phone on Wi-Fi & USB...";
                        TxtDeviceSub.Text = "USB tunnel active (Port 54884). Waiting for phone app...";
                        TxtSidebarStatus.Text = "USB active. Waiting...";
                        break;

                    case DevicePortStatus.NoDevice:
                    default:
                        DotStatus.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
                        TxtDeviceName.Text = "Searching for Phone on Wi-Fi & USB...";
                        TxtDeviceSub.Text = "Ensure phone is on same Wi-Fi network or plug in USB cable.";
                        TxtSidebarStatus.Text = "Searching for Phone...";
                        break;
                }
            });
        }

        private void OnClipboardReceived(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Record incoming text from phone to suppress reflection echo back
            _lastReceivedFromPhoneText = text;
            _lastReceivedFromPhoneTime = DateTime.UtcNow;

            Dispatcher.Invoke(() =>
            {
                // Robust clipboard setter with retries against external clipboard locks
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        Clipboard.SetDataObject(text, true);
                        break;
                    }
                    catch (COMException)
                    {
                        Thread.Sleep(25);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Clipboard] Error setting clipboard: {ex.Message}");
                        break;
                    }
                }

                ClipboardItems.Insert(0, new ClipboardItemViewModel
                {
                    SourceLabel = "📱 FROM PHONE (GALAXY A17)",
                    SourceColor = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                    Text = text,
                    TimestampText = DateTime.Now.ToString("HH:mm:ss")
                });

                while (ClipboardItems.Count > 50)
                {
                    ClipboardItems.RemoveAt(ClipboardItems.Count - 1);
                }

                Notifications.Insert(0, new NotificationItemViewModel
                {
                    AppName = "Clipboard",
                    AppIconGlyph = "\uE8C8",
                    AppIconColor = "#34D399",
                    Title = "Copied from Phone",
                    Body = text.Length > 60 ? text.Substring(0, 60) + "..." : text,
                    TimestampText = DateTime.Now.ToString("HH:mm"),
                    ActionLabel = "Copy",
                    ActionType = "copy",
                    AssociatedData = text
                });
                while (Notifications.Count > 30) Notifications.RemoveAt(Notifications.Count - 1);
                UpdateNotificationBadgeCounts();

                if (!IsVisible)
                {
                    string preview = text.Length > 40 ? text.Substring(0, 40) + "..." : text;
                    _trayManager.ShowBalloon("📋 Copied from Phone", preview);
                }
            });
        }

        private void OnLocalClipboardChanged(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            // 1. Suppression check: Did we just receive this from Phone within 3.5s?
            if (text == _lastReceivedFromPhoneText && (DateTime.UtcNow - _lastReceivedFromPhoneTime).TotalMilliseconds < 3500)
            {
                return;
            }

            // 2. Debounce duplicate send within 800ms
            if (text == _lastSentToPhoneText && (DateTime.UtcNow - _lastSentToPhoneTime).TotalMilliseconds < 800)
            {
                return;
            }

            _lastSentToPhoneText = text;
            _lastSentToPhoneTime = DateTime.UtcNow;

            _ = _server.SendClipboardAsync(text);

            Dispatcher.Invoke(() =>
            {
                ClipboardItems.Insert(0, new ClipboardItemViewModel
                {
                    SourceLabel = "💻 FROM PC (FOLIO 9470M)",
                    SourceColor = new SolidColorBrush(Color.FromRgb(52, 211, 153)),
                    Text = text,
                    TimestampText = DateTime.Now.ToString("HH:mm:ss")
                });

                while (ClipboardItems.Count > 50)
                {
                    ClipboardItems.RemoveAt(ClipboardItems.Count - 1);
                }
            });
        }

        private void OnMediaReceived(string savedPath, byte[] data)
        {
            Dispatcher.Invoke(() =>
            {
                string ext = Path.GetExtension(savedPath).ToLowerInvariant();
                bool isImage = ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp";
                string fileName = Path.GetFileName(savedPath);

                if (isImage && _autoPasteClipboard)
                {
                    // Copy image directly to Windows Clipboard with retry loop against external app locks!
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        try
                        {
                            using var ms = new MemoryStream(data);
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = ms;
                            bitmap.EndInit();
                            bitmap.Freeze();

                            Clipboard.SetImage(bitmap);
                            break;
                        }
                        catch (COMException)
                        {
                            Thread.Sleep(25);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Clipboard] Error setting image on clipboard: {ex.Message}");
                            break;
                        }
                    }
                }

                MediaItems.Insert(0, new MediaItemViewModel
                {
                    FileName = fileName,
                    TimestampText = $"Received {DateTime.Now:HH:mm:ss} • Saved to Pictures\\LinkSync",
                    FilePath = savedPath
                });

                while (MediaItems.Count > 100)
                {
                    MediaItems.RemoveAt(MediaItems.Count - 1);
                }

                Notifications.Insert(0, new NotificationItemViewModel
                {
                    AppName = isImage ? "Photos" : "Video Suite",
                    AppIconGlyph = isImage ? "\uEB9F" : "\uE714",
                    AppIconColor = isImage ? "#38BDF8" : "#C084FC",
                    Title = isImage ? "Screenshot Received" : "Screen Recording Received",
                    Body = $"{fileName} ({(data.Length > 1024 ? data.Length / 1024 : 1)} KB)",
                    TimestampText = DateTime.Now.ToString("HH:mm"),
                    ActionLabel = "View",
                    ActionType = "view",
                    AssociatedData = savedPath
                });
                while (Notifications.Count > 30) Notifications.RemoveAt(Notifications.Count - 1);
                UpdateNotificationBadgeCounts();

                // Show notification in Windows System Tray!
                if (isImage)
                {
                    _trayManager.ShowBalloon("📸 Screenshot Ready to Paste!", $"{fileName} copied to clipboard. Press Ctrl+V to paste.");
                }
                else
                {
                    _trayManager.ShowBalloon("🎬 Video Synced", $"{fileName} saved to Pictures\\LinkSync.");
                }
            });
        }

        private void LoadExistingMediaFiles()
        {
            try
            {
                if (!Directory.Exists(_server.SaveDirectory)) return;
                var dirInfo = new DirectoryInfo(_server.SaveDirectory);
                var files = dirInfo.GetFiles("*.*")
                    .Where(f => f.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                                f.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                f.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.CreationTime);

                MediaItems.Clear();
                foreach (var file in files)
                {
                    MediaItems.Add(new MediaItemViewModel
                    {
                        FileName = file.Name,
                        TimestampText = $"Created {file.CreationTime:MMM dd, HH:mm} • {file.Length / 1024:N0} KB",
                        FilePath = file.FullName
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load existing media: {ex.Message}");
            }
        }

        private async void BtnAdbTunnel_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await _adbManager.EnsureReverseTunnelAsync();
            if (ok)
            {
                MessageBox.Show("USB Reverse Tunnel established!\n(Phone port 54884 is now bridged to laptop)", "LinkSync ADB Tunnel", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Could not establish reverse tunnel.\nPlease verify USB debugging is authorized and phone is connected.\nADB Path: {AdbTunnelManager.ResolvedAdbPath}", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start("explorer.exe", _server.SaveDirectory);
            }
            catch { }
        }

        private void BtnOpenAnnotator_Click(object sender, RoutedEventArgs e)
        {
            if (LstMedia.SelectedItem is MediaItemViewModel item && !item.IsVideo && File.Exists(item.FilePath))
            {
                OpenAnnotator(item.FilePath);
                return;
            }

            var latestImage = LstMedia.Items.OfType<MediaItemViewModel>().FirstOrDefault(m => !m.IsVideo && File.Exists(m.FilePath));
            if (latestImage != null)
            {
                OpenAnnotator(latestImage.FilePath);
                return;
            }

            var annotator = new ImageAnnotatorWindow();
            annotator.Show();
        }

        private void BtnOpenVideoSuite_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "MP4 Video Files (*.mp4)|*.mp4|All Video Files (*.*)|*.*",
                Title = "Select Screen Recording for Video Suite",
                InitialDirectory = _server.SaveDirectory
            };

            if (ofd.ShowDialog() == true)
            {
                OpenVideoSuite(ofd.FileName);
            }
            else
            {
                // Open empty video suite window allowing drag-and-drop or manual load
                var win = new VideoSuiteWindow();
                win.Show();
            }
        }

        private void LstMedia_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstMedia.SelectedItem is MediaItemViewModel item)
            {
                OpenMediaItem(item);
            }
        }

        private void BtnMediaAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is MediaItemViewModel item)
            {
                OpenMediaItem(item);
            }
        }

        private void OpenMediaItem(MediaItemViewModel item)
        {
            if (string.IsNullOrEmpty(item.FilePath) || !File.Exists(item.FilePath)) return;

            if (item.IsVideo)
            {
                OpenVideoSuite(item.FilePath);
            }
            else
            {
                OpenAnnotator(item.FilePath);
            }
        }

        private void OpenAnnotator(string filePath)
        {
            var annotator = new ImageAnnotatorWindow(filePath);
            annotator.Show();
        }

        private void OpenVideoSuite(string filePath)
        {
            var videoSuite = new VideoSuiteWindow(filePath);
            videoSuite.Show();
        }

        private void BtnRecopyClipboard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ClipboardItemViewModel item)
            {
                try
                {
                    _lastReceivedFromPhoneText = item.Text;
                    _lastReceivedFromPhoneTime = DateTime.UtcNow;
                    Clipboard.SetDataObject(item.Text, true);
                    _trayManager.ShowBalloon("📋 Copied to Clipboard", item.Text.Length > 40 ? item.Text.Substring(0, 40) + "..." : item.Text);
                }
                catch { }
            }
        }

        private void TxtManualClipboard_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string text = TxtManualClipboard.Text.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    _lastSentToPhoneText = text;
                    _lastSentToPhoneTime = DateTime.UtcNow;
                    _ = _server.SendClipboardAsync(text);

                    try { Clipboard.SetDataObject(text, true); } catch { }

                    ClipboardItems.Insert(0, new ClipboardItemViewModel
                    {
                        SourceLabel = "💻 FROM PC (FOLIO 9470M)",
                        SourceColor = new SolidColorBrush(Color.FromRgb(52, 211, 153)),
                        Text = text,
                        TimestampText = DateTime.Now.ToString("HH:mm:ss")
                    });

                    TxtManualClipboard.Text = string.Empty;
                }
            }
        }

        public enum NavTab { Overview, Messages, Calls, Contacts, Photos, Clipboard, Video, Annotator, Mirror, Settings }

        private void SwitchTab(NavTab tab)
        {
            ViewOverview.Visibility = tab == NavTab.Overview ? Visibility.Visible : Visibility.Collapsed;
            ViewMessages.Visibility = tab == NavTab.Messages ? Visibility.Visible : Visibility.Collapsed;
            ViewCalls.Visibility = tab == NavTab.Calls ? Visibility.Visible : Visibility.Collapsed;
            ViewContacts.Visibility = tab == NavTab.Contacts ? Visibility.Visible : Visibility.Collapsed;
            ViewPhotos.Visibility = tab == NavTab.Photos ? Visibility.Visible : Visibility.Collapsed;
            ViewClipboard.Visibility = tab == NavTab.Clipboard ? Visibility.Visible : Visibility.Collapsed;
            ViewVideo.Visibility = tab == NavTab.Video ? Visibility.Visible : Visibility.Collapsed;
            ViewAnnotator.Visibility = tab == NavTab.Annotator ? Visibility.Visible : Visibility.Collapsed;
            ViewMirror.Visibility = tab == NavTab.Mirror ? Visibility.Visible : Visibility.Collapsed;
            ViewSettings.Visibility = tab == NavTab.Settings ? Visibility.Visible : Visibility.Collapsed;

            ResetNavButton(BtnNavOverview, tab == NavTab.Overview);
            ResetNavButton(BtnNavMessages, tab == NavTab.Messages);
            ResetNavButton(BtnNavCalls, tab == NavTab.Calls);
            ResetNavButton(BtnNavContacts, tab == NavTab.Contacts);
            ResetNavButton(BtnNavPhotos, tab == NavTab.Photos);
            ResetNavButton(BtnNavClipboard, tab == NavTab.Clipboard);
            ResetNavButton(BtnNavVideo, tab == NavTab.Video);
            ResetNavButton(BtnNavAnnotator, tab == NavTab.Annotator);
            ResetNavButton(BtnNavMirror, tab == NavTab.Mirror);
            ResetNavButton(BtnNavSettings, tab == NavTab.Settings);

            TxtPageTitle.Text = tab switch
            {
                NavTab.Overview => "Overview & Hardware Continuity",
                NavTab.Messages => "SMS & Message Threads",
                NavTab.Calls => "Phone Calls & Digital Dialer",
                NavTab.Contacts => "Phone Contacts Directory",
                NavTab.Photos => "Photos & Synced Screenshots",
                NavTab.Clipboard => "Universal Clipboard Stream",
                NavTab.Video => "Video Suite & Animated GIF Maker",
                NavTab.Annotator => "ShareX-Grade Image Annotator",
                NavTab.Mirror => "Real-Time Phone Mirror & Remote Control",
                NavTab.Settings => "Settings & System Preferences",
                _ => "LinkSync"
            };

            TxtHeaderSub.Text = tab switch
            {
                NavTab.Overview => "Direct peer-to-peer connection • QuickSync acceleration enabled",
                NavTab.Messages => "Read, compose, and send text messages through your Galaxy A17 SIM",
                NavTab.Calls => "Place calls, view active call status, and hang up remotely",
                NavTab.Contacts => "Synced phonebook with instant contact search and 1-click call/text",
                NavTab.Photos => "Double-click item to edit in Image Annotator or Video Suite",
                NavTab.Clipboard => "Two-way clipboard stream with instant local auto-paste",
                NavTab.Video => "Lossless keyframe video trimmer and Lanczos animated GIF creator",
                NavTab.Annotator => "Arrows, step badges, blur redactions, and Samsung Galaxy frames",
                NavTab.Mirror => "60 FPS low-latency stream with mouse clicks and physical keyboard typing",
                NavTab.Settings => "Storage directories, Windows startup, and hardware decode configuration",
                _ => "HP EliteBook Folio 9470M Node"
            };

            if (tab == NavTab.Messages && _cachedSmsMessages.Count == 0)
            {
                _ = _server.RequestSmsListAsync();
            }
            if (tab == NavTab.Contacts && _cachedContacts.Count == 0)
            {
                _ = _server.RequestContactsAsync();
            }
        }

        private void ResetNavButton(Button? btn, bool isActive)
        {
            if (btn == null) return;
            btn.Background = isActive ? (Brush)FindResource("BrushAccentSubtle") : Brushes.Transparent;
            btn.Foreground = isActive ? (Brush)FindResource("BrushAccentLight") : (Brush)FindResource("BrushTextSecondary");
            btn.FontWeight = isActive ? FontWeights.Bold : FontWeights.SemiBold;
        }

        private bool _isSidebarCollapsed = false;

        private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            _isSidebarCollapsed = !_isSidebarCollapsed;
            ApplySidebarState();
        }

        private void BtnCompactNotif_Click(object sender, RoutedEventArgs e)
        {
            if (_isSidebarCollapsed)
            {
                _isSidebarCollapsed = false;
                ApplySidebarState();
            }
        }

        private void ApplySidebarState()
        {
            if (_isSidebarCollapsed)
            {
                ColSidebar.Width = new GridLength(56);
                PanelSidebarExpanded.Visibility = Visibility.Collapsed;
                PanelSidebarCollapsed.Visibility = Visibility.Visible;
            }
            else
            {
                ColSidebar.Width = new GridLength(280);
                PanelSidebarExpanded.Visibility = Visibility.Visible;
                PanelSidebarCollapsed.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateNotificationBadgeCounts()
        {
            int count = Notifications.Count;
            TxtNotifCount.Text = count > 0 ? $"({count})" : "";
            TxtCompactNotifCount.Text = count > 99 ? "99+" : count.ToString();
            BadgeCompactNotif.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnClearNotifications_Click(object sender, RoutedEventArgs e)
        {
            Notifications.Clear();
            UpdateNotificationBadgeCounts();
        }

        private void OnPhoneNotificationReceived(LinkSyncProtocol.PhoneNotificationPayload notif)
        {
            Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrWhiteSpace(notif.Title) && string.IsNullOrWhiteSpace(notif.Text))
                    return;

                // 1. Detect 2FA OTP verification code
                string combined = $"{notif.Title} {notif.Text}";
                Match otpMatch = Regex.Match(combined, @"\b\d{4,8}\b|G-\d{6}");
                string otpCode = otpMatch.Success ? otpMatch.Value : string.Empty;

                if (!string.IsNullOrEmpty(otpCode))
                {
                    try
                    {
                        var toast = new Views.OtpToastWindow(otpCode, notif.AppName, notif.Text);
                        toast.Show();
                    }
                    catch { }
                }

                // 2. Resolve App Glyph and Color
                string glyph = "\uEA8F"; // Default Bell
                string color = "#38BDF8";
                string actionLabel = "View";
                string actionType = "view";
                string data = notif.Title;

                string pkg = (notif.PackageName ?? "").ToLowerInvariant();
                if (pkg.Contains("whatsapp") || pkg.Contains("telegram") || pkg.Contains("signal") || pkg.Contains("mms") || pkg.Contains("messaging"))
                {
                    glyph = "\uE8BD";
                    color = "#22C55E";
                    actionLabel = "Reply";
                    actionType = "reply";
                }
                else if (pkg.Contains("dialer") || pkg.Contains("telecom") || pkg.Contains("phone"))
                {
                    glyph = "\uE717";
                    color = "#F59E0B";
                    actionLabel = "Call";
                    actionType = "call";
                }
                else if (pkg.Contains("mail") || pkg.Contains("outlook") || pkg.Contains("gmail"))
                {
                    glyph = "\uE715";
                    color = "#3B82F6";
                }
                else if (pkg.Contains("instagram") || pkg.Contains("twitter") || pkg.Contains("facebook"))
                {
                    glyph = "\uE90A";
                    color = "#EC4899";
                }
                else if (!string.IsNullOrEmpty(otpCode))
                {
                    glyph = "\uE8D7";
                    color = "#EAB308";
                    actionLabel = $"Copy {otpCode}";
                    actionType = "copy";
                    data = otpCode;
                }

                // 3. Remove existing notification with same Key
                var existing = Notifications.FirstOrDefault(n => n.Id == notif.Key);
                if (existing != null)
                {
                    Notifications.Remove(existing);
                }

                DateTime postTime = notif.Timestamp > 0 
                    ? DateTimeOffset.FromUnixTimeMilliseconds(notif.Timestamp).LocalDateTime 
                    : DateTime.Now;

                Notifications.Insert(0, new NotificationItemViewModel
                {
                    Id = notif.Key,
                    AppName = string.IsNullOrWhiteSpace(notif.AppName) ? "Phone" : notif.AppName,
                    AppIconGlyph = glyph,
                    AppIconColor = color,
                    Title = notif.Title,
                    Body = notif.Text,
                    TimestampText = postTime.ToString("HH:mm"),
                    ActionLabel = actionLabel,
                    ActionType = actionType,
                    AssociatedData = data
                });

                while (Notifications.Count > 40)
                {
                    Notifications.RemoveAt(Notifications.Count - 1);
                }

                UpdateNotificationBadgeCounts();

                if (!IsActive || WindowState == WindowState.Minimized)
                {
                    _trayManager.ShowBalloon($"📱 {notif.AppName}: {notif.Title}", notif.Text);
                }
            });
        }

        private void NotificationAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is NotificationItemViewModel notif)
            {
                switch (notif.ActionType)
                {
                    case "reply":
                        SwitchTab(NavTab.Messages);
                        if (!string.IsNullOrEmpty(notif.AssociatedData))
                        {
                            TxtSmsSearch.Text = notif.AssociatedData;
                        }
                        break;
                    case "call":
                        SwitchTab(NavTab.Calls);
                        if (!string.IsNullOrEmpty(notif.AssociatedData))
                        {
                            TxtDialNumber.Text = notif.AssociatedData;
                        }
                        break;
                    case "copy":
                        if (!string.IsNullOrEmpty(notif.AssociatedData))
                        {
                            Clipboard.SetDataObject(notif.AssociatedData, true);
                            _trayManager.ShowBalloon("📋 Copied", "Text copied to clipboard");
                        }
                        break;
                    case "view":
                    default:
                        if (notif.AppName.Equals("Photos", StringComparison.OrdinalIgnoreCase))
                        {
                            SwitchTab(NavTab.Photos);
                        }
                        else if (notif.AppName.Equals("Clipboard", StringComparison.OrdinalIgnoreCase))
                        {
                            SwitchTab(NavTab.Clipboard);
                        }
                        else
                        {
                            BtnStartMirror_Click(sender, e);
                        }
                        break;
                }
            }
        }

        private void BtnNavOverview_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Overview);
        private void BtnNavMessages_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Messages);
        private void BtnNavCalls_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Calls);
        private void BtnNavContacts_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Contacts);
        private void BtnNavPhotos_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Photos);
        private void BtnNavClipboard_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Clipboard);
        private void BtnNavVideo_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Video);
        private void BtnNavAnnotator_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Annotator);
        private void BtnNavMirror_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Mirror);
        private void BtnNavSettings_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Settings);

        private void BtnSidebarSnip_Click(object sender, RoutedEventArgs e) => _captureService.CaptureRegion();
        private void BtnQuickSnip_Click(object sender, RoutedEventArgs e) => _captureService.CaptureRegion();
        private void BtnFullscreenSnip_Click(object sender, RoutedEventArgs e) => _captureService.CaptureFullScreen();
        private void BtnWindowSnip_Click(object sender, RoutedEventArgs e) => _captureService.CaptureActiveWindow();

        public void StartRegionScreenRecording() => _captureService.StartScreenRecordingRegion();
        public void StartFullscreenScreenRecording() => _captureService.StartScreenRecordingFullScreen();

        private void BtnSidebarRecord_Click(object sender, RoutedEventArgs e) => StartRegionScreenRecording();
        private void BtnQuickRecord_Click(object sender, RoutedEventArgs e) => StartRegionScreenRecording();
        private void BtnFullscreenRecord_Click(object sender, RoutedEventArgs e) => StartFullscreenScreenRecording();
        private void BtnClearClipboardDesktop_Click(object sender, RoutedEventArgs e) => ClipboardItems.Clear();

        private void BtnChangeSaveDir_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select LinkSync Save Directory",
                InitialDirectory = _server.SaveDirectory
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                _server.SaveDirectory = dialog.FolderName;
                TxtSettingsSaveDir.Text = dialog.FolderName;
                LoadExistingMediaFiles();
            }
        }

        private void ChkRunOnStartup_Click(object sender, RoutedEventArgs e)
        {
            SetRunOnStartup(ChkRunOnStartup.IsChecked == true);
        }

        private void ChkAutoPasteClipboard_Click(object sender, RoutedEventArgs e)
        {
            _autoPasteClipboard = ChkAutoPasteClipboard.IsChecked == true;
        }

        private async void BtnResetAdb_Click(object sender, RoutedEventArgs e)
        {
            BtnResetAdb.IsEnabled = false;
            BtnResetAdb.Content = "Resetting ADB Tunnel...";
            bool ok = await _adbManager.EnsureReverseTunnelAsync();
            BtnResetAdb.IsEnabled = true;
            BtnResetAdb.Content = "Reset / Force USB ADB Tunnel (Port 54884)";
            if (ok)
            {
                MessageBox.Show("USB ADB Reverse Tunnel established successfully!\n(Phone port 54884 is now bridged to laptop)", "LinkSync ADB Tunnel", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Could not establish reverse tunnel.\nPlease verify USB debugging is authorized and phone is connected.\nADB Path: {AdbTunnelManager.ResolvedAdbPath}", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task StartPhoneMirroringAsync(bool turnScreenOff)
        {
            if (_mirrorManager.IsMirroringActive)
            {
                _mirrorManager.StopMirror();
                return;
            }

            var options = new MirrorOptions
            {
                TurnScreenOff = turnScreenOff,
                AlwaysOnTop = false,
                BitRateMbps = 8,
                MaxFps = 60
            };

            bool success = await _mirrorManager.StartMirrorAsync(options);
            if (!success)
            {
                MessageBox.Show(
                    "Could not start phone mirroring.\n\nPlease verify:\n1. Your phone is connected via Wi-Fi or USB.\n2. USB debugging / Wireless debugging is enabled in Developer Options on your phone.",
                    "LinkSync Mirror",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async void BtnStartMirror_Click(object sender, RoutedEventArgs e)
        {
            await StartPhoneMirroringAsync(turnScreenOff: false);
        }

        private async void BtnScreenOffMirror_Click(object sender, RoutedEventArgs e)
        {
            await StartPhoneMirroringAsync(turnScreenOff: true);
        }

        private void OnMirrorStatusChanged(bool isActive)
        {
            Dispatcher.Invoke(() =>
            {
                if (isActive)
                {
                    BtnStartMirror.Content = "Stop Mirroring";
                    BtnStartMirror.Background = (Brush)FindResource("BrushDanger");
                    BtnQuickMirror.Content = "Stop Mirror";
                    BtnQuickMirror.Background = (Brush)FindResource("BrushDanger");
                    BtnTabStartMirror.Content = "Stop Mirror";
                    BtnTabStartMirror.Background = (Brush)FindResource("BrushDanger");
                    BadgeMirrorActive.Visibility = Visibility.Visible;
                    BadgeMirrorInactive.Visibility = Visibility.Collapsed;
                    _trayManager.ShowBalloon("Phone Mirror Active", "Streaming Samsung Galaxy A17 at 60 FPS (Hardware QuickSync H.264).\nMouse and keyboard control active! Press Alt+O to turn screen off.");
                }
                else
                {
                    BtnStartMirror.Content = "Mirror Screen (60 FPS)";
                    BtnStartMirror.Background = (Brush)FindResource("BrushAccent");
                    BtnQuickMirror.Content = "Mirror 60 FPS";
                    BtnQuickMirror.Background = (Brush)FindResource("BrushAccent");
                    BtnTabStartMirror.Content = "Launch Mirror";
                    BtnTabStartMirror.Background = (Brush)FindResource("BrushAccent");
                    BadgeMirrorActive.Visibility = Visibility.Collapsed;
                    BadgeMirrorInactive.Visibility = Visibility.Visible;
                }
            });
        }

        private void BtnCopyMediaItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is MediaItemViewModel item)
            {
                if (File.Exists(item.FilePath) && !item.IsVideo)
                {
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(item.FilePath, UriKind.Absolute);
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.EndInit();
                        bi.Freeze();

                        for (int attempt = 0; attempt < 5; attempt++)
                        {
                            try
                            {
                                Clipboard.SetImage(bi);
                                break;
                            }
                            catch
                            {
                                Thread.Sleep(50);
                            }
                        }
                        _trayManager.ShowBalloon("📋 Copied to Clipboard", $"{item.FileName} is ready to paste (Ctrl+V).");
                    }
                    catch { }
                }
            }
        }

        // ========================================================
        // GROUP 9: COMMUNICATION HUB (SMS, CALLS, CONTACTS & OTP)
        // ========================================================
        private readonly List<ContactItem> _cachedContacts = new();
        private readonly List<SmsMessageItem> _cachedSmsMessages = new();
        private readonly List<SmsThreadViewModel> _cachedThreads = new();
        private SmsThreadViewModel? _activeThread;
        private DispatcherTimer? _callDurationTimer;
        private DateTime _callStartTime;

        private void OnContactsReceived(List<ContactItem> contacts)
        {
            Dispatcher.Invoke(() =>
            {
                _cachedContacts.Clear();
                _cachedContacts.AddRange(contacts);
                TxtContactsCount.Text = $"{contacts.Count} Contacts";
                RenderContactsList(_cachedContacts);
                RenderQuickDialList(_cachedContacts);

                if (_cachedSmsMessages.Count > 0)
                {
                    RebuildSmsThreads();
                }
            });
        }

        private void OnSmsListReceived(List<SmsMessageItem> messages)
        {
            Dispatcher.Invoke(() =>
            {
                _cachedSmsMessages.Clear();
                _cachedSmsMessages.AddRange(messages);
                RebuildSmsThreads();
            });
        }

        private void OnSmsReceived(IncomingSmsNotification sms)
        {
            Dispatcher.Invoke(() =>
            {
                // Feature 43: 1-Click Copy Verification Codes (2FA OTP Toast)
                if (!string.IsNullOrWhiteSpace(sms.OtpCode))
                {
                    try
                    {
                        // 1. Auto-copy to clipboard immediately for instant Ctrl + V
                        Clipboard.SetDataObject(sms.OtpCode, true);
                    }
                    catch { }

                    // 2. Windows System Tray Balloon Tip
                    _trayManager.ShowBalloon(
                        "🔑 Verification Code Received",
                        $"Code: {sms.OtpCode} (Copied to clipboard)\n{sms.Address}: {sms.Body}"
                    );

                    // 3. Interactive Floating Toast with 1-Click Button
                    try
                    {
                        var toast = new Views.OtpToastWindow(sms.OtpCode, sms.Address, sms.Body);
                        toast.Show();
                    }
                    catch { }
                }

                // If currently viewing active thread with this sender, display live bubble
                if (_activeThread != null && IsMatchingAddress(_activeThread.Address, sms.Address))
                {
                    var msg = new SmsMessageItem
                    {
                        Address = sms.Address,
                        Body = sms.Body,
                        IsIncoming = true,
                        Timestamp = sms.Timestamp > 0 ? sms.Timestamp : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Read = true
                    };
                    _activeThread.Messages.Add(msg);
                    AppendMessageBubble(msg);
                    ScrollSmsMessages.ScrollToEnd();
                }

                // Live notification in sidebar
                Notifications.Insert(0, new NotificationItemViewModel
                {
                    AppName = "Messages",
                    AppIconGlyph = "\uE8BD",
                    AppIconColor = "#38BDF8",
                    Title = sms.Address,
                    Body = sms.Body,
                    TimestampText = DateTime.Now.ToString("HH:mm"),
                    ActionLabel = "Reply",
                    ActionType = "reply",
                    AssociatedData = sms.Address
                });
                while (Notifications.Count > 30) Notifications.RemoveAt(Notifications.Count - 1);
                UpdateNotificationBadgeCounts();

                // Refresh thread list from mobile node
                _ = _server.RequestSmsListAsync();
            });
        }

        private void OnCallStateChanged(CallStateNotification call)
        {
            Dispatcher.Invoke(() =>
            {
                if (call.State == "RINGING" || call.State == "OFFHOOK")
                {
                    if (call.State == "RINGING")
                    {
                        Notifications.Insert(0, new NotificationItemViewModel
                        {
                            AppName = "Calls",
                            AppIconGlyph = "\uE717",
                            AppIconColor = "#F59E0B",
                            Title = "Incoming Call",
                            Body = string.IsNullOrWhiteSpace(call.Number) ? "Galaxy A17" : call.Number,
                            TimestampText = DateTime.Now.ToString("HH:mm"),
                            ActionLabel = "Call Back",
                            ActionType = "call",
                            AssociatedData = call.Number
                        });
                        while (Notifications.Count > 30) Notifications.RemoveAt(Notifications.Count - 1);
                UpdateNotificationBadgeCounts();
                    }

                    CardActiveCall.Visibility = Visibility.Visible;
                    TxtActiveCallNumber.Text = string.IsNullOrWhiteSpace(call.Number)
                        ? (string.IsNullOrWhiteSpace(TxtDialNumber.Text) ? "Calling on Galaxy A17..." : TxtDialNumber.Text)
                        : call.Number;
                    TxtActiveCallState.Text = call.State == "RINGING" ? "Ringing..." : "Connected / In-Call";

                    if (_callDurationTimer == null)
                    {
                        _callStartTime = DateTime.UtcNow;
                        _callDurationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                        _callDurationTimer.Tick += (s, e) =>
                        {
                            var elapsed = DateTime.UtcNow - _callStartTime;
                            TxtActiveCallTimer.Text = elapsed.ToString(@"mm\:ss");
                        };
                        _callDurationTimer.Start();
                    }
                }
                else // IDLE / Call Ended
                {
                    if (_callDurationTimer != null)
                    {
                        _callDurationTimer.Stop();
                        _callDurationTimer = null;
                    }
                    CardActiveCall.Visibility = Visibility.Collapsed;
                    TxtActiveCallTimer.Text = "00:00";
                }
            });
        }

        private void RenderContactsList(IEnumerable<ContactItem> contacts)
        {
            LstContacts.Items.Clear();
            foreach (var c in contacts)
            {
                var card = CreateContactCard(c);
                LstContacts.Items.Add(card);
            }
        }

        private FrameworkElement CreateContactCard(ContactItem c)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Avatar with initials
            var avatarBorder = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                BorderBrush = (Brush)FindResource("BrushAccent"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 12, 0)
            };
            var txtInitials = new TextBlock
            {
                Text = GetInitials(c.Name),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("BrushAccentLight"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            avatarBorder.Child = txtInitials;
            Grid.SetColumn(avatarBorder, 0);
            grid.Children.Add(avatarBorder);

            // Contact Name & Number
            var detailsPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var namePanel = new StackPanel { Orientation = Orientation.Horizontal };
            var txtName = new TextBlock
            {
                Text = c.Name,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("BrushTextPrimary")
            };
            namePanel.Children.Add(txtName);
            if (c.Starred)
            {
                var starBadge = new TextBlock
                {
                    Text = " ★",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                namePanel.Children.Add(starBadge);
            }
            detailsPanel.Children.Add(namePanel);

            var txtNumber = new TextBlock
            {
                Text = c.Number,
                FontSize = 11,
                Foreground = (Brush)FindResource("BrushTextMuted"),
                Margin = new Thickness(0, 2, 0, 0)
            };
            detailsPanel.Children.Add(txtNumber);
            Grid.SetColumn(detailsPanel, 1);
            grid.Children.Add(detailsPanel);

            // Action Buttons: Call & SMS
            var actionsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            
            var btnCall = new Button
            {
                Style = (Style)FindResource("SecondaryButton"),
                Height = 30,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Call this contact"
            };
            var callSp = new StackPanel { Orientation = Orientation.Horizontal };
            callSp.Children.Add(new TextBlock { Text = "\uE717", FontFamily = (FontFamily)FindResource("FontIcons"), FontSize = 12, Foreground = (Brush)FindResource("BrushSuccess"), Margin = new Thickness(0, 0, 4, 0) });
            callSp.Children.Add(new TextBlock { Text = "Call", FontSize = 11, FontWeight = FontWeights.SemiBold });
            btnCall.Content = callSp;
            btnCall.Click += (s, e) =>
            {
                TxtDialNumber.Text = c.Number;
                SwitchTab(NavTab.Calls);
                _ = _server.DialCallAsync(c.Number);
            };
            actionsPanel.Children.Add(btnCall);

            var btnSms = new Button
            {
                Style = (Style)FindResource("SecondaryButton"),
                Height = 30,
                Padding = new Thickness(8, 2, 8, 2),
                ToolTip = "Send text message"
            };
            var smsSp = new StackPanel { Orientation = Orientation.Horizontal };
            smsSp.Children.Add(new TextBlock { Text = "\uE8BD", FontFamily = (FontFamily)FindResource("FontIcons"), FontSize = 12, Foreground = (Brush)FindResource("BrushAccentLight"), Margin = new Thickness(0, 0, 4, 0) });
            smsSp.Children.Add(new TextBlock { Text = "SMS", FontSize = 11, FontWeight = FontWeights.SemiBold });
            btnSms.Content = smsSp;
            btnSms.Click += (s, e) =>
            {
                SelectOrCreateThread(c.Number, c.Name);
                SwitchTab(NavTab.Messages);
            };
            actionsPanel.Children.Add(btnSms);

            Grid.SetColumn(actionsPanel, 2);
            grid.Children.Add(actionsPanel);

            return grid;
        }

        private void RenderQuickDialList(IEnumerable<ContactItem> contacts)
        {
            LstQuickDial.Items.Clear();
            var favorites = contacts.Where(c => c.Starred).Concat(contacts.Where(c => !c.Starred)).Take(12);
            foreach (var c in favorites)
            {
                var card = CreateContactCard(c);
                LstQuickDial.Items.Add(card);
            }
        }

        private void RebuildSmsThreads()
        {
            _cachedThreads.Clear();
            var grouped = _cachedSmsMessages.GroupBy(m => NormalizeNumber(m.Address));

            foreach (var group in grouped)
            {
                var sorted = group.OrderBy(m => m.Timestamp).ToList();
                var latest = sorted.Last();
                var cleanAddress = latest.Address;
                var matchedContact = _cachedContacts.FirstOrDefault(c => IsMatchingAddress(c.Number, cleanAddress));
                var displayName = matchedContact?.Name ?? cleanAddress;

                var thread = new SmsThreadViewModel
                {
                    ThreadId = latest.ThreadId,
                    Address = cleanAddress,
                    DisplayName = displayName,
                    Snippet = latest.Body,
                    TimestampText = FormatSmsDate(latest.Timestamp),
                    Initials = GetInitials(displayName),
                    HasUnread = sorted.Any(m => m.IsIncoming && !m.Read),
                    Messages = sorted
                };
                _cachedThreads.Add(thread);
            }

            RenderSmsThreads(_cachedThreads);
        }

        private void RenderSmsThreads(IEnumerable<SmsThreadViewModel> threads)
        {
            LstSmsThreads.Items.Clear();
            foreach (var th in threads)
            {
                var item = CreateSmsThreadItem(th);
                LstSmsThreads.Items.Add(item);
            }
        }

        private FrameworkElement CreateSmsThreadItem(SmsThreadViewModel th)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2), Tag = th };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Avatar
            var avatarBorder = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                BorderBrush = (Brush)FindResource("BrushAccent"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 10, 0)
            };
            var txtInitials = new TextBlock
            {
                Text = th.Initials,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("BrushAccentLight"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            avatarBorder.Child = txtInitials;
            Grid.SetColumn(avatarBorder, 0);
            grid.Children.Add(avatarBorder);

            // Name & snippet
            var contentSp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var txtName = new TextBlock
            {
                Text = th.DisplayName,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("BrushTextPrimary"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            contentSp.Children.Add(txtName);

            var txtSnippet = new TextBlock
            {
                Text = th.Snippet,
                FontSize = 11,
                Foreground = (Brush)FindResource("BrushTextMuted"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0),
                MaxHeight = 16
            };
            contentSp.Children.Add(txtSnippet);
            Grid.SetColumn(contentSp, 1);
            grid.Children.Add(contentSp);

            // Timestamp
            var txtTime = new TextBlock
            {
                Text = th.TimestampText,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(4, 0, 0, 0)
            };
            Grid.SetColumn(txtTime, 2);
            grid.Children.Add(txtTime);

            return grid;
        }

        private void LstSmsThreads_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstSmsThreads.SelectedItem is FrameworkElement fe && fe.Tag is SmsThreadViewModel th)
            {
                SetActiveThread(th);
            }
        }

        private void SetActiveThread(SmsThreadViewModel th)
        {
            _activeThread = th;
            TxtActiveThreadName.Text = th.DisplayName;
            TxtActiveThreadNumber.Text = th.Address;
            TxtActiveAvatar.Text = th.Initials;

            RenderActiveThreadMessages();
        }

        private void SelectOrCreateThread(string number, string name)
        {
            var existing = _cachedThreads.FirstOrDefault(t => IsMatchingAddress(t.Address, number));
            if (existing != null)
            {
                SetActiveThread(existing);
            }
            else
            {
                var newThread = new SmsThreadViewModel
                {
                    Address = number,
                    DisplayName = name,
                    Initials = GetInitials(name),
                    TimestampText = "Now",
                    Snippet = "Start a new conversation",
                    Messages = new List<SmsMessageItem>()
                };
                _cachedThreads.Insert(0, newThread);
                RenderSmsThreads(_cachedThreads);
                SetActiveThread(newThread);
            }
        }

        private void RenderActiveThreadMessages()
        {
            PnlSmsMessages.Children.Clear();
            if (_activeThread == null) return;

            foreach (var msg in _activeThread.Messages.OrderBy(m => m.Timestamp))
            {
                AppendMessageBubble(msg);
            }
            ScrollSmsMessages.ScrollToEnd();
        }

        private void AppendMessageBubble(SmsMessageItem msg)
        {
            var container = new Border
            {
                Margin = msg.IsIncoming ? new Thickness(0, 3, 50, 3) : new Thickness(50, 3, 0, 3),
                HorizontalAlignment = msg.IsIncoming ? HorizontalAlignment.Left : HorizontalAlignment.Right
            };

            var bubbleBorder = new Border
            {
                Background = msg.IsIncoming
                    ? new SolidColorBrush(Color.FromRgb(30, 41, 59))  // Slate
                    : (Brush)FindResource("BrushAccent"),              // Cobalt Accent
                BorderBrush = msg.IsIncoming ? (Brush)FindResource("BrushCardBorder") : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = msg.IsIncoming ? new CornerRadius(12, 12, 12, 2) : new CornerRadius(12, 12, 2, 12),
                Padding = new Thickness(12, 8, 12, 8),
                MaxWidth = 480
            };

            var sp = new StackPanel();
            var txtBody = new TextBlock
            {
                Text = msg.Body,
                FontSize = 12,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 17
            };
            sp.Children.Add(txtBody);

            var txtTime = new TextBlock
            {
                Text = DateTimeOffset.FromUnixTimeMilliseconds(msg.Timestamp).LocalDateTime.ToString("h:mm tt"),
                FontSize = 9,
                Foreground = msg.IsIncoming ? new SolidColorBrush(Color.FromRgb(148, 163, 184)) : new SolidColorBrush(Color.FromRgb(191, 219, 254)),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0)
            };
            sp.Children.Add(txtTime);

            bubbleBorder.Child = sp;
            container.Child = bubbleBorder;
            PnlSmsMessages.Children.Add(container);
        }

        private void BtnSendSms_Click(object sender, RoutedEventArgs e) => ExecuteSendSms();

        private void TxtSmsInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift))
            {
                e.Handled = true;
                ExecuteSendSms();
            }
        }

        private void ExecuteSendSms()
        {
            string body = TxtSmsInput.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(body)) return;

            string targetNumber = _activeThread?.Address ?? TxtActiveThreadNumber.Text;
            if (string.IsNullOrWhiteSpace(targetNumber) || targetNumber == "Direct SMS via Galaxy A17")
            {
                MessageBox.Show("Please select or specify a contact phone number first.", "LinkSync SMS", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            TxtSmsInput.Text = string.Empty;

            var optimisticMsg = new SmsMessageItem
            {
                Address = targetNumber,
                Body = body,
                IsIncoming = false,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Read = true
            };

            _activeThread?.Messages.Add(optimisticMsg);
            AppendMessageBubble(optimisticMsg);
            ScrollSmsMessages.ScrollToEnd();

            _ = _server.SendSmsAsync(targetNumber, body);
        }

        private void BtnCallActiveThread_Click(object sender, RoutedEventArgs e)
        {
            if (_activeThread != null && !string.IsNullOrWhiteSpace(_activeThread.Address))
            {
                TxtDialNumber.Text = _activeThread.Address;
                SwitchTab(NavTab.Calls);
                _ = _server.DialCallAsync(_activeThread.Address);
            }
        }

        private void BtnRefreshSms_Click(object sender, RoutedEventArgs e)
        {
            _ = _server.RequestSmsListAsync();
        }

        private void BtnRefreshContacts_Click(object sender, RoutedEventArgs e)
        {
            _ = _server.RequestContactsAsync();
        }

        private void TxtContactSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = TxtContactSearch.Text?.Trim().ToLowerInvariant() ?? "";
            if (string.IsNullOrEmpty(query))
            {
                RenderContactsList(_cachedContacts);
            }
            else
            {
                var filtered = _cachedContacts.Where(c =>
                    c.Name.ToLowerInvariant().Contains(query) ||
                    c.Number.Contains(query));
                RenderContactsList(filtered);
            }
        }

        private void TxtSmsSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = TxtSmsSearch.Text?.Trim().ToLowerInvariant() ?? "";
            if (string.IsNullOrEmpty(query))
            {
                RenderSmsThreads(_cachedThreads);
            }
            else
            {
                var filtered = _cachedThreads.Where(t =>
                    t.DisplayName.ToLowerInvariant().Contains(query) ||
                    t.Address.Contains(query) ||
                    t.Snippet.ToLowerInvariant().Contains(query));
                RenderSmsThreads(filtered);
            }
        }

        // ========================================================
        // DIGITAL KEYPAD & CALL CONTROLS
        // ========================================================
        private void KeypadDigit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string digit)
            {
                TxtDialNumber.Text += digit;
            }
        }

        private void BtnDialBackspace_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtDialNumber.Text))
            {
                TxtDialNumber.Text = TxtDialNumber.Text.Substring(0, TxtDialNumber.Text.Length - 1);
            }
        }

        private void TxtDialNumber_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                BtnPlaceCall_Click(sender, e);
            }
        }

        private void BtnPlaceCall_Click(object sender, RoutedEventArgs e)
        {
            string number = TxtDialNumber.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(number))
            {
                MessageBox.Show("Please enter a phone number to dial.", "LinkSync Dialer", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            CardActiveCall.Visibility = Visibility.Visible;
            TxtActiveCallNumber.Text = number;
            TxtActiveCallState.Text = "Dialing on Galaxy A17...";
            _ = _server.DialCallAsync(number);
        }

        private void BtnEndCall_Click(object sender, RoutedEventArgs e)
        {
            _ = _server.EndCallAsync();
            CardActiveCall.Visibility = Visibility.Collapsed;
            _callDurationTimer?.Stop();
            _callDurationTimer = null;
            TxtActiveCallTimer.Text = "00:00";
        }

        // ========================================================
        // UTILITY STRING HELPERS
        // ========================================================
        private static string GetInitials(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "#";
            var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
            return (parts[0][0].ToString() + parts[^1][0].ToString()).ToUpperInvariant();
        }

        private static string NormalizeNumber(string num)
        {
            if (string.IsNullOrWhiteSpace(num)) return "";
            return System.Text.RegularExpressions.Regex.Replace(num, @"[^0-9]", "");
        }

        private static bool IsMatchingAddress(string a, string b)
        {
            var na = NormalizeNumber(a);
            var nb = NormalizeNumber(b);
            if (na == nb) return true;
            if (na.Length >= 7 && nb.Length >= 7)
            {
                return na.EndsWith(nb) || nb.EndsWith(na);
            }
            return false;
        }

        private static string FormatSmsDate(long timestamp)
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
            if (dt.Date == DateTime.Today)
            {
                return dt.ToString("h:mm tt");
            }
            if (dt.Date == DateTime.Today.AddDays(-1))
            {
                return "Yesterday";
            }
            return dt.ToString("MMM d");
        }
    }

    public class MediaItemViewModel
    {
        public string FileName { get; set; } = string.Empty;
        public string TimestampText { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public bool IsVideo => FileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);
        public string ActionButtonText => IsVideo ? "🎬 Video Tools" : "✏️ Annotate";
        public SolidColorBrush ActionButtonBackground => IsVideo
            ? new SolidColorBrush(Color.FromRgb(124, 58, 237)) // Purple
            : new SolidColorBrush(Color.FromRgb(37, 99, 235));  // Blue

        private ImageSource? _thumbnail;
        public ImageSource? Thumbnail
        {
            get
            {
                if (_thumbnail == null && !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath) && !IsVideo)
                {
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(FilePath, UriKind.Absolute);
                        bi.DecodePixelWidth = 140;
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.EndInit();
                        bi.Freeze();
                        _thumbnail = bi;
                    }
                    catch { }
                }
                return _thumbnail;
            }
            set => _thumbnail = value;
        }
    }

    public class ClipboardItemViewModel
    {
        public string SourceLabel { get; set; } = string.Empty;
        public SolidColorBrush SourceColor { get; set; } = Brushes.White;
        public string Text { get; set; } = string.Empty;
        public string TimestampText { get; set; } = DateTime.Now.ToString("HH:mm:ss");
        public string CharCountText => $"{Text.Length} chars";
    }

    public class SmsThreadViewModel
    {
        public string ThreadId { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Snippet { get; set; } = string.Empty;
        public string TimestampText { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;
        public bool HasUnread { get; set; }
        public List<SmsMessageItem> Messages { get; set; } = new();
    }

    public class ContactViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;
        public bool Starred { get; set; }
        public string Initials { get; set; } = string.Empty;
    }

    public class NotificationItemViewModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string AppName { get; set; } = "Messages";
        public string AppIconGlyph { get; set; } = "\uE8BD";
        public string AppIconColor { get; set; } = "#38BDF8";
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string TimestampText { get; set; } = DateTime.Now.ToString("t");
        public string ActionLabel { get; set; } = "View";
        public string ActionType { get; set; } = "view"; // "reply", "call", "copy", "view"
        public string AssociatedData { get; set; } = string.Empty;
    }
}
