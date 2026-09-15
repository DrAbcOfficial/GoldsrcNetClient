using System.Buffers.Binary;
using System.Text;

namespace GoldsrcNetClient.Core.Io;

/// <summary>
/// Growable byte/bit writer for outgoing messages — the counterpart of
/// <see cref="BufferReader"/>. One absolute bit position drives both byte-level
/// writes (fast path when aligned) and bit-field writes, so a payload can mix
/// both encodings, exactly like the engine's <c>Sizebuf</c>.
/// </summary>
/// <remarks>
/// <code>
/// var writer = new BufferWriter();
/// writer.WriteUInt8((byte)ClientCommandType.StringCmd);
/// writer.WriteString("say hello");
/// byte[] payload = writer.ToArray();
/// </code>
/// </remarks>
public sealed class BufferWriter
{
    private byte[] _buf = new byte[64];
    private int _bitPos;

    /// <summary>Committed length in whole bytes (rounded up past a partial byte).</summary>
    public int Length => (_bitPos + 7) >> 3;

    /// <summary>Absolute bit position of the cursor.</summary>
    public int BitPosition => _bitPos;

    /// <summary>Current byte position (truncates a partial bit position downward).</summary>
    public int BytePosition => _bitPos >> 3;

    // --- Byte-level writes ---

    /// <summary>Writes one byte at any bit alignment.</summary>
    public void WriteUInt8(byte value)
    {
        if ((_bitPos & 7) == 0)
        {
            EnsureCapacity(1);
            _buf[_bitPos >> 3] = value;
            _bitPos += 8;
        }
        else
        {
            WriteBits(value, 8);
        }
    }

    /// <summary>Writes a little-endian 16-bit unsigned integer.</summary>
    public void WriteUInt16(ushort value)
    {
        if ((_bitPos & 7) == 0)
        {
            EnsureCapacity(2);
            BinaryPrimitives.WriteUInt16LittleEndian(_buf.AsSpan(_bitPos >> 3), value);
            _bitPos += 16;
        }
        else
        {
            WriteBits(value, 16);
        }
    }

    /// <summary>Writes a little-endian 32-bit unsigned integer.</summary>
    public void WriteUInt32(uint value)
    {
        if ((_bitPos & 7) == 0)
        {
            EnsureCapacity(4);
            BinaryPrimitives.WriteUInt32LittleEndian(_buf.AsSpan(_bitPos >> 3), value);
            _bitPos += 32;
        }
        else
        {
            WriteBits(value, 32);
        }
    }

    /// <summary>Writes a little-endian 32-bit IEEE 754 float.</summary>
    public void WriteSingle(float value) => WriteUInt32((uint)BitConverter.SingleToInt32Bits(value));

    /// <summary>Appends raw bytes at any bit alignment.</summary>
    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        if ((_bitPos & 7) == 0)
        {
            EnsureCapacity(data.Length);
            data.CopyTo(_buf.AsSpan(_bitPos >> 3));
            _bitPos += data.Length << 3;
        }
        else
        {
            foreach (byte b in data)
                WriteBits(b, 8);
        }
    }

    /// <summary>Writes a null-terminated UTF-8 string.</summary>
    public void WriteString(string value)
    {
        WriteBytes(Encoding.UTF8.GetBytes(value));
        WriteUInt8(0);
    }

    // --- Bit fields ---

    /// <summary>
    /// Writes the low <paramref name="count"/> bits (1–32) of
    /// <paramref name="value"/>, least-significant bit first. The buffer grows
    /// as needed, so writes can never overflow.
    /// </summary>
    public void WriteBits(uint value, int count)
    {
        if (count is < 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(count));

        int requiredBytes = (_bitPos + count + 7) >> 3;
        if (requiredBytes > _buf.Length)
            Array.Resize(ref _buf, Math.Max(requiredBytes, _buf.Length * 2));
        int filled = 0;
        while (filled < count)
        {
            int bit = _bitPos & 7;
            int put = Math.Min(8 - bit, count - filled);
            uint putMask = ((1u << put) - 1u) << bit;
            int idx = _bitPos >> 3;
            _buf[idx] = (byte)(((uint)_buf[idx] & ~putMask) | (((value >> filled) << bit) & putMask));
            filled += put;
            _bitPos += put;
        }
    }

    /// <summary>Advances the cursor to the next whole-byte boundary (rounds up).</summary>
    public void Align() => _bitPos = (_bitPos + 7) & ~7;

    /// <summary>Copies the committed bytes into a new array.</summary>
    public byte[] ToArray() => _buf.AsSpan(0, Length).ToArray();

    /// <summary>The committed bytes as a span (only valid while byte-aligned).</summary>
    public ReadOnlySpan<byte> WrittenSpan => _buf.AsSpan(0, Length);

    private void EnsureCapacity(int byteCount)
    {
        int required = (_bitPos >> 3) + byteCount;
        if (required > _buf.Length)
            Array.Resize(ref _buf, Math.Max(required, _buf.Length * 2));
    }
}
