using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace LinkSync.Desktop.Services
{
    public class SystemTrayManager : IDisposable
    {
        private const int WM_USER = 0x0400;
        public const int WM_TRAYICON = WM_USER + 101;

        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;

        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;
        private const int NIF_INFO = 0x00000010;

        private const int NIIF_INFO = 0x00000001;

        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;

        private const int IDI_APPLICATION = 32512;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x0010;

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private IntPtr _windowHandle;
        private HwndSource? _hwndSource;
        private bool _isCreated;
        private Window? _window;
        private ContextMenu? _contextMenu;

        public event Action? DoubleClicked;
        public event Action? LeftClicked;

        public void Initialize(Window window, string tooltip)
        {
            _window = window;
            var helper = new WindowInteropHelper(window);
            _windowHandle = helper.Handle;

            _hwndSource = HwndSource.FromHwnd(_windowHandle);
            _hwndSource?.AddHook(HwndHook);

            CreateTrayIcon(tooltip);
        }

        public void SetContextMenu(ContextMenu contextMenu)
        {
            _contextMenu = contextMenu;
        }

        private IntPtr GetAppIconHandle()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] possiblePaths = new[]
                {
                    System.IO.Path.Combine(baseDir, "Resources", "app.ico"),
                    System.IO.Path.Combine(baseDir, "app.ico")
                };

                foreach (var path in possiblePaths)
                {
                    if (System.IO.File.Exists(path))
                    {
                        IntPtr h = LoadImage(IntPtr.Zero, path, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                        if (h != IntPtr.Zero) return h;
                    }
                }

                IntPtr hModule = GetModuleHandle(null);
                IntPtr exeIcon = LoadIcon(hModule, (IntPtr)1);
                if (exeIcon != IntPtr.Zero) return exeIcon;
            }
            catch { }

            return LoadIcon(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
        }

        private void CreateTrayIcon(string tooltip)
        {
            var data = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _windowHandle,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = GetAppIconHandle(),
                szTip = tooltip.Length > 127 ? tooltip.Substring(0, 127) : tooltip
            };

            _isCreated = Shell_NotifyIcon(NIM_ADD, ref data);
        }

        public void UpdateTooltip(string tooltip)
        {
            if (!_isCreated) return;

            var data = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _windowHandle,
                uID = 1,
                uFlags = NIF_TIP,
                szTip = tooltip.Length > 127 ? tooltip.Substring(0, 127) : tooltip
            };

            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }

        public void ShowBalloon(string title, string text)
        {
            if (!_isCreated) return;

            var data = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _windowHandle,
                uID = 1,
                uFlags = NIF_INFO,
                szInfoTitle = title.Length > 63 ? title.Substring(0, 63) : title,
                szInfo = text.Length > 255 ? text.Substring(0, 255) : text,
                dwInfoFlags = NIIF_INFO
            };

            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_TRAYICON)
            {
                int mouseEvent = lParam.ToInt32();
                switch (mouseEvent)
                {
                    case WM_LBUTTONDBLCLK:
                        DoubleClicked?.Invoke();
                        handled = true;
                        break;

                    case WM_LBUTTONUP:
                        LeftClicked?.Invoke();
                        handled = true;
                        break;

                    case WM_RBUTTONUP:
                        ShowContextMenu();
                        handled = true;
                        break;
                }
            }

            return IntPtr.Zero;
        }

        private void ShowContextMenu()
        {
            if (_contextMenu == null || _window == null) return;

            GetCursorPos(out POINT pt);
            SetForegroundWindow(_windowHandle);

            _contextMenu.Placement = PlacementMode.AbsolutePoint;
            _contextMenu.HorizontalOffset = pt.X;
            _contextMenu.VerticalOffset = pt.Y;
            _contextMenu.IsOpen = true;
        }

        public void Dispose()
        {
            if (_isCreated)
            {
                var data = new NOTIFYICONDATA
                {
                    cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _windowHandle,
                    uID = 1
                };
                Shell_NotifyIcon(NIM_DELETE, ref data);
                _isCreated = false;
            }

            _hwndSource?.RemoveHook(HwndHook);
        }
    }
}
