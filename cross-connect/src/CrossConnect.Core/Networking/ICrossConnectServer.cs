using CrossConnect.Core.Protocol;

namespace CrossConnect.Core.Networking;

/// <summary>
/// Contract for the CrossConnect TCP server (Host role).
/// Implementations must be thread-safe with respect to their events.
/// </summary>
public interface ICrossConnectServer : IAsyncDisposable
{
    /// <summary>Raised when a remote client has connected and completed the HELLO handshake.</summary>
    event EventHandler<ConnectionEventArgs>? ClientConnected;

    /// <summary>Raised when a remote client has disconnected.</summary>
    event EventHandler<ConnectionEventArgs>? ClientDisconnected;

    /// <summary>Raised when a protocol message is received from any connected client.</summary>
    event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    /// <summary>Start listening for incoming connections on the given port.</summary>
    Task StartAsync(int port, CancellationToken cancellationToken = default);

    /// <summary>Stop accepting new connections and close all active ones.</summary>
    Task StopAsync();

    /// <summary>Send a message to a specific connected client identified by its endpoint string.</summary>
    Task SendAsync(string remoteEndpoint, BaseMessage message, CancellationToken cancellationToken = default);

    /// <summary>Broadcast a message to all currently connected clients.</summary>
    Task BroadcastAsync(BaseMessage message, CancellationToken cancellationToken = default);

    bool IsRunning { get; }
}
