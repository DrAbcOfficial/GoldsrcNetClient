using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Munge;
using GoldsrcNetClient.Core.Protocol;
using ICSharpCode.SharpZipLib.BZip2;
using Microsoft.Extensions.Logging;
using System.Buffers.Binary;
using System.Net;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// Sequenced-channel (netchan) implementation: header flags, Munge2 decryption,
/// reliable-message acknowledgement and retransmission, fragment-stream reassembly,
/// and keepalive packet layout.
/// </summary>
public partial class GoldsrcConnection
{
    /// <summary>Bit 31 of the sequence field: the packet carries reliable payload data.</summary>
    internal const uint SequenceFlagReliable = 0x80000000;

    /// <summary>Bit 30 of the sequence field: the packet contains fragment stream headers.</summary>
    internal const uint SequenceFlagFragment = 0x40000000;

    /// <summary>Outgoing packets smaller than this are padded with <c>svc_nop</c> bytes
    /// (some networks misbehave on tiny UDP datagrams).</summary>
    internal const int MinPacketSize = 16;

    /// <summary>Serializes netchan state between the receive loop, keepalive task, and senders.</summary>
    private readonly object _chanLock = new();

    /// <summary>
    /// Receives and processes one connected (sequenced) packet:
    /// decrypts the payload, drops duplicates/out-of-order packets, updates acknowledgement
    /// state, reassembles fragment streams, and dispatches message data.
    /// </summary>
    private void ProcessNetchanPacket(IPEndPoint ep, byte[] datagram)
    {
        var ctx = _contexts[ep];
        uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(datagram);
        uint sequenceAck = BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(4));
        int payloadLen = datagram.Length - MessageConstants.ConnectedHeadSize;

        if (payloadLen <= 0)
            return;

        bool reliableMessage = (sequence & SequenceFlagReliable) != 0;
        bool containsFragments = (sequence & SequenceFlagFragment) != 0;
        uint reliableAck = sequenceAck >> 31;
        uint seq = sequence & MessageConstants.SequenceMask;
        uint ack = sequenceAck & MessageConstants.SequenceMask;

        // The payload of every connected packet is Munge2-encrypted with the low byte
        // of the (flagged) sequence value as key.
        byte[] payload = new byte[payloadLen];
        Array.Copy(datagram, MessageConstants.ConnectedHeadSize, payload, 0, payloadLen);
        MungeEngine.UnMunge2(payload, payloadLen, (int)(sequence & 0xFF));

        // Read fragment stream headers (they precede the message data).
        int headerBytes = 0;
        var fragHeaders = new (uint fragId, int offset, int length)[MessageConstants.MaxFragmentStreams];
        bool[] streamActive = new bool[MessageConstants.MaxFragmentStreams];

