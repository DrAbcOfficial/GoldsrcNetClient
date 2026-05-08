using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Core.Messages;

public static class MessageConstants
{
    public const int ConnectedHeadSize = 8;
    public const uint ConnectionlessMarker = 0xFFFFFFFF;
    public const uint SplitMarker = 0xFFFFFFFE;
    public const int MaxEdictBits = 11;
    public const int MaxFragmentStreams = 2;
    public const uint SequenceModeCommand = 0x80000000;
    public const uint SequenceModeFragment = 0x40000000;
    public const uint SequenceMask = 0x3FFFFFFF;
    public const ulong AckData = 0x0101010101010101UL;
}
