using System.Net;
using System.Net.Sockets;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// The UDP transport seam: everything the connection needs from a socket.
/// The default implementation wraps <see cref="System.Net.Sockets.UdpClient"/>;
/// tests substitute a fake to drive the receive loop without network I/O.
/// In dependency injection, register a <c>Func&lt;ITransport&gt;</c> so every
/// created connection gets its own instance — a transport shared across
/// connections would multiplex them over one socket (and one bound port).
/// <see cref="GoldsrcServiceCollectionExtensions.AddGoldsrcClient"/> registers
/// the default factory over <see cref="UdpTransport"/>.
/// </summary>
public interface ITransport : IDisposable
{
    /// <summary>Sends one datagram to the target endpoint.</summary>
    Task SendAsync(ReadOnlyMemory<byte> buffer, IPEndPoint target, CancellationToken ct);

    /// <summary>Receives the next datagram (buffer + remote endpoint).</summary>
    Task<(byte[] Buffer, IPEndPoint RemoteEndPoint)> ReceiveAsync(CancellationToken ct);
}

/// <summary>
/// Real-socket transport over <see cref="System.Net.Sockets.UdpClient"/> with the
/// large receive buffer the signon flood needs: the burst (delta descriptions +
/// resource list + baselines ≈ 150 KB compressed) plus per-frame unreliable
/// traffic must fit in the OS buffer while the receive loop is momentarily busy.
/// A small buffer silently drops datagrams, which stalls the reliable-stream
/// acknowledgement and ends in a "Reliable channel overflowed" drop.
/// </summary>
public sealed class UdpTransport : ITransport
{
    private readonly UdpClient _socket;

    /// <summary>Binds a new UDP socket to <paramref name="localPort"/> (0 = OS-assigned).</summary>
    public UdpTransport(int localPort)
    {
        _socket = new UdpClient(localPort);
        _socket.Client.ReceiveBufferSize = 4 * 1024 * 1024;
    }

    /// <summary>Creates the transport over an existing bound socket.</summary>
    public UdpTransport(UdpClient socket) => _socket = socket;

    /// <inheritdoc />
    public Task SendAsync(ReadOnlyMemory<byte> buffer, IPEndPoint target, CancellationToken ct)
        => _socket.SendAsync(buffer, target, ct).AsTask();

    /// <inheritdoc />
    public async Task<(byte[] Buffer, IPEndPoint RemoteEndPoint)> ReceiveAsync(CancellationToken ct)
    {
        var result = await _socket.ReceiveAsync(ct);
        return (result.Buffer, result.RemoteEndPoint);
    }

    /// <inheritdoc />
    public void Dispose() => _socket.Dispose();
}
