using System.Text;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Core.Messages;

public static class MessageWriter
{
    public static byte[] BuildCommandPacket(uint srcSequence, uint dstSequence, byte command, byte[] data)
    {
        uint mode = MessageConstants.SequenceModeCommand;
        byte[] packet = new byte[MessageConstants.ConnectedHeadSize + data.Length];

        BitConverter.GetBytes(srcSequence | mode).CopyTo(packet, 0);
        BitConverter.GetBytes(dstSequence & MessageConstants.SequenceMask).CopyTo(packet, 4);
        data.CopyTo(packet, MessageConstants.ConnectedHeadSize);

        return packet;
    }

    public static byte[] BuildAckPacket(uint srcSequence, uint dstSequence)
    {
        byte[] packet = new byte[MessageConstants.ConnectedHeadSize + 8];
        BitConverter.GetBytes(srcSequence).CopyTo(packet, 0);
        BitConverter.GetBytes(dstSequence & MessageConstants.SequenceMask).CopyTo(packet, 4);
        BitConverter.GetBytes(MessageConstants.AckData).CopyTo(packet, MessageConstants.ConnectedHeadSize);
        return packet;
    }

    public static void WriteStringCmd(List<byte> output, ClientCommandType cmd, string str)
    {
        output.Add((byte)cmd);
        output.AddRange(Encoding.ASCII.GetBytes(str));
        output.Add(0);
    }
}
