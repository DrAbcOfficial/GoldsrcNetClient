namespace GoldsrcNetClient.Core.Util;

/// <summary>
/// Static helper for reading bits from a byte buffer.
/// Used by the GoldSrc delta compression and message parsing subsystems.
/// </summary>
public static class BitReader
{
    /// <summary>Reads bits from a source buffer into a destination buffer.</summary>
    /// <param name="source">Source byte array to read bits from.</param>
    /// <param name="sourceBitIndex">Current bit position in the source. Advanced after reading.</param>
    /// <param name="sourceSize">Total size of the source buffer in bytes.</param>
    /// <param name="destination">Destination byte array to write bits into.</param>
    /// <param name="destBitIndex">Current bit position in the destination. Advanced after writing.</param>
    /// <param name="bitCount">Number of bits to read.</param>
    /// <returns>True if enough bits were available; false on underflow.</returns>
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

    /// <summary>Reads the specified number of bits into an unsigned 32-bit integer.</summary>
    /// <param name="source">Source byte array.</param>
    /// <param name="sourceBitIndex">Current bit position. Advanced after reading.</param>
    /// <param name="sourceSize">Total source size in bytes.</param>
    /// <param name="destination">Output value.</param>
    /// <param name="bitCount">Number of bits to read (1-32).</param>
    /// <returns>True on success; false on underflow.</returns>
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

    /// <summary>Reads the specified number of bits into a signed 32-bit integer.</summary>
    /// <param name="source">Source byte array.</param>
    /// <param name="sourceBitIndex">Current bit position. Advanced after reading.</param>
    /// <param name="sourceSize">Total source size in bytes.</param>
    /// <param name="destination">Output value.</param>
    /// <param name="bitCount">Number of bits to read (1-32).</param>
    /// <returns>True on success; false on underflow.</returns>
    public static bool ReadBits(byte[] source, ref int sourceBitIndex, int sourceSize,
        ref int destination, int bitCount)
    {
        uint v = 0;
        if (!ReadBits(source, ref sourceBitIndex, sourceSize, ref v, bitCount))
            return false;
        destination = (int)v;
        return true;
    }

    /// <summary>Reads a null-terminated string from the bitstream.</summary>
    /// <param name="source">Source byte array.</param>
    /// <param name="sourceBitIndex">Current bit position. Advanced after reading.</param>
    /// <param name="sourceSize">Total source size in bytes.</param>
    /// <param name="result">Output byte array (trimmed to actual length).</param>
    /// <param name="maxSize">Maximum string length in bytes (default 64).</param>
    /// <returns>True on success; false on underflow.</returns>
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

    /// <summary>Reads a GoldSrc bit-coordinate (fixed-point position with sign).</summary>
    /// <param name="source">Source byte array.</param>
    /// <param name="sourceBitIndex">Current bit position. Advanced after reading.</param>
    /// <param name="sourceSize">Total source size in bytes.</param>
    /// <param name="f">Output floating-point value.</param>
    /// <returns>True on success; false on underflow.</returns>
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

    /// <summary>Reads a GoldSrc compressed angle from the bitstream.</summary>
    /// <param name="source">Source byte array.</param>
    /// <param name="sourceBitIndex">Current bit position. Advanced after reading.</param>
    /// <param name="sourceSize">Total source size in bytes.</param>
    /// <param name="angle">Output angle in degrees, wrapped to [-180, 180].</param>
    /// <param name="numBits">Number of bits used to encode the angle (e.g. 8, 16).</param>
    /// <returns>True on success; false on underflow.</returns>
    public static bool ReadBitAngle(byte[] source, ref int sourceBitIndex, int sourceSize,
        ref float angle, int numBits)
    {
        uint raw = 0;
        if (!ReadBits(source, ref sourceBitIndex, sourceSize, ref raw, numBits))
            return false;

        float shift = (float)(1 << numBits);
        angle = raw * (360.0f / shift);

        if (angle < -180.0f) angle += 360.0f;
        else if (angle > 180.0f) angle -= 360.0f;

        return true;
    }
}
