namespace GoldsrcNetClient.Core.Util;

public static class BitReader
{
    public static bool ReadBits(byte[] source, ref int sourceBitIndex, int sourceSize,
        byte[] destination, ref int destBitIndex, int bitCount)
    {
        if (sourceSize * 8 - sourceBitIndex < bitCount)
            return false;

        while (bitCount > 0)
        {
            int m = sourceBitIndex % 8;
            int l = 8 - m;
            if (l > bitCount) l = bitCount;
            bitCount -= l;

            byte n = source[sourceBitIndex / 8];
            sourceBitIndex += l;

            n = (byte)(n >> m);
            n &= (byte)((1 << l) - 1);

            while (l > 0)
            {
                int dm = destBitIndex % 8;
                int dl = 8 - dm;
                if (dl > l) dl = l;

                destination[destBitIndex / 8] |= (byte)(n << dm);

                l -= dl;
                n = (byte)(n >> dl);
                destBitIndex += dl;
            }
        }

        return true;
    }

    public static bool ReadBits(byte[] source, ref int sourceBitIndex, int sourceSize,
        ref uint destination, int bitCount)
    {
        if (sourceSize * 8 - sourceBitIndex < bitCount)
            return false;

        destination = 0;
        int destIndex = 0;

        while (bitCount > 0)
        {
            int m = sourceBitIndex % 8;
            int l = 8 - m;
            if (l > bitCount) l = bitCount;
            bitCount -= l;

            byte n = source[sourceBitIndex / 8];
            sourceBitIndex += l;

            n = (byte)(n >> m);
            n &= (byte)((1 << l) - 1);

            destination |= (uint)(n << destIndex);
            destIndex += l;
        }

        return true;
    }

    public static bool ReadBits(byte[] source, ref int sourceBitIndex, int sourceSize,
        ref int destination, int bitCount)
    {
        uint v = 0;
        if (!ReadBits(source, ref sourceBitIndex, sourceSize, ref v, bitCount))
            return false;
        destination = (int)v;
        return true;
    }

    public static bool ReadBitString(byte[] source, ref int sourceBitIndex, int sourceSize,
        out byte[] result, int maxSize = 64)
    {
        result = new byte[maxSize];
        int byteIndex = 0;

        while (byteIndex < maxSize)
        {
            result[byteIndex] = 0;
            uint b = 0;
            if (!ReadBits(source, ref sourceBitIndex, sourceSize, ref b, 8))
                return false;

            result[byteIndex] = (byte)b;
            if (result[byteIndex] == 0)
                break;

            byteIndex++;
        }

        Array.Resize(ref result, byteIndex);
        return true;
    }

    public static bool ReadBitCoord(byte[] source, ref int sourceBitIndex, int sourceSize,
        ref float f)
    {
        uint intval = 0;
        if (!ReadBits(source, ref sourceBitIndex, sourceSize, ref intval, 1))
            return false;
        uint fractval = 0;
        if (!ReadBits(source, ref sourceBitIndex, sourceSize, ref fractval, 1))
            return false;

        if (intval != 0 || fractval != 0)
        {
            uint signbit = 0;
            if (!ReadBits(source, ref sourceBitIndex, sourceSize, ref signbit, 1))
                return false;

            if (intval != 0)
            {
                intval = 0;
                if (!ReadBits(source, ref sourceBitIndex, sourceSize, ref intval, 12))
                    return false;
            }

            if (fractval != 0)
            {
                fractval = 0;
                if (!ReadBits(source, ref sourceBitIndex, sourceSize, ref fractval, 3))
                    return false;
            }

            f = (float)(fractval / 8.0 + intval);
            if (signbit != 0) f = -f;
        }
        else
        {
            f = 0;
        }

        return true;
    }
}
