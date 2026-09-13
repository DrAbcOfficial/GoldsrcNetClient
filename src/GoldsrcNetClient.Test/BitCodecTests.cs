using GoldsrcNetClient.Core.Util;

namespace GoldsrcNetClient.Test;

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

public class BitReaderExtendedTests
{
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
        Assert.Equal("Hello", System.Text.Encoding.UTF8.GetString(result));
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
