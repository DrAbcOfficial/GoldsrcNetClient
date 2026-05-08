using System.Text;

namespace GoldsrcNetClient.Core.Messages;

/// <summary>
/// Reads primitive types and strings from a GoldSrc network message byte buffer.
/// Initialize with the raw packet data; the instance tracks the read offset internally.
/// </summary>
/// <remarks>
/// <para>Usage:
/// <code>
/// var reader = new MessageReader(data, size);
/// string msg = reader.ReadString();
/// reader.Offset += 2; // skip padding
/// ushort val = reader.ReadUInt16();
/// </code>
/// </para>
/// <para>The <see cref="Offset"/> property is publicly settable — advance it directly
/// when combining byte-level reads with struct overlays or external parsing logic.</para>
/// </remarks>
public class MessageReader
{
    private readonly byte[] _data;
    private readonly int _size;

    /// <summary>Creates a reader over the full length of <paramref name="data"/>.</summary>
    public MessageReader(byte[] data) : this(data, data.Length) { }

    /// <summary>Creates a reader over <paramref name="data"/> limited to <paramref name="size"/> bytes.</summary>
    public MessageReader(byte[] data, int size)
    {
        _data = data;
        _size = size;
    }

    /// <summary>Current read offset in bytes. Set this to skip or rewind.</summary>
    public int Offset { get; set; }

    /// <summary>Total usable bytes in the buffer.</summary>
    public int Size => _size;

    /// <summary>Bytes remaining between <see cref="Offset"/> and <see cref="Size"/>.</summary>
    public int Remaining => _size - Offset;

    /// <summary>The underlying byte array.</summary>
    public byte[] Data => _data;

    /// <summary>
    /// Reads a null-terminated byte string (raw bytes) from the buffer.
    /// Advances <see cref="Offset"/> past the null terminator.
    /// </summary>
    /// <returns>True if a null terminator was found; false if the buffer was exhausted.</returns>
    public bool ReadString(out byte[] str)
    {
        str = [];
        int start = Offset;

        while (Offset < _size)
        {
            if (_data[Offset] == 0)
            {
                str = _data[start..Offset];
                Offset++;
                return true;
            }
            Offset++;
        }

        Offset = _size;
        return false;
    }

    /// <summary>
    /// Reads a null-terminated UTF-8 string from the buffer.
    /// Advances <see cref="Offset"/> past the null terminator.
    /// If no null terminator is found, consumes the rest of the buffer as the string.
    /// </summary>
    public string ReadString()
    {
        int start = Offset;

        while (Offset < _size)
        {
            if (_data[Offset] == 0)
            {
                Offset++;
                return Encoding.UTF8.GetString(_data, start, Offset - start - 1);
            }
            Offset++;
        }

        Offset = _size;
        return Encoding.UTF8.GetString(_data, start, _size - start);
    }

    /// <summary>
    /// Copies a fixed number of bytes from the buffer into <paramref name="dest"/>.
    /// </summary>
    /// <returns>True if enough bytes were available; false otherwise.</returns>
    public bool ReadBytes(Span<byte> dest)
    {
        if (_size - Offset < dest.Length)
            return false;

        _data.AsSpan(Offset, dest.Length).CopyTo(dest);
        Offset += dest.Length;
        return true;
    }

    /// <summary>Reads a little-endian 16-bit unsigned integer. Returns 0 on overflow.</summary>
    public ushort ReadUInt16()
    {
        if (_size - Offset < 2) return 0;
        ushort v = BitConverter.ToUInt16(_data, Offset);
        Offset += 2;
        return v;
    }

    /// <summary>Reads a little-endian 32-bit unsigned integer. Returns 0 on overflow.</summary>
    public uint ReadUInt32()
    {
        if (_size - Offset < 4) return 0;
        uint v = BitConverter.ToUInt32(_data, Offset);
        Offset += 4;
        return v;
    }

    /// <summary>Reads a little-endian 64-bit unsigned integer. Returns 0 on overflow.</summary>
    public ulong ReadUInt64()
    {
        if (_size - Offset < 8) return 0;
        ulong v = BitConverter.ToUInt64(_data, Offset);
        Offset += 8;
        return v;
    }

    /// <summary>Reads a single unsigned byte. Returns 0 on overflow.</summary>
    public byte ReadByte()
    {
        if (_size - Offset < 1) return 0;
        return _data[Offset++];
    }

    /// <summary>Reads a signed 8-bit integer.</summary>
    /// <returns>True on success; false on overflow (value set to 0).</returns>
    public bool ReadSByte(out sbyte value)
    {
        if (_size - Offset < 1) { value = 0; return false; }
        value = (sbyte)_data[Offset++];
        return true;
    }

    /// <summary>Reads a little-endian signed 16-bit integer.</summary>
    /// <returns>True on success; false on overflow (value set to 0).</returns>
    public bool ReadInt16(out short value)
    {
        if (_size - Offset < 2) { value = 0; return false; }
        value = BitConverter.ToInt16(_data, Offset);
        Offset += 2;
        return true;
    }

    /// <summary>Reads a little-endian signed 32-bit integer.</summary>
    /// <returns>True on success; false on overflow (value set to 0).</returns>
    public bool ReadInt32(out int value)
    {
        if (_size - Offset < 4) { value = 0; return false; }
        value = BitConverter.ToInt32(_data, Offset);
        Offset += 4;
        return true;
    }

    /// <summary>Reads a little-endian signed 64-bit integer.</summary>
    /// <returns>True on success; false on overflow (value set to 0).</returns>
    public bool ReadInt64(out long value)
    {
        if (_size - Offset < 8) { value = 0; return false; }
        value = BitConverter.ToInt64(_data, Offset);
        Offset += 8;
        return true;
    }

    /// <summary>Reads a little-endian 32-bit IEEE 754 float.</summary>
    /// <returns>True on success; false on overflow (value set to 0).</returns>
    public bool ReadSingle(out float value)
    {
        if (_size - Offset < 4) { value = 0; return false; }
        value = BitConverter.ToSingle(_data, Offset);
        Offset += 4;
        return true;
    }

    /// <summary>Reads a little-endian 64-bit IEEE 754 double.</summary>
    /// <returns>True on success; false on overflow (value set to 0).</returns>
    public bool ReadDouble(out double value)
    {
        if (_size - Offset < 8) { value = 0; return false; }
        value = BitConverter.ToDouble(_data, Offset);
        Offset += 8;
        return true;
    }

    /// <summary>
    /// Reads a null-terminated or newline-terminated string from the buffer.
    /// Advances <see cref="Offset"/> past the terminator.
    /// If no terminator is found, consumes the rest of the buffer as the string.
    /// </summary>
    public string ReadStringLine()
    {
        int start = Offset;

        while (Offset < _size)
        {
            byte b = _data[Offset];
            if (b == 0 || b == (byte)'\n')
            {
                Offset++;
                return Encoding.UTF8.GetString(_data, start, Offset - start - 1);
            }
            Offset++;
        }

        Offset = _size;
        return Encoding.UTF8.GetString(_data, start, _size - start);
    }
}
