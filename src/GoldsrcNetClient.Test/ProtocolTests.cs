using System.Numerics;
using GoldsrcNetClient.Core.Munge;
using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.Core.Util;
using GoldsrcNetClient.Core.Delta;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Network;

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

    [Fact]
    public void ConnectedPacket_FragmentFlag()
    {
        uint seq = 7;
        uint header = seq | MessageConstants.SequenceModeFragment;
        Assert.Equal(seq, header & MessageConstants.SequenceMask);
        Assert.True((header & MessageConstants.SequenceModeFragment) != 0);
    }

    [Fact]
    public void Sequence_Overflow()
    {
        uint seq = 0x3FFFFFFF;
        uint next = (seq + 1) & MessageConstants.SequenceMask;
        Assert.Equal(0u, next);
    }
}

public class MungeExtendedTests
{
    [Fact]
    public void Munge1_EncryptDecrypt_Roundtrip()
    {
        byte[] original = [0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89];
        byte[] data = (byte[])original.Clone();

        MungeEngine.Munge1(data, data.Length, 0x55);
        Assert.NotEqual(original, data);

        MungeEngine.UnMunge1(data, data.Length, 0x55);
        Assert.Equal(original, data);
    }

    [Fact]
    public void Munge1_ZeroSequence()
    {
        byte[] original = [0x11, 0x22, 0x33, 0x44];
        byte[] data = (byte[])original.Clone();

        MungeEngine.Munge1(data, data.Length, 0);
        Assert.NotEqual(original, data);

        MungeEngine.UnMunge1(data, data.Length, 0);
        Assert.Equal(original, data);
    }

    [Fact]
    public void Munge2_VaryingSize_4Bytes()
    {
        byte[] original = [0xAA, 0xBB, 0xCC, 0xDD];
        byte[] data = (byte[])original.Clone();

        MungeEngine.Munge2(data, data.Length, 100);
        MungeEngine.UnMunge2(data, data.Length, 100);
        Assert.Equal(original, data);
    }

    [Fact]
    public void Munge2_VaryingSize_16Bytes()
    {
        byte[] original = new byte[16];
        for (int i = 0; i < 16; i++) original[i] = (byte)(i * 17);
        byte[] data = (byte[])original.Clone();

        MungeEngine.Munge2(data, data.Length, 255);
        MungeEngine.UnMunge2(data, data.Length, 255);
        Assert.Equal(original, data);
    }

    [Fact]
    public void Munge2_VaryingSize_5Bytes_NonAligned()
    {
        byte[] original = [0x01, 0x02, 0x03, 0x04, 0x05];
        byte[] data = (byte[])original.Clone();

        MungeEngine.Munge2(data, data.Length, 199);
        MungeEngine.UnMunge2(data, data.Length, 199);
        Assert.Equal(original, data);
    }

    [Fact]
    public void Munge3_VaryingSequence()
    {
        for (int seq = 0; seq < 256; seq++)
        {
            byte[] original = [0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE];
            byte[] data = (byte[])original.Clone();

            MungeEngine.Munge3(data, data.Length, seq);
            MungeEngine.UnMunge3(data, data.Length, seq);
            Assert.Equal(original, data);
        }
    }

    [Fact]
    public void Munge2_LargeData_100Bytes()
    {
        byte[] original = new byte[100];
        for (int i = 0; i < 100; i++) original[i] = (byte)i;
        byte[] data = (byte[])original.Clone();

        MungeEngine.Munge2(data, data.Length, 42);
        Assert.False(original.SequenceEqual(data));

        MungeEngine.UnMunge2(data, data.Length, 42);
        Assert.Equal(original, data);
    }

    [Fact]
    public void Munge2_EmptyData()
    {
        byte[] data = [];
        MungeEngine.Munge2(data, data.Length, 0);
        MungeEngine.UnMunge2(data, data.Length, 0);
        Assert.Empty(data);
    }

    [Fact]
    public void Munge3_SingleByte_NonAligned()
    {
        byte[] original = [0x7F];
        byte[] data = (byte[])original.Clone();

        MungeEngine.Munge3(data, data.Length, 0xAB);
        MungeEngine.UnMunge3(data, data.Length, 0xAB);
        Assert.Equal(original, data);
    }
}

