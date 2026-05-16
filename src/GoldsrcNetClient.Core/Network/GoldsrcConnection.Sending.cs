using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Munge;
using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.Core.Util;
using Microsoft.Extensions.Logging;
using System.Net;

namespace GoldsrcNetClient.Core.Network;

public partial class GoldsrcConnection
{
    private async Task SendRawAsync(IPEndPoint ep, byte[] data, CancellationToken ct)
    {
        var ctx = _contexts[ep];
        uint srcSeq = ctx.SrcSequence++;

        var payload = new byte[data.Length + MessageConstants.ConnectedHeadSize];
        BitConverter.GetBytes(srcSeq | MessageConstants.SequenceModeCommand).CopyTo(payload, 0);
        BitConverter.GetBytes(ctx.DstSequence & MessageConstants.SequenceMask).CopyTo(payload, 4);
        data.CopyTo(payload, MessageConstants.ConnectedHeadSize);

        Logger.LogDebug($"[SendRaw] srcSeq={srcSeq}, dstSeq={ctx.DstSequence}, totalLen={payload.Length}");
        await _socket.SendAsync(new ReadOnlyMemory<byte>(payload), ep, ct);
    }

    private async Task SendAckAsync(IPEndPoint ep)
    {
        var ctx = _contexts[ep];
        uint srcSeq = ctx.SrcSequence++;
        var ackPacket = new byte[MessageConstants.ConnectedHeadSize];
        BitConverter.GetBytes(srcSeq | MessageConstants.SequenceModeCommand).CopyTo(ackPacket, 0);
        BitConverter.GetBytes(ctx.DstSequence & MessageConstants.SequenceMask).CopyTo(ackPacket, 4);
        Logger.LogDebug($"[SendAck] srcSeq={srcSeq}, dstSeq={ctx.DstSequence}");
        await _socket.SendAsync(new ReadOnlyMemory<byte>(ackPacket), ep, CancellationToken.None);
    }

    private void StartMoveTask()
    {
        if (_moveCts != null) return;
        _moveCts = new CancellationTokenSource();
        var token = _moveCts.Token;
        _moveTask = Task.Run(async () =>
        {
            Logger.LogDebug("[Move] starting move task");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await SendMoveAsync(token);
                    await Task.Delay(Settings.MoveIntervalMs, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Logger.LogWarning($"[Move] error: {ex.Message}");
                }
            }
            Logger.LogDebug("[Move] move task stopped");
        }, token);
    }

    private async Task SendMoveAsync(CancellationToken ct)
    {
        if (_activeEndpoint == null) return;
        await SendCommandAsync(ClientCommandType.Nop, [], ct);
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

    internal static List<byte> MungeBytes(List<byte> data, int seq)
    {
        var arr = data.ToArray();
        MungeEngine.Munge2(arr, arr.Length, seq);
        return [.. arr];
    }
}
