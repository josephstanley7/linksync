using System;
using System.Diagnostics;
using System.IO;
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

        public async Task<bool> StartMirrorAsync(MirrorOptions? options = null)
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
                // Attempt Wi-Fi connect if wireless debugging is running on phone
                await TryConnectAdbWifiAsync("192.168.100.8");
                serial = await _adbManager.GetActiveDeviceSerialAsync();
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

        private async Task TryConnectAdbWifiAsync(string ip)
        {
            string adbPath = AdbTunnelManager.ResolveAdbPath();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = $"connect {ip}:5555",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null) await proc.WaitForExitAsync();
            }
            catch { }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            StopMirror();
        }
    }
}
