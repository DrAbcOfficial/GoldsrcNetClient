using GoldsrcNetClient.Core.Delta;
using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Test;

/// <summary>
/// Test-side mirror of Sven hw.dll's delta writer (DELTA_WriteDelta @ 0x1d44f10
/// and the meta table @ 0x1edf890, both Ghidra-verified), used to synthesize
/// svc_deltadescription payloads the same way SV_SendServerinfo does.
/// </summary>
public static class DeltaWire
{
    public const float MetaFloatScale = 4000.0f;

    /// <summary>Writes one field entry exactly as the engine's DELTA_WriteDelta does
    /// against an all-zero baseline: [byteCount:NB][bitmap bytes] + marked fields.</summary>
    public static void WriteFieldEntry(BitBuffer buf, DeltaFieldDescription f, int byteCountBits)
    {
        // Marked = value differs from the zero baseline (MarkSendFields compares per type).
        bool[] marked =
        {
            (uint)f.FieldType != 0,                                  // 0: fieldType (DT_INTEGER 32b)
            f.FieldName.Length > 0,                                  // 1: fieldName (DT_STRING)
            f.FieldOffset != 0,                                      // 2: fieldOffset (DT_INTEGER 16b)
            f.FieldSize != 0,                                        // 3: fieldSize (DT_INTEGER 8b)
            f.SignificantBits != 0,                                  // 4: significant_bits (8b)
            f.Premultiply != 0f,                                     // 5: premultiply (DT_FLOAT 32b)
            f.PostMultiply != 0f,                                    // 6: postmultiply (DT_FLOAT 32b)
        };

        int lastBit = -1;
        for (int i = marked.Length - 1; i >= 0; i--)
            if (marked[i]) { lastBit = i; break; }
        Assert.True(lastBit >= 0, "field entry would not be sent at all");

        int byteCount = (lastBit >> 3) + 1;
        buf.WriteBits((uint)byteCount, byteCountBits);
        for (int b = 0; b < byteCount; b++)
        {
            uint bitmap = 0;
            for (int i = 0; i < 8; i++)
            {
                int idx = b * 8 + i;
                if (idx < marked.Length && marked[idx]) bitmap |= 1u << i;
            }
            buf.WriteBits(bitmap, 8);
        }

        if (marked[0]) buf.WriteBits((uint)f.FieldType, 32);
        if (marked[1]) buf.WriteString(f.FieldName);
        if (marked[2]) buf.WriteBits((uint)f.FieldOffset, 16);
        if (marked[3]) buf.WriteBits((uint)f.FieldSize, 8);
        if (marked[4]) buf.WriteBits((uint)f.SignificantBits, 8);
        if (marked[5]) buf.WriteBits((uint)MathF.Round(f.Premultiply * MetaFloatScale), 32);
        if (marked[6]) buf.WriteBits((uint)MathF.Round(f.PostMultiply * MetaFloatScale), 32);
    }

    /// <summary>Writes a full svc_deltadescription body (without the 0x0E type byte):
    /// name string, 16-bit field count, then one field entry per field.</summary>
    public static void WriteDescription(BitBuffer buf, string name, DeltaFieldDescription[] fields, int byteCountBits)
    {
        buf.WriteString(name);
        buf.WriteBits((uint)fields.Length, 16);
        foreach (var f in fields)
            WriteFieldEntry(buf, f, byteCountBits);
    }
}

/// <summary>Minimal LSB-first bit buffer over a byte array (test helper).</summary>
public class BitBuffer
{
    private readonly byte[] _data;
    private int _bit;

    public BitBuffer(int bytes = 4096) => _data = new byte[bytes];

    public int BitLength => _bit;
    public byte[] Data => _data;

    public void WriteBits(uint value, int bits)
    {
        for (int i = 0; i < bits; i++)
        {
            if ((value >> i & 1) != 0)
                _data[_bit >> 3] |= (byte)(1 << (_bit & 7));
            _bit++;
        }
    }

    public void WriteString(string s)
    {
        foreach (var c in System.Text.Encoding.ASCII.GetBytes(s))
            WriteBits(c, 8);
        WriteBits(0, 8);
    }
}

