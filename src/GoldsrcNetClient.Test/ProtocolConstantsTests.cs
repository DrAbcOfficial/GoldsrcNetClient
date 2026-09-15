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

    /// <summary>The 30-bit sequence mask must leave the two top bits for the
    /// reliable/fragment flags — getting this wrong corrupts netchan sequencing.</summary>
    [Fact]
    public void SequenceMask_LeavesFlagBitsFree()
    {
        Assert.Equal(30, System.Numerics.BitOperations.PopCount(MessageConstants.SequenceMask));
        Assert.Equal(0u, MessageConstants.SequenceMask & MessageConstants.SequenceFlagReliable);
        Assert.Equal(0u, MessageConstants.SequenceMask & MessageConstants.SequenceFlagFragment);
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

    /// <summary>Wire-pinning: these enum values travel on the wire, so renumbering
    /// them would silently break the protocol.</summary>
    [Fact]
    public void SoundFlags_MatchClientDllBitLayout()
    {
        Assert.Equal(1u << 0, SoundFlags.Volume);
        Assert.Equal(1u << 1, SoundFlags.Attenuation);
        Assert.Equal(1u << 2, SoundFlags.LargeIndex);
        Assert.Equal(1u << 3, SoundFlags.Pitch);
        Assert.Equal(1u << 8, SoundFlags.Spawning);
    }

    [Theory]
    [InlineData(ServerMessageType.Bad, 0x00)]
    [InlineData(ServerMessageType.Nop, 0x01)]
    [InlineData(ServerMessageType.Disconnect, 0x02)]
    [InlineData(ServerMessageType.Print, 0x08)]
    [InlineData(ServerMessageType.ServerInfo, 0x0B)]
    [InlineData(ServerMessageType.UpdateUserInfo, 0x0D)]
    [InlineData(ServerMessageType.DeltaDescription, 0x0E)]
    [InlineData(ServerMessageType.SignOnNum, 0x19)]
    public void ServerMessageType_WireValues(ServerMessageType type, byte expected)
    {
        Assert.Equal(expected, (byte)type);
    }

    /// <summary>User messages start at 0x40; anything at or above is a server-registered index.</summary>
    [Fact]
    public void UserMessageStart_Is_0x40()
    {
        Assert.Equal(0x40, (byte)ServerMessageType.UserMessageStart);
    }
}
