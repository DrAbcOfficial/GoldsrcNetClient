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
}
