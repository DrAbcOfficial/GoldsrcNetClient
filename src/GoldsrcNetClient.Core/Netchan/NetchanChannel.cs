using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Munge;
using GoldsrcNetClient.Core.Protocol;
using ICSharpCode.SharpZipLib.BZip2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Buffers.Binary;
using System.Net;

namespace GoldsrcNetClient.Core.Netchan;

/// <summary>
/// Sequenced channel (netchan) to a single GoldSrc server endpoint — the
/// client-side port of the engine's <c>netchan_t</c>. Owns packet sequencing,
/// reliable-message acknowledgement and retransmission, fragment-stream
/// reassembly, Munge2 packet encryption, and the keepalive packet layout.
/// The connection feeds received datagrams in and dispatches the
/// reassembled message payloads out.
/// </summary>
public sealed class NetchanChannel
{
    /// <summary>Outgoing packets smaller than this are padded with <c>svc_nop</c> bytes
    /// (some networks misbehave on tiny UDP datagrams).</summary>
    internal const int MinPacketSize = 16;

    /// <summary>Minimum interval between retransmissions of the same reliable payload.
    /// The payload rides every keepalive in the reference engine, but each received
    /// copy toggles the server's acknowledgement counter, so limiting retransmission
    /// frequency keeps the acknowledgement parity clean on lossy, high-latency links.</summary>
    internal const int ReliableRetransmitMs = 500;

    private readonly ILogger _logger;
    private readonly Func<ReadOnlyMemory<byte>, IPEndPoint, CancellationToken, Task> _send;
    private readonly IPEndPoint _remote;
    private readonly Lock _lock = new();
    private readonly Queue<byte[]> _reliableQueue = [];
    private readonly FragmentStream[] _streams = [new(), new()];
    private readonly bool _useEncryption;
    private readonly bool _longFragmentFields;

    // Sequencing and reliability state (mirrors the engine's per-channel bookkeeping).
    private uint _srcSequence = 1;
    private uint _incomingSequence;
    private uint _incomingAcknowledged;
    private uint _incomingReliableSequence;
    private uint _outgoingReliableSequence;
    private uint _lastReliableSequence;
    private long _lastReliableTransmitMs;
    private byte[]? _pendingReliable;

    /// <summary>
    /// Creates the channel for one server endpoint.
    /// </summary>
    /// <param name="remote">The server endpoint every packet is sent to.</param>
    /// <param name="send">Transport delegate (typically <see cref="System.Net.Sockets.UdpClient.SendAsync"/>).</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="useEncryption">Whether payloads are Munge2-encrypted on transmit and
    /// decrypted on receive. Disable for engine branches whose netchan is plaintext
    /// (e.g. Sven Co-op, see <see cref="Game.IGameLoginProvider.UseNetchanEncryption"/>).</param>
    /// <param name="longFragmentFields">Parse fragment-stream startpos/length as 32-bit
    /// fields. The Sven Co-op engine branch widened them from Valve's 16-bit fields
    /// (verified against wire captures); leave false for standard GoldSrc servers.</param>
    public NetchanChannel(IPEndPoint remote, Func<ReadOnlyMemory<byte>, IPEndPoint, CancellationToken, Task> send, ILogger? logger = null, bool useEncryption = true, bool longFragmentFields = false)
    {
        _remote = remote;
        _send = send;
        _logger = logger ?? NullLogger.Instance;
        _useEncryption = useEncryption;
        _longFragmentFields = longFragmentFields;
    }

    /// <summary>Queues a reliable message for transmission and sends a packet immediately.</summary>
    public void SendReliable(byte[] payload)
    {
        lock (_lock)
        {
            _reliableQueue.Enqueue(payload);
            Transmit(CancellationToken.None);
        }
    }

    /// <summary>Sends an acknowledgement-only packet (plus any reliable retransmission).</summary>
    public void SendKeepAlive()
    {
        lock (_lock)
        {
            Transmit(CancellationToken.None);
        }
    }

