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

    [Fact]
    public void Encode_NegativeMove_WrapsToUnsigned16Bit()
    {
        // sidemove -1 encodes as 0xFFFF — the engine's wire layout for negative moves.
        byte[] payload = UserCmd.Encode(sidemove: -1);
        byte[] expected = UserCmd.Encode(sidemove: unchecked((short)0xFFFF));
        Assert.Equal(expected, payload);
    }
}
