using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LinkSync.Desktop.Services
{
    /// <summary>
    /// Broadcasts UDP discovery beacons on local Wi-Fi and replies to discovery pings from mobile devices.
    /// Completely eliminates any hardcoded IP addresses and supports Android mobile hotspots via direct Gateway routing.
    /// </summary>
    public class DiscoveryBeaconServer : IDisposable
    {
        private const int DiscoveryPort = 54885;
        private readonly int _tcpPort;
        private readonly string _deviceName;
        private readonly string _deviceId;
        private UdpClient? _udpClient;
        private CancellationTokenSource? _cts;
        private bool _isDisposed;

        public string DeviceId => _deviceId;
        public string DeviceName => _deviceName;

        public DiscoveryBeaconServer(int tcpPort = 54884, string? deviceName = null)
        {
            _tcpPort = tcpPort;
            _deviceName = deviceName ?? Environment.MachineName;
            _deviceId = GetOrCreateDeviceId();
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                _udpClient = new UdpClient();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.EnableBroadcast = true;
                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

                // 1. Listen for mobile discovery requests
                Task.Run(() => ListenLoopAsync(_udpClient, token), token);

                // 2. Broadcast periodic beacons every 2.0 seconds
                Task.Run(() => BroadcastLoopAsync(_udpClient, token), token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DiscoveryBeaconServer] Error starting UDP discovery: {ex.Message}");
            }
        }

        private async Task ListenLoopAsync(UdpClient client, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await client.ReceiveAsync(token);
                    string message = Encoding.UTF8.GetString(result.Buffer);

                    if (message.StartsWith("LINKSYNC_PING"))
                    {
                        // Reply with machine name, TCP sync port, and unique Device ID
                        byte[] response = Encoding.UTF8.GetBytes($"LINKSYNC_PONG|{_deviceName}|{_tcpPort}|{_deviceId}");
                        await client.SendAsync(response, response.Length, result.RemoteEndPoint);
                        Debug.WriteLine($"[DiscoveryBeaconServer] Responded to mobile ping from {result.RemoteEndPoint}");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DiscoveryBeaconServer] Receive error: {ex.Message}");
                    await Task.Delay(1000, token);
                }
            }
        }

        private async Task BroadcastLoopAsync(UdpClient client, CancellationToken token)
        {
            byte[] beacon = Encoding.UTF8.GetBytes($"LINKSYNC_DISCOVERY|{_deviceName}|{_tcpPort}|{_deviceId}");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var targets = GetDiscoveryTargetAddresses();
                    foreach (var target in targets)
                    {
                        try
                        {
                            var endpoint = new IPEndPoint(target, DiscoveryPort);
                            await client.SendAsync(beacon, beacon.Length, endpoint);
                        }
                        catch { }
                    }

                    await Task.Delay(1200, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DiscoveryBeaconServer] Broadcast error: {ex.Message}");
                    await Task.Delay(1500, token);
                }
            }
        }

        public void TriggerBroadcastNow()
        {
            if (_udpClient == null || _cts == null || _cts.IsCancellationRequested) return;
            Task.Run(async () =>
            {
                try
                {
                    byte[] beacon = Encoding.UTF8.GetBytes($"LINKSYNC_DISCOVERY|{_deviceName}|{_tcpPort}|{_deviceId}");
                    var targets = GetDiscoveryTargetAddresses();
                    foreach (var target in targets)
                    {
                        try
                        {
                            var endpoint = new IPEndPoint(target, DiscoveryPort);
                            await _udpClient.SendAsync(beacon, beacon.Length, endpoint);
                        }
                        catch { }
                    }
                }
                catch { }
            });
        }

        private static List<IPAddress> GetDiscoveryTargetAddresses()
        {
            var list = new List<IPAddress> { IPAddress.Broadcast };
            try
            {
                foreach (var netInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (netInterface.OperationalStatus != OperationalStatus.Up) continue;
                    var ipProps = netInterface.GetIPProperties();

                    // 1. Calculate subnet directed broadcast
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork && addr.IPv4Mask != null)
                        {
                            byte[] ipBytes = addr.Address.GetAddressBytes();
                            byte[] maskBytes = addr.IPv4Mask.GetAddressBytes();
                            byte[] broadcastBytes = new byte[ipBytes.Length];
                            for (int i = 0; i < ipBytes.Length; i++)
                            {
                                broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                            }
                            var bcastAddr = new IPAddress(broadcastBytes);
                            if (!list.Contains(bcastAddr))
                            {
                                list.Add(bcastAddr);
                            }
                        }
                    }

                    // 2. Default Gateways (Crucial for Mobile Hotspots: the phone IS the default gateway!)
                    foreach (var gw in ipProps.GatewayAddresses)
                    {
                        if (gw.Address.AddressFamily == AddressFamily.InterNetwork && 
                            !gw.Address.Equals(IPAddress.Any) && 
                            !gw.Address.Equals(IPAddress.None))
                        {
                            if (!list.Contains(gw.Address))
                            {
                                list.Add(gw.Address);
                            }
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        public static string GetOrCreateDeviceId()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkSync");
                Directory.CreateDirectory(dir);
                string idFile = Path.Combine(dir, "device_id.txt");
                if (File.Exists(idFile))
                {
                    string id = File.ReadAllText(idFile).Trim();
                    if (!string.IsNullOrEmpty(id)) return id;
                }

                string newId = "PC-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
                File.WriteAllText(idFile, newId);
                return newId;
            }
            catch
            {
                return "PC-DEFAULT";
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _cts?.Cancel();
            try { _udpClient?.Close(); } catch { }
            try { _udpClient?.Dispose(); } catch { }
            _cts?.Dispose();
        }
    }
}