    /// <summary>
    /// Processes one received sequenced packet: decrypts the payload, drops
    /// duplicates/out-of-order packets, updates acknowledgement state, reassembles
    /// fragment streams, and dispatches message payloads. Exactly one ack packet
    /// is transmitted after processing, mirroring the engine's next-frame ack
    /// cadence (acking only on the keepalive cadence lets the server's outgoing
    /// reliable buffer grow faster than it is drained during the sign-on flood,
    /// which ends in a "Reliable channel overflowed" drop).
    /// </summary>
    /// <param name="datagram">Raw UDP payload including the 8-byte sequence header.</param>
    /// <param name="dispatchMessage">Receives each ready (decrypted, reassembled,
    /// BZ2-decompressed) message stream. The inline stream is dispatched while the
    /// channel lock is held.</param>
    public void ProcessIncoming(byte[] datagram, Action<byte[]> dispatchMessage)
    {
        uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(datagram);
        uint sequenceAck = BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(4));
        int payloadLen = datagram.Length - MessageConstants.ConnectedHeadSize;

        if (payloadLen <= 0)
            return;

        bool reliableMessage = (sequence & MessageConstants.SequenceFlagReliable) != 0;
        bool containsFragments = (sequence & MessageConstants.SequenceFlagFragment) != 0;
        uint reliableAck = sequenceAck >> 31;
        uint seq = sequence & MessageConstants.SequenceMask;
        uint ack = sequenceAck & MessageConstants.SequenceMask;

        // The payload of every connected packet is Munge2-encrypted with the low byte
        // of the (flagged) sequence value as key.
        byte[] payload = new byte[payloadLen];
        Array.Copy(datagram, MessageConstants.ConnectedHeadSize, payload, 0, payloadLen);
        if (_useEncryption)
            MungeEngine.UnMunge2(payload, payloadLen, (int)(sequence & 0xFF));

        // Read fragment stream headers (they precede the message data).
        int headerBytes = 0;
        var fragHeaders = new (uint FragId, int Offset, int Length)[MessageConstants.MaxFragmentStreams];
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

