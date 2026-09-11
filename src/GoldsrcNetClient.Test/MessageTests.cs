using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Protocol;
using System.Text;

namespace GoldsrcNetClient.Test;

public class MessageReaderTests
{
    [Fact]
    public void ReadString_ReturnsString()
    {
        byte[] data = [(byte)'t', (byte)'e', (byte)'s', (byte)'t', 0x00, 0xFF];
        var reader = new MessageReader(data);

        string result = reader.ReadString();
        Assert.Equal("test", result);
        Assert.Equal(5, reader.Offset);
    }

    [Fact]
    public void ReadString_NoNullTerminator()
    {
        byte[] data = [(byte)'a', (byte)'b', (byte)'c'];
        var reader = new MessageReader(data);

        string result = reader.ReadString();
        Assert.Equal("abc", result);
        Assert.Equal(3, reader.Offset);
    }

    [Fact]
    public void ReadString_EmptyAtNull()
    {
        byte[] data = [0x00, 0xFF];
        var reader = new MessageReader(data);

        string result = reader.ReadString();
        Assert.Equal("", result);
        Assert.Equal(1, reader.Offset);
    }

    [Fact]
    public void ReadString_Bytes_ReturnsBytes()
    {
        byte[] data = [(byte)'A', (byte)'B', 0x00, 0xFF];
        var reader = new MessageReader(data);

        bool ok = reader.ReadString(out byte[] str);
        Assert.True(ok);
        Assert.Equal([(byte)'A', (byte)'B'], str);
        Assert.Equal(3, reader.Offset);
    }

    [Fact]
    public void ReadString_Bytes_NoNull_ReturnsFalse()
    {
        byte[] data = [(byte)'X', (byte)'Y', (byte)'Z'];
        var reader = new MessageReader(data);

        bool ok = reader.ReadString(out byte[] str);
        Assert.False(ok);
        Assert.Equal(3, reader.Offset);
    }

    [Fact]
    public void ReadBytes_Success()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05];
        var reader = new MessageReader(data);
        reader.Offset = 1;
        Span<byte> dest = new byte[3];

        bool ok = reader.ReadBytes(dest);
        Assert.True(ok);
        Assert.Equal(0x02, dest[0]);
        Assert.Equal(0x04, dest[2]);
        Assert.Equal(4, reader.Offset);
    }

    [Fact]
    public void ReadBytes_Overflow_ReturnsFalse()
    {
        byte[] data = [0x01, 0x02];
        var reader = new MessageReader(data);
        reader.Offset = 1;
        Span<byte> dest = new byte[3];

        bool ok = reader.ReadBytes(dest);
        Assert.False(ok);
    }

    [Fact]
    public void ReadUInt16_Valid()
    {
        byte[] data = [0xCD, 0xAB, 0xFF];
        var reader = new MessageReader(data);

        ushort val = reader.ReadUInt16();
        Assert.Equal(0xABCDu, val);
        Assert.Equal(2, reader.Offset);
    }

    [Fact]
    public void ReadUInt16_Overflow_ReturnsZero()
    {
        byte[] data = [0x01];
        var reader = new MessageReader(data);

        ushort val = reader.ReadUInt16();
        Assert.Equal(0u, val);
    }

    [Fact]
    public void ReadUInt32_Valid()
    {
        byte[] data = [0x78, 0x56, 0x34, 0x12, 0xFF];
        var reader = new MessageReader(data);

        uint val = reader.ReadUInt32();
        Assert.Equal(0x12345678u, val);
        Assert.Equal(4, reader.Offset);
    }

    [Fact]
    public void ReadUInt32_Overflow_ReturnsZero()
    {
        byte[] data = [0x01, 0x02];
        var reader = new MessageReader(data);

        uint val = reader.ReadUInt32();
        Assert.Equal(0u, val);
    }
}

public class MessageWriterTests
{
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

public class Utf8EncodingTests
{
    [Fact]
    public void MessageReader_ReadString_HandlesAscii()
    {
        byte[] data = [(byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0];
        var reader = new MessageReader(data);
        var result = reader.ReadString();
        Assert.Equal("hello", result);
    }

    [Fact]
    public void MessageReader_ReadString_HandlesMultiByteUtf8()
    {
        var utf8Bytes = Encoding.UTF8.GetBytes("café");
        byte[] data = new byte[utf8Bytes.Length + 1];
        utf8Bytes.CopyTo(data, 0);
        data[^1] = 0;
        var reader = new MessageReader(data);
        var result = reader.ReadString();
        Assert.Equal("café", result);
    }

    [Fact]
    public void MessageReader_ReadString_HandlesCjkCharacters()
    {
        var utf8Bytes = Encoding.UTF8.GetBytes("玩家");
        byte[] data = new byte[utf8Bytes.Length + 1];
        utf8Bytes.CopyTo(data, 0);
        data[^1] = 0;
        var reader = new MessageReader(data);
        var result = reader.ReadString();
        Assert.Equal("玩家", result);
    }

    [Fact]
    public void MessageWriter_WriteStringCmd_ProducesUtf8Bytes()
    {
        var output = new List<byte>();
        MessageWriter.WriteStringCmd(output, ClientCommandType.StringCmd, "café");
        Assert.Equal((byte)ClientCommandType.StringCmd, output[0]);
        var terminatorIndex = output.IndexOf((byte)0, 1);
        var payload = output.GetRange(1, terminatorIndex - 1).ToArray();
        var decoded = Encoding.UTF8.GetString(payload);
        Assert.Equal("café", decoded);
    }

    [Fact]
    public void MessageWriter_WriteStringCmd_RoundtripCjk()
    {
        var output = new List<byte>();
        MessageWriter.WriteStringCmd(output, ClientCommandType.StringCmd, "玩家");
        Assert.Equal((byte)ClientCommandType.StringCmd, output[0]);
        var terminatorIndex = output.IndexOf((byte)0, 1);
        var payload = output.GetRange(1, terminatorIndex - 1).ToArray();
        var decoded = Encoding.UTF8.GetString(payload);
        Assert.Equal("玩家", decoded);
    }

    [Fact]
    public void MessageReader_ReadString_ReturnsRawBytes()
    {
        byte[] data = [(byte)'A', (byte)'B', 0, (byte)'C'];
        var reader = new MessageReader(data);
        var result = reader.ReadString(out byte[] raw);
        Assert.True(result);
        Assert.Equal(new byte[] { (byte)'A', (byte)'B' }, raw);
    }
}
