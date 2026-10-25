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
using LinkSync.Core;
using LinkSync.Desktop.Services;

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

        private string? _lastReceivedFromPhoneText = null;
        private DateTime _lastReceivedFromPhoneTime = DateTime.MinValue;
        private string? _lastSentToPhoneText = null;
        private DateTime _lastSentToPhoneTime = DateTime.MinValue;

        private const string StartupRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegName = "LinkSync";

        public static MainWindow? Instance { get; private set; }
        public SyncServer SyncServer => _server;

        public ObservableCollection<MediaItemViewModel> MediaItems { get; } = new();
        public ObservableCollection<ClipboardItemViewModel> ClipboardItems { get; } = new();

        public MainWindow()
        {
            Instance = this;
            App.LogStartup("MainWindow ctor entered");
            InitializeComponent();
            App.LogStartup("InitializeComponent finished");

            LstMedia.ItemsSource = MediaItems;
            LstClipboard.ItemsSource = ClipboardItems;

            Closing += OnWindowClosing;

            _server = new SyncServer(54884);
            _server.DeviceConnected += OnDeviceConnected;
            _server.DeviceDisconnected += OnDeviceDisconnected;
            _server.ClipboardReceived += OnClipboardReceived;
            _server.MediaReceived += OnMediaReceived;

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
                TxtSidebarBattery.Text = "🔋 Active";
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
                TxtSidebarBattery.Text = "🔋 Waiting";
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
                        break;

                    case DevicePortStatus.ConnectedMtpOnly:
                        DotStatus.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
                        TxtDeviceName.Text = "⚠️ USB Cable Detected: File Transfer / Debugging Needed";
                        TxtDeviceSub.Text = "On your phone, enable File Transfer or USB Debugging in Settings.";
                        break;

                    case DevicePortStatus.ConnectedAuthorized:
                        DotStatus.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
                        TxtDeviceName.Text = "Searching for Phone on Wi-Fi & USB...";
                        TxtDeviceSub.Text = "USB tunnel active (Port 54884). Waiting for phone app...";
                        break;

                    case DevicePortStatus.NoDevice:
                    default:
                        DotStatus.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
                        TxtDeviceName.Text = "Searching for Phone on Wi-Fi & USB...";
                        TxtDeviceSub.Text = "Ensure phone is on same Wi-Fi network or plug in USB cable.";
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

                if (isImage)
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

        public enum NavTab { Overview, Photos, Clipboard, Video, Annotator, Mirror }

        private void SwitchTab(NavTab tab)
        {
            ViewOverview.Visibility = tab == NavTab.Overview ? Visibility.Visible : Visibility.Collapsed;
            ViewPhotos.Visibility = tab == NavTab.Photos ? Visibility.Visible : Visibility.Collapsed;
            ViewClipboard.Visibility = tab == NavTab.Clipboard ? Visibility.Visible : Visibility.Collapsed;
            ViewVideo.Visibility = tab == NavTab.Video ? Visibility.Visible : Visibility.Collapsed;
            ViewAnnotator.Visibility = tab == NavTab.Annotator ? Visibility.Visible : Visibility.Collapsed;
            ViewMirror.Visibility = tab == NavTab.Mirror ? Visibility.Visible : Visibility.Collapsed;

            ResetNavButton(BtnNavOverview, tab == NavTab.Overview);
            ResetNavButton(BtnNavPhotos, tab == NavTab.Photos);
            ResetNavButton(BtnNavClipboard, tab == NavTab.Clipboard);
            ResetNavButton(BtnNavVideo, tab == NavTab.Video);
            ResetNavButton(BtnNavAnnotator, tab == NavTab.Annotator);
            ResetNavButton(BtnNavMirror, tab == NavTab.Mirror);

            TxtPageTitle.Text = tab switch
            {
                NavTab.Overview => "Overview & Hardware Status",
                NavTab.Photos => "Photos & Synced Screenshots",
                NavTab.Clipboard => "Universal Clipboard Stream",
                NavTab.Video => "Video Suite & Animated GIF Maker",
                NavTab.Annotator => "ShareX-Grade Image Annotator",
                NavTab.Mirror => "Real-Time Phone Mirror & Remote Control",
                _ => "LinkSync"
            };
        }

        private void ResetNavButton(Button btn, bool isActive)
        {
            btn.Background = isActive ? new SolidColorBrush(Color.FromRgb(30, 41, 59)) : Brushes.Transparent;
            btn.Foreground = isActive ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : new SolidColorBrush(Color.FromRgb(148, 163, 184));
            btn.FontWeight = isActive ? FontWeights.Bold : FontWeights.Medium;
        }

        private void BtnNavOverview_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Overview);
        private void BtnNavPhotos_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Photos);
        private void BtnNavClipboard_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Clipboard);
        private void BtnNavVideo_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Video);
        private void BtnNavAnnotator_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Annotator);
        private void BtnNavMirror_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Mirror);

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
                    BtnStartMirror.Content = "⏹ Stop Mirroring";
                    BtnStartMirror.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                    BtnQuickMirror.Content = "⏹ Stop Mirror";
                    BtnQuickMirror.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                    BtnTabStartMirror.Content = "⏹ Stop Mirror";
                    BtnTabStartMirror.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                    BadgeMirrorActive.Visibility = Visibility.Visible;
                    BadgeMirrorInactive.Visibility = Visibility.Collapsed;
                    _trayManager.ShowBalloon("📱 Phone Mirror Active", "Streaming Samsung Galaxy A17 at 60 FPS (Hardware QuickSync H.264).\nMouse and keyboard control active! Press Alt+O to turn screen off.");
                }
                else
                {
                    BtnStartMirror.Content = "📱 Start Mirroring";
                    BtnStartMirror.Background = new SolidColorBrush(Color.FromRgb(2, 132, 199));
                    BtnQuickMirror.Content = "📱 Mirror (60 FPS)";
                    BtnQuickMirror.Background = new SolidColorBrush(Color.FromRgb(2, 132, 199));
                    BtnTabStartMirror.Content = "📱 Launch Mirror";
                    BtnTabStartMirror.Background = new SolidColorBrush(Color.FromRgb(2, 132, 199));
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
}