                uint fragId = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(pos)); pos += 4;
                int fragOffset, fragLength;
                if (_longFragmentFields)
                {
                    // Sven Co-op widened the fragment position/size fields to 32 bit
                    // (wire-verified: a 1-of-1 fragment announces length 43, i.e. the
                    // BZ2-wrapped serverinfo, which 16-bit parsing reads as zero).
                    fragOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(pos)); pos += 4;
                    fragLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(pos)); pos += 4;
                }
                else
                {
                    fragOffset = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(pos)); pos += 2;
                    fragLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(pos)); pos += 2;
                }

                if (pos + fragOffset + fragLength > payloadLen) { valid = false; break; }
                fragHeaders[s] = (fragId, fragOffset, fragLength);
                streamActive[s] = true;
            }

            if (!valid)
            {
                _logger.LogWarning("[Netchan] invalid fragment headers in packet seq={Seq} from {Ep}", seq, _remote);
                return;
            }

            headerBytes = pos;
        }

        lock (_lock)
        {
            if (seq <= _incomingSequence)
            {
                _logger.LogDebug("[Netchan] duplicate/out-of-order packet seq={Seq} (incoming={Incoming})", seq, _incomingSequence);
                return;
            }

            if (seq > _incomingSequence + 1)
                _logger.LogDebug("[Netchan] dropped {Count} packet(s) before seq={Seq}", seq - _incomingSequence - 1, seq);

            _logger.LogDebug("[Netchan] rx seq={Seq} reliable={Reliable} frag={Frag} relAck={RelAck} | inReliable={InReliable}",
                seq, reliableMessage, containsFragments, reliableAck, _incomingReliableSequence);

            // Server acknowledged our pending reliable message?
            if (reliableAck == _outgoingReliableSequence
                && _incomingAcknowledged + 1 >= _lastReliableSequence)
            {
                _logger.LogDebug("[Netchan] reliable payload acked (reliableSeq={ReliableSeq})", _outgoingReliableSequence);
                _pendingReliable = null;
            }

            _incomingSequence = seq;
            _incomingAcknowledged = ack;

            // Track the server's reliable-message counter. The server toggles it once
            // per NEW reliable transmission and we toggle on every reliable packet
            // received; if the server is retransmitting a message our bit keeps
            // alternating, which brings the acknowledgement back into phase within a
            // couple of copies — the same self-correcting behaviour as the engine.
            if (reliableMessage)
            {
                _incomingReliableSequence ^= 1;
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
                        fragHeaders[j].Offset -= fragLength;
                }

                Array.Copy(payload, chunkStart + fragLength, payload, chunkStart, payloadLen - chunkStart - fragLength);
                payloadLen -= fragLength;

                _streams[s].Push(fragId, chunk);
            }

            // Inline message stream (after fragment headers, with chunk data removed).
            if (payloadLen > headerBytes)
                dispatchMessage(payload[headerBytes..payloadLen]);
        }

        // Deliver any fragment streams that just completed (normal stream first, then file stream).
        for (int s = 0; s < MessageConstants.MaxFragmentStreams; s++)
        {
            var stream = _streams[s];
            if (!stream.IsComplete)
                continue;

            byte[] assembled = stream.Assemble();
            stream.Reset();
            if (assembled.Length == 0)
                continue;

            if (s == 1)
            {
                // File transfer stream: only used for custom resource downloads,
                // which this client does not request. Discard.
                _logger.LogWarning("[Netchan] discarding {Length} bytes on the file fragment stream", assembled.Length);
                continue;
            }

            if (TryDecompressBz2(assembled) is { } message)
                dispatchMessage(message);
        }

        TransmitAck();
    }

    /// <summary>
    /// Builds and sends one sequenced packet: ack header, pending reliable payload
    /// (pulling the next queued reliable message if the slot is free), and
    /// <c>svc_nop</c> padding up to the minimum packet size.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private void Transmit(CancellationToken ct)
    {
        bool pulled = false;
        if (_pendingReliable == null && _reliableQueue.Count > 0)
        {
            _pendingReliable = _reliableQueue.Dequeue();
            _outgoingReliableSequence ^= 1;
            pulled = true;
        }

        // A freshly queued message is transmitted immediately; retransmissions of an
        // unacknowledged message are throttled.
        bool sendReliable = _pendingReliable != null &&
                            (pulled || Environment.TickCount64 - _lastReliableTransmitMs >= ReliableRetransmitMs);
        if (sendReliable)
        {
            _lastReliableSequence = _srcSequence;
            _lastReliableTransmitMs = Environment.TickCount64;
        }

        bool includePayload = sendReliable && _pendingReliable != null;

        uint w1 = _srcSequence & MessageConstants.SequenceMask;
        if (sendReliable)
            w1 |= MessageConstants.SequenceFlagReliable;
        uint w2 = (_incomingSequence & MessageConstants.SequenceMask)
                  | (_incomingReliableSequence << 31);

        _srcSequence = (_srcSequence & MessageConstants.SequenceMask) + 1;

        var reliableBytes = includePayload ? _pendingReliable : null;
        int totalLen = MessageConstants.ConnectedHeadSize + (reliableBytes?.Length ?? 0);
        var packet = new byte[Math.Max(totalLen, MinPacketSize)];

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0), w1);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), w2);

        int pos = MessageConstants.ConnectedHeadSize;
        if (reliableBytes != null)
        {
            reliableBytes.CopyTo(packet, pos);
            pos += reliableBytes.Length;
        }

        // Pad undersized packets with svc_nop bytes (the server parses them as no-ops).
        for (; pos < packet.Length; pos++)
            packet[pos] = (byte)ServerMessageType.Nop;

        // Encrypt the whole body (payload + padding) with the sequence's low byte as key —
        // the server unconditionally decrypts every connected packet. Engine branches
        // without netchan munge (Sven Co-op) receive the body as-is instead.
        int bodySize = packet.Length - MessageConstants.ConnectedHeadSize;
        byte[] body = packet[MessageConstants.ConnectedHeadSize..];
        if (_useEncryption)
            MungeEngine.Munge2(body, bodySize, (int)(w1 & 0xFF));
        body.CopyTo(packet, MessageConstants.ConnectedHeadSize);

        _logger.LogDebug("[Netchan] tx seq={Seq} ack={Ack} reliable={Reliable} ackReliable={AckReliable} bytes={Len} pending={Pending}",
            w1 & MessageConstants.SequenceMask, w2 & MessageConstants.SequenceMask, sendReliable,
            _incomingReliableSequence, packet.Length, _pendingReliable != null);

        _send(packet, _remote, ct).GetAwaiter().GetResult();
    }

    private void TransmitAck()
    {
        lock (_lock)
        {
            Transmit(CancellationToken.None);
        }
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
}