public class BitReaderExtendedTests
{
    [Fact]
    public void ReadBits_DestinationByteArray()
    {
        byte[] source = [0b10101100, 0b11110000];
        byte[] dest = new byte[4];
        int srcIdx = 0, dstIdx = 0;

        bool ok = BitReader.ReadBits(source, ref srcIdx, source.Length, dest, ref dstIdx, 16);
        Assert.True(ok);
        Assert.Equal(0b10101100, dest[0]);
        Assert.Equal(0b11110000, dest[1]);
        Assert.Equal(16, dstIdx);
    }

    [Fact]
    public void ReadBits_Overflow_ReturnsFalse()
    {
        byte[] source = [0xFF];
        int bitIdx = 0;
        uint result = 0;

        bool ok = BitReader.ReadBits(source, ref bitIdx, source.Length, ref result, 9);
        Assert.False(ok);
    }

    [Fact]
    public void ReadBits_Int32_Overload()
    {
        byte[] data = [0b00001100];
        int bitIdx = 0;
        int result = 0;

        bool ok = BitReader.ReadBits(data, ref bitIdx, data.Length, ref result, 4);
        Assert.True(ok);
        Assert.Equal(12, result);
    }

    [Fact]
    public void ReadBits_AtNonZeroOffset()
    {
        byte[] source = [0b11111111, 0b00001010];
        int bitIdx = 4;
        uint result = 0;

        bool ok = BitReader.ReadBits(source, ref bitIdx, source.Length, ref result, 8);
        Assert.True(ok);
        Assert.Equal(0xAFu, result & 0xFF); // 0b10101111 = lower nibble of byte0 + upper nibble of byte1
    }

    [Fact]
    public void ReadBits_CrossesByteBoundary()
    {
        byte[] source = [0b11110000, 0b00001111];
        int bitIdx = 4;
        uint result = 0;

        bool ok = BitReader.ReadBits(source, ref bitIdx, source.Length, ref result, 8);
        Assert.True(ok);
        Assert.Equal(0x0Fu, (result >> 4) & 0xF);
        Assert.Equal(0x0Fu, result & 0xF);
    }

