using System.Text.Json.Serialization;

namespace CrossConnect.Core.Pairing;

/// <summary>
/// A remote device that this machine has successfully paired with.
/// Stored in the trusted device list on disk.
/// </summary>
public sealed class TrustedDevice
{
    [JsonPropertyName("deviceId")]
    public required string DeviceId { get; init; }

    [JsonPropertyName("deviceName")]
    public required string DeviceName { get; set; }

    [JsonPropertyName("pairedAt")]
    public DateTime PairedAt { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("lastSeenAt")]
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
