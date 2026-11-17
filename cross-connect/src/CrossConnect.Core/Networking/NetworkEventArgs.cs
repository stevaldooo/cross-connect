using CrossConnect.Core.Protocol;

namespace CrossConnect.Core.Networking;

/// <summary>
/// Arguments for the MessageReceived event raised by both server and client.
/// </summary>
public sealed class MessageReceivedEventArgs : EventArgs
{
    public BaseMessage Message { get; }

    /// <summary>
    /// The remote endpoint description (IP:port) that sent the message.
    /// Useful for the server side where multiple clients may connect.
    /// </summary>
    public string RemoteEndpoint { get; }

    public MessageReceivedEventArgs(BaseMessage message, string remoteEndpoint)
    {
        Message = message;
        RemoteEndpoint = remoteEndpoint;
    }
}

/// <summary>
/// Arguments for connection lifecycle events.
/// </summary>
public sealed class ConnectionEventArgs : EventArgs
{
    public string RemoteEndpoint { get; }
    public string? Reason { get; }

    public ConnectionEventArgs(string remoteEndpoint, string? reason = null)
    {
        RemoteEndpoint = remoteEndpoint;
        Reason = reason;
    }
}