    [Fact]
    public void ReadBitString_Simple()
    {
        byte[] data = [(byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0x00, 0xFF];
        int bitIdx = 0;

        bool ok = BitReader.ReadBitString(data, ref bitIdx, data.Length, out byte[] result, 32);
        Assert.True(ok);
        Assert.Equal("Hello", System.Text.Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void ReadBitString_Empty()
    {
        byte[] data = [0x00, 0xFF];
        int bitIdx = 0;

        bool ok = BitReader.ReadBitString(data, ref bitIdx, data.Length, out byte[] result, 32);
        Assert.True(ok);
        Assert.Empty(result);
    }

    [Fact]
    public void ReadBitString_Overflow_ReturnsFalse()
    {
        byte[] data = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
        int bitIdx = 0;

        bool ok = BitReader.ReadBitString(data, ref bitIdx, 5, out _, 32);
        Assert.False(ok);
    }

    [Fact]
    public void ReadBitCoord_Zero()
    {
        byte[] data = [0b00000000];
        int bitIdx = 0;
        float f = 99;

        bool ok = BitReader.ReadBitCoord(data, ref bitIdx, data.Length, ref f);
        Assert.True(ok);
        Assert.Equal(0f, f);
    }

    [Fact]
    public void ReadBitCoord_PositiveInteger()
    {
        // intflag=1, fractflag=0, sign=0, int=2 (12 bits = 010...0)
        // bitstream LSB-first: 1 0 0 0 1 0 0 0 0 0 0 0 0 0 0
        byte[] data = [0x11, 0x00];
        int bitIdx = 0;
        float f = 0;

        bool ok = BitReader.ReadBitCoord(data, ref bitIdx, data.Length, ref f);
        Assert.True(ok);
        Assert.Equal(2.0f, f);
    }

    [Fact]
    public void ReadBitCoord_NegativeInteger()
    {
        // intflag=1, fractflag=0, sign=1, int=2 (12 bits = 010...0)
        // bitstream LSB-first: 1 0 1 0 1 0 0 0 0 0 0 0 0 0 0
        byte[] data = [0x15, 0x00];
        int bitIdx = 0;
        float f = 0;

        bool ok = BitReader.ReadBitCoord(data, ref bitIdx, data.Length, ref f);
        Assert.True(ok);
        Assert.Equal(-2.0f, f);
    }

    [Fact]
    public void ReadBits_UInt32_AllOnes()
    {
        byte[] data = [0xFF, 0xFF, 0xFF, 0xFF];
        int bitIdx = 0;
        uint result = 0;

        bool ok = BitReader.ReadBits(data, ref bitIdx, data.Length, ref result, 32);
        Assert.True(ok);
        Assert.Equal(0xFFFFFFFFu, result);
    }

    [Fact]
    public void ReadBits_UInt32_SingleBit()
    {
        byte[] data = [0b00000001];
        int bitIdx = 0;
        uint result = 0;

        bool ok = BitReader.ReadBits(data, ref bitIdx, data.Length, ref result, 1);
        Assert.True(ok);
        Assert.Equal(1u, result);
    }
}

public class BitWriterTests
{
    [Fact]
    public void WriteBits_UInt_Simple()
    {
        byte[] dest = new byte[4];
        int bitIdx = 0;

        BitWriter.WriteBits(0xAAu, 8, dest, ref bitIdx, dest.Length);
        Assert.Equal(0xAA, dest[0]);
        Assert.Equal(8, bitIdx);
    }

    [Fact]
    public void WriteBits_UInt_CrossByte()
    {
        byte[] dest = new byte[4];
        int bitIdx = 4;

        BitWriter.WriteBits(0xFFu, 8, dest, ref bitIdx, dest.Length);
        Assert.Equal(0xF0, dest[0] & 0xF0);
        Assert.Equal(0x0F, dest[1] & 0x0F);
        Assert.Equal(12, bitIdx);
    }

    [Fact]
    public void WriteBits_UInt_16Bits()
    {
        byte[] dest = new byte[4];
        int bitIdx = 0;

        BitWriter.WriteBits(0xABCDu, 16, dest, ref bitIdx, dest.Length);
        Assert.Equal(0xCD, dest[0]);
        Assert.Equal(0xAB, dest[1]);
    }

    [Fact]
    public void WriteBits_ByteArray_Simple()
    {
        byte[] source = [0x55, 0xAA];
        byte[] dest = new byte[4];
        int bitIdx = 0;

        BitWriter.WriteBits(source, 16, dest, ref bitIdx, dest.Length);
        Assert.Equal(0x55, dest[0]);
        Assert.Equal(0xAA, dest[1]);
    }

    [Fact]
    public void WriteBits_UInt_Roundtrip_WithBitReader()
    {
        byte[] dest = new byte[4];
        int writeIdx = 0;
        BitWriter.WriteBits(0b11011u, 5, dest, ref writeIdx, dest.Length);
        BitWriter.WriteBits(0b101u, 3, dest, ref writeIdx, dest.Length);

        int readIdx = 0;
        uint val1 = 0, val2 = 0;
        BitReader.ReadBits(dest, ref readIdx, dest.Length, ref val1, 5);
        BitReader.ReadBits(dest, ref readIdx, dest.Length, ref val2, 3);

        Assert.Equal(0b11011u, val1);
        Assert.Equal(0b101u, val2);
    }
}

public class MessageReaderTests
{
    [Fact]
    public void ReadString_ReturnsString()
    {
        byte[] data = [(byte)'t', (byte)'e', (byte)'s', (byte)'t', 0x00, 0xFF];
        int offset = 0;

        string result = MessageReader.ReadString(ref data, ref offset, data.Length);
        Assert.Equal("test", result);
        Assert.Equal(5, offset);
    }

    [Fact]
    public void ReadString_NoNullTerminator()
    {
        byte[] data = [(byte)'a', (byte)'b', (byte)'c'];
        int offset = 0;

        string result = MessageReader.ReadString(ref data, ref offset, data.Length);
        Assert.Equal("abc", result);
        Assert.Equal(3, offset);
    }

    [Fact]
    public void ReadString_EmptyAtNull()
    {
        byte[] data = [0x00, 0xFF];
        int offset = 0;

        string result = MessageReader.ReadString(ref data, ref offset, data.Length);
        Assert.Equal("", result);
        Assert.Equal(1, offset);
    }

    [Fact]
    public void ReadString_Bytes_ReturnsBytes()
    {
        byte[] data = [(byte)'A', (byte)'B', 0x00, 0xFF];
        int offset = 0;

        bool ok = MessageReader.ReadString(ref data, ref offset, data.Length, out byte[] str);
        Assert.True(ok);
        Assert.Equal([(byte)'A', (byte)'B'], str);
        Assert.Equal(3, offset);
    }

    [Fact]
    public void ReadString_Bytes_NoNull_ReturnsFalse()
    {
        byte[] data = [(byte)'X', (byte)'Y', (byte)'Z'];
        int offset = 0;

        bool ok = MessageReader.ReadString(ref data, ref offset, data.Length, out byte[] str);
        Assert.False(ok);
        Assert.Equal(3, offset);
    }

    [Fact]
    public void ReadBytes_Success()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05];
        int offset = 1;
        Span<byte> dest = new byte[3];

        bool ok = MessageReader.ReadBytes(ref data, ref offset, data.Length, dest);
        Assert.True(ok);
        Assert.Equal(0x02, dest[0]);
        Assert.Equal(0x04, dest[2]);
        Assert.Equal(4, offset);
    }

    [Fact]
    public void ReadBytes_Overflow_ReturnsFalse()
    {
        byte[] data = [0x01, 0x02];
        int offset = 1;
        Span<byte> dest = new byte[3];

        bool ok = MessageReader.ReadBytes(ref data, ref offset, data.Length, dest);
        Assert.False(ok);
    }

    [Fact]
    public void ReadUInt16_Valid()
    {
        byte[] data = [0xCD, 0xAB, 0xFF];
        int offset = 0;

        ushort val = MessageReader.ReadUInt16(ref data, ref offset, data.Length);
        Assert.Equal(0xABCDu, val);
        Assert.Equal(2, offset);
    }

    [Fact]
    public void ReadUInt16_Overflow_ReturnsZero()
    {
        byte[] data = [0x01];
        int offset = 0;

        ushort val = MessageReader.ReadUInt16(ref data, ref offset, data.Length);
        Assert.Equal(0u, val);
    }

    [Fact]
    public void ReadUInt32_Valid()
    {
        byte[] data = [0x78, 0x56, 0x34, 0x12, 0xFF];
        int offset = 0;

        uint val = MessageReader.ReadUInt32(ref data, ref offset, data.Length);
        Assert.Equal(0x12345678u, val);
        Assert.Equal(4, offset);
    }

    [Fact]
    public void ReadUInt32_Overflow_ReturnsZero()
    {
        byte[] data = [0x01, 0x02];
        int offset = 0;

        uint val = MessageReader.ReadUInt32(ref data, ref offset, data.Length);
        Assert.Equal(0u, val);
    }
}

public class MessageWriterTests
{
    [Fact]
    public void BuildCommandPacket_HeaderLayout()
    {
        byte[] payload = [0xDE, 0xAD];
        byte[] packet = MessageWriter.BuildCommandPacket(100, 200, 0x03, payload);

        Assert.Equal(10, packet.Length);
        uint header = BitConverter.ToUInt32(packet, 0);
        Assert.Equal(100u, header & MessageConstants.SequenceMask);
        Assert.True((header & MessageConstants.SequenceModeCommand) != 0);

        uint dst = BitConverter.ToUInt32(packet, 4);
        Assert.Equal(200u, dst);

        Assert.Equal(0xDE, packet[8]);
        Assert.Equal(0xAD, packet[9]);
    }

