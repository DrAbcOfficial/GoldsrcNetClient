namespace GoldsrcNetClient.Core.Util;

public static class BitWriter
{
    public static bool WriteBits(byte[] source, int sourceBitIndex, int sourceBitCount,
        byte[] destination, ref int destBitIndex, int destSize)
    {
        if (sourceBitCount > destSize * 8 - destBitIndex)
            return false;

        int srcIndex = sourceBitIndex;

        while (sourceBitCount > 0)
        {
            int m = srcIndex % 8;
            int l = 8 - m;
            if (l > sourceBitCount) l = sourceBitCount;
            sourceBitCount -= l;

            byte n = source[srcIndex / 8];
            srcIndex += l;

            n = (byte)(n >> m);
            n &= (byte)((1 << l) - 1);

            while (l > 0)
            {
                int dm = destBitIndex % 8;
                int dl = 8 - dm;
                if (dl > l) dl = l;

                if (dm == 0)
                    destination[destBitIndex / 8] = 0;

                destination[destBitIndex / 8] |= (byte)(n << dm);

                l -= dl;
                n = (byte)(n >> dl);
                destBitIndex += dl;
            }
        }

        return true;
    }

    public static void WriteBits(uint value, int bitCount,
        byte[] destination, ref int destBitIndex, int destSize)
    {
        int srcIndex = 0;

        while (bitCount > 0)
        {
            int m = srcIndex % 8;
            int l = 8 - m;
            if (l > bitCount) l = bitCount;
            bitCount -= l;

            uint n = (value >> srcIndex);
            srcIndex += l;

            n &= (uint)((1 << l) - 1);

            while (l > 0)
            {
                int dm = destBitIndex % 8;
                int dl = 8 - dm;
                if (dl > l) dl = l;

                if (dm == 0)
                    destination[destBitIndex / 8] = 0;

                destination[destBitIndex / 8] |= (byte)(n << dm);

                l -= dl;
                n >>= dl;
                destBitIndex += dl;
            }
        }
    }

    public static void WriteBits(byte[] source, int bitCount,
        byte[] destination, ref int destBitIndex, int destSize)
    {
        WriteBits(source, 0, bitCount, destination, ref destBitIndex, destSize);
    }
}
