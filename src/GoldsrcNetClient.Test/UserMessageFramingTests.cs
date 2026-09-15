using GoldsrcNetClient.Core.Game;
using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;
using System.Text;

namespace GoldsrcNetClient.Test;

public class UserMessageFramingTests
{
    /// <summary>Builds the payload of an svc_newusermsg registration (18 bytes).</summary>
    private static byte[] Registration(byte index, byte size, string name)
    {
        var nameBytes = new byte[16];
        var nameRaw = Encoding.UTF8.GetBytes(name);
        Array.Copy(nameRaw, nameBytes, Math.Min(nameRaw.Length, 16));
        return [index, size, .. nameBytes];
    }

    private static void Register(GameMessageHandler handler, byte[] registration)
    {
        var reader = new BufferReader(registration);
        handler.Registry.Register(ref reader);
    }

    private sealed class TestGameHandler : GameMessageHandler
    {
        public List<RawUserMessage> RawMessages { get; } = [];

        protected override bool DispatchUserMessage(GoldsrcConnection connection, byte index, string name, ref BufferReader reader)
        {
            RawMessages.Add(new RawUserMessage(index, name, reader.RemainingSpan.ToArray()));
            return true;
        }
    }

    /// <summary>
    /// Regression: a variable-length (registered 255) user message must consume
    /// exactly index + length byte + payload — everything after it in the packet
    /// (svc_print here) must remain parseable. Swallowing the tail desynchronised
    /// the reliable stream and got real servers to drop the client with
    /// "Reliable channel overflowed".
    /// </summary>
    [Fact]
    public void VariableLengthMessage_ConsumesLengthPrefixAndPayloadOnly()
    {
        var stream = new List<byte>();
        stream.AddRange(Registration(0x4C, 0xFF, "SayText"));
        // After the registration block: SayText with 16-bit length 4, payload
        // 01 02 03 04, then an svc_print that must survive the SayText dispatch.
        stream.AddRange([0x04, 0x00, 0x01, 0x02, 0x03, 0x04]);
        stream.AddRange([(byte)ServerMessageType.Print, (byte)'h', (byte)'i', 0]);

        using var conn = new GoldsrcConnection();
        var handler = new TestGameHandler();
        Register(handler, stream.ToArray()[..18]);

        var reader = new BufferReader(stream.ToArray()) { BytePosition = 18 };

        Assert.True(handler.HandleMessage(conn, 0x4C, ref reader));
        Assert.Equal(18 + 6, reader.BytePosition); // 16-bit length word + 4 payload bytes consumed
        Assert.Equal((byte)ServerMessageType.Print, reader.RemainingSpan[0]);
    }

    [Fact]
    public void VariableLengthMessage_PayloadIsDispatchedBounded()
    {
        // Reader sits after the index byte: 16-bit length 3 declares three payload
        // bytes; the 0xFF bytes behind the message must not leak into the payload.
        var stream = new List<byte> { 0x04, 0x03, 0x00, 0xAA, 0xBB, 0xCC, 0xFF, 0xFF, 0xFF };

        using var conn = new GoldsrcConnection();
        var handler = new TestGameHandler();
        Register(handler, Registration(0x4C, 0xFF, "SayText"));
        var reader = new BufferReader(stream.ToArray()) { BytePosition = 1 };

        Assert.True(handler.HandleMessage(conn, 0x4C, ref reader));
        var raw = Assert.Single(handler.RawMessages);
        Assert.Equal("SayText", raw.Name);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, raw.Data);
        Assert.Equal(6, reader.BytePosition);
    }

    [Fact]
    public void ZeroSizeMessage_ConsumesIndexByteOnly()
    {
        // Registered with size 0: no length byte, no payload. The svc_print byte
        // behind it must remain parseable (it used to be swallowed whole).
        // Byte 0 is the message index slot the caller has already consumed.
        var stream = new List<byte> { 0x50, (byte)ServerMessageType.Print, (byte)'x', 0 };

        using var conn = new GoldsrcConnection();
        var handler = new TestGameHandler();
        Register(handler, Registration(0x50, 0x00, "InitHUD"));

        var reader = new BufferReader(stream.ToArray()) { BytePosition = 1 };
        Assert.True(handler.HandleMessage(conn, 0x50, ref reader));
        Assert.Equal(1, reader.BytePosition);
        Assert.Equal((byte)ServerMessageType.Print, reader.RemainingSpan[0]);
    }

    [Fact]
    public void FixedSizeMessage_ConsumesRegisteredLength()
    {
        // Byte 0 is the index slot; payload byte 0x64; then an intact svc_print.
        var stream = new List<byte> { 0x47, 0x64, (byte)ServerMessageType.Print, (byte)'x', 0 };

        using var conn = new GoldsrcConnection();
        var handler = new TestGameHandler();
        Register(handler, Registration(0x47, 0x01, "Health"));

        var reader = new BufferReader(stream.ToArray()) { BytePosition = 1 };
        Assert.True(handler.HandleMessage(conn, 0x47, ref reader));
        Assert.Equal(2, reader.BytePosition);
        Assert.Equal((byte)ServerMessageType.Print, reader.RemainingSpan[0]);
    }
}
