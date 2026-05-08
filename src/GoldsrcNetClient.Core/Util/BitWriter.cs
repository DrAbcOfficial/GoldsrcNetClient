namespace GoldsrcNetClient.Core.Util;

/// <summary>
/// Static helper for writing bits into a byte buffer.
/// Used by the GoldSrc delta compression and message building subsystems.
/// </summary>
public static class BitWriter
{
    /// <summary>Writes bits from a source buffer at an arbitrary bit offset into a destination buffer.</summary>
    /// <param name="source">Source byte array.</param>
    /// <param name="sourceBitIndex">Bit position in the source to start reading from.</param>
    /// <param name="sourceBitCount">Number of bits to write.</param>
    /// <param name="destination">Destination byte array.</param>
    /// <param name="destBitIndex">Current write bit position. Advanced after writing.</param>
    /// <param name="destSize">Total destination size in bytes.</param>
    /// <returns>True on success; false if the destination cannot fit the bits.</returns>
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

    /// <summary>Writes the least-significant bits of an unsigned integer into the destination buffer.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="bitCount">Number of bits to write (1-32).</param>
    /// <param name="destination">Destination byte array.</param>
    /// <param name="destBitIndex">Current write bit position. Advanced after writing.</param>
    /// <param name="destSize">Total destination size in bytes.</param>
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

    /// <summary>Writes bits from a source byte buffer (starting at bit 0) into the destination.</summary>
    /// <param name="source">Source byte array.</param>
    /// <param name="bitCount">Number of bits to write.</param>
    /// <param name="destination">Destination byte array.</param>
    /// <param name="destBitIndex">Current write bit position. Advanced after writing.</param>
    /// <param name="destSize">Total destination size in bytes.</param>
    public static void WriteBits(byte[] source, int bitCount,
        byte[] destination, ref int destBitIndex, int destSize)
    {
        WriteBits(source, 0, bitCount, destination, ref destBitIndex, destSize);
    }
}