        if (containsFragments)
        {
            int pos = 0;
            bool valid = true;
            for (int s = 0; s < MessageConstants.MaxFragmentStreams && valid; s++)
            {
                if (pos >= payloadLen) { valid = false; break; }
                byte streamFlag = payload[pos++];
                if (streamFlag == 0)
                    continue;

                if (pos + 8 > payloadLen) { valid = false; break; }
                uint fragId = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(pos)); pos += 4;
                int fragOffset = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(pos)); pos += 2;
                int fragLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(pos)); pos += 2;

                if (pos + fragOffset + fragLength > payloadLen) { valid = false; break; }
                fragHeaders[s] = (fragId, fragOffset, fragLength);
                streamActive[s] = true;
            }

            if (!valid)
            {
                Logger.LogWarning("[Netchan] invalid fragment headers in packet seq={Seq} from {Ep}", seq, ep);
                return;
            }

            headerBytes = pos;
        }

        lock (_chanLock)
        {
            if (seq <= ctx.IncomingSequence)
            {
                Logger.LogDebug("[Netchan] duplicate/out-of-order packet seq={Seq} (incoming={Incoming})", seq, ctx.IncomingSequence);
                return;
            }

            if (seq > ctx.IncomingSequence + 1)
                Logger.LogDebug("[Netchan] dropped {Count} packet(s) before seq={Seq}", seq - ctx.IncomingSequence - 1, seq);

            Logger.LogDebug("[Netchan] rx seq={Seq} reliable={Reliable} frag={Frag} relAck={RelAck} | inReliable={InReliable}", seq, reliableMessage, containsFragments, reliableAck, ctx.IncomingReliableSequence);

            // Server acknowledged our pending reliable message?
            if (reliableAck == ctx.OutgoingReliableSequence
                && ctx.IncomingAcknowledged + 1 >= ctx.LastReliableSequence)
            {
                Logger.LogDebug("[Netchan] reliable payload acked (reliableSeq={ReliableSeq})", ctx.OutgoingReliableSequence);
                ctx.PendingReliable = null;
            }

            ctx.IncomingSequence = seq;
            ctx.IncomingAcknowledged = ack;

            // Track the server's reliable-message counter. The server toggles it once
            // per NEW reliable transmission and we toggle on every reliable packet
            // received; if the server is retransmitting a message our bit keeps
            // alternating, which brings the acknowledgement back into phase within a
            // couple of copies — the same self-correcting behaviour as the engine.
            if (reliableMessage)
            {
                ctx.IncomingReliableSequence ^= 1;
            }
            // Copy out fragment chunk data, then strip it from the payload so the
            // inline message stream stays contiguous.
            for (int s = 0; s < MessageConstants.MaxFragmentStreams; s++)
            {
                if (!streamActive[s])
                    continue;

                var (fragId, fragOffset, fragLength) = fragHeaders[s];
                int chunkStart = headerBytes + fragOffset;

                byte[] chunk = new byte[fragLength];
                Array.Copy(payload, chunkStart, chunk, 0, fragLength);

                // Adjust offsets of later streams before removing this chunk's bytes.
                for (int j = s + 1; j < MessageConstants.MaxFragmentStreams; j++)
                {
                    if (streamActive[j])
                        fragHeaders[j].offset -= fragLength;
                }

                Array.Copy(payload, chunkStart + fragLength, payload, chunkStart, payloadLen - chunkStart - fragLength);
                payloadLen -= fragLength;

                StoreFragmentChunk(ctx.FragmentStreams[s], fragId, chunk);
            }

            // Inline message stream (after fragment headers, with chunk data removed).
            if (payloadLen > headerBytes)
                ProcessConnected(ep, payload[headerBytes..payloadLen]);
        }

        // Deliver any fragment streams that just completed (normal stream first, then file stream).
        for (int s = 0; s < MessageConstants.MaxFragmentStreams; s++)
        {
            var st = ctx.FragmentStreams[s];
            if (!st.IsComplete)
                continue;

            byte[] assembled = AssembleFragments(st);
            st.Reset();
            if (assembled.Length == 0)
                continue;

            if (s == 1)
            {
                // File transfer stream: only used for custom resource downloads,
                // which this client does not request. Discard.
                Logger.LogWarning("[Netchan] discarding {Length} bytes on the file fragment stream", assembled.Length);
                continue;
            }

            byte[]? message = TryDecompressBz2(assembled);
            if (message != null)
                ProcessConnected(ep, message);        }

        // Acknowledge every packet as soon as it is processed, exactly like the engine
        // client (which emits an ack on its next frame). Acking only on the keepalive
        // cadence lets the server's outgoing reliable buffer grow faster than it is
        // drained during the sign-on flood, which ends in a "Reliable channel
        // overflowed" drop.
        SendAckPacket(ep);
    }

    /// <summary>Sends an acknowledgement-only packet for the current incoming sequences.</summary>
    private void SendAckPacket(IPEndPoint ep)
    {
        lock (_chanLock)
        {
            if (_contexts.TryGetValue(ep, out var ctx))
                TransmitLocked(ep, ctx, null, CancellationToken.None);
        }
    }

    /// <summary>
    /// Stores one fragment chunk on a stream. Chunks must carry sequentially increasing
    /// buffer ids starting at 1; gaps reset the transfer so the server's retransmission
    /// can refill it.
    /// </summary>
    private static void StoreFragmentChunk(FragmentStreamState st, uint fragId, byte[] chunk)
    {
        int id = (int)((fragId >> 16) & 0xFFFF);
        int totalCount = (int)(fragId & 0xFFFF);

        if (!st.Active || st.TotalCount != totalCount)
        {
            st.Reset();
            st.Active = true;
            st.TotalCount = totalCount;
        }

        if (id < 1 || totalCount < 1 || id > totalCount)
            return;

        if (id == st.Chunks.Count + 1)
            st.Chunks.Add(chunk);
        else if (id <= st.Chunks.Count)
            st.Chunks[id - 1] = chunk; // duplicate retransmission: overwrite
        // else: gap — dropped; the server re-sends the whole fragment set until acked.
    }

    private static byte[] AssembleFragments(FragmentStreamState st)
    {
        int total = 0;
        foreach (var c in st.Chunks)
            total += c.Length;

        var assembled = new byte[total];
        int pos = 0;
        foreach (var c in st.Chunks)
        {
            Array.Copy(c, 0, assembled, pos, c.Length);
            pos += c.Length;
        }
        return assembled;
    }

    /// <summary>Decompresses a "BZ2\0"-prefixed payload; returns the input unchanged otherwise.</summary>
    private static byte[]? TryDecompressBz2(byte[] data)
    {
        if (data.Length <= 4 || data[0] != 'B' || data[1] != 'Z' || data[2] != '2' || data[3] != 0)
            return data;

        try
        {
            using var msIn = new MemoryStream(data, 4, data.Length - 4);
            using var msOut = new MemoryStream();
            BZip2.Decompress(msIn, msOut, false);
            return msOut.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Hashes a short prefix of an inline reliable payload. The reliable portion is
    /// always written first by the sender, so a retransmission shares this prefix
    /// even when fresh unreliable frame data follows it.
    /// </summary>
    private static long Fingerprint(byte[] payload, int len)
    {
        long hash = 1469598103934665603L;
        int n = Math.Min(len, 16);
        for (int i = 0; i < n; i++)
        {
            hash ^= payload[i];
            hash *= 1099511628211L;
        }
        hash ^= n;
        hash *= 1099511628211L;
        return hash;
    }

    /// <summary>Minimum interval between retransmissions of the same reliable payload.
    /// The payload rides every keepalive in the reference engine, but each received
    /// copy toggles the server's acknowledgement counter, so limiting retransmission
    /// frequency keeps the acknowledgement parity clean on lossy, high-latency links.</summary>
    internal static readonly int ReliableRetransmitMs = 500;

    /// <summary>
    /// Builds and sends one sequenced packet: ack header, pending reliable payload
    /// (pulling the next queued reliable message if the slot is free), optional extra
    /// unreliable payload, and <c>svc_nop</c> padding up to the minimum packet size.
    /// Must be called while holding <see cref="_chanLock"/>.
    /// </summary>
    private void TransmitLocked(IPEndPoint ep, ConnectionContext ctx, byte[]? unreliablePayload, CancellationToken ct)
    {
        bool pulled = false;
        if (ctx.PendingReliable == null && ctx.ReliableQueue.Count > 0)
        {
            ctx.PendingReliable = ctx.ReliableQueue.Dequeue();
            ctx.OutgoingReliableSequence ^= 1;
            pulled = true;
        }

        // A freshly queued message is transmitted immediately; retransmissions of an
        // unacknowledged message are throttled.
        bool sendReliable = ctx.PendingReliable != null &&
                            (pulled || Environment.TickCount64 - ctx.LastReliableTransmitMs >= ReliableRetransmitMs);
        if (sendReliable)
        {
            ctx.LastReliableSequence = ctx.SrcSequence;
            ctx.LastReliableTransmitMs = Environment.TickCount64;
        }

        bool includePayload = sendReliable && ctx.PendingReliable != null;

        uint w1 = ctx.SrcSequence & MessageConstants.SequenceMask;
        if (sendReliable)
            w1 |= SequenceFlagReliable;
        uint w2 = (ctx.IncomingSequence & MessageConstants.SequenceMask)
                  | (ctx.IncomingReliableSequence << 31);

        ctx.SrcSequence = (ctx.SrcSequence & MessageConstants.SequenceMask) + 1;

        var reliableBytes = includePayload ? ctx.PendingReliable : null;
        int bodyLen = (reliableBytes?.Length ?? 0) + (unreliablePayload?.Length ?? 0);
        int totalLen = MessageConstants.ConnectedHeadSize + bodyLen;
        var packet = new byte[Math.Max(totalLen, MinPacketSize)];

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0), w1);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), w2);

        int pos = MessageConstants.ConnectedHeadSize;
        if (reliableBytes != null)
        {
            reliableBytes.CopyTo(packet, pos);
            pos += reliableBytes.Length;
        }
        if (unreliablePayload != null)
        {
            unreliablePayload.CopyTo(packet, pos);
            pos += unreliablePayload.Length;
        }

        // Pad undersized packets with svc_nop bytes (the server parses them as no-ops).
        for (; pos < packet.Length; pos++)
            packet[pos] = (byte)ServerMessageType.Nop;

        // Encrypt the whole body (payload + padding) with the sequence's low byte as key —
        // the server unconditionally decrypts every connected packet.
        int bodySize = packet.Length - MessageConstants.ConnectedHeadSize;
        byte[] body = packet[MessageConstants.ConnectedHeadSize..];
        MungeEngine.Munge2(body, bodySize, (int)(w1 & 0xFF));
        body.CopyTo(packet, MessageConstants.ConnectedHeadSize);

        Logger.LogDebug("[Netchan] tx seq={Seq} ack={Ack} reliable={Reliable} ackReliable={AckReliable} bytes={Len} pending={Pending}",
            w1 & MessageConstants.SequenceMask, w2 & MessageConstants.SequenceMask, sendReliable,
            ctx.IncomingReliableSequence, packet.Length, ctx.PendingReliable != null);

        _socket.SendAsync(new ReadOnlyMemory<byte>(packet), ep, ct).GetAwaiter().GetResult();
    }

    /// <summary>Queues a reliable message for transmission and sends a packet immediately.</summary>
    private void SendReliable(IPEndPoint ep, byte[] payload)
    {
        lock (_chanLock)
        {
            var ctx = _contexts[ep];
            ctx.ReliableQueue.Enqueue(payload);
            TransmitLocked(ep, ctx, null, CancellationToken.None);
        }
    }

    /// <summary>Sends a packet carrying only acknowledgements (plus any reliable retransmission).</summary>
    private void SendKeepAlive(IPEndPoint ep)
    {
        lock (_chanLock)
        {
            TransmitLocked(ep, _contexts[ep], null, CancellationToken.None);
        }
    }
}
