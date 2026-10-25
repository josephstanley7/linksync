using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LinkSync.Desktop.Services
{
    public class Win32ClipboardWatcher : IDisposable
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        private HwndSource? _hwndSource;
        private IntPtr _windowHandle;
        private bool _isDisposed;

        public event Action<string>? ClipboardTextChanged;

        public void Initialize(Window window)
        {
            var helper = new WindowInteropHelper(window);
            _windowHandle = helper.Handle;

            _hwndSource = HwndSource.FromHwnd(_windowHandle);
            _hwndSource?.AddHook(HwndHook);

            AddClipboardFormatListener(_windowHandle);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                OnClipboardChanged();
            }
            return IntPtr.Zero;
        }

        private void OnClipboardChanged()
        {
            // Robust retry loop: external apps (Chrome, Word, Notepad, etc.) often hold the clipboard lock for 10-40ms while copying
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string text = Clipboard.GetText();
                        if (!string.IsNullOrEmpty(text))
                        {
                            ClipboardTextChanged?.Invoke(text);
                            return;
                        }
                    }
                    else
                    {
                        // Clipboard contains non-text format (image/file), skip retry
                        return;
                    }
                }
                catch (COMException)
                {
                    Thread.Sleep(20);
                }
                catch (Exception)
                {
                    Thread.Sleep(20);
                }
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                if (_windowHandle != IntPtr.Zero)
                {
                    RemoveClipboardFormatListener(_windowHandle);
                }
                _hwndSource?.RemoveHook(HwndHook);
                _isDisposed = true;
            }
        }
    }
}
