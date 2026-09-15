using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Core.Delta;

/// <summary>
/// Reads delta-compressed records from a bitstream: a byte-count prefix,
/// a per-field presence bitmap, then one encoded value per marked field
/// (string fields are read as null-terminated 8-bit strings).
/// </summary>
/// <remarks>
/// The byte-count prefix is 3 bits on Valve GoldSrc branches (hw.dll build 8684,
/// ReHLDS MSG_WriteBits(bytecount, 3)) and <c>4 bits</c> on Sven Co-op's engine
/// (hw.dll build 10257 writes <c>MSG_WriteBits(bytecount, 4)</c> in
/// DELTA_WriteDelta @ 0x1d44f10 — Ghidra-verified). The same width applies to
/// every delta payload: entity baselines, clientdata, and the field entries of
/// svc_deltadescription itself.
/// </remarks>
public static class DeltaReader
{
    /// <summary>
    /// Skips one delta-compressed record of type <paramref name="dt"/> at the
    /// reader's bit position, advancing past it.
    /// </summary>
    /// <param name="byteCountBits">Width of the bitmap byte-count prefix (3 Valve, 4 Sven).</param>
    public static void ReadFields(DeltaType dt, ref BufferReader reader, int byteCountBits = 3)
    {
        uint byteCount = reader.ReadBits(byteCountBits);
        if (byteCount > dt.FieldAmount / 8 + (dt.FieldAmount % 8 != 0 ? 1 : 0))
            throw new InvalidDataException($"Delta bitmap byte count {byteCount} exceeds the field count of \"{dt.DeltaName}\".");

        ulong markArray = 0;
        for (uint i = 0; i < byteCount; i++)
            markArray |= (ulong)reader.ReadBits(8) << (int)(i * 8);

        uint toIterate = byteCount * 8;
        if (toIterate > dt.FieldAmount) toIterate = dt.FieldAmount;

        for (uint i = 0; i < toIterate; i++)
        {
            if ((markArray & 1) != 0)
            {
                var field = dt.Fields[i];
                if ((field.FieldFlag & DeltaFieldFlag.StringField) != 0)
                {
                    // Null-terminated with no fixed cap: clientdata_t.physinfo
                    // carries the whole userinfo string and routinely exceeds
                    // 32 bytes (a cap here desynchronises the stream).
                    while (reader.ReadBits(8) != 0)
                    {
                    }
                }
                else
                {
                    reader.ReadBits(field.Bits);
                }
            }
            markArray >>= 1;
        }
    }

    /// <summary>
    /// Meta layout of <c>svc_deltadescription</c> field entries. The engine
    /// delta-encodes each entry against an all-zero baseline using this fixed
    /// 7-field table (Sven hw.dll g_MetaDelta @ 0x1edf890, identical to ReHLDS),
    /// so the client reconstructs each entry from the presence bitmap:
    /// [fieldType 32b] [fieldName string] [fieldOffset 16b] [fieldSize 8b]
    /// [significant_bits 8b] [premultiply 32b] [postmultiply 32b].
    /// A field whose value is zero (only possible for fieldOffset) is absent
    /// from the bitmap and decodes as zero.
    /// </summary>
    private const int MetaFieldCount = 7;
    private const float MetaFloatScale = 4000.0f;

    /// <summary>
    /// Reads one delta_description_t field entry (the payload of
    /// svc_deltadescription fields), advancing the reader past it.
    /// </summary>
    /// <param name="byteCountBits">Width of the bitmap byte-count prefix (3 Valve, 4 Sven).</param>
    public static DeltaFieldDescription ReadFieldDescription(ref BufferReader reader, int byteCountBits)
    {
        uint byteCount = reader.ReadBits(byteCountBits);
        if (byteCount > 8)
            throw new InvalidDataException($"Delta description bitmap byte count {byteCount} exceeds the meta field count.");

        ulong markArray = 0;
        for (uint i = 0; i < byteCount; i++)
            markArray |= (ulong)reader.ReadBits(8) << (int)(i * 8);

        uint toIterate = byteCount * 8;
        if (toIterate > MetaFieldCount) toIterate = MetaFieldCount;

        uint fieldType = 0;
        byte[] nameBytes = [];
        uint fieldOffset = 0;
        uint fieldSize = 0;
        uint sigBits = 0;
        uint preWire = 0;
        uint postWire = 0;

        for (uint i = 0; i < toIterate; i++)
        {
            if ((markArray & 1) != 0)
            {
                switch (i)
                {
                    case 0: fieldType = reader.ReadBits(32); break;
                    case 1: nameBytes = reader.ReadBitString(); break;
                    case 2: fieldOffset = reader.ReadBits(16); break;
                    case 3: fieldSize = reader.ReadBits(8); break;
                    case 4: sigBits = reader.ReadBits(8); break;
                    case 5: preWire = reader.ReadBits(32); break;
                    case 6: postWire = reader.ReadBits(32); break;
                }
            }
            markArray >>= 1;
        }

        if (fieldType == 0 || sigBits == 0)
            throw new InvalidDataException("Delta description entry has no field type or significant bits.");

        return new DeltaFieldDescription
        {
            FieldName = System.Text.Encoding.ASCII.GetString(nameBytes),
            FieldType = (DeltaFieldFlag)fieldType,
            FieldOffset = (int)fieldOffset,
            FieldSize = (int)fieldSize,
            SignificantBits = (int)sigBits,
            // The engine writes premultiply/postmultiply as (uint)ROUND(value * 4000).
            Premultiply = preWire / MetaFloatScale,
            PostMultiply = postWire / MetaFloatScale,
        };
    }
}
