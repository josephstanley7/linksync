using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace LinkSync.Desktop.Services
{
    public class ScreenCaptureService
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private readonly SyncServer _server;
        private readonly SystemTrayManager _trayManager;
        private readonly string _saveDirectory;
        private readonly DesktopScreenRecorder _recorder;

        public DesktopScreenRecorder Recorder => _recorder;

        public event Action<string, byte[]>? CaptureSaved;

        public ScreenCaptureService(SyncServer server, SystemTrayManager trayManager)
        {
            _server = server;
            _trayManager = trayManager;
            _saveDirectory = server.SaveDirectory;
            Directory.CreateDirectory(_saveDirectory);
            _recorder = new DesktopScreenRecorder(server, trayManager);
        }

        public static Bitmap CaptureVirtualScreen()
        {
            int left = (int)SystemParameters.VirtualScreenLeft;
            int top = (int)SystemParameters.VirtualScreenTop;
            int width = (int)SystemParameters.VirtualScreenWidth;
            int height = (int)SystemParameters.VirtualScreenHeight;

            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }

        public void CaptureRegion(bool isRecordingMode = false)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var fullScreen = CaptureVirtualScreen();
                var snipWindow = new ScreenSnippingWindow(fullScreen, isRecordingMode);
                snipWindow.SnipCompleted += (bitmap, action) =>
                {
                    Task.Run(() => HandleSnipResultAsync(bitmap, action));
                };
                snipWindow.RecordRegionRequested += (rect) =>
                {
                    _recorder.StartRecording(rect);
                };
                snipWindow.Show();
                snipWindow.Activate();
            });
        }

        public void StartScreenRecordingRegion()
        {
            CaptureRegion(isRecordingMode: true);
        }

        public void StartScreenRecordingFullScreen()
        {
            _recorder.StartRecording(null);
        }

        public void CaptureFullScreen()
        {
            Task.Run(async () =>
            {
                var bmp = CaptureVirtualScreen();
                await ProcessAndSyncCapturedBitmapAsync(bmp, "Fullscreen");
            });
        }

        public void CaptureActiveWindow()
        {
            Task.Run(async () =>
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                {
                    CaptureFullScreen();
                    return;
                }

                RECT rect;
                if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) != 0)
                {
                    GetWindowRect(hwnd, out rect);
                }

                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    CaptureFullScreen();
                    return;
                }

                var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(rect.Width, rect.Height), CopyPixelOperation.SourceCopy);
                }

                await ProcessAndSyncCapturedBitmapAsync(bmp, "Window");
            });
        }

        private async Task HandleSnipResultAsync(Bitmap bitmap, SnipAction action)
        {
            string fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string targetPath = Path.Combine(_saveDirectory, fileName);

            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                byte[] bytes = ms.ToArray();
                await File.WriteAllBytesAsync(targetPath, bytes);

                // Copy to Windows Clipboard
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var source = ToBitmapSource(bitmap);
                        Clipboard.SetImage(source);
                    }
                    catch { }
                });

                // Sync to Phone
                await _server.SendFileAsync(fileName, bytes, "image/png");

                CaptureSaved?.Invoke(targetPath, bytes);

                if (action == SnipAction.Annotate)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var annotator = new ImageAnnotatorWindow(targetPath);
                        annotator.Show();
                        annotator.Activate();
                    });
                }
                else
                {
                    _trayManager.ShowBalloon("📸 Screenshot Synced!", "Copied to PC & Phone clipboard. Ready to paste (Ctrl + V).");
                }
            }
        }

        private async Task ProcessAndSyncCapturedBitmapAsync(Bitmap bitmap, string tag)
        {
            await HandleSnipResultAsync(bitmap, SnipAction.CopyAndSync);
        }

        private static BitmapSource ToBitmapSource(Bitmap bitmap)
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
    }
}
