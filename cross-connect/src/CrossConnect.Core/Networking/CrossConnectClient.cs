using System.Net.Sockets;
using System.Text;
using CrossConnect.Core.Protocol;

namespace CrossConnect.Core.Networking;

/// <summary>
/// TCP client implementation for the Client role.
///
/// Design notes:
/// - ConnectAsync performs the HELLO handshake before raising the Connected event.
/// - The read loop runs on a background Task; callers must dispatch UI updates themselves.
/// - PING messages from the host are answered automatically with PONG.
/// - On any network error the Disconnected event is raised once.
/// </summary>
public sealed class CrossConnectClient : ICrossConnectClient
{
    // ── Events ──────────────────────────────────────────────────────────────

    public event EventHandler<ConnectionEventArgs>?      Connected;
    public event EventHandler<ConnectionEventArgs>?      Disconnected;
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    // ── State ────────────────────────────────────────────────────────────────

    private TcpClient?             _tcpClient;
    private NetworkStream?         _stream;
    private StreamReader?          _reader;
    private readonly SemaphoreSlim _writeLock  = new(1, 1);
    private CancellationTokenSource? _cts;
    private string                 _hostEndpoint = string.Empty;
    private bool                   _disposed;

    public bool IsConnected => _tcpClient?.Connected == true;

    // ── Public API ───────────────────────────────────────────────────────────

    public async Task ConnectAsync(
        string host,
        int port,
        string localDeviceName,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsConnected) return;

        _tcpClient    = new TcpClient();
        _cts          = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _hostEndpoint = $"{host}:{port}";

        await _tcpClient.ConnectAsync(host, port, _cts.Token);

        _stream = _tcpClient.GetStream();
        _reader = new StreamReader(_stream, Encoding.UTF8);

        // ── Handshake ────────────────────────────────────────────────────────
        await SendRawAsync(new HelloMessage { DeviceName = localDeviceName }, _cts.Token);

        var line = await _reader.ReadLineAsync(_cts.Token);
        if (line is null || MessageSerializer.Deserialize(line) is not HelloAckMessage)
        {
            await CleanupAsync();
            throw new InvalidOperationException("Host did not send a valid HELLO_ACK.");
        }

        Connected?.Invoke(this, new ConnectionEventArgs(_hostEndpoint));

        // Start background read loop.
        _ = ReadLoopAsync(_cts.Token);
    }

    public async Task SendAsync(BaseMessage message, CancellationToken cancellationToken = default)
        => await SendRawAsync(message, cancellationToken);

    public async Task DisconnectAsync()
    {
        if (!IsConnected) return;
        await SendRawAsync(new DisconnectMessage { Reason = "Client disconnecting" });
        await CleanupAsync();
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _reader!.ReadLineAsync(ct);
                if (line is null) break; // connection closed by host

                var message = MessageSerializer.Deserialize(line);
                if (message is null) continue;

                if (message is PingMessage)
                {
                    await SendRawAsync(new PongMessage(), ct);
                    continue;
                }

                if (message is DisconnectMessage)
                    break;

                MessageReceived?.Invoke(this, new MessageReceivedEventArgs(message, _hostEndpoint));
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch { /* network error */ }
        finally
        {
            var endpoint = _hostEndpoint;
            await CleanupAsync();
            Disconnected?.Invoke(this, new ConnectionEventArgs(endpoint));
        }
    }

    private async Task SendRawAsync(BaseMessage message, CancellationToken ct = default)
    {
        if (_stream is null) return;
        byte[] data = MessageSerializer.Serialize(message);

        await _writeLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(data, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task CleanupAsync()
    {
        _cts?.Cancel();
        _reader?.Dispose();
        if (_stream is not null) await _stream.DisposeAsync();
        _tcpClient?.Dispose();
        _tcpClient = null;
        _stream    = null;
        _reader    = null;
    }

    // ── IAsyncDisposable ─────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DisconnectAsync();
        _writeLock.Dispose();
        _cts?.Dispose();
    }
}
