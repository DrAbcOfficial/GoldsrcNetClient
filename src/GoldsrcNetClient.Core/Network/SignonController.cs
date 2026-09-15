using GoldsrcNetClient.Core.Messages.Engine;
using GoldsrcNetClient.Core.Munge;
using GoldsrcNetClient.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// The client side of the sign-on sequence, driven by parsed server messages:
/// serverinfo → <c>sendres</c> → resourcelist / baselines →
/// <c>spawn &lt;count&gt; &lt;mungedCrc&gt;</c> → <c>sendents</c>. Owns the
/// once-per-session progress flags; the connection delegates the signon-family
/// messages here (a fresh <see cref="Reset"/> runs per
/// <c>ConnectAsync</c> session).
/// </summary>
/// <remarks>
/// <para>
/// The worldmap-CRC un-munge/re-munge pair is the engine's
/// SV_CheckMapDifferences handshake; getting it wrong surfaces as a fake
/// "Reliable channel overflowed" kick seconds after entering the game.
/// </para>
/// <para>
/// The Sven Co-op branch sends the worldmap CRC plaintext and its signon has no
/// parseable terminal <c>svc_signonnum</c> (the delta-description flood tail is
/// undecodable), so <c>sendents</c> follows the spawn after a fixed delay
/// (wire-verified against a real 5.26 client).
/// </para>
/// </remarks>
public sealed class SignonController(
    IEngineVariant variant,
    Func<ClientCommandType, string, CancellationToken, Task> sendStringCmd,
    Func<ClientCommandType, byte[], CancellationToken, Task> sendCommand,
    ILogger? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <summary>Delay between spawn and sendents on branches without a terminal signonnum (Sven).</summary>
    private readonly TimeSpan _sendentsDelay = TimeSpan.FromSeconds(1);

    private bool _sentContinueLoading;
    private bool _sentSpawn;

    /// <summary>Creates the controller with a non-default sendents delay (tests).</summary>
    internal SignonController(
        IEngineVariant variant,
        Func<ClientCommandType, string, CancellationToken, Task> sendStringCmd,
        Func<ClientCommandType, byte[], CancellationToken, Task> sendCommand,
        TimeSpan sendentsDelay,
        ILogger? logger = null) : this(variant, sendStringCmd, sendCommand, logger)
    {
        _sendentsDelay = sendentsDelay;
    }

    /// <summary>Clears the per-session progress flags. Call at session start.</summary>
    public void Reset()
    {
        _sentContinueLoading = false;
        _sentSpawn = false;
    }

    /// <summary>
    /// svc_serverinfo: records the session facts and (once) requests the
    /// resource list. The worldmap CRC arrives already un-munged in
    /// <see cref="ServerInfoMessage.WorldmapCrc"/>.
    /// </summary>
    public void OnServerInfo(SessionData state, ServerInfoMessage message)
    {
        state.MaxClients = message.Data.MaxClients;
        state.PlayerNumber = message.Data.PlayerNumber;
        state.WorldmapCrc = message.WorldmapCrc;

        if (_sentContinueLoading)
            return;
        _sentContinueLoading = true;
        _logger.LogDebug("[SignOn] sending sendres");
        _ = sendStringCmd(ClientCommandType.StringCmd, "sendres", CancellationToken.None);
    }

    /// <summary>svc_deltadescription: stores the live delta table (always wins over the compiled fallback).</summary>
    public void OnDeltaDescription(SessionData state, DeltaDescriptionMessage message)
    {
        state.DeltaTables[message.Name] = message.Type;
        _logger.LogDebug("[DeltaDescription] registered \"{Name}\" with {Count} fields", message.Name, message.Type.FieldAmount);
    }

    /// <summary>
    /// svc_resourcerequest: records the spawn count and replies by echoing the
    /// server's own svc_resourcelist payload back (real-client behavior). This
    /// client holds no custom content, so with no list received yet the reply is
    /// an empty count (16-bit) — byte-aligned, unlike the server's bit-packed list.
    /// </summary>
    public void OnResourceRequest(SessionData state, ResourceRequestMessage message)
    {
        state.SpawnCount = message.SpawnCount;

        byte[] reply = state.ResourceListRawBytes.Length > 0
            ? (byte[])state.ResourceListRawBytes.Clone()
            : [0x00, 0x00];
        _logger.LogDebug("[ResourceRequest] replying with clc_resourcelist ({Length} bytes)", reply.Length);
        _ = sendCommand(ClientCommandType.ResourceList, reply, CancellationToken.None);
    }

    /// <summary>svc_resourcelist: stores the resources (and raw payload for echo-back), then requests the spawn.</summary>
    public void OnResourceList(SessionData state, ResourceListMessage message)
    {
        state.Resources = message.Resources;
        state.ResourceListRawBytes = message.RawBytes;
        TrySendSpawn(state);
    }

    /// <summary>svc_spawnbaseline: baselines complete the loading phase — request the spawn.</summary>
    public void OnSpawnBaseline(SessionData state) => TrySendSpawn(state);

    /// <summary>svc_signonnum: stage 1 completes the signon — request the entities.</summary>
    public void OnSignOnNum(SignOnNumMessage message)
    {
        if (message.Value != 1)
        {
            _logger.LogDebug("[SignOn] signon={Value} received (not 1, no action)", message.Value);
            return;
        }

        _logger.LogInformation("[SignOn] signon=1 received, signon sequence complete. Sending sendents.");
        _ = sendStringCmd(ClientCommandType.StringCmd, "sendents", CancellationToken.None);
    }

    /// <summary>
    /// Sends the final <c>spawn &lt;count&gt; &lt;mungedCrc&gt;</c> stringcmd of the
    /// sign-on sequence. The CRC is the decrypted worldmap CRC re-munged with an
    /// 8-bit key — the server unmunges with COM_UnMunge2(..., (-1 - spawncount) &amp; 0xFF)
    /// and SV_CheckMapDifferences drops the connection (as a fake overflow) if the
    /// recovered value does not match the real worldmap CRC. Fires once per session.
    /// </summary>
    private void TrySendSpawn(SessionData? state)
    {
        if (_sentSpawn || !_sentContinueLoading || state is null)
            return;
        _sentSpawn = true;

        uint spawnCount = state.SpawnCount;
        byte[] crcBytes = BitConverter.GetBytes((int)state.WorldmapCrc);
        if (!variant.PlaintextSpawnCrc)
            MungeEngine.Munge2(crcBytes, 4, (-1 - (int)spawnCount) & 0xFF);
        int spawnCrc = BitConverter.ToInt32(crcBytes, 0);
        var spawnCmd = $"spawn {spawnCount} {spawnCrc}";
        _logger.LogDebug("[SignOn] sending spawn (spawnCount={SpawnCount}, crc=0x{Crc:X8})", spawnCount, spawnCrc);
        _ = sendStringCmd(ClientCommandType.StringCmd, spawnCmd, CancellationToken.None);

        if (variant.LongFragmentFields)
        {
            // The Sven signon has no parseable terminal svc_signonnum for us; the
            // real client sends sendents right after spawn.
            _ = Task.Run(async () =>
            {
                await Task.Delay(_sendentsDelay);
                _logger.LogDebug("[SignOn] sending sendents (Sven flow)");
                await sendStringCmd(ClientCommandType.StringCmd, "sendents", CancellationToken.None);
            });
        }
    }
}
