using System.Text.Json.Serialization;

namespace CrossConnect.Core.Pairing;

/// <summary>
/// The persistent identity of THIS machine.
/// Created once on first launch and stored in AppData.
///
/// DeviceId is a random GUID — it survives machine renames and
/// does not collide with other devices on the network.
/// </summary>
public sealed class DeviceIdentity
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("deviceName")]
    public string DeviceName { get; init; } = Environment.MachineName;
}
