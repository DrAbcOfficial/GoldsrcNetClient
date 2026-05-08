using System.Text;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Core.Messages;

/// <summary>
/// Static helpers for building GoldSrc network message packets
/// (connected command packets, acknowledgement packets, string commands).
/// </summary>
public static class MessageWriter
{
    /// <summary>
    /// Builds a connected command packet with a custom command byte and payload.
    /// </summary>
    /// <param name="srcSequence">Local source sequence number (will be OR'd with <see cref="MessageConstants.SequenceModeCommand"/>).</param>
    /// <param name="dstSequence">Remote destination sequence number.</param>
    /// <param name="command">Single-byte command identifier.</param>
    /// <param name="data">Command payload bytes.</param>
    public static byte[] BuildCommandPacket(uint srcSequence, uint dstSequence, byte command, byte[] data)
    {
        uint mode = MessageConstants.SequenceModeCommand;
        byte[] packet = new byte[MessageConstants.ConnectedHeadSize + data.Length];

        BitConverter.GetBytes(srcSequence | mode).CopyTo(packet, 0);
        BitConverter.GetBytes(dstSequence & MessageConstants.SequenceMask).CopyTo(packet, 4);
        data.CopyTo(packet, MessageConstants.ConnectedHeadSize);

        return packet;
    }

    /// <summary>
    /// Builds an acknowledgement (keep-alive) packet containing the constant <see cref="MessageConstants.AckData"/> value.
    /// </summary>
    /// <param name="srcSequence">Local source sequence number.</param>
    /// <param name="dstSequence">Remote destination sequence number.</param>
    public static byte[] BuildAckPacket(uint srcSequence, uint dstSequence)
    {
        byte[] packet = new byte[MessageConstants.ConnectedHeadSize + 8];
        BitConverter.GetBytes(srcSequence).CopyTo(packet, 0);
        BitConverter.GetBytes(dstSequence & MessageConstants.SequenceMask).CopyTo(packet, 4);
        BitConverter.GetBytes(MessageConstants.AckData).CopyTo(packet, MessageConstants.ConnectedHeadSize);
        return packet;
    }

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
