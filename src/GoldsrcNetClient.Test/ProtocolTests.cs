using GoldsrcNetClient.Core.Munge;
using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.Core.Util;
using GoldsrcNetClient.Core.Delta;
using GoldsrcNetClient.Core.Messages;

namespace GoldsrcNetClient.Test;

public class MungeTests
{
    [Fact]
    public void Munge2_EncryptDecrypt_Roundtrip()
    {
        byte[] original = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        byte[] data = (byte[])original.Clone();

        MungeEngine.Munge2(data, data.Length, 42);
        Assert.NotEqual(original, data);

        MungeEngine.UnMunge2(data, data.Length, 42);
        Assert.Equal(original, data);
    }

    [Fact]
    public void Munge3_EncryptDecrypt_Roundtrip()
    {
        byte[] original = [0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80];
        byte[] data = (byte[])original.Clone();

        MungeEngine.Munge3(data, data.Length, 0xFF);
        Assert.NotEqual(original, data);

        MungeEngine.UnMunge3(data, data.Length, 0xFF);
        Assert.Equal(original, data);
    }
}

public class BitReaderTests
{
    [Fact]
    public void ReadBits_UInt32_Simple()
    {
        byte[] data = [0b00101101, 0b11000000];
        int bitIdx = 0;
        uint result = 0;

        bool ok = BitReader.ReadBits(data, ref bitIdx, data.Length, ref result, 10);
        Assert.True(ok);
        Assert.Equal(45u, result);
    }

    [Fact]
    public void ReadBits_ByteCount_Smaller()
    {
        byte[] data = [0b00000101];
        int bitIdx = 0;
        uint result = 0;

        bool ok = BitReader.ReadBits(data, ref bitIdx, data.Length, ref result, 3);
        Assert.True(ok);
        Assert.Equal(5u, result);
    }
}

public class DeltaDefinitionsTests
{
    [Fact]
    public void Find_Event_ReturnsCorrectType()
    {
        var dt = DeltaDefinitions.Find("event_t");
        Assert.True(dt.HasValue);
        Assert.Equal(0x0E, dt.Value.FieldAmount);
        Assert.Equal("event_t", dt.Value.DeltaName);
    }

    [Fact]
    public void Find_ClientData_ReturnsCorrectType()
    {
        var dt = DeltaDefinitions.Find("clientdata_t");
        Assert.True(dt.HasValue);
        Assert.Equal(0x32, dt.Value.FieldAmount);
    }

    [Fact]
    public void Find_Unknown_ReturnsNull()
    {
        var dt = DeltaDefinitions.Find("nonexistent_t");
        Assert.False(dt.HasValue);
    }
}

public class PacketParsingTests
{
    [Fact]
    public void ConnectionlessPacket_ValidHeader()
    {
        byte[] packet = [0xFF, 0xFF, 0xFF, 0xFF, (byte)'g', (byte)'e', (byte)'t'];
        uint header = BitConverter.ToUInt32(packet, 0);
        Assert.Equal(MessageConstants.ConnectionlessMarker, header);
    }

    [Fact]
    public void ConnectedPacket_SequenceParsing()
    {
        uint seq = 42;
        uint mode = MessageConstants.SequenceModeCommand;
        uint header = seq | mode;

        Assert.Equal(seq, header & MessageConstants.SequenceMask);
        Assert.True((header & MessageConstants.SequenceModeCommand) != 0);
        Assert.False((header & MessageConstants.SequenceModeFragment) != 0);
    }
}
