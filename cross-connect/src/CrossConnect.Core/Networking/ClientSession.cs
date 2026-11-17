using System.Net.Sockets;
using System.Text;
using CrossConnect.Core.Protocol;

namespace CrossConnect.Core.Networking;

/// <summary>
/// Represents a single connected client managed by CrossConnectServer.
/// Wraps the TcpClient and its NetworkStream with a lock-protected write path.
/// </summary>
internal sealed class ClientSession : IDisposable
{
    private readonly TcpClient     _tcpClient;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly StreamReader  _reader;
    private bool                   _disposed;

    public string Endpoint   { get; }
    public string DeviceName { get; set; } = string.Empty;

    public ClientSession(TcpClient tcpClient, string endpoint)
    {
        _tcpClient = tcpClient;
        _stream    = tcpClient.GetStream();
        _reader    = new StreamReader(_stream, Encoding.UTF8);
        Endpoint   = endpoint;
    }

    /// <summary>
    /// Reads one newline-terminated line from the stream.
    /// Returns null when the stream is closed.
    /// </summary>
    public async Task<string?> ReadLineAsync(CancellationToken ct = default)
        => await _reader.ReadLineAsync(ct);

    /// <summary>
    /// Thread-safe write: serializes the message and writes the newline-delimited bytes.
    /// </summary>
    public async Task SendAsync(BaseMessage message, CancellationToken ct = default)
    {
        if (_disposed) return;
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reader.Dispose();
        _stream.Dispose();
        _tcpClient.Dispose();
        _writeLock.Dispose();
    }
}
