using GoldsrcNetClient.Core.Delta;
using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Messages.Engine;
using GoldsrcNetClient.Core.Messages.Parsing;
using GoldsrcNetClient.Core.Munge;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Test;

/// <summary>
/// Engine (<c>svc_*</c>) parser tests. Each drives the real parser registered by
/// <see cref="EngineMessageParsers"/> through the pipeline, so registration and
/// parsing are covered together.
/// </summary>
public class EngineMessageParserTests
{
    private static (MessagePipeline Pipeline, MessageHub Hub) Pipeline(IEngineVariant? variant = null)
    {
        var hub = new MessageHub();
        var state = new SessionData();
        var builder = new ParserRegistry.Builder();
        EngineMessageParsers.Register(builder, variant ?? EngineVariants.Valve, state);
        return (new MessagePipeline(builder.Build(), new GoldsrcNetClient.Core.Game.UserMessageRegistry(), hub), hub);
    }

    /// <summary>Builds an svc_serverinfo payload: the 33-byte struct, four strings, one flag byte.</summary>
    private static byte[] ServerInfoPayload(uint rawMunge3Crc, byte playerNumber, byte maxClients, byte flag = 1)
    {
        var writer = new BufferWriter();
        writer.WriteUInt32(48);                  // protocol
        writer.WriteUInt32(1);                   // spawn count
        writer.WriteUInt32(rawMunge3Crc);        // encrypted worldmap CRC
        writer.WriteBytes(new byte[16]);         // client.dll MD5
        writer.WriteUInt8(maxClients);
        writer.WriteUInt8(playerNumber);
        writer.WriteUInt8(0);                    // padding
        writer.WriteString("valve");             // gamedir
        writer.WriteString("cl_dlls/client.dll"); // client dll
        writer.WriteString("maps/test.bsp");     // map
        writer.WriteString("Half-Life");         // game description
        writer.WriteUInt8(flag);
        return writer.ToArray();
    }

    [Fact]
    public void ServerInfo_ValveBranch_UnMungesWorldmapCrc()
    {
        // Encrypt the CRC the way the engine does for this slot, then verify the
        // parser inverts it (this is the SV_CheckMapDifferences handshake value).
        byte[] crcBytes = BitConverter.GetBytes(0x11223344u);
        MungeEngine.Munge3(crcBytes, 4, (-1 - 2) & 0xFF);
        uint munged = BitConverter.ToUInt32(crcBytes);

        var (pipeline, hub) = Pipeline();
        ServerInfoMessage? info = null;
        hub.Subscribe<ServerInfoMessage>(m => info = m);

        pipeline.Process([.. new byte[] { (byte)ServerMessageType.ServerInfo }, .. ServerInfoPayload(munged, playerNumber: 2, maxClients: 32)]);

        Assert.NotNull(info);
        Assert.Equal(0x11223344u, info!.WorldmapCrc);
        Assert.Equal("maps/test.bsp", info.MapName);
        Assert.Equal("valve", info.GameDir);
        Assert.Equal((byte)2, info.Data.PlayerNumber);
    }

    [Fact]
    public void ServerInfo_PlaintextBranch_KeepsCrc()
    {
        var (pipeline, hub) = Pipeline(EngineVariants.SvenCoop);
        ServerInfoMessage? info = null;
        hub.Subscribe<ServerInfoMessage>(m => info = m);

        pipeline.Process([.. new byte[] { (byte)ServerMessageType.ServerInfo }, .. ServerInfoPayload(1038585952u, playerNumber: 0, maxClients: 32)]);

        Assert.Equal(1038585952u, info!.WorldmapCrc);
    }

    [Fact]
    public void Print_PublishesText()
    {
        var (pipeline, hub) = Pipeline();
        PrintMessage? print = null;
        hub.Subscribe<PrintMessage>(m => print = m);

        pipeline.Process([(byte)ServerMessageType.Print, .. "hello world\0"u8.ToArray()]);

        Assert.Equal("hello world", print!.Text);
    }

    [Fact]
    public void Version_ReadsProtocol()
    {
        var (pipeline, hub) = Pipeline();
        VersionMessage? version = null;
        hub.Subscribe<VersionMessage>(m => version = m);

        pipeline.Process([(byte)ServerMessageType.Version, 48, 0, 0, 0]);

        Assert.Equal(48u, version!.Protocol);
    }

    [Fact]
    public void SetPause_ReadsBit()
    {
        var (pipeline, hub) = Pipeline();
        SetPauseMessage? pause = null;
        hub.Subscribe<SetPauseMessage>(m => pause = m);

        pipeline.Process([(byte)ServerMessageType.SetPause, 0b0000_0001]);

        Assert.True(pause!.Paused);
    }

    [Fact]
    public void UnknownEngineType_DiscardsPacketTail()
    {
        var (pipeline, hub) = Pipeline();
        var prints = new List<string>();
        hub.Subscribe<PrintMessage>(m => prints.Add(m.Text));

        pipeline.Process([0xF0, (byte)ServerMessageType.Print, .. "x\0"u8.ToArray()]);

        Assert.Empty(prints);
    }

    [Fact]
    public void OpaqueMessages_KeepStreamInSync()
    {
        var (pipeline, hub) = Pipeline();
        var prints = new List<string>();
        hub.Subscribe<PrintMessage>(m => prints.Add(m.Text));

        // PacketEntities consumes the tail by definition; a preceding Print must still arrive.
        pipeline.Process([(byte)ServerMessageType.Print, .. "a\0"u8.ToArray(), (byte)ServerMessageType.PacketEntities, 0x01, 0x02]);

        Assert.Equal("a", Assert.Single(prints));
    }

    [Fact]
    public void SetView_SkipsFixedBytes()
    {
        var (pipeline, hub) = Pipeline();
        var print = new List<string>();
        hub.Subscribe<PrintMessage>(m => print.Add(m.Text));

        pipeline.Process([(byte)ServerMessageType.SetView, 0x10, 0x20, (byte)ServerMessageType.Print, .. "ok\0"u8.ToArray()]);

        Assert.Equal("ok", Assert.Single(print));
    }

    [Fact]
    public void NewUserMsg_IsFramableByRegistry()
    {
        var (pipeline, hub) = Pipeline();
        NewUserMsgMessage? msg = null;
        hub.Subscribe<NewUserMsgMessage>(m => msg = m);

        var writer = new BufferWriter();
        writer.WriteUInt8(0x50);
        writer.WriteUInt8(0x05);
        byte[] name = new byte[16];
        "SayText"u8.ToArray().CopyTo(name, 0); // 16-byte fixed name buffer
        writer.WriteBytes(name);
        pipeline.Process([(byte)ServerMessageType.NewUserMsg, .. writer.ToArray()]);

        Assert.Equal((byte)0x50, msg!.Index);
        Assert.Equal("SayText", msg.Name);
        Assert.Equal((byte)5, msg.DeclaredSize);
    }
}
