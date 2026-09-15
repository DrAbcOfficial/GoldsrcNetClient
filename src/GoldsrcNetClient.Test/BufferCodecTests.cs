using GoldsrcNetClient.Core.Io;
using System.Runtime.InteropServices;

namespace GoldsrcNetClient.Test;

public class BufferReaderTests
{
    [Fact]
    public void UInt8_ReadsInOrder()
    {
        var reader = new BufferReader((byte[])[0x01, 0x02, 0x03]);
        Assert.Equal(0x01, reader.ReadUInt8());
        Assert.Equal(0x02, reader.ReadUInt8());
        Assert.Equal(0x03, reader.ReadUInt8());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void UInt16_IsLittleEndian()
    {
        var reader = new BufferReader((byte[])[0x34, 0x12]);
        Assert.Equal(0x1234, reader.ReadUInt16());
    }

    [Fact]
    public void UInt32_IsLittleEndian()
    {
        var reader = new BufferReader((byte[])[0x78, 0x56, 0x34, 0x12]);
        Assert.Equal(0x12345678u, reader.ReadUInt32());
    }

    [Fact]
    public void Int16_Int32_SignCorrect()
    {
        var reader = new BufferReader((byte[])[0xFE, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);
        Assert.Equal(-2, reader.ReadInt16());
        Assert.Equal(-1, reader.ReadInt32());
    }

    [Fact]
    public void Single_ReadsIeee754()
    {
        var reader = new BufferWriter().Also(w => w.WriteSingle(3.5f)).ToReader();
        Assert.Equal(3.5f, reader.ReadSingle());
    }

    [Fact]
    public void String_NullTerminated()
    {
        var reader = new BufferReader("hello\0rest"u8.ToArray());
        Assert.Equal("hello", reader.ReadString());
        Assert.Equal("rest", reader.ReadString());
    }

    [Fact]
    public void String_Utf8Preserved()
    {
        var reader = new BufferReader("你好\0"u8.ToArray());
        Assert.Equal("你好", reader.ReadString());
    }

    [Fact]
    public void String_WithoutTerminator_ConsumesRest()
    {
        var reader = new BufferReader("tail"u8.ToArray());
        Assert.Equal("tail", reader.ReadString());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void StringLine_StopsAtNewlineOrNul()
    {
        var reader = new BufferReader("line1\nline2\0tail"u8.ToArray());
        Assert.Equal("line1", reader.ReadStringLine());
        Assert.Equal("line2", reader.ReadStringLine());
    }

    [Fact]
    public void ReadBytes_Bounded()
    {
        var reader = new BufferReader((byte[])[1, 2, 3, 4]);
        Assert.Equal((byte[])[1, 2], reader.ReadBytes(2));
        Assert.Equal(2, reader.Remaining);
    }

    [Fact]
    public void Overflow_ThrowsEndOfBuffer()
    {
        var reader = new BufferReader((byte[])[0x01]);
        reader.ReadUInt8();
        Assert.Equal(0, reader.Remaining);
        Assert.Throws<EndOfBufferException>(() => new BufferReader((byte[])[0x01]).ReadUInt16());
        Assert.Throws<EndOfBufferException>(() =>
        {
            var r = new BufferReader((byte[])[0x01]);
            r.ReadUInt8();
            r.ReadBits(2);
        });
    }

    [Fact]
    public void Struct_OverlaysPackedLayout()
    {
        var reader = new BufferWriter().Also(w =>
        {
            w.WriteUInt8(0xAA);
            w.WriteUInt8(7);
            w.WriteUInt16(0x1234);
        }).ToReader();
        Assert.Equal(0xAA, reader.ReadUInt8());
        Assert.Equal(7, reader.ReadStruct<TestPacked>().Value);
        Assert.Equal(0x1234, (int)reader.ReadUInt16());
    }

    [Fact]
    public void Slice_BoundsReaderAndAdvancesOuter()
    {
        var reader = new BufferReader((byte[])[1, 2, 3, 4, 5]);
        var slice = reader.Slice(3);
        Assert.Equal((byte[])[1, 2, 3], slice.ReadBytes(3));
        Assert.Equal(2, reader.Remaining);
        Assert.Equal(4, reader.ReadUInt8());
    }

    [Fact]
    public void BytePosition_SetRealigns()
    {
        var reader = new BufferReader((byte[])[0, 0, 9]);
        reader.BytePosition = 2;
        Assert.Equal(9, reader.ReadUInt8());
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TestPacked(byte value)
    {
        public byte Value = value;
    }
}

public class BufferReaderBitsTests
{
    [Fact]
    public void Bits_SpanByteBoundary()
    {
        // 10 bits from 0b00101101 0b11000000 -> 0b0000101101 = 45 (legacy BitReader case).
        var reader = new BufferReader((byte[])[0b00101101, 0b11000000]);
        Assert.Equal(45u, reader.ReadBits(10));
    }

    [Fact]
    public void Bits_AtNonZeroOffset()
    {
        var reader = new BufferReader((byte[])[0b11111111, 0b00001010]);
        Assert.Equal(0b1111111u, reader.ReadBits(7));
        // bit 7 of byte 0 (=1) becomes result bit 0; byte 1 low 7 bits (=10) shift up.
        Assert.Equal(1u | (10u << 1), reader.ReadBits(8));
        Assert.Equal(0u, reader.ReadBits(1));
    }

    [Fact]
    public void Bits_32BitLittleEndianOrdering()
    {
        var reader = new BufferReader((byte[])[0xD2, 0x91, 0xA6, 0x78]);
        Assert.Equal(0x78A691D2u, reader.ReadBits(32));
    }

    [Fact]
    public void ByteRead_WorksAtUnalignedPosition()
    {
        var writer = new BufferWriter();
        writer.WriteBits(0b101, 3);
        writer.WriteUInt8(0xAB);
        var reader = writer.ToReader();
        Assert.Equal(0b101u, reader.ReadBits(3));
        Assert.Equal(0xAB, reader.ReadUInt8());
    }

    [Fact]
    public void Unaligned_Primitives_MatchAligned()
    {
        var writer = new BufferWriter();
        writer.WriteBits(1, 1);
        writer.WriteUInt16(0xBEEF);
        writer.WriteUInt32(0xDEADBEEF);
        writer.WriteSingle(-2.25f);
        writer.Align();
        var reader = writer.ToReader();
        Assert.Equal(1u, reader.ReadBits(1));
        Assert.Equal(0xBEEF, reader.ReadUInt16());
        Assert.Equal(0xDEADBEEFu, reader.ReadUInt32());
        Assert.Equal(-2.25f, reader.ReadSingle());
    }

    [Fact]
    public void BitCoord_PositiveWithFraction()
    {
        // flags 1,1,0 + intval 4 (12 bits) + fract 1 (3 bits) = 4 + 1/8
        var writer = new BufferWriter();
        writer.WriteBits(1, 1);
        writer.WriteBits(1, 1);
        writer.WriteBits(0, 1);
        writer.WriteBits(4, 12);
        writer.WriteBits(1, 3);
        var reader = writer.ToReader();
        Assert.Equal(4.125f, reader.ReadBitCoord());
    }

    [Fact]
    public void BitCoord_Negative()
    {
        var writer = new BufferWriter();
        writer.WriteBits(1, 1);
        writer.WriteBits(1, 1);
        writer.WriteBits(1, 1); // sign
        writer.WriteBits(4, 12);
        writer.WriteBits(1, 3);
        var reader = writer.ToReader();
        Assert.Equal(-4.125f, reader.ReadBitCoord());
    }

    [Fact]
    public void BitCoord_ZeroIsCompact()
    {
        var reader = new BufferReader((byte[])[0x00, 0xFF]);
        Assert.Equal(0f, reader.ReadBitCoord());
        Assert.Equal(2, reader.BitPosition); // only the two flag bits consumed
    }

    [Theory]
    [InlineData(0x00, 0.0f)]
    [InlineData(0x40, 90.0f)]   // 64 * 360/256
    [InlineData(0x80, 180.0f)]
    [InlineData(0xFF, 358.59375f)]
    public void BitAngle_8Bit(byte raw, float expected)
    {
        var reader = new BufferReader([raw]);
        Assert.Equal(expected, reader.ReadBitAngle(8), 5);
    }

    [Fact]
    public void BitCoordWide_16Point16()
    {
        var writer = new BufferWriter();
        writer.WriteUInt32(unchecked((uint)(-2 * 65536 + 16384))); // -1.75
        var reader = writer.ToReader();
        Assert.Equal(-1.75f, reader.ReadBitCoordWide());
    }

    [Fact]
    public void BitString_StopsAtNullAndTrims()
    {
        var writer = new BufferWriter();
        writer.WriteBits(0b1, 1); // force unaligned string
        writer.WriteBytes("maps/mymap.bsp"u8.ToArray());
        writer.WriteUInt8(0);
        var reader = writer.ToReader();
        Assert.Equal(1u, reader.ReadBits(1));
        Assert.Equal("maps/mymap.bsp"u8.ToArray(), reader.ReadBitString(64));
    }

    [Fact]
    public void UnalignedString_ReadsCorrectly()
    {
        var writer = new BufferWriter();
        writer.WriteBits(0b011, 3);
        writer.WriteString("héllo");
        writer.Align();
        var reader = writer.ToReader();
        Assert.Equal(0b011u, reader.ReadBits(3));
        Assert.Equal("héllo", reader.ReadString());
    }

    [Fact]
    public void UnalignedString_LongerThanStackBuffer()
    {
        string longText = new string('x', 300) + "é";
        var writer = new BufferWriter();
        writer.WriteBits(1, 1);
        writer.WriteString(longText);
        writer.Align();
        var reader = writer.ToReader();
        Assert.Equal(1u, reader.ReadBits(1));
        Assert.Equal(longText, reader.ReadString());
    }
}

public class BufferWriterTests
{
    [Fact]
    public void Primitives_RoundTrip()
    {
        var reader = new BufferWriter().Also(w =>
        {
            w.WriteUInt8(0x7F);
            w.WriteUInt16(0x1234);
            w.WriteUInt32(0xCAFEBABEu);
            w.WriteSingle(1.5f);
        }).ToReader();
        Assert.Equal(0x7F, reader.ReadUInt8());
        Assert.Equal(0x1234, reader.ReadUInt16());
        Assert.Equal(0xCAFEBABEu, reader.ReadUInt32());
        Assert.Equal(1.5f, reader.ReadSingle());
    }

    [Fact]
    public void String_WritesNullTerminator()
    {
        var bytes = new BufferWriter().Also(w => w.WriteString("say hi")).ToArray();
        Assert.Equal("say hi\0"u8.ToArray(), bytes);
    }

    [Fact]
    public void Bits_RoundTrip()
    {
        var reader = new BufferWriter().Also(w =>
        {
            w.WriteBits(0b1011, 4);
            w.WriteBits(0x123456, 24);
            w.WriteBits(3, 2);
        }).ToReader();
        Assert.Equal(0b1011u, reader.ReadBits(4));
        Assert.Equal(0x123456u, reader.ReadBits(24));
        Assert.Equal(3u, reader.ReadBits(2));
        Assert.Equal(30, reader.BitPosition);
    }

    [Fact]
    public void Bits_Unaligned32BitAcrossFiveBytes()
    {
        // 3 + 32 bits spans five bytes; this is the growth/overlap regression case.
        var reader = new BufferWriter().Also(w =>
        {
            w.WriteBits(0b110, 3);
            w.WriteBits(0xDEADBEEFu, 32);
        }).ToReader();
        Assert.Equal(0b110u, reader.ReadBits(3));
        Assert.Equal(0xDEADBEEFu, reader.ReadBits(32));
    }

    [Fact]
    public void GrowsBeyondInitialCapacity()
    {
        var writer = new BufferWriter();
        byte[] payload = new byte[1000];
        Random.Shared.NextBytes(payload);
        writer.WriteBytes(payload);
        Assert.Equal(1000, writer.Length);
        Assert.Equal(payload, writer.ToArray());
    }

    [Fact]
    public void WriterReader_FullRoundTrip()
    {
        var writer = new BufferWriter();
        writer.WriteUInt8(1);
        writer.WriteBits(5, 3);
        writer.WriteUInt16(0xFFFF);
        writer.WriteString("done");
        writer.Align();
        var reader = writer.ToReader();
        Assert.Equal(1, reader.ReadUInt8());
        Assert.Equal(5u, reader.ReadBits(3));
        Assert.Equal(0xFFFF, reader.ReadUInt16());
        Assert.Equal("done", reader.ReadString());
    }
}

file static class TestExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }

    public static BufferReader ToReader(this BufferWriter writer) => new(writer.WrittenSpan);
}
