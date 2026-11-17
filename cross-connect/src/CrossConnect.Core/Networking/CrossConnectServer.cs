using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using CrossConnect.Core.Protocol;

namespace CrossConnect.Core.Networking;

/// <summary>
/// TCP server implementation for the Host role.
///
/// Design notes:
/// - Each accepted client runs its read loop on its own Task (not a thread).
/// - A PING timer fires every PingIntervalMs to check client liveness.
/// - The server cleans up a client session when it disconnects for any reason.
/// - All public events are raised on the Task that detected the event; callers
///   that update UI must marshal back to the UI thread themselves.
/// </summary>
public sealed class CrossConnectServer : ICrossConnectServer
{
    // ── Events ──────────────────────────────────────────────────────────────

    public event EventHandler<ConnectionEventArgs>?    ClientConnected;
    public event EventHandler<ConnectionEventArgs>?    ClientDisconnected;
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    // ── State ────────────────────────────────────────────────────────────────

    private TcpListener?                                     _listener;
    private CancellationTokenSource?                         _cts;
    private readonly ConcurrentDictionary<string, ClientSession> _sessions = new();
    private bool                                             _disposed;

    public bool IsRunning => _listener is not null;

    // ── Public API ───────────────────────────────────────────────────────────

    public async Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning) return;

        _cts      = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        // Accept loop runs in the background; we do not await it here.
        _ = AcceptLoopAsync(_cts.Token);

        // Ping timer — fires every PingIntervalMs to keep connections alive.
        _ = PingLoopAsync(_cts.Token);

        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;

        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;

        // Send DISCONNECT to all clients before closing.
        foreach (var session in _sessions.Values)
            await session.SendAsync(new DisconnectMessage { Reason = "Server stopping" });

        foreach (var session in _sessions.Values)
            session.Dispose();

        _sessions.Clear();
    }

    public async Task SendAsync(string remoteEndpoint, BaseMessage message, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(remoteEndpoint, out var session))
            await session.SendAsync(message, cancellationToken);
    }

    public async Task BroadcastAsync(BaseMessage message, CancellationToken cancellationToken = default)
    {
        var tasks = _sessions.Values.Select(s => s.SendAsync(message, cancellationToken));
        await Task.WhenAll(tasks);
    }

    // ── Internal loops ───────────────────────────────────────────────────────

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcpClient;
            try
            {
                tcpClient = await _listener!.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch { break; }

            // Handle each client on its own background task.
            _ = HandleClientAsync(tcpClient, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken ct)
    {
        var endpoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";
        var session  = new ClientSession(tcpClient, endpoint);

        try
        {
            // ── Handshake: wait for HELLO ─────────────────────────────────
            var line = await session.ReadLineAsync(ct);
            if (line is null || MessageSerializer.Deserialize(line) is not HelloMessage hello)
            {
                // Not a valid HELLO — close immediately.
                session.Dispose();
                return;
            }

            session.DeviceName = hello.DeviceName;

            // Send HELLO_ACK
            await session.SendAsync(new HelloAckMessage
            {
                DeviceName = Environment.MachineName
            }, ct);

            _sessions[endpoint] = session;
            ClientConnected?.Invoke(this, new ConnectionEventArgs(endpoint));

            // ── Read loop ─────────────────────────────────────────────────
            while (!ct.IsCancellationRequested)
            {
                var msgLine = await session.ReadLineAsync(ct);
                if (msgLine is null) break; // connection closed

                var message = MessageSerializer.Deserialize(msgLine);
                if (message is null) continue; // unknown type, skip

                if (message is PingMessage)
                {
                    await session.SendAsync(new PongMessage(), ct);
                    continue;
                }

                if (message is DisconnectMessage)
                    break;

                MessageReceived?.Invoke(this, new MessageReceivedEventArgs(message, endpoint));
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch { /* network error — treat as disconnect */ }
        finally
        {
            _sessions.TryRemove(endpoint, out _);
            session.Dispose();
            ClientDisconnected?.Invoke(this, new ConnectionEventArgs(endpoint));
        }
    }

    private async Task PingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ProtocolConstants.PingIntervalMs, ct);
                await BroadcastAsync(new PingMessage(), ct);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    // ── IAsyncDisposable ─────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
        _cts?.Dispose();
    }
}