    [Fact]
    public void BuildAckPacket_ContainsAckData()
    {
        byte[] packet = MessageWriter.BuildAckPacket(5, 10);
        Assert.Equal(16, packet.Length);

        ulong ack = BitConverter.ToUInt64(packet, 8);
        Assert.Equal(MessageConstants.AckData, ack);
    }

    [Fact]
    public void WriteStringCmd_ProducesCorrectBytes()
    {
        var output = new List<byte>();
        MessageWriter.WriteStringCmd(output, ClientCommandType.StringCmd, "new");

        Assert.Equal(5, output.Count);
        Assert.Equal((byte)ClientCommandType.StringCmd, output[0]);
        Assert.Equal((byte)'n', output[1]);
        Assert.Equal((byte)'e', output[2]);
        Assert.Equal((byte)'w', output[3]);
        Assert.Equal((byte)0, output[4]);
    }
}

public class FragmentBufferTests
{
    [Fact]
    public void Start_ClearsAndActivates()
    {
        var fb = new FragmentBuffer();
        fb.AddData([0x01, 0x02]);
        fb.Start();

        Assert.True(fb.Active);
        Assert.Empty(fb.Data);
        Assert.Equal(0, fb.Offset);
    }

    [Fact]
    public void Start_SetsCallback()
    {
        var fb = new FragmentBuffer();
        int called = 0;
        fb.Start(() => called++);

        Assert.NotNull(fb.Callback);
        fb.Callback?.Invoke();
        Assert.Equal(1, called);
    }

