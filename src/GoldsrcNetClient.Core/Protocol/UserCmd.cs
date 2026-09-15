using GoldsrcNetClient.Core.Io;

namespace GoldsrcNetClient.Core.Protocol;

/// <summary>
/// Encodes the <c>clc_move</c> usercmd payload (protocol 48 bit layout):
/// backlog count, delta flag, light level, view angles, movement values,
/// buttons bitmask, impulse, and frame time.
/// </summary>
public static class UserCmd
{
    /// <summary>
    /// Encodes one movement command. Negative movement values wrap to unsigned
    /// 16-bit — the same wire layout the engine uses. A trailing zero byte
    /// follows the encoded bits, matching the engine's buffer commit.
    /// </summary>
    public static byte[] Encode(short forwardmove = 0, short sidemove = 0, short upmove = 0, ushort buttons = 0, byte impulse = 0)
    {
        var writer = new BufferWriter();
        writer.WriteBits(1u, 8);                          // outgoing sequence backlog
        writer.WriteBits(1u, 1);                          // has-delta flag
        writer.WriteBits(0u, 9);                          // light level
        writer.WriteBits(0u, 8);                          // viewangles upper bits
        writer.WriteBits(0u, 16);                         // viewangles
        writer.WriteBits((uint)(ushort)forwardmove, 16);  // forwardmove
        writer.WriteBits((uint)(ushort)sidemove, 16);     // sidemove
        writer.WriteBits((uint)(ushort)upmove, 16);       // upmove
        writer.WriteBits(0u, 12);                         // spare
        writer.WriteBits(0u, 12);                         // spare
        writer.WriteBits(0u, 12);                         // spare
        writer.WriteBits(buttons, 16);                    // button bitmask
        writer.WriteBits(impulse, 8);                     // impulse
        writer.WriteBits(0u, 8);                          // msec

        writer.Align();
        var result = new byte[writer.Length + 1]; // + the engine's trailing zero byte
        writer.WrittenSpan.CopyTo(result);
        return result;
    }
}
