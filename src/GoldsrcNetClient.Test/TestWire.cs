using System.Text;

namespace GoldsrcNetClient.Test;

/// <summary>Shared wire-format helpers for building message fixtures in tests.</summary>
public static class TestWire
{
    /// <summary>Builds the payload of an svc_newusermsg registration (18 bytes):
    /// index byte, declared size byte, and a fixed 16-byte NUL-padded name.</summary>
    public static byte[] Registration(byte index, byte size, string name)
    {
        var nameBytes = new byte[16];
        var nameRaw = Encoding.UTF8.GetBytes(name);
        Array.Copy(nameRaw, nameBytes, Math.Min(nameRaw.Length, 16));
        return [index, size, .. nameBytes];
    }

    /// <summary>Wraps a 32-bit Sven coordinate (value × 8) in little-endian bytes.</summary>
    public static IEnumerable<byte> Coord(int raw)
    {
        yield return (byte)(raw & 0xFF);
        yield return (byte)((raw >> 8) & 0xFF);
        yield return (byte)((raw >> 16) & 0xFF);
        yield return (byte)((raw >> 24) & 0xFF);
    }
}
