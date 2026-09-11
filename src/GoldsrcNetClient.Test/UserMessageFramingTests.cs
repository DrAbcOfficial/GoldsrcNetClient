using GoldsrcNetClient.Core.Game;
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

    private sealed class TestGameHandler : GameMessageHandler
    {
        public List<RawUserMessage> RawMessages { get; } = [];

        protected override bool DispatchUserMessage(GoldsrcConnection connection, byte index, string name, MessageReader reader)
        {
            RawMessages.Add(new RawUserMessage(index, name, reader.Data[reader.Offset..reader.Size].ToArray()));
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
        // After the registration block: SayText with length 4, payload 01 02 03 04,
        // then an svc_print that must survive the SayText dispatch.
        stream.AddRange([0x04, 0x01, 0x02, 0x03, 0x04]);
        stream.AddRange([(byte)ServerMessageType.Print, (byte)'h', (byte)'i', 0]);

        using var conn = new GoldsrcConnection();
        var handler = new TestGameHandler();
        handler.Registry.Register(new MessageReader(stream.ToArray(), 18));

        var reader = new MessageReader(stream.ToArray(), stream.Count) { Offset = 18 };

        Assert.True(handler.HandleMessage(conn, 0x4C, reader));
        Assert.Equal(18 + 5, reader.Offset); // length byte + 4 payload bytes consumed
        Assert.Equal((byte)ServerMessageType.Print, reader.Data[reader.Offset]);
    }

    [Fact]
    public void VariableLengthMessage_PayloadIsDispatchedBounded()
    {
        // Reader sits after the index byte: length byte 3 declares three payload
        // bytes; the 0xFF bytes behind the message must not leak into the payload.
        var stream = new List<byte> { 0x04, 0x03, 0xAA, 0xBB, 0xCC, 0xFF, 0xFF, 0xFF };

        using var conn = new GoldsrcConnection();
        var handler = new TestGameHandler();
        handler.Registry.Register(new MessageReader(Registration(0x4C, 0xFF, "SayText")));
        var reader = new MessageReader(stream.ToArray(), stream.Count) { Offset = 1 };

        Assert.True(handler.HandleMessage(conn, 0x4C, reader));
        var raw = Assert.Single(handler.RawMessages);
        Assert.Equal("SayText", raw.Name);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, raw.Data);
        Assert.Equal(5, reader.Offset);
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
        handler.Registry.Register(new MessageReader(Registration(0x50, 0x00, "InitHUD")));

        var reader = new MessageReader(stream.ToArray(), stream.Count) { Offset = 1 };
        Assert.True(handler.HandleMessage(conn, 0x50, reader));
        Assert.Equal(1, reader.Offset);
        Assert.Equal((byte)ServerMessageType.Print, reader.Data[reader.Offset]);
    }

    [Fact]
    public void FixedSizeMessage_ConsumesRegisteredLength()
    {
        // Byte 0 is the index slot; payload byte 0x64; then an intact svc_print.
        var stream = new List<byte> { 0x47, 0x64, (byte)ServerMessageType.Print, (byte)'x', 0 };

        using var conn = new GoldsrcConnection();
        var handler = new TestGameHandler();
        handler.Registry.Register(new MessageReader(Registration(0x47, 0x01, "Health")));

        var reader = new MessageReader(stream.ToArray(), stream.Count) { Offset = 1 };
        Assert.True(handler.HandleMessage(conn, 0x47, reader));
        Assert.Equal(2, reader.Offset);
        Assert.Equal((byte)ServerMessageType.Print, reader.Data[reader.Offset]);
    }
}
