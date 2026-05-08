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

    /// <summary>Writes a GoldSrc compressed angle (in degrees) into the bitstream.</summary>
    /// <param name="angle">Angle in degrees. Clamped to [0, 360) before encoding.</param>
    /// <param name="numBits">Number of bits to use (e.g. 8, 16).</param>
    /// <param name="destination">Destination byte array.</param>
    /// <param name="destBitIndex">Current write bit position. Advanced after writing.</param>
    /// <param name="destSize">Total destination size in bytes.</param>
    public static void WriteBitAngle(float angle, int numBits,
        byte[] destination, ref int destBitIndex, int destSize)
    {
        uint shift = (uint)(1 << numBits);
        uint mask = shift - 1;

        angle = angle % 360.0f;
        if (angle < 0) angle += 360.0f;

        int d = (int)(angle * shift / 360.0f);
        d &= (int)mask;

        WriteBits((uint)d, numBits, destination, ref destBitIndex, destSize);
    }

    /// <summary>Writes a GoldSrc bit-coordinate (fixed-point position with sign) into the bitstream.</summary>
    /// <param name="value">Coordinate value.</param>
    /// <param name="destination">Destination byte array.</param>
    /// <param name="destBitIndex">Current write bit position. Advanced after writing.</param>
    /// <param name="destSize">Total destination size in bytes.</param>
    public static void WriteBitCoord(float value,
        byte[] destination, ref int destBitIndex, int destSize)
    {
        if (value == 0.0f)
        {
            WriteBits(0u, 1, destination, ref destBitIndex, destSize);
            WriteBits(0u, 1, destination, ref destBitIndex, destSize);
            return;
        }

        WriteBits(1u, 1, destination, ref destBitIndex, destSize);
        WriteBits(1u, 1, destination, ref destBitIndex, destSize);

        uint signbit = value < 0 ? 1u : 0u;
        if (signbit != 0) value = -value;
        WriteBits(signbit, 1, destination, ref destBitIndex, destSize);

        int intval = (int)value;
        float fractval = value - intval;
        int fractbits = (int)(fractval * 8.0f + 0.5f);

        WriteBits((uint)intval, 12, destination, ref destBitIndex, destSize);
        WriteBits((uint)fractbits, 3, destination, ref destBitIndex, destSize);
    }
}
