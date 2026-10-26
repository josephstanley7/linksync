using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace LinkSync.Desktop.Services
{
    public class MirrorOptions
    {
        public bool TurnScreenOff { get; set; } = false;
        public bool AlwaysOnTop { get; set; } = false;
        public int BitRateMbps { get; set; } = 8;
        public int MaxFps { get; set; } = 60;
    }

    public class PhoneMirrorManager : IDisposable
    {
        private readonly AdbTunnelManager _adbManager;
        private Process? _mirrorProcess;
        private bool _isDisposed;

        public event Action<bool>? MirrorStatusChanged;
        public bool IsMirroringActive => _mirrorProcess != null && !_mirrorProcess.HasExited;

        public PhoneMirrorManager(AdbTunnelManager adbManager)
        {
            _adbManager = adbManager;
        }

        public static string? ResolveScrcpyPath()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string wingetPackages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
                if (Directory.Exists(wingetPackages))
                {
                    var dirs = Directory.GetDirectories(wingetPackages, "*Genymobile.scrcpy*", SearchOption.TopDirectoryOnly);
                    foreach (var dir in dirs)
                    {
                        var files = Directory.GetFiles(dir, "scrcpy.exe", SearchOption.AllDirectories);
                        if (files.Length > 0 && File.Exists(files[0]))
                        {
                            return files[0];
                        }
                    }
                }
            }
            catch { }

            // Check PATH
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = "scrcpy.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit();
                    if (!string.IsNullOrEmpty(output) && File.Exists(output))
                    {
                        return output;
                    }
                }
            }
            catch { }

            return null;
        }

        public static List<string> GetCandidateDeviceIps(string? hintIp = null)
        {
            var candidates = new List<string>();

            // 1. Hint from connected TCP client (Phone's real IP on Wi-Fi or Hotspot)
            if (!string.IsNullOrEmpty(hintIp))
            {
                if (IPAddress.TryParse(hintIp, out var parsedIp) && !IPAddress.IsLoopback(parsedIp))
                {
                    candidates.Add(hintIp);
                }
            }

            // 2. Default Gateways from active network adapters
            // (When laptop is connected to Mobile Hotspot, the phone IS the default gateway!)
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    var ipProps = ni.GetIPProperties();
                    foreach (var gw in ipProps.GatewayAddresses)
                    {
                        if (gw.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !gw.Address.Equals(IPAddress.Any) &&
                            !gw.Address.Equals(IPAddress.None) &&
                            !IPAddress.IsLoopback(gw.Address))
                        {
                            string gwStr = gw.Address.ToString();
                            if (!candidates.Contains(gwStr))
                            {
                                candidates.Add(gwStr);
                            }
                        }
                    }
                }
            }
            catch { }

            return candidates;
        }

        public async Task<bool> StartMirrorAsync(MirrorOptions? options = null, string? clientIp = null)
        {
            if (IsMirroringActive)
            {
                return true;
            }

            options ??= new MirrorOptions();

            string? scrcpyPath = ResolveScrcpyPath();
            if (string.IsNullOrEmpty(scrcpyPath) || !File.Exists(scrcpyPath))
            {
                Debug.WriteLine("[PhoneMirrorManager] scrcpy.exe not found on system.");
                return false;
            }

            string? serial = await _adbManager.GetActiveDeviceSerialAsync();
            if (string.IsNullOrEmpty(serial))
            {
                // Attempt Wi-Fi / Hotspot connect if wireless debugging is running on phone
                var candidateIps = GetCandidateDeviceIps(clientIp);
                foreach (var ip in candidateIps)
                {
                    await TryConnectAdbWifiAsync(ip);
                    serial = await _adbManager.GetActiveDeviceSerialAsync();
                    if (!string.IsNullOrEmpty(serial)) break;
                }
            }

            var args = $"--video-codec=h264 -b {options.BitRateMbps}M --max-fps={options.MaxFps} " +
                       $"--window-title=\"LinkSync Phone Mirror (Samsung Galaxy A17)\"";

            if (!string.IsNullOrEmpty(serial))
            {
                args = $"-s {serial} " + args;
            }

            if (options.TurnScreenOff)
            {
                args += " --turn-screen-off --stay-awake";
            }

            if (options.AlwaysOnTop)
            {
                args += " --always-on-top";
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = scrcpyPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true, // Suppresses the black Windows console window completely
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(scrcpyPath)
                };

                // Enforce using the exact Android SDK ADB binary
                psi.EnvironmentVariables["ADB"] = AdbTunnelManager.ResolveAdbPath();

                _mirrorProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _mirrorProcess.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null) Debug.WriteLine($"[scrcpy] {e.Data}");
                };
                _mirrorProcess.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null) Debug.WriteLine($"[scrcpy err] {e.Data}");
                };
                _mirrorProcess.Exited += (s, e) =>
                {
                    MirrorStatusChanged?.Invoke(false);
                };

                if (_mirrorProcess.Start())
                {
                    _mirrorProcess.BeginOutputReadLine();
                    _mirrorProcess.BeginErrorReadLine();
                    MirrorStatusChanged?.Invoke(true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PhoneMirrorManager] Start error: {ex.Message}");
            }

            return false;
        }

        public void StopMirror()
        {
            try
            {
                if (_mirrorProcess != null && !_mirrorProcess.HasExited)
                {
                    _mirrorProcess.Kill(entireProcessTree: true);
                }
            }
            catch { }
            finally
            {
                _mirrorProcess = null;
                MirrorStatusChanged?.Invoke(false);
            }
        }

        public async Task ToggleScreenOffAsync()
        {
            string adbPath = AdbTunnelManager.ResolveAdbPath();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = "shell cmd display power-off",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null) await proc.WaitForExitAsync();
            }
            catch { }
        }

        private static async Task<bool> TryConnectAdbWifiAsync(string ip)
        {
            string adbPath = AdbTunnelManager.ResolveAdbPath();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = $"connect {ip}:5555",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    using var cts = new CancellationTokenSource(2500);
                    await proc.WaitForExitAsync(cts.Token);
                    string output = await proc.StandardOutput.ReadToEndAsync();
                    return output.Contains("connected to", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }
            return false;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            StopMirror();
        }
    }
}
