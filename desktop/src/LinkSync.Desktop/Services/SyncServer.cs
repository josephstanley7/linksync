using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LinkSync.Core;

namespace LinkSync.Desktop.Services
{
    public class SyncServer
    {
        private readonly int _port;
        private TcpListener? _listener;
        private TcpClient? _currentClient;
        private NetworkStream? _currentStream;
        private CancellationTokenSource? _cts;

        public event Action<DeviceHandshake>? DeviceConnected;
        public event Action? DeviceDisconnected;
        public event Action<string>? ClipboardReceived;
        public event Action<string, byte[]>? MediaReceived;

        public bool IsClientConnected => _currentClient?.Connected == true;
        public string SaveDirectory { get; }

        public SyncServer(int port = 54884)
        {
            _port = port;
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            SaveDirectory = Path.Combine(pictures, "LinkSync", "Screenshots");
            Directory.CreateDirectory(SaveDirectory);
        }

        public bool Start()
        {
            _cts = new CancellationTokenSource();
            try
            {
                _listener = new TcpListener(IPAddress.Any, _port);
                _listener.Start();
                Task.Run(() => AcceptLoopAsync(_cts.Token));
                return true;
            }
            catch (SocketException ex)
            {
                Debug.WriteLine($"[SyncServer] Socket error on port {_port}: {ex.Message}");
                return false;
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _currentStream?.Close(); } catch { }
            try { _currentClient?.Close(); } catch { }
            try { _listener?.Stop(); } catch { }
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener!.AcceptTcpClientAsync(ct);
                    _currentClient = client;
                    _currentStream = client.GetStream();

                    _ = Task.Run(() => HandleClientAsync(_currentStream, ct), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SyncServer] Accept error: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(NetworkStream stream, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var frame = await LinkSyncProtocol.ReadFrameAsync(stream, ct);
                    if (frame == null) break;

                    switch (frame.OpCode)
                    {
                        case LinkSyncProtocol.OpHandshake:
                            string json = Encoding.UTF8.GetString(frame.Payload);
                            var handshake = JsonSerializer.Deserialize<DeviceHandshake>(json);
                            if (handshake != null)
                            {
                                _ = Task.Run(() => DeviceConnected?.Invoke(handshake));

                                var desktopHandshake = new DeviceHandshake
                                {
                                    DeviceId = DiscoveryBeaconServer.GetOrCreateDeviceId(),
                                    DeviceName = "HP EliteBook Folio 9470M",
                                    DeviceManufacturer = "Hewlett-Packard",
                                    AndroidVersion = "Windows 10 Pro"
                                };
                                byte[] resp = JsonSerializer.SerializeToUtf8Bytes(desktopHandshake);
                                await LinkSyncProtocol.WriteFrameAsync(stream, LinkSyncProtocol.OpHandshake, resp, ct);
                            }
                            break;

                        case LinkSyncProtocol.OpClipboardText:
                            string clipText = Encoding.UTF8.GetString(frame.Payload);
                            ClipboardReceived?.Invoke(clipText);
                            break;

                        case LinkSyncProtocol.OpFileSync:
                            HandleIncomingFile(frame.Payload, stream);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SyncServer] Client disconnected: {ex.Message}");
            }
            finally
            {
                if (_currentStream == stream)
                {
                    _currentStream = null;
                    _currentClient = null;
                    DeviceDisconnected?.Invoke();
                }
            }
        }

        private void HandleIncomingFile(byte[] payload, NetworkStream? stream = null)
        {
            try
            {
                // Format: [4 bytes metaLen] [metaLen bytes JSON] [raw file bytes]
                int metaLen = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0, 4));
                string metaJson = Encoding.UTF8.GetString(payload, 4, metaLen);
                var meta = JsonSerializer.Deserialize<FileSyncMetadata>(metaJson);

                int fileOffset = 4 + metaLen;
                int fileLen = payload.Length - fileOffset;
                byte[] fileBytes = new byte[fileLen];
                Buffer.BlockCopy(payload, fileOffset, fileBytes, 0, fileLen);

                string fileName = meta?.FileName ?? $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
                {
                    fileName += ".png";
                }

                string targetPath = Path.Combine(SaveDirectory, fileName);
                File.WriteAllBytes(targetPath, fileBytes);

                MediaReceived?.Invoke(targetPath, fileBytes);

                // Feature 13: Transmit confirmation ACK to phone so it can safely auto-delete local file if enabled
                if (stream != null)
                {
                    var ack = new FileSyncAck
                    {
                        FileName = fileName,
                        Status = "saved",
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                    byte[] ackBytes = JsonSerializer.SerializeToUtf8Bytes(ack);
                    _ = LinkSyncProtocol.WriteFrameAsync(stream, LinkSyncProtocol.OpFileSyncAck, ackBytes);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SyncServer] File save error: {ex.Message}");
            }
        }

        public async Task SendClipboardAsync(string text)
        {
            var stream = _currentStream;
            if (stream != null && IsClientConnected)
            {
                byte[] payload = Encoding.UTF8.GetBytes(text);
                await LinkSyncProtocol.WriteFrameAsync(stream, LinkSyncProtocol.OpClipboardText, payload);
            }
        }

        public async Task SendFileAsync(string fileName, byte[] fileBytes, string mimeType = "image/png")
        {
            var stream = _currentStream;
            if (stream != null && IsClientConnected)
            {
                var meta = new FileSyncMetadata
                {
                    FileName = fileName,
                    FileSize = fileBytes.Length,
                    MimeType = mimeType,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                byte[] metaBytes = JsonSerializer.SerializeToUtf8Bytes(meta);
                byte[] payload = new byte[4 + metaBytes.Length + fileBytes.Length];
                BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), metaBytes.Length);
                Buffer.BlockCopy(metaBytes, 0, payload, 4, metaBytes.Length);
                Buffer.BlockCopy(fileBytes, 0, payload, 4 + metaBytes.Length, fileBytes.Length);

                await LinkSyncProtocol.WriteFrameAsync(stream, LinkSyncProtocol.OpFileSync, payload);
            }
        }
    }
}
