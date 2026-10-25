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
}
