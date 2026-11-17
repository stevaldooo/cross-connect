namespace CrossConnect.Core.Protocol;

/// <summary>
/// Shared constants for the CrossConnect protocol.
/// </summary>
public static class ProtocolConstants
{
    /// <summary>Current protocol version. Increment this on breaking changes.</summary>
    public const int Version = 1;

    /// <summary>Default TCP port CrossConnect listens on.</summary>
    public const int DefaultPort = 55400;

    /// <summary>How often the host sends a PING to connected clients (milliseconds).</summary>
    public const int PingIntervalMs = 5000;

    /// <summary>How long to wait for a PONG before considering the connection dead (milliseconds).</summary>
    public const int PingTimeoutMs = 10000;
}
