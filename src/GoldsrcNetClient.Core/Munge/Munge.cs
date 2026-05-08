using System.Buffers.Binary;

namespace GoldsrcNetClient.Core.Munge;

/// <summary>
/// XOR-based packet encryption/decryption used by the GoldSrc engine.
/// Munge and UnMunge are inverse operations that use different lookup tables
/// depending on the context:
/// <list type="bullet">
///   <item><b>Munge1</b> / <b>UnMunge1</b> — connectionless packet encryption.</item>
///   <item><b>Munge2</b> / <b>UnMunge2</b> — connected packet encryption.</item>
///   <item><b>Munge3</b> / <b>UnMunge3</b> — worldmap CRC encryption.</item>
/// </list>
/// All tables are derived from the original GoldSrc engine source.
/// </summary>
public static class MungeEngine
{
    private static readonly byte[] _table1 = [0x7A, 0x64, 0x05, 0xF1, 0x1B, 0x9B, 0xA0, 0xB5, 0xCA, 0xED, 0x61, 0x0D, 0x4A, 0xDF, 0x8E, 0xC7];
    private static readonly byte[] _table2 = [0x05, 0x61, 0x7A, 0xED, 0x1B, 0xCA, 0x0D, 0x9B, 0x4A, 0xF1, 0x64, 0xC7, 0xB5, 0x8E, 0xDF, 0xA0];
    private static readonly byte[] _table3 = [0x20, 0x07, 0x13, 0x61, 0x03, 0x45, 0x17, 0x72, 0x0A, 0x2D, 0x48, 0x0C, 0x4A, 0x12, 0xA9, 0xB5];

    private static void Munge(byte[] data, int len, int seq, byte[] table)
    {
        int mungelen = len & ~3;
        mungelen /= 4;
        Span<byte> sp = data.AsSpan(0, len);
        Span<byte> p = stackalloc byte[4];

        for (int i = 0; i < mungelen; i++)
        {
            int offset = i * 4;
            int c = BinaryPrimitives.ReadInt32LittleEndian(sp[offset..]);
            c ^= ~seq;
            c = BinaryPrimitives.ReverseEndianness(c);

            BinaryPrimitives.WriteInt32LittleEndian(p, c);

            for (int j = 0; j < 4; j++)
                p[j] ^= (byte)(0xA5 | (j << j) | j | table[(i + j) & 0x0F]);

            c = BinaryPrimitives.ReadInt32LittleEndian(p);
            c ^= seq;
            BinaryPrimitives.WriteInt32LittleEndian(sp[offset..], c);
        }
    }

    private static void UnMunge(byte[] data, int len, int seq, byte[] table)
    {
        int mungelen = len & ~3;
        mungelen /= 4;
        Span<byte> sp = data.AsSpan(0, len);
        Span<byte> p = stackalloc byte[4];

        for (int i = 0; i < mungelen; i++)
        {
            int offset = i * 4;
            int c = BinaryPrimitives.ReadInt32LittleEndian(sp[offset..]);
            c ^= seq;

            BinaryPrimitives.WriteInt32LittleEndian(p, c);

            for (int j = 0; j < 4; j++)
                p[j] ^= (byte)(0xA5 | (j << j) | j | table[(i + j) & 0x0F]);

            c = BinaryPrimitives.ReadInt32LittleEndian(p);
            c = BinaryPrimitives.ReverseEndianness(c);
            c ^= ~seq;
            BinaryPrimitives.WriteInt32LittleEndian(sp[offset..], c);
        }
    }

    /// <summary>Encrypts data using Munge table 1 (connectionless packets).</summary>
    /// <param name="data">The byte array to munge in-place.</param>
    /// <param name="len">Number of bytes to process (rounded down to nearest 4).</param>
    /// <param name="seq">Sequence number used as the XOR key component.</param>
    public static void Munge1(byte[] data, int len, int seq) => Munge(data, len, seq, _table1);

    /// <summary>Decrypts data using UnMunge table 1 (connectionless packets).</summary>
    /// <param name="data">The byte array to unmunge in-place.</param>
    /// <param name="len">Number of bytes to process (rounded down to nearest 4).</param>
    /// <param name="seq">Sequence number used as the XOR key component.</param>
    public static void UnMunge1(byte[] data, int len, int seq) => UnMunge(data, len, seq, _table1);

    /// <summary>Encrypts data using Munge table 2 (connected packets).</summary>
    /// <param name="data">The byte array to munge in-place.</param>
    /// <param name="len">Number of bytes to process (rounded down to nearest 4).</param>
    /// <param name="seq">Sequence number used as the XOR key component.</param>
    public static void Munge2(byte[] data, int len, int seq) => Munge(data, len, seq, _table2);

    /// <summary>Decrypts data using UnMunge table 2 (connected packets). Inverts <see cref="Munge2"/>.</summary>
    /// <param name="data">The byte array to unmunge in-place.</param>
    /// <param name="len">Number of bytes to process (rounded down to nearest 4).</param>
    /// <param name="seq">Sequence number used as the XOR key component.</param>
    public static void UnMunge2(byte[] data, int len, int seq) => UnMunge(data, len, seq, _table2);

    /// <summary>Encrypts data using Munge table 3 (worldmap CRC).</summary>
    /// <param name="data">The byte array to munge in-place.</param>
    /// <param name="len">Number of bytes to process (rounded down to nearest 4).</param>
    /// <param name="seq">Sequence number used as the XOR key component.</param>
    public static void Munge3(byte[] data, int len, int seq) => Munge(data, len, seq, _table3);

    /// <summary>Decrypts data using UnMunge table 3 (worldmap CRC). Inverts <see cref="Munge3"/>.</summary>
    /// <param name="data">The byte array to unmunge in-place.</param>
    /// <param name="len">Number of bytes to process (rounded down to nearest 4).</param>
    /// <param name="seq">Sequence number used as the XOR key component.</param>
    public static void UnMunge3(byte[] data, int len, int seq) => UnMunge(data, len, seq, _table3);
}