public class DeltaDescriptionWireTests
{
    /// <summary>Sven delta.lst clientdata_t head: flTimeStepSound sits at struct
    /// offset 0, so the engine omits fieldOffset from its entry — the decoder
    /// must reconstruct it as 0 instead of consuming 16 bits.</summary>
    public static readonly DeltaFieldDescription[] ClientDataHead =
    [
        new() { FieldName = "flTimeStepSound", FieldType = DeltaFieldFlag.Integer, FieldOffset = 0, FieldSize = 1, SignificantBits = 10, Premultiply = 1f, PostMultiply = 1f },
        new() { FieldName = "origin[0]", FieldType = DeltaFieldFlag.Signed | DeltaFieldFlag.Float, FieldOffset = 4, FieldSize = 1, SignificantBits = 24, Premultiply = 32f, PostMultiply = 1f },
        new() { FieldName = "physinfo", FieldType = DeltaFieldFlag.StringField, FieldOffset = 8, FieldSize = 32, SignificantBits = 1, Premultiply = 1f, PostMultiply = 1f },
    ];

    [Fact]
    public void SvenMetaEntries_RoundTrip()
    {
        var buf = new BitBuffer();
        DeltaWire.WriteDescription(buf, "clientdata_t", ClientDataHead, byteCountBits: 4);

        var reader = new BufferReader(buf.Data);
        Assert.Equal("clientdata_t", reader.ReadString());

        uint fieldCount = reader.ReadBits(16);
        Assert.Equal((uint)ClientDataHead.Length, fieldCount);

        for (int i = 0; i < ClientDataHead.Length; i++)
        {
            var desc = DeltaReader.ReadFieldDescription(ref reader, byteCountBits: 4);
            Assert.Equal(ClientDataHead[i].FieldName, desc.FieldName);
            Assert.Equal(ClientDataHead[i].FieldType, desc.FieldType);
            Assert.Equal(ClientDataHead[i].FieldOffset, desc.FieldOffset);
            Assert.Equal(ClientDataHead[i].SignificantBits, desc.SignificantBits);
            Assert.Equal(ClientDataHead[i].Premultiply, desc.Premultiply, 5);
            Assert.Equal(ClientDataHead[i].PostMultiply, desc.PostMultiply, 5);

            var field = desc.ToDeltaField();
            Assert.Equal(ClientDataHead[i].FieldName, field.FieldName);
            Assert.Equal((byte)ClientDataHead[i].SignificantBits, field.Bits);
        }

        Assert.Equal(buf.BitLength, reader.BitPosition);
    }

    [Fact]
    public void SvenByteCount_IsFourBits_ValveIsThree()
    {
        // A 3-bit reader misparses a 4-bit stream: the extra bit shifts every
        // subsequent field. Verify the Valve width still decodes Valve content.
        var buf = new BitBuffer();
        DeltaWire.WriteDescription(buf, "event_t", ClientDataHead, byteCountBits: 3);
        var reader = new BufferReader(buf.Data);
        reader.ReadString();
        uint fieldCount = reader.ReadBits(16);
        var desc = DeltaReader.ReadFieldDescription(ref reader, byteCountBits: 3);
        Assert.Equal("flTimeStepSound", desc.FieldName);
    }

    [Fact]
    public void DynamicTable_IsUsedByReadFields_WithFourBitPrefix()
    {
        // Register a dynamic 2-field table and craft a delta record marking only
        // the second field — the reader must consume exactly 4+8+bits bits.
        var dynamic = new DeltaType("clientdata_t", 2,
        [
            new DeltaField("health", DeltaFieldFlag.Signed | DeltaFieldFlag.Float, 10, 1f),
            new DeltaField("origin[0]", DeltaFieldFlag.Signed | DeltaFieldFlag.Float, 24, 32f),
        ]);

        var buf = new BitBuffer();
        buf.WriteBits(1, 4);          // byteCount = 1 (Sven prefix)
        buf.WriteBits(0b10, 8);       // bit 1 marked (origin[0]), bit 0 clear
        buf.WriteBits(123456, 24);    // origin[0] payload

        var reader = new BufferReader(buf.Data);
        DeltaReader.ReadFields(dynamic, ref reader, byteCountBits: 4);
        Assert.Equal(buf.BitLength, reader.BitPosition);
    }

    [Fact]
    public void ValveByteCount_StillThreeBits()
    {
        var dynamic = new DeltaType("clientdata_t", 2,
        [
            new DeltaField("health", DeltaFieldFlag.Signed | DeltaFieldFlag.Float, 10, 1f),
            new DeltaField("origin[0]", DeltaFieldFlag.Signed | DeltaFieldFlag.Float, 24, 32f),
        ]);

        var buf = new BitBuffer();
        buf.WriteBits(1, 3);          // byteCount = 1 (Valve prefix)
        buf.WriteBits(0b10, 8);
        buf.WriteBits(123456, 24);

        var reader = new BufferReader(buf.Data);
        DeltaReader.ReadFields(dynamic, ref reader, byteCountBits: 3);
        Assert.Equal(buf.BitLength, reader.BitPosition);
    }
}
