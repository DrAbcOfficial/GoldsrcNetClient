using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace GoldsrcNetClient.Core.Io;

/// <summary>
/// Unified byte/bit reader over a read-only buffer, mirroring the engine's
/// <c>bf_read</c>: a single absolute bit position drives both byte-level reads
/// (fast path when byte-aligned) and bit-field reads, so handlers can mix both
/// without manual bit-to-byte bridging.
/// </summary>
/// <remarks>
/// <para>
/// All reads throw <see cref="EndOfBufferException"/> when they would cross the
/// end of the buffer. Malformed-input handling therefore lives in one place:
/// the caller of the parsing layer catches the exception and discards the rest
/// of the packet, which is what the engine does on a malformed stream.
/// </para>
/// <para>
/// Strings are decoded as UTF-8 (the project-wide wire encoding). A missing
/// null terminator consumes the rest of the buffer as the string, matching the
/// engine's lenient behaviour.
/// </para>
/// <code>
/// var reader = new BufferReader(data);
/// byte type = reader.ReadUInt8();
/// string text = reader.ReadString();
/// uint flags = reader.ReadBits(3);      // bit position advances by 3
/// byte next = reader.ReadUInt8();       // works at any alignment
/// </code>
/// </remarks>
public ref struct BufferReader(ReadOnlySpan<byte> buffer)
{
    private readonly ReadOnlySpan<byte> _buffer = buffer;
    private int _bitPos;

    /// <summary>Total buffer size in bytes.</summary>
    public readonly int Length => _buffer.Length;

    /// <summary>Absolute bit position of the cursor. Bits, not bytes.</summary>
    public int BitPosition => _bitPos;

    /// <summary>Remaining bits between the cursor and the end of the buffer.</summary>
    public readonly int RemainingBits => _buffer.Length * 8 - _bitPos;

    /// <summary>
    /// Current byte position (truncates a partial bit position downward).
    /// Setting it realigns the cursor to a whole byte.
    /// </summary>
    public int BytePosition
    {
        readonly get => _bitPos >> 3;
        set => _bitPos = value << 3;
    }

    /// <summary>Whole bytes left after the cursor (rounded down).</summary>
    public readonly int Remaining => _buffer.Length - BytePosition;

    /// <summary>The unread portion of the buffer as a span (only valid while byte-aligned).</summary>
    public readonly ReadOnlySpan<byte> RemainingSpan => _buffer[BytePosition..];

    /// <summary>The full underlying buffer.</summary>
    public readonly ReadOnlySpan<byte> Buffer => _buffer;

    /// <summary>Advances the cursor by whole bytes.</summary>
    public void Skip(int byteCount) => _bitPos += byteCount << 3;

    /// <summary>Advances the cursor to the next whole-byte boundary (rounds up).</summary>
    public void Align() => _bitPos = (_bitPos + 7) & ~7;

    /// <summary>
    /// Returns a bounded reader over the next <paramref name="byteCount"/> bytes and
    /// advances this cursor past them. A mis-parsing handler on the slice can never
    /// desynchronise the surrounding stream.
    /// </summary>
    public BufferReader Slice(int byteCount)
    {
        if (BytePosition + byteCount > _buffer.Length)
            throw new EndOfBufferException();
        var slice = new BufferReader(_buffer[BytePosition..(BytePosition + byteCount)]);
        _bitPos += byteCount << 3;
        return slice;
    }

    // --- Byte-aligned primitives (bit path when unaligned) ---

    /// <summary>Reads one byte. Works at any bit alignment.</summary>
    public byte ReadUInt8() => (_bitPos & 7) == 0 ? ReadAlignedByte() : (byte)ReadBits(8);

    /// <summary>Reads one signed byte. Works at any bit alignment.</summary>
    public sbyte ReadInt8() => (sbyte)ReadUInt8();

    /// <summary>Reads a little-endian 16-bit unsigned integer.</summary>
    public ushort ReadUInt16()
    {
        if ((_bitPos & 7) == 0 && Remaining >= 2)
        {
            int start = BytePosition;
            _bitPos += 16;
            return BinaryPrimitives.ReadUInt16LittleEndian(_buffer[start..]);
        }
        return (ushort)ReadBits(16);
    }

    /// <summary>Reads a little-endian 16-bit signed integer.</summary>
    public short ReadInt16() => (short)ReadUInt16();

    /// <summary>Reads a little-endian 32-bit unsigned integer.</summary>
    public uint ReadUInt32()
    {
        if ((_bitPos & 7) == 0 && Remaining >= 4)
        {
            int start = BytePosition;
            _bitPos += 32;
            return BinaryPrimitives.ReadUInt32LittleEndian(_buffer[start..]);
        }
        return ReadBits(32);
    }

    /// <summary>Reads a little-endian 32-bit signed integer.</summary>
    public int ReadInt32() => (int)ReadUInt32();

    /// <summary>Reads a little-endian 32-bit IEEE 754 float.</summary>
    public float ReadSingle() => BitConverter.Int32BitsToSingle((int)ReadUInt32());

    /// <summary>
    /// Overlays a blittable <c>LayoutKind.Sequential, Pack=1</c> struct on the bytes
    /// at the cursor — the same view the engine takes of signon blocks. Requires
    /// byte alignment.
    /// </summary>
    public T ReadStruct<T>() where T : unmanaged
    {
        if ((_bitPos & 7) != 0)
            throw new InvalidOperationException("Struct overlay requires byte alignment.");
        int size;
        unsafe { size = sizeof(T); }
        if (Remaining < size)
            throw new EndOfBufferException();
        int start = BytePosition;
        _bitPos += size << 3;
        return MemoryMarshal.Read<T>(_buffer[start..]);
    }

    /// <summary>Copies the next <paramref name="byteCount"/> bytes into a new array.</summary>
    public byte[] ReadBytes(int byteCount)
    {
        if (BytePosition + byteCount > _buffer.Length)
            throw new EndOfBufferException();
        var bytes = _buffer[BytePosition..(BytePosition + byteCount)].ToArray();
        _bitPos += byteCount << 3;
        return bytes;
    }

    // --- Strings ---

    /// <summary>
    /// Reads a null-terminated UTF-8 string, advancing past the terminator.
    /// With no terminator, consumes the rest of the buffer as the string.
    /// </summary>
    public string ReadString() => ReadDelimited(b => b == 0);

    /// <summary>
    /// Reads a null- or newline-terminated UTF-8 string, advancing past the
    /// terminator. With no terminator, consumes the rest of the buffer.
    /// </summary>
    public string ReadStringLine() => ReadDelimited(b => b == 0 || b == (byte)'\n');

    /// <summary>
    /// Reads a null-terminated raw byte string without decoding. Throws when the
    /// terminator is missing — for protocols (A2S) where an unterminated string
    /// means a malformed reply, not a lenient tail read. Requires byte alignment.
    /// </summary>
    public byte[] ReadStringBytes()
    {
        if ((_bitPos & 7) != 0)
            throw new InvalidOperationException("ReadStringBytes requires byte alignment.");
        int start = BytePosition;
        int relative = _buffer[start..].IndexOf((byte)0);
        if (relative < 0)
            throw new EndOfBufferException("String is not null-terminated.");
        _bitPos = (start + relative + 1) << 3;
        return _buffer[start..(start + relative)].ToArray();
    }

    private string ReadDelimited(Func<byte, bool> isTerminator)
    {
        if ((_bitPos & 7) == 0)
        {
            int start = BytePosition;
            for (int i = start; i < _buffer.Length; i++)
            {
                if (isTerminator(_buffer[i]))
                {
                    _bitPos = (i + 1) << 3;
                    return Encoding.UTF8.GetString(_buffer[start..i]);
                }
            }
            _bitPos = _buffer.Length << 3;
            return Encoding.UTF8.GetString(_buffer[start..]);
        }

        // Unaligned: fall back to 8-bit reads (delta/tempentity paths).
        Span<byte> tmp = stackalloc byte[256];
        int n = 0;
        byte[]? overflow = null;
        while (true)
        {
            if (RemainingBits < 8)
            {
                _bitPos = _buffer.Length << 3;
                return Encoding.UTF8.GetString(overflow is null ? tmp[..n] : overflow.AsSpan(..n));
            }
            byte b = (byte)ReadBits(8);
            if (isTerminator(b))
                return Encoding.UTF8.GetString(overflow is null ? tmp[..n] : overflow.AsSpan(..n));
            if (n == tmp.Length && overflow is null)
            {
                overflow = new byte[tmp.Length * 4];
                tmp.CopyTo(overflow);
            }
            else if (overflow is not null && n == overflow.Length)
                Array.Resize(ref overflow, overflow.Length * 2);
            if (overflow is null)
                tmp[n] = b;
            else
                overflow[n] = b;
            n++;
        }
    }

    // --- Bit fields ---

    /// <summary>
    /// Reads <paramref name="count"/> bits (1–32) into the low bits of a
    /// <see cref="uint"/>, first wire bit first. Throws on underflow.
    /// </summary>
    public uint ReadBits(int count)
    {
        if (count is < 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (RemainingBits < count)
            throw new EndOfBufferException();

        uint result = 0;
        int filled = 0;
        while (filled < count)
        {
            int bit = _bitPos & 7;
            int take = Math.Min(8 - bit, count - filled);
            uint chunk = (uint)(_buffer[_bitPos >> 3] >> bit) & ((1u << take) - 1u);
            result |= chunk << filled;
            filled += take;
            _bitPos += take;
        }
        return result;
    }

    /// <summary>
    /// Reads a GoldSrc bit-coordinate: optional sign, 12-bit integer and 3-bit
    /// fraction, each present only when the preceding flag bit is set.
    /// </summary>
    public float ReadBitCoord()
    {
        uint intval = ReadBits(1);
        uint fractval = ReadBits(1);
        if (intval == 0 && fractval == 0)
            return 0f;

        uint signbit = ReadBits(1);
        if (intval != 0)
            intval = ReadBits(12);
        if (fractval != 0)
            fractval = ReadBits(3);

        float value = fractval / 8.0f + intval;
        return signbit != 0 ? -value : value;
    }

    /// <summary>
    /// Reads a Sven Co-op wide coordinate: a raw 32-bit little-endian 16.16
    /// fixed-point value (integer range ±32768, versus Half-Life's 18-bit
    /// bit-coordinate with a ±4096 integer range).
    /// </summary>
    public float ReadBitCoordWide() => (int)ReadBits(32) / 65536.0f;

    /// <summary>
    /// Reads the byte-aligned 16-bit fixed-point coordinate used inside user
    /// messages (signed short / 8): a distinct encoding from the bit-packed
    /// <see cref="ReadBitCoord"/> used by engine messages.
    /// </summary>
    public float ReadCoord16() => ReadInt16() / 8f;

    /// <summary>
    /// Reads a GoldSrc compressed angle encoded in <paramref name="numBits"/> bits,
    /// mapped to [0, 360) degrees.
    /// </summary>
    public float ReadBitAngle(int numBits) => ReadBits(numBits) * (360.0f / (1 << numBits));

    /// <summary>
    /// Reads a bit-granular null-terminated byte string (at most
    /// <paramref name="maxBytes"/> bytes including the terminator), trimmed to its
    /// actual length.
    /// </summary>
    public byte[] ReadBitString(int maxBytes = 64)
    {
        byte[] result = new byte[maxBytes];
        int n = 0;
        while (n < maxBytes)
        {
            byte b = (byte)ReadBits(8);
            if (b == 0)
                break;
            result[n++] = b;
        }
        return n == maxBytes ? result : result[..n];
    }

    // --- Aligned fast paths ---

    private byte ReadAlignedByte()
    {
        int idx = _bitPos >> 3;
        if (idx >= _buffer.Length)
            throw new EndOfBufferException();
        _bitPos += 8;
        return _buffer[idx];
    }
}
