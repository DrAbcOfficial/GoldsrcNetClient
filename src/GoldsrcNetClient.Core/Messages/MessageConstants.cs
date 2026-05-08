using GoldsrcNetClient.Core.Protocol;

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

    /// <summary>Maximum number of bits used to encode an entity index (supports up to 2048 entities).</summary>
    public const int MaxEdictBits = 11;

    /// <summary>Maximum number of concurrent fragment streams supported.</summary>
    public const int MaxFragmentStreams = 2;

    /// <summary>Flag OR'd with the source sequence to mark a packet as a command/stringcmd.</summary>
    public const uint SequenceModeCommand = 0x80000000;

    /// <summary>Flag OR'd with the source sequence to mark a packet as a fragment.</summary>
    public const uint SequenceModeFragment = 0x40000000;

    /// <summary>Bitmask applied to sequence numbers to strip mode flags.</summary>
    public const uint SequenceMask = 0x3FFFFFFF;

    /// <summary>Constant 8-byte value sent in acknowledgement (keep-alive) packets.</summary>
    public const ulong AckData = 0x0101010101010101UL;
}
