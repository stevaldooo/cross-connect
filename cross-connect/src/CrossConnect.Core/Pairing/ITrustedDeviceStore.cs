namespace CrossConnect.Core.Pairing;

/// <summary>
/// Persists and retrieves the list of trusted (paired) devices and this machine's identity.
/// </summary>
public interface ITrustedDeviceStore
{
    /// <summary>
    /// Returns this machine's persistent identity, creating and saving it on first call.
    /// </summary>
    DeviceIdentity GetLocalIdentity();

    /// <summary>Returns all trusted devices.</summary>
    IReadOnlyList<TrustedDevice> GetAll();

    /// <summary>Returns true if a device with the given ID is in the trusted list.</summary>
    bool IsTrusted(string deviceId);

    /// <summary>Adds or updates a trusted device and persists the store.</summary>
    void AddOrUpdate(TrustedDevice device);

    /// <summary>Removes a trusted device by ID and persists the store.</summary>
    void Remove(string deviceId);
}
