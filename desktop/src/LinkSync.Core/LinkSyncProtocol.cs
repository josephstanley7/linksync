using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LinkSync.Core
{
    public static class LinkSyncProtocol
    {
        public const byte MagicByte1 = 0x4C; // 'L'
        public const byte MagicByte2 = 0x53; // 'S'

        public const ushort OpHandshake = 0x0001;
        public const ushort OpClipboardText = 0x0010;
        public const ushort OpFileSync = 0x0020;
        public const ushort OpFileReversePush = 0x0021;
        public const ushort OpFileSyncAck = 0x0022;
        public const ushort OpMirrorStart = 0x0030;
        public const ushort OpMirrorStop = 0x0031;
        public const ushort OpVideoStreamData = 0x0032;
        public const ushort OpInputEvent = 0x0033;
        public const ushort OpSmsIncoming = 0x0040;
        public const ushort OpSmsSend = 0x0041;
        public const ushort OpCallAction = 0x0042;
        public const ushort OpContactsReq = 0x0043;
        public const ushort OpContactsResp = 0x0044;
        public const ushort OpSmsListReq = 0x0045;
        public const ushort OpSmsListResp = 0x0046;
        public const ushort OpCallState = 0x0047;
        public const ushort OpDeviceStatus = 0x0050;
        public const ushort OpLockPc = 0x0051;
        public const ushort OpNotification = 0x0060;

        public record Frame(ushort OpCode, byte[] Payload);
        public record PhoneNotificationPayload(string Key, string PackageName, string AppName, string Title, string Text, long Timestamp);

        public static async Task WriteFrameAsync(Stream stream, ushort opCode, byte[] payload, CancellationToken cancellationToken = default)
        {
            byte[] header = new byte[8];
            header[0] = MagicByte1;
            header[1] = MagicByte2;
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2, 2), opCode);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), payload.Length);

            await stream.WriteAsync(header, cancellationToken);
            if (payload.Length > 0)
            {
                await stream.WriteAsync(payload, cancellationToken);
            }
            await stream.FlushAsync(cancellationToken);
        }

        public static async Task<Frame?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            byte[] header = new byte[8];
            int read = 0;
            while (read < 8)
            {
                int r = await stream.ReadAsync(header.AsMemory(read, 8 - read), cancellationToken);
                if (r == 0) return null;
                read += r;
            }

            if (header[0] != MagicByte1 || header[1] != MagicByte2)
            {
                return null;
            }

            ushort opCode = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
            int payloadLen = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4, 4));

            byte[] payload = new byte[payloadLen];
            int payloadRead = 0;
            while (payloadRead < payloadLen)
            {
                int r = await stream.ReadAsync(payload.AsMemory(payloadRead, payloadLen - payloadRead), cancellationToken);
                if (r == 0) return null;
                payloadRead += r;
            }

            return new Frame(opCode, payload);
        }
    }
}
