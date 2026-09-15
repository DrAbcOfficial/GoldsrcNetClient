using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Test;

public class UserCmdTests
{
    [Fact]
    public void Encode_Defaults_ProducePaddedPayload()
    {
        byte[] payload = UserCmd.Encode();

        // 158 payload bits round up to 20 bytes plus the trailing zero byte the
        // reference client appends — 21 bytes total.
        Assert.Equal(21, payload.Length);
        Assert.Equal(1, payload[0]); // outgoing sequence backlog
        Assert.Equal(0, payload[^1]);
    }

    [Fact]
    public void Encode_IsDeterministic()
    {
        Assert.Equal(UserCmd.Encode(250, -120, 5, 0x0103, 101), UserCmd.Encode(250, -120, 5, 0x0103, 101));
    }

    [Fact]
    public void Encode_ButtonsDifferFromDefaults()
    {
        byte[] idle = UserCmd.Encode();
        byte[] attack = UserCmd.Encode(buttons: 1);

        Assert.NotEqual(idle, attack);
    }

    /// <summary>Reads an arbitrary bit count (BufferReader.ReadBits caps at 32 per call).
    /// Takes the reader by reference — it is a ref struct, so a by-value parameter
    /// would be a copy and the cursor advance would be lost.</summary>
    private static ulong ReadBits(ref Core.Io.BufferReader reader, int count)
    {
        ulong value = 0;
        int shift = 0;
        while (count > 0)
        {
            int take = Math.Min(count, 32);
            value |= (ulong)reader.ReadBits(take) << shift;
            shift += take;
            count -= take;
        }
        return value;
    }

    /// <summary>
    /// Wire-pinning: negative movement values occupy their 16-bit slot as two's
    /// complement. sidemove sits at bit 58 (8 backlog + 1 delta + 9 light + 8
    /// view-upper + 16 view + 16 forward).
    /// </summary>
    [Fact]
    public void Encode_NegativeMove_WrapsToUnsigned16Bit()
    {
        byte[] payload = UserCmd.Encode(sidemove: -1);

        var reader = new Core.Io.BufferReader(payload);
        ReadBits(ref reader, 58);
        Assert.Equal(0xFFFFu, reader.ReadBits(16));
    }

    [Fact]
    public void Encode_MovementValues_AtTheirWirePositions()
    {
        byte[] payload = UserCmd.Encode(forwardmove: 400, sidemove: -400, upmove: 100, buttons: 0x0003, impulse: 7);

        var reader = new Core.Io.BufferReader(payload);
        Assert.Equal(1u, ReadBits(ref reader, 8));    // backlog
        Assert.Equal(1u, ReadBits(ref reader, 1));    // has-delta
        ReadBits(ref reader, 9 + 8 + 16);             // light level + viewangles
        Assert.Equal(400u, reader.ReadBits(16));  // forwardmove
        Assert.Equal(0xFE70u, reader.ReadBits(16)); // sidemove -400
        Assert.Equal(100u, reader.ReadBits(16));  // upmove
        ReadBits(ref reader, 36);                     // three 12-bit spares
        Assert.Equal(0x0003u, reader.ReadBits(16)); // buttons
        Assert.Equal(7u, reader.ReadBits(8));     // impulse
        Assert.Equal(0u, reader.ReadBits(8));     // msec
    }
}
