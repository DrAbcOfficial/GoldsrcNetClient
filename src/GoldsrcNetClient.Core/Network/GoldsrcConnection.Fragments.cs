using System.Net;
using ICSharpCode.SharpZipLib.BZip2;
using Microsoft.Extensions.Logging;

namespace GoldsrcNetClient.Core.Network;

public partial class GoldsrcConnection
{
    private void ProcessFragmentCommand(ConnectionContext ctx, IPEndPoint ep, byte[] payload, int payloadLen, ref uint srcSeq, ref uint dstSeq)
    {
        int hdr = 0;
        Logger.LogDebug($"[FragHdr] payloadLen={payloadLen}, firstBytes={BitConverter.ToString(payload, 0, Math.Min(payloadLen, 16))}");
        uint stream0FragId = 0;
        ushort stream0FragLen = 0;
        ushort stream0StartPos = 0;
        uint stream1FragId = 0;
        ushort stream1FragLen = 0;
        ushort stream1StartPos = 0;
        bool stream0Active = false;
        bool stream1Active = false;

        for (int i = 0; i < 2; i++)
        {
            if (hdr >= payloadLen) break;
            byte streamFlag = payload[hdr++];
            Logger.LogDebug($"[FragHdr] stream[{i}] flag={(int)streamFlag}, hdrPos={hdr - 1}");
            if (streamFlag != 0)
            {
                uint fragId = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(hdr));
                hdr += 4;
                ushort startPos = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(hdr));
                hdr += 2;
                ushort fragLen = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(hdr));
                hdr += 2;
                Logger.LogDebug($"[FragHdr] stream[{i}] fragId=0x{fragId:X8}, startPos={startPos}, fragLen={fragLen}, hdrPos={hdr - 4}");

                if (i == 0) { stream0Active = true; stream0FragId = fragId; stream0FragLen = fragLen; stream0StartPos = startPos; }
                else { stream1Active = true; stream1FragId = fragId; stream1FragLen = fragLen; stream1StartPos = startPos; }
            }
        }

        int msgLen = payloadLen - hdr;
        if (msgLen <= 0) return;

        int reliableLen = msgLen;
        int firstFragDataStart = msgLen;

        bool hasStream0Frag = stream0Active && stream0FragLen > 0 && stream0FragId != 0;
        bool hasStream1Frag = stream1Active && stream1FragLen > 0 && stream1FragId != 0;

        if (hasStream0Frag)
        {
            firstFragDataStart = Math.Min(firstFragDataStart, stream0StartPos);
        }
        if (hasStream1Frag)
        {
            firstFragDataStart = Math.Min(firstFragDataStart, stream1StartPos);
        }
        reliableLen = Math.Min(reliableLen, firstFragDataStart);

        if (hasStream0Frag)
        {
            int fragStart = hdr + stream0StartPos;
            int fragLen = Math.Min(stream0FragLen, payloadLen - fragStart);
            if (fragLen > 0)
            {
                byte[] fragChunk = new byte[fragLen];
                Array.Copy(payload, fragStart, fragChunk, 0, fragLen);
                AccumulateFragment(ctx, stream0FragId, fragChunk, Logger);
                if (stream0StartPos == 0)
                    reliableLen = 0;
            }
        }

        if (hasStream1Frag)
        {
            int fragStart = hdr + stream1StartPos;
            int fragLen = Math.Min(stream1FragLen, payloadLen - fragStart);
            if (fragLen > 0)
            {
                byte[] fragChunk = new byte[fragLen];
                Array.Copy(payload, fragStart, fragChunk, 0, fragLen);
                AccumulateFragment(ctx, stream1FragId, fragChunk, Logger);
            }
        }

        byte[]? completedData = TryCompleteFragments(ctx, Logger);
        if (completedData != null)
        {
            _ = SendAckAsync(ep);
            _sessions[ep] = ProcessConnected(ep, ref srcSeq, ref dstSeq, completedData, completedData.Length);
            return;
        }

        if (reliableLen > 0)
        {
            byte[] msgData = new byte[reliableLen];
            Array.Copy(payload, hdr, msgData, 0, reliableLen);
            Logger.LogDebug($"[Fragment] parsed {hdr} bytes of headers, {reliableLen} bytes reliable data");
            _ = SendAckAsync(ep);
            _sessions[ep] = ProcessConnected(ep, ref srcSeq, ref dstSeq, msgData, reliableLen);
        }
        else
        {
            Logger.LogDebug($"[Fragment] parsed {hdr} bytes of headers, accumulating fragment data only");
            _ = SendAckAsync(ep);
        }
    }

    private static void AccumulateFragment(ConnectionContext ctx, uint fragId, byte[] data, ILogger logger)
    {
        int count = (int)(fragId & 0xFFFF);
        int id = (int)((fragId >> 16) & 0xFFFF);

        if (!ctx.FragmentActive || ctx.FragmentTotalCount != count)
        {
            ctx.FragmentActive = true;
            ctx.FragmentTotalCount = count;
            ctx.FragmentChunks.Clear();
            logger.LogDebug($"[FragAccum] new message: totalFragments={count}");
        }

        ctx.FragmentChunks.Add(data);
        logger.LogDebug($"[FragAccum] stored fragment id={id}/{count}, received={ctx.FragmentChunks.Count}/{ctx.FragmentTotalCount}, size={data.Length}");
    }

    private static byte[]? TryCompleteFragments(ConnectionContext ctx, ILogger logger)
    {
        if (!ctx.FragmentActive || ctx.FragmentChunks.Count < ctx.FragmentTotalCount)
            return null;

        logger.LogDebug($"[FragAccum] all {ctx.FragmentChunks.Count} fragments received, reassembling...");

        int totalSize = 0;
        foreach (var c in ctx.FragmentChunks)
            totalSize += c.Length;

        byte[] assembled = new byte[totalSize];
        int pos = 0;
        foreach (var c in ctx.FragmentChunks)
        {
            Array.Copy(c, 0, assembled, pos, c.Length);
            pos += c.Length;
        }

        ctx.FragmentActive = false;
        ctx.FragmentTotalCount = 0;
        ctx.FragmentChunks.Clear();

        if (totalSize > 4 && assembled[0] == 'B' && assembled[1] == 'Z' && assembled[2] == '2' && assembled[3] == 0)
        {
            logger.LogDebug($"[BZ2] decompressing {totalSize} bytes of sign-on data...");
            try
            {
                using var msIn = new MemoryStream(assembled, 4, totalSize - 4);
                using var msOut = new MemoryStream();
                BZip2.Decompress(msIn, msOut, false);
                var decompressed = msOut.ToArray();
                logger.LogDebug($"[BZ2] decompressed to {decompressed.Length} bytes");
                return decompressed;
            }
            catch (Exception ex)
            {
                logger.LogWarning($"[BZ2] decompression failed: {ex.Message}");
                return null;
            }
        }

        return assembled;
    }

    private static bool ScanForBz2(byte[] data, int offset, int size)
    {
        for (int i = offset; i <= size - 4; i++)
        {
            if (data[i] == 'B' && data[i + 1] == 'Z' && data[i + 2] == '2' && data[i + 3] == 0)
                return true;
        }
        return false;
    }
}
