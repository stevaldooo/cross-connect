using CrossConnect.Core.Protocol;

namespace CrossConnect.Core.Networking;

/// <summary>
/// Contract for the CrossConnect TCP client (Client role).
/// </summary>
public interface ICrossConnectClient : IAsyncDisposable
{
    /// <summary>Raised after the HELLO/HELLO_ACK handshake completes successfully.</summary>
    event EventHandler<ConnectionEventArgs>? Connected;

    /// <summary>Raised when the connection to the host is lost or deliberately closed.</summary>
    event EventHandler<ConnectionEventArgs>? Disconnected;

    /// <summary>Raised when a protocol message is received from the host.</summary>
    event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    /// <summary>Connect to a host and perform the HELLO handshake.</summary>
    Task ConnectAsync(string host, int port, string localDeviceName, CancellationToken cancellationToken = default);

    /// <summary>Send a message to the host.</summary>
    Task SendAsync(BaseMessage message, CancellationToken cancellationToken = default);

    /// <summary>Gracefully disconnect from the host.</summary>
    Task DisconnectAsync();

    bool IsConnected { get; }
}
