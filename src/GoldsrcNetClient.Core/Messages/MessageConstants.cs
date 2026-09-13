namespace GoldsrcNetClient.Core.Messages;

/// <summary>
/// Core constants for the GoldSrc engine network message protocol.
/// </summary>
public static class MessageConstants
{
    /// <summary>Size in bytes of the header prepended to every connected packet (sequence numbers).</summary>
    public const int ConnectedHeadSize = 8;

    /// <summary>Magic header value for connectionless (non-sequenced) packets.</summary>
    public const uint ConnectionlessMarker = 0xFFFFFFFF;

    /// <summary>Magic header value for split/fragmented packets.</summary>
    public const uint SplitMarker = 0xFFFFFFFE;

    /// <summary>Largest datagram the engine sends without UDP-level splitting
    /// (<c>MAX_ROUTEABLE_PACKET</c>). Anything larger is split by the sender's
    /// transport into <see cref="SplitPayloadSize"/>-byte fragments, each
    /// prefixed with the <see cref="SplitHeaderSize"/>-byte
    /// <see cref="SplitMarker"/> header.</summary>
    public const int MaxRouteablePacket = 1400;

    /// <summary>Header size of one UDP split fragment as emitted by the Sven Co-op
    /// engine (wire-verified, build 5.0.1.8/10257):
    /// <c>[uint32 0xFFFFFFFE][int32 splitSetId][byte fragmentCount][byte fragmentNumber]</c>.
    /// Note the Valve/ReHLDS layout differs (9 bytes, fragment number and count
    /// packed into one nibble-split byte).</summary>
    public const int SplitHeaderSize = 10;

    /// <summary>Payload bytes carried by each non-final split fragment
    /// (<c>MAX_ROUTEABLE_PACKET - SplitHeaderSize</c>).</summary>
    public const int SplitPayloadSize = MaxRouteablePacket - SplitHeaderSize;

    /// <summary>Maximum size of a reassembled datagram (<c>MAX_UDP_PACKET</c>).</summary>
    public const int MaxUdpPacket = 4010;

    /// <summary>Maximum number of bits used to encode an entity index (supports up to 2048 entities).</summary>
    public const int MaxEdictBits = 11;

    /// <summary>Maximum number of concurrent fragment streams supported.</summary>
    public const int MaxFragmentStreams = 2;

    /// <summary>Bit 31 of a connected packet's sequence field: the packet carries reliable payload data.</summary>
    public const uint SequenceFlagReliable = 0x80000000;

    /// <summary>Bit 30 of a connected packet's sequence field: the packet contains fragment stream headers.</summary>
    public const uint SequenceFlagFragment = 0x40000000;

    /// <summary>Bitmask applied to sequence numbers to strip the flag bits.</summary>
    public const uint SequenceMask = 0x3FFFFFFF;
}
