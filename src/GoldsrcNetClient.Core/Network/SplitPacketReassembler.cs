using GoldsrcNetClient.Core.Messages;
using System.Buffers.Binary;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// Reassembles UDP-level split packets (the engine's <c>NET_GetLong</c>
/// mechanism). The engine transport splits any datagram larger than
/// <see cref="MessageConstants.MaxRouteablePacket"/> (1400 bytes) into
/// fragments of <see cref="MessageConstants.SplitPayloadSize"/> payload bytes,
/// each prefixed with the Sven Co-op 10-byte header (wire-verified):
/// <c>[uint32 0xFFFFFFFE][int32 splitSetId][byte fragmentCount][byte fragmentNumber]</c>.
///
/// The reassembled buffer is an ordinary datagram (netchan-sequenced or
/// connectionless) and must be fed back into the normal classification path.
/// Dropping these fragments instead makes the netchan see a sequence gap, the
/// reliable acknowledgement stalls, the server's outgoing reliable buffer
/// fills up, and the connection is dropped with
/// <c>"Reliable channel overflowed"</c> — the failure this class exists to
/// prevent.
/// </summary>
public sealed class SplitPacketReassembler
{
    private readonly int[] _receivedWith = new int[16];
    private readonly byte[] _buffer = new byte[MessageConstants.MaxUdpPacket];

    private int _currentSetId = -1;
    private int _fragmentsRemaining;
    private int _totalSize;

    /// <summary>
    /// Adds one split fragment. Returns the fully reassembled datagram when
    /// the last missing fragment of the set arrived, or <c>null</c> while the
    /// set is still incomplete (or the fragment was a duplicate / malformed).
    /// </summary>
    public byte[]? TryAdd(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length < MessageConstants.SplitHeaderSize + 1)
            return null;

        int setId = BinaryPrimitives.ReadInt32LittleEndian(datagram.Slice(4, 4));
        int fragmentCount = datagram[8];
        int fragmentNumber = datagram[9];

        if (fragmentNumber >= fragmentCount || fragmentCount > 15 || fragmentCount == 0)
            return null; // malformed

        int payloadSize = datagram.Length - MessageConstants.SplitHeaderSize;
        int offset = MessageConstants.SplitPayloadSize * fragmentNumber;
        if (offset + payloadSize > MessageConstants.MaxUdpPacket)
            return null; // malformed size

        // A new split set resets the accumulator; late fragments of a previous
        // set are discarded with it.
        if (_currentSetId != setId)
        {
            _currentSetId = setId;
            _fragmentsRemaining = fragmentCount;
            _totalSize = 0;
            Array.Clear(_receivedWith);
        }
        else if (_receivedWith[fragmentNumber] == setId)
        {
            return null; // duplicate fragment
        }

        if (fragmentNumber == fragmentCount - 1)
            _totalSize = offset + payloadSize;

        _fragmentsRemaining--;
        _receivedWith[fragmentNumber] = setId;
        datagram.Slice(MessageConstants.SplitHeaderSize).CopyTo(
            _buffer.AsSpan(offset, payloadSize));

        if (_fragmentsRemaining > 0)
            return null;

        // All fragments accounted for?
        for (int i = 0; i < fragmentCount; i++)
        {
            if (_receivedWith[i] != setId)
            {
                _currentSetId = -1; // cannot complete; wait for a resend
                return null;
            }
        }

        _currentSetId = -1;
        return _buffer[.._totalSize];
    }
}