    [Fact]
    public void AddData_AppendsAndUpdatesOffset()
    {
        var fb = new FragmentBuffer();
        fb.Start();
        fb.AddData([0x01, 0x02, 0x03]);
        fb.AddData([0x04, 0x05]);

        Assert.Equal(5, fb.Data.Count);
        Assert.Equal(5, fb.Offset);
        Assert.Equal([0x01, 0x02, 0x03, 0x04, 0x05], fb.Data);
    }

    [Fact]
    public void Reset_DeactivatesAndClears()
    {
        var fb = new FragmentBuffer();
        fb.Start();
        fb.AddData([0x01]);
        fb.Reset();

        Assert.False(fb.Active);
        Assert.Empty(fb.Data);
        Assert.Equal(0, fb.Offset);
        Assert.Null(fb.Callback);
    }

    [Fact]
    public void Default_NotActive()
    {
        var fb = new FragmentBuffer();
        Assert.False(fb.Active);
        Assert.Empty(fb.Data);
    }
}

public class StructLayoutTests
{
    [Fact]
    public void ServerInfoData_Size()
    {
        unsafe
        {
            int size = sizeof(ServerInfoData);
            Assert.Equal(31, size);
        }
    }

    [Fact]
    public void FragHead_Fields()
    {
        var fh = new FragHead { To = 10, At = 3, StartPosition = 100, Size = 200 };
        Assert.Equal(10, fh.To);
        Assert.Equal(3, fh.At);
        Assert.Equal(100, fh.StartPosition);
        Assert.Equal(200, fh.Size);
    }

    [Fact]
    public void DeltaType_Construction()
    {
        var fields = new DeltaField[] { new("test", DeltaFieldFlag.Float, 16, 1.0f) };
        var dt = new DeltaType("test_t", 1, fields);

        Assert.Equal("test_t", dt.DeltaName);
        Assert.Equal(1, dt.FieldAmount);
        Assert.Equal("test", dt.Fields[0].FieldName);
    }

    [Fact]
    public void ResourceInfo_Defaults()
    {
        var ri = new ResourceInfo();
        Assert.Empty(ri.Name);
        Assert.Equal(0, ri.Flag);
        Assert.Equal(16, ri.Md5.Length);
        Assert.Equal(32, ri.Reserved.Length);
        Assert.False(ri.NeedConsistency);
    }
}

public class DeltaDefinitionsExtendedTests
{
    [Fact]
    public void All_HasSevenEntries()
    {
        Assert.Equal(7, DeltaDefinitions.All.Length);
    }

    [Fact]
    public void Find_WeaponData_ReturnsCorrectType()
    {
        var dt = DeltaDefinitions.Find("weapon_data_t");
        Assert.True(dt.HasValue);
        Assert.Equal(0x14, dt.Value.FieldAmount);
        Assert.Equal("weapon_data_t", dt.Value.DeltaName);
    }

