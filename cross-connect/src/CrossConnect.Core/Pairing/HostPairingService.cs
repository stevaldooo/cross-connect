namespace CrossConnect.Core.Pairing;

/// <summary>
/// Manages the PIN-based pairing flow for the Host role.
///
/// Flow:
///   1. Client sends PAIR_REQUEST (deviceId, deviceName).
///   2. Host checks TrustedDeviceStore:
///      a. Already trusted  → reply PAIR_ACCEPT immediately.
///      b. Unknown device   → generate PIN, raise PinRequested event,
///                            wait for the client to send PAIR_CONFIRM.
///   3. Client sends PAIR_CONFIRM (pin).
///   4. Host validates:
///      a. Correct PIN      → add to trusted store, reply PAIR_ACCEPT.
///      b. Wrong / timeout  → reply PAIR_REJECT.
///
/// One PairingSession is created per active pairing attempt and disposed
/// after the attempt resolves (accepted, rejected, or timed out).
/// </summary>
public sealed class HostPairingService
{
    private readonly ITrustedDeviceStore _store;

    /// <summary>
    /// Raised on the calling thread when a new device requests pairing and needs a PIN displayed.
    /// The UI must show the PIN to the user; the client will time out after PinTimeoutMs.
    /// </summary>
    public event EventHandler<PinRequestedEventArgs>? PinRequested;

    /// <summary>Raised when a pairing attempt completes (accepted or rejected).</summary>
    public event EventHandler<PairingCompletedEventArgs>? PairingCompleted;

    // Active pairing sessions keyed by remote endpoint.
    private readonly Dictionary<string, PairingSession> _sessions = new();
    private readonly object _lock = new();

    public HostPairingService(ITrustedDeviceStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Called by the server when it receives a PAIR_REQUEST from a client.
    /// Returns the action the caller should take (send PAIR_ACCEPT or wait for PIN).
    /// </summary>
    public PairingSessionAction HandlePairRequest(
        string remoteEndpoint, string remoteDeviceId, string remoteDeviceName)
    {
        // Already trusted — skip PIN entirely.
        if (_store.IsTrusted(remoteDeviceId))
        {
            _store.AddOrUpdate(new TrustedDevice
            {
                DeviceId   = remoteDeviceId,
                DeviceName = remoteDeviceName,
                LastSeenAt = DateTime.UtcNow
            });
            PairingCompleted?.Invoke(this, new PairingCompletedEventArgs(
                remoteEndpoint, remoteDeviceId, remoteDeviceName, PairingResult.AlreadyTrusted));
            return PairingSessionAction.AcceptImmediately;
        }

        // New device — generate PIN and wait.
        string pin = GeneratePin();
        var session = new PairingSession(remoteDeviceId, remoteDeviceName, pin);

        lock (_lock)
            _sessions[remoteEndpoint] = session;

        PinRequested?.Invoke(this, new PinRequestedEventArgs(
            remoteEndpoint, remoteDeviceId, remoteDeviceName, pin));

        return PairingSessionAction.WaitForPin;
    }

    /// <summary>
    /// Called by the server when it receives a PAIR_CONFIRM from a client.
    /// Returns the pairing result so the caller can send the appropriate reply.
    /// </summary>
    public PairingResult HandlePairConfirm(string remoteEndpoint, string submittedPin)
    {
        PairingSession? session;
        lock (_lock)
            _sessions.TryGetValue(remoteEndpoint, out session);

        if (session is null)
            return PairingResult.Rejected;

        if (session.Pin != submittedPin.Trim())
        {
            RemoveSession(remoteEndpoint);
            PairingCompleted?.Invoke(this, new PairingCompletedEventArgs(
                remoteEndpoint, session.DeviceId, session.DeviceName, PairingResult.Rejected));
            return PairingResult.Rejected;
        }

        // PIN matched — persist the device.
        _store.AddOrUpdate(new TrustedDevice
        {
            DeviceId   = session.DeviceId,
            DeviceName = session.DeviceName,
            PairedAt   = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        });

        RemoveSession(remoteEndpoint);
        PairingCompleted?.Invoke(this, new PairingCompletedEventArgs(
            remoteEndpoint, session.DeviceId, session.DeviceName, PairingResult.Accepted));
        return PairingResult.Accepted;
    }

    /// <summary>Cancels any active pairing session for a disconnected client.</summary>
    public void CancelSession(string remoteEndpoint) => RemoveSession(remoteEndpoint);

    private void RemoveSession(string endpoint)
    {
        lock (_lock) _sessions.Remove(endpoint);
    }

    /// <summary>Generates a random 6-digit numeric PIN.</summary>
    private static string GeneratePin()
        => Random.Shared.Next(100_000, 999_999).ToString();
}

public enum PairingSessionAction { AcceptImmediately, WaitForPin }

/// <summary>Raised when a pairing attempt on the host finishes.</summary>
public sealed class PairingCompletedEventArgs : EventArgs
{
    public string RemoteEndpoint  { get; }
    public string RemoteDeviceId  { get; }
    public string RemoteDeviceName { get; }
    public PairingResult Result   { get; }

    public PairingCompletedEventArgs(
        string endpoint, string deviceId, string deviceName, PairingResult result)
    {
        RemoteEndpoint   = endpoint;
        RemoteDeviceId   = deviceId;
        RemoteDeviceName = deviceName;
        Result           = result;
    }
}

/// <summary>Holds state for one in-progress pairing attempt.</summary>
internal sealed class PairingSession
{
    public string DeviceId   { get; }
    public string DeviceName { get; }
    public string Pin        { get; }

    public PairingSession(string deviceId, string deviceName, string pin)
    {
        DeviceId   = deviceId;
        DeviceName = deviceName;
        Pin        = pin;
    }
}
