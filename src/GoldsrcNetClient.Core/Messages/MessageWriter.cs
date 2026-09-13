using GoldsrcNetClient.Core.Protocol;
using System.Text;

namespace GoldsrcNetClient.Core.Messages;

/// <summary>
/// Static helpers for building GoldSrc network message payloads
/// (client command bytes, null-terminated strings, scalars).
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

    /// <summary>Appends a little-endian unsigned 32-bit integer to the output list.</summary>
    public static void WriteUInt32(List<byte> output, uint value) => output.AddRange(BitConverter.GetBytes(value));

    /// <summary>Appends a null-terminated UTF-8 string to the output list.</summary>
    public static void WriteString(List<byte> output, string value)
    {
        output.AddRange(Encoding.UTF8.GetBytes(value));
        output.Add(0);
    }
}
