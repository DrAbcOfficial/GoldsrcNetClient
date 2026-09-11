using GoldsrcNetClient.Core.Protocol;
using System.Text;

namespace GoldsrcNetClient.Core.Messages;

/// <summary>
/// Static helpers for building GoldSrc network message payloads
/// (client command bytes, primitive types, null-terminated strings).
/// </summary>
public static class MessageWriter
{
    /// <summary>
    /// Appends a string command (command byte + null-terminated UTF-8 string) to the output list.
    /// </summary>
    /// <param name="output">The target byte list to append to.</param>
    /// <param name="cmd">The client command type byte.</param>
    /// <param name="str">The command string payload.</param>
    public static void WriteStringCmd(List<byte> output, ClientCommandType cmd, string str)
    {
        output.Add((byte)cmd);
        output.AddRange(Encoding.UTF8.GetBytes(str));
        output.Add(0);
    }

    /// <summary>Appends an unsigned byte to the output list.</summary>
    public static void WriteByte(List<byte> output, byte value) => output.Add(value);

    /// <summary>Appends a signed byte to the output list.</summary>
    public static void WriteSByte(List<byte> output, sbyte value) => output.Add((byte)value);

    /// <summary>Appends a little-endian signed 16-bit integer to the output list.</summary>
    public static void WriteInt16(List<byte> output, short value) => output.AddRange(BitConverter.GetBytes(value));

    /// <summary>Appends a little-endian unsigned 16-bit integer to the output list.</summary>
    public static void WriteUInt16(List<byte> output, ushort value) => output.AddRange(BitConverter.GetBytes(value));

    /// <summary>Appends a little-endian signed 32-bit integer to the output list.</summary>
    public static void WriteInt32(List<byte> output, int value) => output.AddRange(BitConverter.GetBytes(value));

    /// <summary>Appends a little-endian unsigned 32-bit integer to the output list.</summary>
    public static void WriteUInt32(List<byte> output, uint value) => output.AddRange(BitConverter.GetBytes(value));

    /// <summary>Appends a little-endian signed 64-bit integer to the output list.</summary>
    public static void WriteInt64(List<byte> output, long value) => output.AddRange(BitConverter.GetBytes(value));

    /// <summary>Appends a little-endian unsigned 64-bit integer to the output list.</summary>
    public static void WriteUInt64(List<byte> output, ulong value) => output.AddRange(BitConverter.GetBytes(value));

    /// <summary>Appends a little-endian 32-bit IEEE 754 float to the output list.</summary>
    public static void WriteSingle(List<byte> output, float value) => output.AddRange(BitConverter.GetBytes(value));

    /// <summary>Appends a little-endian 64-bit IEEE 754 double to the output list.</summary>
    public static void WriteDouble(List<byte> output, double value) => output.AddRange(BitConverter.GetBytes(value));

    /// <summary>Appends a null-terminated UTF-8 string to the output list.</summary>
    public static void WriteString(List<byte> output, string value)
    {
        output.AddRange(Encoding.UTF8.GetBytes(value));
        output.Add(0);
    }

    /// <summary>Appends raw bytes to the output list.</summary>
    public static void WriteBytes(List<byte> output, byte[] value) => output.AddRange(value);

    /// <summary>Appends a span of raw bytes to the output list.</summary>
    public static void WriteBytes(List<byte> output, ReadOnlySpan<byte> value) => output.AddRange(value);
}
