namespace GoldsrcNetClient.Core.Messages;

/// <summary>
/// Static helpers for reading primitive types and strings from
/// GoldSrc network message byte buffers.
/// </summary>
public static class MessageReader
{
    /// <summary>
    /// Reads a null-terminated byte string (raw bytes) from the buffer.
    /// Advances <paramref name="offset"/> past the null terminator.
    /// </summary>
    /// <returns>True if a null terminator was found; false if the buffer was exhausted.</returns>
    public static bool ReadString(ref byte[] data, ref int offset, int size, out byte[] str)
    {
        str = [];
        int start = offset;

        while (offset < size)
        {
            if (data[offset] == 0)
            {
                str = data[start..offset];
                offset++;
                return true;
            }
            offset++;
        }

        offset = size;
        return false;
    }

    /// <summary>
    /// Reads a null-terminated UTF-8 string from the buffer.
    /// Advances <paramref name="offset"/> past the null terminator.
    /// If no null terminator is found, consumes the rest of the buffer as the string.
    /// </summary>
    public static string ReadString(ref byte[] data, ref int offset, int size)
    {
        int start = offset;

        while (offset < size)
        {
            if (data[offset] == 0)
            {
                offset++;
                return System.Text.Encoding.UTF8.GetString(data, start, offset - start - 1);
            }
            offset++;
        }

        offset = size;
        return System.Text.Encoding.UTF8.GetString(data, start, size - start);
    }

    /// <summary>
    /// Copies a fixed number of bytes from the buffer into <paramref name="dest"/>.
    /// </summary>
    /// <returns>True if enough bytes were available; false otherwise.</returns>
    public static bool ReadBytes(ref byte[] data, ref int offset, int size, Span<byte> dest)
    {
        if (size - offset < dest.Length)
            return false;

        data.AsSpan(offset, dest.Length).CopyTo(dest);
        offset += dest.Length;
        return true;
    }

    /// <summary>Reads a little-endian 16-bit unsigned integer from the buffer. Returns 0 on overflow.</summary>
    public static ushort ReadUInt16(ref byte[] data, ref int offset, int size)
    {
        if (size - offset < 2) return 0;
        ushort v = BitConverter.ToUInt16(data, offset);
        offset += 2;
        return v;
    }

    /// <summary>Reads a little-endian 32-bit unsigned integer from the buffer. Returns 0 on overflow.</summary>
    public static uint ReadUInt32(ref byte[] data, ref int offset, int size)
    {
        if (size - offset < 4) return 0;
        uint v = BitConverter.ToUInt32(data, offset);
        offset += 4;
        return v;
    }
}
