using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.Core.Util;

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
    /// Skips one delta-compressed record of type <paramref name="dt"/> at the given
    /// bit position, advancing <paramref name="bitIdx"/> past it.
    /// </summary>
    /// <param name="byteCountBits">Width of the bitmap byte-count prefix (3 Valve, 4 Sven).</param>
    public static bool ReadFields(DeltaType dt, byte[] data, int size, ref int bitIdx, int byteCountBits = 3)
    {
        uint byteCount = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref byteCount, byteCountBits))
            return false;

        if (byteCount > dt.FieldAmount / 8 + (dt.FieldAmount % 8 != 0 ? 1 : 0))
            return false;

        ulong markArray = 0;
        for (uint i = 0; i < byteCount; i++)
        {
            uint b = 0;
            if (!BitReader.ReadBits(data, ref bitIdx, size, ref b, 8))
                return false;
            markArray |= (ulong)b << (int)(i * 8);
        }

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
                    while (true)
                    {
                        uint ch = 0;
                        if (!BitReader.ReadBits(data, ref bitIdx, size, ref ch, 8))
                            return false;
                        if (ch == 0) break;
                    }
                }
                else
                {
                    uint filler = 0;
                    if (!BitReader.ReadBits(data, ref bitIdx, size, ref filler, field.Bits))
                        return false;
                }
            }
            markArray >>= 1;
        }

        return true;
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
    /// svc_deltadescription fields), advancing <paramref name="bitIdx"/> past it.
    /// </summary>
    /// <param name="byteCountBits">Width of the bitmap byte-count prefix (3 Valve, 4 Sven).</param>
    /// <param name="description">Decoded field description.</param>
    public static bool TryReadFieldDescription(byte[] data, int size, ref int bitIdx, int byteCountBits,
        out DeltaFieldDescription description)
    {
        description = default;

        uint byteCount = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref byteCount, byteCountBits))
            return false;
        if (byteCount > 8)
            return false;

        ulong markArray = 0;
        for (uint i = 0; i < byteCount; i++)
        {
            uint b = 0;
            if (!BitReader.ReadBits(data, ref bitIdx, size, ref b, 8))
                return false;
            markArray |= (ulong)b << (int)(i * 8);
        }

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
                    case 0:
                        if (!BitReader.ReadBits(data, ref bitIdx, size, ref fieldType, 32)) return false;
                        break;
                    case 1:
                        if (!BitReader.ReadBitString(data, ref bitIdx, size, out nameBytes)) return false;
                        break;
                    case 2:
                        if (!BitReader.ReadBits(data, ref bitIdx, size, ref fieldOffset, 16)) return false;
                        break;
                    case 3:
                        if (!BitReader.ReadBits(data, ref bitIdx, size, ref fieldSize, 8)) return false;
                        break;
                    case 4:
                        if (!BitReader.ReadBits(data, ref bitIdx, size, ref sigBits, 8)) return false;
                        break;
                    case 5:
                        if (!BitReader.ReadBits(data, ref bitIdx, size, ref preWire, 32)) return false;
                        break;
                    case 6:
                        if (!BitReader.ReadBits(data, ref bitIdx, size, ref postWire, 32)) return false;
                        break;
                }
            }
            markArray >>= 1;
        }

        if (fieldType == 0 || sigBits == 0)
            return false;

        description = new DeltaFieldDescription
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
        return true;
    }
}
