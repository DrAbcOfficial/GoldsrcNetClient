namespace GoldsrcNetClient.Core.Messages;

public static class MessageReader
{
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

    public static bool ReadBytes(ref byte[] data, ref int offset, int size, Span<byte> dest)
    {
        if (size - offset < dest.Length)
            return false;

        data.AsSpan(offset, dest.Length).CopyTo(dest);
        offset += dest.Length;
        return true;
    }

    public static ushort ReadUInt16(ref byte[] data, ref int offset, int size)
    {
        if (size - offset < 2) return 0;
        ushort v = BitConverter.ToUInt16(data, offset);
        offset += 2;
        return v;
    }

    public static uint ReadUInt32(ref byte[] data, ref int offset, int size)
    {
        if (size - offset < 4) return 0;
        uint v = BitConverter.ToUInt32(data, offset);
        offset += 4;
        return v;
    }
}
