using GoldsrcNetClient.Core.Delta;
using GoldsrcNetClient.Core.Messages.Engine;
using GoldsrcNetClient.Core.Munge;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;
using System.Runtime.InteropServices;

namespace GoldsrcNetClient.Test;

/// <summary>
/// Signon-sequence behavior tests: the controller is driven with parsed messages
/// and its outgoing stringcmds are captured through an injected port, so the
/// exact wire payloads (sendres / spawn &lt;crc&gt; / sendents) are asserted.
/// </summary>
public class SignonControllerTests
{
    private sealed class FakePort
    {
        public List<(ClientCommandType Cmd, string Payload)> Strings { get; } = [];
        public List<(ClientCommandType Cmd, byte[] Data)> Commands { get; } = [];

        public Task SendString(ClientCommandType cmd, string payload, CancellationToken ct)
        {
            Strings.Add((cmd, payload));
            return Task.CompletedTask;
        }

        public Task SendCommand(ClientCommandType cmd, byte[] data, CancellationToken ct)
        {
            Commands.Add((cmd, data));
            return Task.CompletedTask;
        }
    }

    /// <summary>Server info as the parser would deliver it: the worldmap CRC is
    /// already un-munged by <c>ServerInfoMessage.WorldmapCrc</c>.</summary>
    private static ServerInfoMessage MakeServerInfo(
        byte playerNumber, byte maxClients, uint spawnCount, uint worldmapCrc)
    {
        var data = new ServerInfoData
        {
            ProtocolVersion = 48,
            SpawnCount = spawnCount,
            Munge3WorldmapCrc = worldmapCrc,
            MaxClients = maxClients,
            PlayerNumber = playerNumber,
        };
        return new ServerInfoMessage(data, "valve", "cl_dll", "maps/test.bsp", "Half-Life", worldmapCrc);
    }

    private static (SignonController Controller, FakePort Port, SessionData State) Setup(IEngineVariant? variant = null)
    {
        var port = new FakePort();
        var state = new SessionData();
        var controller = new SignonController(
            variant ?? EngineVariants.Valve, port.SendString, port.SendCommand, logger: null);
        return (controller, port, state);
    }

    [Fact]
    public void ServerInfo_SendsSendresOnce()
    {
        var (controller, port, state) = Setup();

        controller.OnServerInfo(state, MakeServerInfo(playerNumber: 3, maxClients: 32, spawnCount: 1, worldmapCrc: 0xDEADBEEF));
        controller.OnServerInfo(state, MakeServerInfo(playerNumber: 3, maxClients: 32, spawnCount: 2, worldmapCrc: 0xDEADBEEF));

        var sent = Assert.Single(port.Strings);
        Assert.Equal(ClientCommandType.StringCmd, sent.Cmd);
        Assert.Equal("sendres", sent.Payload);
        Assert.Equal(3, state.PlayerNumber);
        Assert.Equal(32, state.MaxClients);
    }

    [Fact]
    public void Spawn_FiresOnceAfterResourceList_WithReMungedCrc()
    {
        var (controller, port, state) = Setup();
        controller.OnServerInfo(state, MakeServerInfo(playerNumber: 1, maxClients: 32, spawnCount: 1, worldmapCrc: 0x55667788));
        controller.OnResourceRequest(state, new ResourceRequestMessage(SpawnCount: 1, StartIndex: 0));
        port.Strings.Clear();
        port.Commands.Clear();

        var resources = new ResourceListMessage([], []);
        controller.OnResourceList(state, resources);
        controller.OnResourceList(state, resources); // must not send a second spawn

        var spawn = Assert.Single(port.Strings); // only the spawn
        Assert.Equal(ClientCommandType.StringCmd, spawn.Cmd);
        Assert.StartsWith("spawn 1 ", spawn.Payload);

        // The server unmunges with (-1 - spawnCount) & 0xFF and compares to the real CRC.
        int sentCrc = int.Parse(spawn.Payload.Split(' ')[2]);
        byte[] crcBytes = BitConverter.GetBytes(sentCrc);
        MungeEngine.UnMunge2(crcBytes, 4, (-1 - 1) & 0xFF);
        Assert.Equal(0x55667788u, BitConverter.ToUInt32(crcBytes));
    }

    [Fact]
    public void Spawn_PlaintextBranch_SendsRawCrc()
    {
        var (controller, port, state) = Setup(EngineVariants.SvenCoop);
        controller.OnServerInfo(state, MakeServerInfo(playerNumber: 0, maxClients: 32, spawnCount: 1, worldmapCrc: 1038585952u));
        controller.OnResourceRequest(state, new ResourceRequestMessage(SpawnCount: 1, StartIndex: 0));
        controller.OnResourceList(state, new ResourceListMessage([], []));

        Assert.Equal("spawn 1 1038585952", port.Strings[^1].Payload);
    }

    [Fact]
    public void ResourceRequest_EchoesRawResourceList()
    {
        var (controller, port, state) = Setup();
        byte[] raw = [0xAB, 0xCD, 0xEF];
        state.ResourceListRawBytes = raw;

        controller.OnResourceRequest(state, new ResourceRequestMessage(SpawnCount: 5, StartIndex: 0));

        Assert.Equal(5u, state.SpawnCount);
        var sent = Assert.Single(port.Commands);
        Assert.Equal(ClientCommandType.ResourceList, sent.Cmd);
        Assert.Equal(raw, sent.Data);
    }

    [Fact]
    public void ResourceRequest_WithoutList_SendsEmptyCount()
    {
        var (controller, port, state) = Setup();
        controller.OnResourceRequest(state, new ResourceRequestMessage(SpawnCount: 1, StartIndex: 0));
        Assert.Equal((byte[])[0x00, 0x00], Assert.Single(port.Commands).Data);
    }

    [Fact]
    public void SignOnNumOne_SendsSendents()
    {
        var (controller, port, _) = Setup();
        controller.OnSignOnNum(new SignOnNumMessage(1));
        Assert.Equal("sendents", Assert.Single(port.Strings).Payload);
    }

    [Fact]
    public void SignOnNumOtherThanOne_DoesNothing()
    {
        var (controller, port, _) = Setup();
        controller.OnSignOnNum(new SignOnNumMessage(2));
        Assert.Empty(port.Strings);
    }

    [Fact]
    public void Reset_AllowsSpawnAgain()
    {
        var (controller, port, state) = Setup();
        controller.OnServerInfo(state, MakeServerInfo(playerNumber: 0, maxClients: 32, spawnCount: 1, worldmapCrc: 0x1));
        controller.OnResourceList(state, new ResourceListMessage([], []));
        Assert.Equal(2, port.Strings.Count);

        controller.Reset();
        controller.OnServerInfo(state, MakeServerInfo(playerNumber: 0, maxClients: 32, spawnCount: 2, worldmapCrc: 0x1));
        controller.OnResourceList(state, new ResourceListMessage([], []));
        Assert.Equal(4, port.Strings.Count); // sendres + spawn again
    }

    [Fact]
    public void DeltaDescription_RecordsLiveTable()
    {
        var (controller, _, state) = Setup();
        var type = new DeltaType("entity_state_t", 1, [new DeltaField("origin", DeltaFieldFlag.Float, 16, 1f)]);
        controller.OnDeltaDescription(state, new DeltaDescriptionMessage("entity_state_t", type));
        Assert.True(state.DeltaTables.ContainsKey("entity_state_t"));
    }
}
