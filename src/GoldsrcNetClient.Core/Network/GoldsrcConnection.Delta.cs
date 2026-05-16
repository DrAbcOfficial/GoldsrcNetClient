using GoldsrcNetClient.Core.Delta;
using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.Core.Util;

namespace GoldsrcNetClient.Core.Network;

public partial class GoldsrcConnection
{
    internal static bool ParseDeltaFields(DeltaType dt, byte[] data, int size, ref int bitIdx)
    {
        uint byteCount = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref byteCount, 3))
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
                    for (int s = 0; s < 32; s++)
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

    internal static bool ParseDeltaFieldDescriptions(byte[] data, int size, ref int bitIdx)
    {
        uint fieldType = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref fieldType, 32)) return false;
        if (!BitReader.ReadBitString(data, ref bitIdx, size, out _)) return false;
        uint fieldOffset = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref fieldOffset, 16)) return false;
        uint fieldSize = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref fieldSize, 8)) return false;
        uint sigBits = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref sigBits, 8)) return false;
        uint preMul = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref preMul, 32)) return false;
        uint postMul = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref postMul, 32)) return false;
        return true;
    }
}
