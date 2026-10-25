using System;
using System.Text.Json.Serialization;

namespace LinkSync.Core
{
    public class DeviceHandshake
    {
        [JsonPropertyName("deviceId")]
        public string DeviceId { get; set; } = string.Empty;

        [JsonPropertyName("deviceName")]
        public string DeviceName { get; set; } = string.Empty;

        [JsonPropertyName("deviceManufacturer")]
        public string DeviceManufacturer { get; set; } = string.Empty;

        [JsonPropertyName("androidVersion")]
        public string AndroidVersion { get; set; } = string.Empty;
    }

    public class FileSyncMetadata
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("fileSize")]
        public long FileSize { get; set; }

        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }
    }

    public class FileSyncAck
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "saved";

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }
    }

    public class ContactItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("number")]
        public string Number { get; set; } = string.Empty;

        [JsonPropertyName("starred")]
        public bool Starred { get; set; }
    }

    public class SmsMessageItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("threadId")]
        public string ThreadId { get; set; } = string.Empty;

        [JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("isIncoming")]
        public bool IsIncoming { get; set; }

        [JsonPropertyName("read")]
        public bool Read { get; set; }
    }

    public class IncomingSmsNotification
    {
        [JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("otpCode")]
        public string OtpCode { get; set; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }
    }

    public class SendSmsRequest
    {
        [JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;
    }

    public class CallActionRequest
    {
        [JsonPropertyName("action")]
        public string Action { get; set; } = "DIAL"; // "DIAL", "HANGUP"

        [JsonPropertyName("number")]
        public string Number { get; set; } = string.Empty;
    }

    public class CallStateNotification
    {
        [JsonPropertyName("state")]
        public string State { get; set; } = "IDLE"; // "RINGING", "OFFHOOK", "IDLE"

        [JsonPropertyName("number")]
        public string Number { get; set; } = string.Empty;

        [JsonPropertyName("durationSeconds")]
        public int DurationSeconds { get; set; }
    }
}
