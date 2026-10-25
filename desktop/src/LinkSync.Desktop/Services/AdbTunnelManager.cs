using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace LinkSync.Desktop.Services
{
    public enum DevicePortStatus
    {
        NoDevice,
        ConnectedAuthorized,
        ConnectedUnauthorized,
        ConnectedMtpOnly
    }

    public class AdbTunnelManager : IDisposable
    {
        private readonly int _port;
        private CancellationTokenSource? _cts;
        private bool _isDisposed;

        public event Action<DevicePortStatus>? PortStatusChanged;

        public static string ResolvedAdbPath => ResolveAdbPath();

        public AdbTunnelManager(int port = 54884)
        {
            _port = port;
        }

        public static string ResolveAdbPath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string sdkPath = Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "adb.exe");
            if (File.Exists(sdkPath))
            {
                return sdkPath;
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string sdkPathPF = Path.Combine(programFiles, "Android", "platform-tools", "adb.exe");
            if (File.Exists(sdkPathPF))
            {
                return sdkPathPF;
            }

            return "adb.exe";
        }

        private readonly SemaphoreSlim _lock = new(1, 1);

        public async Task<DevicePortStatus> CheckDevicePortStatusAsync()
        {
            string adbPath = ResolveAdbPath();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = "devices",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = await proc.StandardOutput.ReadToEndAsync();
                    await proc.WaitForExitAsync();

                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
                        if (line.Contains("device", StringComparison.OrdinalIgnoreCase) && 
                            !line.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) &&
                            !line.Contains("offline", StringComparison.OrdinalIgnoreCase))
                        {
                            return DevicePortStatus.ConnectedAuthorized;
                        }
                        if (line.Contains("offline", StringComparison.OrdinalIgnoreCase))
                        {
                            _ = Task.Run(() =>
                            {
                                try
                                {
                                    var reconProc = Process.Start(new ProcessStartInfo
                                    {
                                        FileName = adbPath,
                                        Arguments = "reconnect",
                                        UseShellExecute = false,
                                        CreateNoWindow = true
                                    });
                                    reconProc?.WaitForExit(2000);
                                }
                                catch { }
                            });
                            return DevicePortStatus.ConnectedUnauthorized;
                        }
                        if (line.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
                        {
                            return DevicePortStatus.ConnectedUnauthorized;
                        }
                    }
                }
            }
            catch { }

            // If ADB daemon does not report an active device, check if a phone is plugged in via Windows USB Registry
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
                if (key != null)
                {
                    foreach (var subName in key.GetSubKeyNames())
                    {
                        // Samsung VID is 04E8
                        if (subName.Contains("VID_04E8", StringComparison.OrdinalIgnoreCase))
                        {
                            return DevicePortStatus.ConnectedMtpOnly;
                        }
                    }
                }
            }
            catch { }

            return DevicePortStatus.NoDevice;
        }

        public async Task<bool> EnsureReverseTunnelAsync()
        {
            if (!await _lock.WaitAsync(100)) return false;
            try
            {
                string adbPath = ResolveAdbPath();
                return await Task.Run(() =>
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = adbPath,
                            Arguments = $"reverse tcp:{_port} tcp:{_port}",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };

                        using var proc = Process.Start(psi);
                        if (proc == null) return false;

                        proc.WaitForExit(3000);
                        return proc.ExitCode == 0;
                    }
                    catch
                    {
                        return false;
                    }
                });
            }
            finally
            {
                _lock.Release();
            }
        }

        public void StartWatchdog(Func<bool> isClientConnectedPredicate, TimeSpan interval)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (!isClientConnectedPredicate())
                        {
                            var status = await CheckDevicePortStatusAsync();
                            PortStatusChanged?.Invoke(status);

                            if (status == DevicePortStatus.ConnectedAuthorized)
                            {
                                await EnsureReverseTunnelAsync();
                            }
                        }

                        await Task.Delay(interval, token);
                    }
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            }, token);
        }

        public static string GetLocalWifiIpAddress()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                // Connect to a public non-routable address to discover the active local interface IP
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is IPEndPoint endPoint)
                {
                    return endPoint.Address.ToString();
                }
            }
            catch { }

            return "127.0.0.1";
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
