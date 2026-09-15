using GoldsrcNetClient.Core.Game;
using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Messages.Engine;
using GoldsrcNetClient.Core.Messages.Parsing;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Test;

public class ParserRegistryTests
{
    private sealed record DummyMessage(byte Value) : IServerMessage;

    [Fact]
    public void Engine_LookupByTypeByte()
    {
        var registry = new ParserRegistry.Builder()
            .AddEngine((byte)ServerMessageType.Print, static (ref BufferReader r) => new PrintMessage(r.ReadString()))
            .Build();

        Assert.NotNull(registry.GetEngine((byte)ServerMessageType.Print));
        Assert.Null(registry.GetEngine((byte)ServerMessageType.Nop));
    }

    [Fact]
    public void Engine_ReRegistrationReplaces()
    {
        var builder = new ParserRegistry.Builder();
        builder.AddEngine(0x20, static (ref BufferReader r) => new DummyMessage(1));
        builder.AddEngine(0x20, static (ref BufferReader r) => new DummyMessage(2));
        var registry = builder.Build();

        var reader = new BufferReader((byte[])[0x00]);
        Assert.Equal(2, ((DummyMessage)registry.GetEngine(0x20)!(ref reader)).Value);
    }

    [Fact]
    public void User_ByName_ReRegistrationReplaces()
    {
        var builder = new ParserRegistry.Builder();
        builder.AddUser("SayText", static (ref BufferReader r) => new DummyMessage(1));
        builder.AddUser("SayText", static (ref BufferReader r) => new DummyMessage(2));
        var registry = builder.Build();

        Assert.False(registry.TryGetUser("saytext", out _)); // wire names are case-sensitive
        Assert.True(registry.TryGetUser("SayText", out var parser));
        var reader = new BufferReader((byte[])[0x00]);
        Assert.Equal(2, ((DummyMessage)parser(ref reader)).Value);
    }
}

public class MessageHubTests
{
    private sealed record MsgA(int Value) : IServerMessage;
    private sealed record MsgB(int Value) : IServerMessage;

    [Fact]
    public void Subscribe_ReceivesExactTypeOnly()
    {
        var hub = new MessageHub();
        var a = new List<int>();
        var b = new List<int>();
        hub.Subscribe<MsgA>(m => a.Add(m.Value));
        hub.Subscribe<MsgB>(m => b.Add(m.Value));

        hub.Publish(new MsgA(1));
        hub.Publish(new MsgB(2));
        hub.Publish(new MsgA(3));

        Assert.Equal([1, 3], a);
        Assert.Equal([2], b);
    }

    [Fact]
    public void MultipleHandlers_AllInvokedInOrder()
    {
        var hub = new MessageHub();
        var order = new List<int>();
        hub.Subscribe<MsgA>(_ => order.Add(1));
        hub.Subscribe<MsgA>(_ => order.Add(2));

        hub.Publish(new MsgA(0));
        Assert.Equal([1, 2], order);
    }

    [Fact]
    public void Dispose_Unsubscribes()
    {
        var hub = new MessageHub();
        var count = 0;
        using (hub.Subscribe<MsgA>(_ => count++))
        {
            hub.Publish(new MsgA(0));
        }
        hub.Publish(new MsgA(0));
        Assert.Equal(1, count);
    }

    [Fact]
    public void FailingHandler_DoesNotAffectOthers()
    {
        var hub = new MessageHub();
        var ok = false;
        hub.Subscribe<MsgA>(_ => throw new InvalidOperationException("boom"));
        hub.Subscribe<MsgA>(_ => ok = true);

        hub.Publish(new MsgA(0));
        Assert.True(ok);
    }

    [Fact]
    public void PublishWithoutSubscribers_IsNoOp()
    {
        var hub = new MessageHub();
        hub.Publish(new MsgA(0)); // must not throw
    }
}

public class MessagePipelineTests
{
    private sealed record ChatMessage(byte Slot, string Text) : IServerMessage;

    private static ParserRegistry Registry(Action<ParserRegistry.Builder>? extra = null)
    {
        var builder = new ParserRegistry.Builder()
            .AddEngine((byte)ServerMessageType.Print, static (ref BufferReader r) => new PrintMessage(r.ReadString()))
            .AddEngine((byte)ServerMessageType.Disconnect, static (ref BufferReader r) =>
            {
                var msg = new DisconnectMessage(r.ReadString());
                r.BytePosition = r.Length; // the engine discards the packet tail
                return msg;
            })
            .AddUser("SayText", static (ref BufferReader r) => new ChatMessage(r.ReadUInt8(), r.ReadString()));
        extra?.Invoke(builder);
        return builder.Build();
    }

    private static (MessagePipeline Pipeline, MessageHub Hub) MakePipeline(ParserRegistry registry)
    {
        var hub = new MessageHub();
        var userMessages = new UserMessageRegistry();
        var regReader = new BufferReader(TestWire.Registration(0x4C, 0xFF, "SayText"));
        userMessages.Register(ref regReader);
        return (new MessagePipeline(registry, userMessages, hub), hub);
    }

