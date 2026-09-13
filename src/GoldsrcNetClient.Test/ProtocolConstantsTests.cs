using GoldsrcNetClient.Core.Delta;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Test;

public class MessageConstantsTests
{
    [Fact]
    public void Constants_HaveExpectedValues()
    {
        Assert.Equal(8, MessageConstants.ConnectedHeadSize);
        Assert.Equal(0xFFFFFFFFu, MessageConstants.ConnectionlessMarker);
        Assert.Equal(0xFFFFFFFEu, MessageConstants.SplitMarker);
        Assert.Equal(11, MessageConstants.MaxEdictBits);
        Assert.Equal(2, MessageConstants.MaxFragmentStreams);
        Assert.Equal(0x80000000u, MessageConstants.SequenceFlagReliable);
        Assert.Equal(0x40000000u, MessageConstants.SequenceFlagFragment);
        Assert.Equal(0x3FFFFFFFu, MessageConstants.SequenceMask);
    }

    [Fact]
    public void SequenceMask_Covers30Bits()
    {
        Assert.Equal(0x3FFFFFFFu, MessageConstants.SequenceMask);
        Assert.Equal(30, System.Numerics.BitOperations.PopCount(MessageConstants.SequenceMask));
    }
}

public class PacketParsingTests
{
    [Fact]
    public void ConnectedPacket_SequenceParsing()
    {
        uint seq = 42;
        uint header = seq | MessageConstants.SequenceFlagReliable;

        Assert.Equal(seq, header & MessageConstants.SequenceMask);
        Assert.True((header & MessageConstants.SequenceFlagReliable) != 0);
        Assert.False((header & MessageConstants.SequenceFlagFragment) != 0);
    }

    [Fact]
    public void ConnectedPacket_FragmentFlag()
    {
        uint seq = 7;
        uint header = seq | MessageConstants.SequenceFlagFragment;
        Assert.Equal(seq, header & MessageConstants.SequenceMask);
        Assert.True((header & MessageConstants.SequenceFlagFragment) != 0);
    }

    [Fact]
    public void Sequence_Overflow()
    {
        uint seq = 0x3FFFFFFF;
        uint next = (seq + 1) & MessageConstants.SequenceMask;
        Assert.Equal(0u, next);
    }
}

public class EnumsTests
{
    [Fact]
    public void DeltaFieldFlag_Values()
    {
        Assert.Equal(1u << 0, (uint)DeltaFieldFlag.Byte);
        Assert.Equal(1u << 1, (uint)DeltaFieldFlag.Short);
        Assert.Equal(1u << 2, (uint)DeltaFieldFlag.Float);
        Assert.Equal(1u << 3, (uint)DeltaFieldFlag.Integer);
        Assert.Equal(1u << 4, (uint)DeltaFieldFlag.Angle);
        Assert.Equal(1u << 7, (uint)DeltaFieldFlag.StringField);
        Assert.Equal(1u << 31, (uint)DeltaFieldFlag.Signed);
    }

    [Fact]
    public void ServerMessageType_ServerInfo_Is_0x0B()
    {
        Assert.Equal(0x0B, (byte)ServerMessageType.ServerInfo);
    }

    [Fact]
    public void ClientCommandType_StringCmd_Is_0x03()
    {
        Assert.Equal(0x03, (byte)ClientCommandType.StringCmd);
    }

    [Fact]
    public void ResourceFlag_FatalIfMissing_Unused()
    {
        Assert.Equal(1, (int)ResourceFlag.FatalIfMissing & 1);
    }

    [Fact]
    public void SoundFlags_MaxSpawningBit()
    {
        Assert.Equal(1u << 8, (uint)SoundFlags.Spawning);
    }
}
