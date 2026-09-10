using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.Core.Util;
using Microsoft.Extensions.Logging;

namespace GoldsrcNetClient.Core.Network;

public partial class GoldsrcConnection
{
    /// <summary>
    /// Background task that transmits a packet at a fixed interval. Each packet carries
    /// acknowledgements for the server, retransmits unacknowledged reliable payload,
    /// and keeps the netchan alive on the server's timeout radar.
    /// </summary>
    private void StartKeepAliveTask()
    {
        if (_moveCts != null) return;
        _moveCts = new CancellationTokenSource();
        var token = _moveCts.Token;
        _moveTask = Task.Run(async () =>
        {
            Logger.LogDebug("[KeepAlive] starting keepalive task");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(Settings.MoveIntervalMs, token);
                    var ep = _activeEndpoint;
                    if (ep != null && _contexts.ContainsKey(ep))
                        SendKeepAlive(ep);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Logger.LogWarning($"[KeepAlive] error: {ex.Message}");
                }
            }
            Logger.LogDebug("[KeepAlive] keepalive task stopped");
        }, token);
    }

    internal byte[] BuildMovePayload()
    {
        var data = new byte[32];
        int bitIdx = 0;
        int destSize = data.Length;

        BitWriter.WriteBits(1u, 8, data, ref bitIdx, destSize);

        BitWriter.WriteBits(1u, 1, data, ref bitIdx, destSize);

        BitWriter.WriteBits(0u, 9, data, ref bitIdx, destSize);
        BitWriter.WriteBits(0u, 8, data, ref bitIdx, destSize);
        BitWriter.WriteBits(0u, 16, data, ref bitIdx, destSize);
        BitWriter.WriteBits(0u, 16, data, ref bitIdx, destSize);
        BitWriter.WriteBits(0u, 16, data, ref bitIdx, destSize);
        BitWriter.WriteBits(0u, 12, data, ref bitIdx, destSize);
        BitWriter.WriteBits(0u, 12, data, ref bitIdx, destSize);
        BitWriter.WriteBits(0u, 12, data, ref bitIdx, destSize);
        BitWriter.WriteBits((uint)MoveButtons, 16, data, ref bitIdx, destSize);
        BitWriter.WriteBits(0u, 8, data, ref bitIdx, destSize);
        BitWriter.WriteBits(0u, 8, data, ref bitIdx, destSize);

        int byteSize = bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
        if (byteSize < data.Length) data[byteSize] = 0;
        byteSize++;

        var result = new byte[byteSize];
        Array.Copy(data, result, byteSize);
        return result;
    }
}
