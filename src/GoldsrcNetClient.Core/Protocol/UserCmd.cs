using GoldsrcNetClient.Core.Util;

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
    /// 16-bit — the same wire layout the engine uses.
    /// </summary>
    public static byte[] Encode(short forwardmove = 0, short sidemove = 0, short upmove = 0, ushort buttons = 0, byte impulse = 0)
    {
        byte[] data = new byte[32];
        int bitIdx = 0;
        int size = data.Length;

        BitWriter.WriteBits(1u, 8, data, ref bitIdx, size);                          // outgoing sequence backlog
        BitWriter.WriteBits(1u, 1, data, ref bitIdx, size);                          // has-delta flag
        BitWriter.WriteBits(0u, 9, data, ref bitIdx, size);                          // light level
        BitWriter.WriteBits(0u, 8, data, ref bitIdx, size);                          // viewangles upper bits
        BitWriter.WriteBits(0u, 16, data, ref bitIdx, size);                         // viewangles
        BitWriter.WriteBits((uint)(ushort)forwardmove, 16, data, ref bitIdx, size);  // forwardmove
        BitWriter.WriteBits((uint)(ushort)sidemove, 16, data, ref bitIdx, size);     // sidemove
        BitWriter.WriteBits((uint)(ushort)upmove, 16, data, ref bitIdx, size);       // upmove
        BitWriter.WriteBits(0u, 12, data, ref bitIdx, size);                         // spare
        BitWriter.WriteBits(0u, 12, data, ref bitIdx, size);                         // spare
        BitWriter.WriteBits(0u, 12, data, ref bitIdx, size);                         // spare
        BitWriter.WriteBits(buttons, 16, data, ref bitIdx, size);                    // button bitmask
        BitWriter.WriteBits(impulse, 8, data, ref bitIdx, size);                     // impulse
        BitWriter.WriteBits(0u, 8, data, ref bitIdx, size);                          // msec

        int byteSize = bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
        if (byteSize < data.Length) data[byteSize] = 0;
        byteSize++;

        var result = new byte[byteSize];
        Array.Copy(data, result, byteSize);
        return result;
    }
}