    [Fact]
    public void Find_EntityState_ReturnsCorrectType()
    {
        var dt = DeltaDefinitions.Find("entity_state_t");
        Assert.True(dt.HasValue);
        Assert.Equal(0x34, dt.Value.FieldAmount);
    }

    [Fact]
    public void Find_EntityStatePlayer_ReturnsCorrectType()
    {
        var dt = DeltaDefinitions.Find("entity_state_player_t");
        Assert.True(dt.HasValue);
        Assert.Equal(0x31, dt.Value.FieldAmount);
    }

    [Fact]
    public void Find_UserCmd_ReturnsCorrectType()
    {
        var dt = DeltaDefinitions.Find("usercmd_t");
        Assert.True(dt.HasValue);
        Assert.Equal(0x0F, dt.Value.FieldAmount);
    }

    [Fact]
    public void Find_CustomEntityState_ReturnsCorrectType()
    {
        var dt = DeltaDefinitions.Find("custom_entity_state_t");
        Assert.True(dt.HasValue);
        Assert.Equal(0x13, dt.Value.FieldAmount);
    }

    [Fact]
    public void EntityStatePlayer_HasAimentField()
    {
        var dt = DeltaDefinitions.EntityStatePlayer;
        Assert.Contains(dt.Fields, f => f.FieldName == "aiment");
        var aiment = dt.Fields.First(f => f.FieldName == "aiment");
        ulong flag = (ulong)aiment.FieldFlag;
        ulong cleaned = flag & ~(ulong)DeltaFieldFlag.Signed & ~(ulong)DeltaFieldFlag.StringField & ~(ulong)DeltaFieldFlag.Byte & ~(ulong)DeltaFieldFlag.Short & ~(ulong)DeltaFieldFlag.Float & ~(ulong)DeltaFieldFlag.Angle & ~(ulong)DeltaFieldFlag.TimeWindow8 & ~(ulong)DeltaFieldFlag.TimeWindowBig;
        Assert.Equal((ulong)DeltaFieldFlag.Integer, cleaned);
    }

    [Fact]
    public void EntityState_HasBeamEndpos()
    {
        var dt = DeltaDefinitions.EntityState;
        Assert.Contains(dt.Fields, f => f.FieldName == "endpos[0]");
        Assert.Contains(dt.Fields, f => f.FieldName == "startpos[0]");
    }

    [Fact]
    public void ClientData_HasHealth()
    {
        var dt = DeltaDefinitions.ClientData;
        Assert.Contains(dt.Fields, f => f.FieldName == "health");
        Assert.Contains(dt.Fields, f => f.FieldName == "flags");
    }

    [Fact]
    public void StaticFields_Exist()
    {
        Assert.Equal("event_t", DeltaDefinitions.Event.DeltaName);
        Assert.Equal("weapon_data_t", DeltaDefinitions.WeaponData.DeltaName);
        Assert.Equal("usercmd_t", DeltaDefinitions.UserCmd.DeltaName);
        Assert.Equal("custom_entity_state_t", DeltaDefinitions.CustomEntityState.DeltaName);
        Assert.Equal("entity_state_player_t", DeltaDefinitions.EntityStatePlayer.DeltaName);
        Assert.Equal("entity_state_t", DeltaDefinitions.EntityState.DeltaName);
        Assert.Equal("clientdata_t", DeltaDefinitions.ClientData.DeltaName);
    }
}

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
        Assert.Equal(0x80000000u, MessageConstants.SequenceModeCommand);
        Assert.Equal(0x40000000u, MessageConstants.SequenceModeFragment);
        Assert.Equal(0x3FFFFFFFu, MessageConstants.SequenceMask);
        Assert.Equal(0x0101010101010101UL, MessageConstants.AckData);
    }

    [Fact]
    public void SequenceMask_Covers30Bits()
    {
        Assert.Equal(0x3FFFFFFFu, MessageConstants.SequenceMask);
        Assert.Equal(30, System.Numerics.BitOperations.PopCount(MessageConstants.SequenceMask));
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
