namespace CrossConnect.Core.Pairing;

/// <summary>
/// Result of a pairing attempt on the host side.
/// </summary>
public enum PairingResult
{
    /// <summary>PIN matched — device is now trusted.</summary>
    Accepted,
    /// <summary>PIN did not match or timed out.</summary>
    Rejected,
    /// <summary>Device was already in the trusted list — no PIN needed.</summary>
    AlreadyTrusted
}

/// <summary>
/// Arguments raised when the host needs to display a PIN to the user.
/// </summary>
public sealed class PinRequestedEventArgs : EventArgs
{
    /// <summary>The endpoint of the device requesting pairing.</summary>
    public string RemoteEndpoint { get; }
    public string RemoteDeviceId { get; }
    public string RemoteDeviceName { get; }

    /// <summary>The 6-digit PIN the user must share with the other machine.</summary>
    public string Pin { get; }

    public PinRequestedEventArgs(string remoteEndpoint, string remoteDeviceId, string remoteDeviceName, string pin)
    {
        RemoteEndpoint  = remoteEndpoint;
        RemoteDeviceId  = remoteDeviceId;
        RemoteDeviceName = remoteDeviceName;
        Pin             = pin;
    }
}
