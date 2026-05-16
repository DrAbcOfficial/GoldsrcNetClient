namespace GoldsrcNetClient.Tui.Views;

public static class MovePayloadBuilder
{
    public static byte[] Build(
        short forwardmove = 0,
        short sidemove = 0,
        short upmove = 0,
        ushort buttons = 0,
        byte impulse = 0)
    {
        var data = new byte[32];
        int bitIdx = 0;
        int destSize = data.Length;

        GoldsrcNetClient.Core.Util.BitWriter.WriteBits(1u, 8, data, ref bitIdx, destSize);
        GoldsrcNetClient.Core.Util.BitWriter.WriteBits(1u, 1, data, ref bitIdx, destSize);
        GoldsrcNetClient.Core.Util.BitWriter.WriteBits(0u, 9, data, ref bitIdx, destSize);
        GoldsrcNetClient.Core.Util.BitWriter.WriteBits(0u, 8, data, ref bitIdx, destSize);
        GoldsrcNetClient.Core.Util.BitWriter.WriteBits(0u, 16, data, ref bitIdx, destSize);
        GoldsrcNetClient.Core.Util.BitWriter.WriteBits((uint)(ushort)forwardmove, 16, data, ref bitIdx, destSize);
        GoldsrcNetClient.Core.Util.BitWriter.WriteBits((uint)(ushort)sidemove, 16, data, ref bitIdx, destSize);
        GoldsrcNetClient.Core.Util.BitWriter.WriteBits((uint)(ushort)upmove, 16, data, ref bitIdx, destSize);
        GoldsrcNetClient.Core.Util.BitWriter.WriteBits(0u, 12, data, ref bitIdx, destSize);
        GoldsrcNetClient.Core.Util.BitWriter.WriteBits(0u, 12, data, ref bitIdx, destSize);
        GoldsrcNetClient.Core.Util.BitWriter.WriteBits(0u, 12, data, ref bitIdx, destSize);
        GoldsrcNetClient.Core.Util.BitWriter.WriteBits(buttons, 16, data, ref bitIdx, destSize);
        GoldsrcNetClient.Core.Util.BitWriter.WriteBits(impulse, 8, data, ref bitIdx, destSize);
        GoldsrcNetClient.Core.Util.BitWriter.WriteBits(0u, 8, data, ref bitIdx, destSize);

        int byteSize = bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
        if (byteSize < data.Length) data[byteSize] = 0;
        byteSize++;

        var result = new byte[byteSize];
        Array.Copy(data, result, byteSize);
        return result;
    }
}
