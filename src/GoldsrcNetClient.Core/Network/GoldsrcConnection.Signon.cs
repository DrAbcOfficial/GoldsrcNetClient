using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Munge;
using GoldsrcNetClient.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// The signon sequence driven by server messages: serverinfo → sendres →
/// resourcelist/baseline → <c>spawn &lt;count&gt; &lt;crc&gt;</c> → sendents.
/// The worldmap-CRC un-munge/re-munge pair here is the engine's
/// SV_CheckMapDifferences handshake; getting it wrong surfaces as a fake
/// "Reliable channel overflowed" kick seconds after entering the game.
/// </summary>
public partial class GoldsrcConnection
{
    private bool HandleServerInfo(ConnectionContext ctx, MessageReader reader)
    {
        const int structSize = 33;
        if (!reader.ReadStruct<ServerInfoData>(out var si))
        {
            Logger.LogWarning($"[ServerInfo] buffer overflow: offset={reader.Offset}, need={structSize}, size={reader.Size}");
            return false;
        }

        ctx.MaxClients = si.MaxClients;
        ctx.PlayerNumber = si.PlayerNumber;

        // The Valve engine encrypts the worldmap CRC with an 8-bit key:
        // COM_Munge3(..., (-1 - playernum) & 0xFF) (ReHLDS SV_SendServerinfo,
        // Xash3D same). A wider key does not invert it and corrupts the CRC we
        // echo back in the spawn command — the server's periodic
        // SV_CheckMapDifferences then flags us as overflowed ("Reliable channel
        // overflowed") within ~5 seconds.
        int unmungeKey = (-1 - ctx.PlayerNumber) & 0xFF;
        Logger.LogDebug($"[ServerInfo] proto={si.ProtocolVersion}, spawnCount={si.SpawnCount}, maxClients={si.MaxClients}, playerNum={si.PlayerNumber}, worldmapCrcRaw=0x{si.Munge3WorldmapCrc:X8}, unmungeKey=0x{unmungeKey:X2}");
        if (_variant.PlaintextWorldmapCrc)
        {
            // Sven Co-op sends the true worldmap CRC in serverinfo and the real
            // client echoes it back verbatim in the spawn command (wire-verified:
            // "spawn 1 1038585952" equals the map CRC the server logged).
            ctx.WorldmapCrc = si.Munge3WorldmapCrc;
            Logger.LogDebug($"[ServerInfo] plaintext worldmap CRC=0x{ctx.WorldmapCrc:X8}");
        }
        else
        {
            byte[] crcBytes = BitConverter.GetBytes(si.Munge3WorldmapCrc);
            MungeEngine.UnMunge3(crcBytes, 4, unmungeKey);
            ctx.WorldmapCrc = BitConverter.ToUInt32(crcBytes);
            Logger.LogDebug($"[ServerInfo] worldmapCrcUnMunged=0x{ctx.WorldmapCrc:X8}");
        }

        for (int i = 0; i < 4; i++)
            reader.ReadString();

        if (reader.Offset < reader.Size)
            reader.Offset++;

        OnServerInfo?.Invoke(this, si);

        if (!_sentContinueLoading)
        {
            _sentContinueLoading = true;
            Logger.LogDebug("[SignOn] sending sendres");
            _ = SendStringCmdAsync(ClientCommandType.StringCmd, "sendres", CancellationToken.None);
        }

        return true;
    }

    private bool HandleResourceRequest(ConnectionContext ctx, MessageReader reader)
    {
        if (reader.Offset + 4 > reader.Size) { Logger.LogWarning("[ResourceRequest] buffer overflow"); return false; }
        ctx.SpawnCount = reader.ReadUInt32();
        if (reader.Offset + 4 > reader.Size) { Logger.LogWarning("[ResourceRequest] buffer overflow after spawnCount"); return false; }
        uint startIndex = reader.ReadUInt32();
        Logger.LogDebug($"[ResourceRequest] spawnCount={ctx.SpawnCount}, startIndex={startIndex}");

        // Reply by echoing the server's svc_resourcelist payload back. This client
        // holds no custom content, so with no list received yet the reply is an
        // empty count (16-bit) — byte-aligned, unlike the server's bit-packed list.
        byte[] reply = ctx.ResourceListRawBytes.Length > 0 ? (byte[])ctx.ResourceListRawBytes.Clone() : [0x00, 0x00];
        Logger.LogDebug($"[ResourceRequest] replying with clc_resourcelist ({reply.Length} bytes)");
        _ = SendCommandAsync(ClientCommandType.ResourceList, reply, CancellationToken.None);
        return true;
    }

    private bool HandleSignOnNum(MessageReader reader)
    {
        if (reader.Offset + 1 > reader.Size) { Logger.LogWarning("[SignOnNum] buffer overflow"); return true; }
        byte signOn = reader.Data[reader.Offset++];
        Logger.LogDebug($"[SignOnNum] value={signOn}");
        if (signOn == 1)
        {
            Logger.LogInformation("[SignOn] signon=1 received, signon sequence complete. Sending sendents.");
            _ = SendStringCmdAsync(ClientCommandType.StringCmd, "sendents", CancellationToken.None);
        }
        else
        {
            Logger.LogDebug($"[SignOn] signon={signOn} received (not 1, no action)");
        }
        return true;
    }

    /// <summary>
    /// Sends the final <c>spawn &lt;count&gt; &lt;mungedCrc&gt;</c> stringcmd of the
    /// sign-on sequence. The CRC is the decrypted worldmap CRC re-munged with an
    /// 8-bit key — the server unmunges with COM_UnMunge2(..., (-1 - spawncount) &amp; 0xFF)
    /// and SV_CheckMapDifferences drops the connection (as a fake overflow) if the
    /// recovered value does not match the real worldmap CRC. Fires once per connection.
    /// </summary>
    /// <remarks>
    /// The Sven Co-op engine branch sends the worldmap CRC as-is (wire-verified against
    /// a real 5.26 client: <c>spawn 1 1038585952</c> equals the raw map CRC), so the
    /// Munge2 pass is skipped for variants with <see cref="IEngineVariant.PlaintextSpawnCrc"/>.
    /// </remarks>
    private void TrySendSpawn(ConnectionContext ctx)
    {
        if (_sentSpawn || !_sentContinueLoading)
            return;
        _sentSpawn = true;

        uint spawnCount = ctx.SpawnCount;
        byte[] crcBytes = BitConverter.GetBytes((int)ctx.WorldmapCrc);
        if (!_variant.PlaintextSpawnCrc)
            MungeEngine.Munge2(crcBytes, 4, (-1 - (int)spawnCount) & 0xFF);
        int spawnCrc = BitConverter.ToInt32(crcBytes, 0);
        var spawnCmd = $"spawn {spawnCount} {spawnCrc}";
        Logger.LogDebug($"[SignOn] sending spawn (spawnCount={spawnCount}, crc=0x{spawnCrc:X8})");
        _ = SendStringCmdAsync(ClientCommandType.StringCmd, spawnCmd, CancellationToken.None);

        if (_variant.LongFragmentFields)
        {
            // Sven's signon does not end with a parseable svc_signonnum for us (its
            // delta descriptions diverge from the Valve layout, so the flood tail is
            // not decodable); the real client sends sendents right after spawn.
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                Logger.LogDebug("[SignOn] sending sendents (Sven flow)");
                await SendStringCmdAsync(ClientCommandType.StringCmd, "sendents", CancellationToken.None);
            });
        }
    }
}
