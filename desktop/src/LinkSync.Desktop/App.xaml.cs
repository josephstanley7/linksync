using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace LinkSync.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public const string RestoreMessageName = "LinkSync_Restore_MainWindow_Folio9470M";

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public const int HWND_BROADCAST = 0xffff;
    public const int SW_RESTORE = 9;

    public static bool StartMinimized { get; private set; }
    private static System.Threading.EventWaitHandle? _wakeupEvent;
    private static System.Threading.RegisteredWaitHandle? _registeredWait;

    public static void LogStartup(string msg)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkSync");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "startup.log");
            File.AppendAllText(file, $"[{DateTime.Now:HH:mm:ss.fff}] [PID {Environment.ProcessId}] {msg}\n");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        LogStartup("OnStartup started");

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogStartup($"AppDomain exception: {args.ExceptionObject}");
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogStartup($"Dispatcher exception: {args.Exception}");
        };

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        LogStartup("ShutdownMode set to OnExplicitShutdown");

        if (e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--background", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("/minimized", StringComparison.OrdinalIgnoreCase)))
        {
            StartMinimized = true;
            LogStartup("StartMinimized set to true");
        }

        // Single-instance enforcement: if LinkSync is already running, wake it up and exit
        bool createdNew;
        try
        {
            _wakeupEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\LinkSync_App_Wakeup_Signal_9470M", out createdNew);
        }
        catch (Exception ex)
        {
            LogStartup($"EventWaitHandle exception: {ex.Message}");
            createdNew = true;
        }

        if (!createdNew)
        {
            LogStartup($"Another instance is already running (StartMinimized={StartMinimized}).");
            if (!StartMinimized)
            {
                LogStartup("Signaling primary instance to restore MainWindow.");
                try
                {
                    _wakeupEvent?.Set();
                }
                catch (Exception ex)
                {
                    LogStartup($"Failed to set wakeup event: {ex.Message}");
                }

                try
                {
                    int msg = RegisterWindowMessage(RestoreMessageName);
                    PostMessage((IntPtr)HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);
                }
                catch { }

                try
                {
                    Process current = Process.GetCurrentProcess();
                    var existing = Process.GetProcessesByName(current.ProcessName)
                                          .FirstOrDefault(p => p.Id != current.Id);
                    if (existing != null && existing.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(existing.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(existing.MainWindowHandle);
                    }
                }
                catch { }
            }

            Shutdown();
            return;
        }

        // We are the primary instance. Listen for wakeup signals from secondary instances:
        if (_wakeupEvent != null)
        {
            _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                _wakeupEvent,
                (state, timedOut) =>
                {
                    if (!timedOut)
                    {
                        LogStartup("Wakeup event signaled by secondary instance! Dispatching RestoreWindow.");
                        Current?.Dispatcher.BeginInvoke(() =>
                        {
                            (Current?.MainWindow as LinkSync.Desktop.MainWindow)?.RestoreWindow();
                            LinkSync.Desktop.MainWindow.Instance?.RestoreWindow();
                        });
                    }
                },
                null,
                -1,
                false
            );
        }

        base.OnStartup(e);
        LogStartup("base.OnStartup completed successfully");

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        LogStartup($"MainWindow created. StartMinimized={StartMinimized}");

        mainWindow.InitializeServices();

        if (!StartMinimized)
        {
            mainWindow.Show();
            mainWindow.Activate();
            LogStartup("mainWindow.Show() called");
        }
        else
        {
            mainWindow.TrimWorkingSet();
            LogStartup("mainWindow trimmed to background");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogStartup($"OnExit called with code {e.ApplicationExitCode}");
        try
        {
            _registeredWait?.Unregister(null);
            _wakeupEvent?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }
}