    [Fact]
    public void EngineMessages_ParsedAndPublished()
    {
        var (pipeline, hub) = MakePipeline(Registry());
        var prints = new List<string>();
        hub.Subscribe<PrintMessage>(m => prints.Add(m.Text));

        pipeline.Process([(byte)ServerMessageType.Print, (byte)'h', (byte)'i', 0]);

        var print = Assert.Single(prints);
        Assert.Equal("hi", print);
    }

    [Fact]
    public void MultipleMessagesInOneStream_AllParsedInOrder()
    {
        var (pipeline, hub) = MakePipeline(Registry());
        var seen = new List<string>();
        hub.Subscribe<PrintMessage>(m => seen.Add("print:" + m.Text));
        hub.Subscribe<ChatMessage>(m => seen.Add($"chat:{m.Slot}:{m.Text}"));

        pipeline.Process(
        [
            (byte)ServerMessageType.Print, (byte)'a', 0,
            0x4C, 0x03, 0x00, 0x07, (byte)'x', 0, // SayText: slot 7, text "x"
            (byte)ServerMessageType.Print, (byte)'b', 0,
        ]);

        Assert.Equal(["print:a", "chat:7:x", "print:b"], seen);
    }

    /// <summary>
    /// Regression: a variable-length user message must consume exactly index +
    /// length word + payload — the svc_print behind it must survive. Swallowing
    /// the tail desynchronised the reliable stream and got real servers to drop
    /// the client with "Reliable channel overflowed".
    /// </summary>
    [Fact]
    public void VariableLengthUserMessage_ConsumesLengthPrefixAndPayloadOnly()
    {
        var (pipeline, hub) = MakePipeline(Registry());
        var prints = new List<string>();
        hub.Subscribe<PrintMessage>(m => prints.Add(m.Text));

        pipeline.Process(
        [
            0x4C, 0x04, 0x00, 0x01, 0x02, 0x03, 0x04, // SayText declares 4 raw bytes
            (byte)ServerMessageType.Print, (byte)'h', (byte)'i', 0,
        ]);

        // The 4 raw payload bytes are not a valid ChatMessage (slot=1, text="\x02\x03\x04")
        // — but the framing keeps the stream in sync either way.
        Assert.Equal("hi", Assert.Single(prints));
    }

    [Fact]
    public void UnregisteredUserName_PublishesRawUserMessage()
    {
        var hub = new MessageHub();
        var userMessages = new UserMessageRegistry();
        var regReader = new BufferReader(TestWire.Registration(0x50, 0x02, "CustomThing"));
        userMessages.Register(ref regReader);
        var pipeline = new MessagePipeline(Registry(), userMessages, hub);

        var raw = new List<RawUserMessage>();
        hub.Subscribe<RawUserMessage>(m => raw.Add(m));

        pipeline.Process([0x50, 0xAB, 0xCD]);

        var msg = Assert.Single(raw);
        Assert.Equal("CustomThing", msg.Name);
        Assert.Equal((byte[])[0xAB, 0xCD], msg.Data);
    }

    [Fact]
    public void UnknownEngineType_DiscardsPacketTail()
    {
        var (pipeline, hub) = MakePipeline(Registry());
        var prints = new List<string>();
        hub.Subscribe<PrintMessage>(m => prints.Add(m.Text));

        pipeline.Process([(byte)ServerMessageType.Print, (byte)'a', 0, 0xFE, 0x01, (byte)ServerMessageType.Print, (byte)'b', 0]);

        Assert.Equal(["a"], prints); // everything after the unknown 0xFE is discarded
    }

    [Fact]
    public void TruncatedEngineMessage_DiscardsPacketTail()
    {
        // Register a fixed-size parser so truncation is unambiguous (a bare
        // string read is lenient: no terminator = consume the rest).
        var registry = Registry(b => b.AddEngine(0x30, static (ref BufferReader r) =>
            new VersionMessage(r.ReadUInt32())));
        var (pipeline, hub) = MakePipeline(registry);
        var versions = new List<uint>();
        hub.Subscribe<VersionMessage>(m => versions.Add(m.Protocol));

        pipeline.Process([0x30, 0x01, 0x02, 0x03]); // only 3 payload bytes: the 4-byte read fails

        Assert.Empty(versions); // the 4-byte read failed: tail discarded, print never parsed
    }

    [Fact]
    public void Disconnect_DiscardsRestOfPacket()
    {
        var (pipeline, hub) = MakePipeline(Registry());
        var disconnects = new List<string>();
        hub.Subscribe<DisconnectMessage>(m => disconnects.Add(m.Reason));
        var prints = new List<string>();
        hub.Subscribe<PrintMessage>(m => prints.Add(m.Text));

        pipeline.Process([(byte)ServerMessageType.Disconnect, (byte)'b', (byte)'y', (byte)'e', 0, (byte)ServerMessageType.Print, (byte)'x', 0]);

        Assert.Equal("bye", Assert.Single(disconnects));
        Assert.Empty(prints);
    }
}
