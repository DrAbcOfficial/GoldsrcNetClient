using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Messages.Engine;
using GoldsrcNetClient.Core.Munge;
using GoldsrcNetClient.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// Protocol-internal behavior: everything the client DOES in response to a
/// parsed server message (as opposed to how it parses it — that lives in
/// <see cref="Messages.Parsing.EngineMessageParsers"/>). Signon replies
/// (sendres → spawn → sendents), userinfo adoption, cvar answers, the
/// resourcelist echo-back, and the runtime user-message registry feed.
/// </summary>
public partial class GoldsrcConnection
{
    /// <summary>Session of the most recent <see cref="ConnectAsync"/> target, if any.</summary>
    private Session? ActiveSession
        => _sessions.TryGetValue(_activeEndpoint ?? DummyEndpoint, out var session) ? session : null;

    /// <summary>Context of the most recent <see cref="ConnectAsync"/> target, if any.</summary>
    private ConnectionContext? ActiveContext => ActiveSession?.Context;

    /// <summary>
    /// Raw bit-packed payload of the most recent <c>svc_resourcelist</c> from the server
    /// (the data after the 0x2B type byte). Used to echo back identical data when the
    /// server sends a <see cref="ServerMessageType.ResourceRequest"/>.
    /// Returns an empty array if no resource list has been received yet.
    /// </summary>
    public byte[] ResourceListRawBytes => ActiveSession?.Context.ResourceListRawBytes ?? [];

    /// <summary>
    /// Subscribes the protocol-internal behaviors. Runs once in the constructor;
    /// every handler resolves the active session's context so a fresh
    /// <see cref="ConnectAsync"/> on the same connection sees fresh state
    /// (except the once-per-connection signon flags).
    /// </summary>
    private void AttachProtocolBehaviors()
    {
        Messages.Subscribe<NewUserMsgMessage>(m =>
            ActiveSession?.Pipeline.UserMessages.Register(m.Index, m.Name, m.DeclaredSize));

        Messages.Subscribe<ServerInfoMessage>(m =>
        {
            if (ActiveContext is not { } ctx) return;
            ctx.MaxClients = m.Data.MaxClients;
            ctx.PlayerNumber = m.Data.PlayerNumber;
            ctx.WorldmapCrc = m.WorldmapCrc;

            if (_sentContinueLoading) return;
            _sentContinueLoading = true;
            Logger.LogDebug("[SignOn] sending sendres");
            _ = SendStringCmdAsync(ClientCommandType.StringCmd, "sendres", CancellationToken.None);
        });

        Messages.Subscribe<DeltaDescriptionMessage>(m =>
        {
            if (ActiveContext is not { } ctx) return;
            ctx.DeltaTables[m.Name] = m.Type;
            Logger.LogDebug("[DeltaDescription] registered \"{Name}\" with {Count} fields", m.Name, m.Type.FieldAmount);
        });

        Messages.Subscribe<UpdateUserInfoMessage>(m =>
        {
            // The server broadcasts this message for every player. Only adopt the
            // server-normalized copy of OUR userinfo — other slots belong to other
            // clients and must never overwrite the local settings.
            if (ActiveContext is { PlayerNumber: var slot } && m.Slot == slot)
                UserInfo = m.UserInfo;
        });

        Messages.Subscribe<ResourceRequestMessage>(m =>
        {
            if (ActiveContext is not { } ctx) return;
            ctx.SpawnCount = m.SpawnCount;

            // Reply by echoing the server's svc_resourcelist payload back. This client
            // holds no custom content, so with no list received yet the reply is an
            // empty count (16-bit) — byte-aligned, unlike the server's bit-packed list.
            byte[] reply = ctx.ResourceListRawBytes.Length > 0 ? (byte[])ctx.ResourceListRawBytes.Clone() : [0x00, 0x00];
            Logger.LogDebug("[ResourceRequest] replying with clc_resourcelist ({Length} bytes)", reply.Length);
            _ = SendCommandAsync(ClientCommandType.ResourceList, reply, CancellationToken.None);
        });

        Messages.Subscribe<ResourceListMessage>(m =>
        {
            if (ActiveContext is not { } ctx) return;
            ctx.Resources = m.Resources;
            ctx.ResourceListRawBytes = m.RawBytes;

            // Resource parsing completes the client's loading phase — request
            // the spawn now (the engine sends it at the end of resource processing).
            TrySendSpawn();
        });

        Messages.Subscribe<SpawnBaselineMessage>(_ => TrySendSpawn());

        Messages.Subscribe<SignOnNumMessage>(m =>
        {
            if (m.Value != 1) return;
            Logger.LogInformation("[SignOn] signon=1 received, signon sequence complete. Sending sendents.");
            _ = SendStringCmdAsync(ClientCommandType.StringCmd, "sendents", CancellationToken.None);
        });

        Messages.Subscribe<SendCvarValueMessage>(m =>
        {
            Logger.LogDebug("[SendCvarValue] cvar=\"{Name}\"", m.Name);
            _ = SendCvarValueAsync(m.Name, Settings.GetDefaultCvarValue(m.Name));
        });

        Messages.Subscribe<SendCvarValue2Message>(m =>
        {
            Logger.LogDebug("[SendCvarValue2] requestId={RequestId}, cvar=\"{Name}\"", m.RequestId, m.Name);
            _ = SendCvarValue2Async((int)m.RequestId, m.Name, Settings.GetDefaultCvarValue(m.Name));
        });
    }

    /// <summary>
    /// Diagnostics: with GOLDSRC_MSGDUMP=&lt;path&gt; set, appends every message
    /// stream to a binary file (length-prefixed records). Far cheaper than
    /// console logging, so it does not destabilise the receive loop.
    /// </summary>
    private void DumpMessageStream(byte[] data)
    {
        var dumpStream = _messageDumpStream;
        if (dumpStream == null)
            return;
        try
        {
            dumpStream.Write(BitConverter.GetBytes(data.Length), 0, 4);
            dumpStream.Write(data, 0, data.Length);
            dumpStream.Flush();
        }
        catch { /* diagnostics only */ }
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
    private void TrySendSpawn()
    {
        if (_sentSpawn || !_sentContinueLoading || ActiveContext is not { } ctx)
            return;
        _sentSpawn = true;

        uint spawnCount = ctx.SpawnCount;
        byte[] crcBytes = BitConverter.GetBytes((int)ctx.WorldmapCrc);
        if (!_variant.PlaintextSpawnCrc)
            MungeEngine.Munge2(crcBytes, 4, (-1 - (int)spawnCount) & 0xFF);
        int spawnCrc = BitConverter.ToInt32(crcBytes, 0);
        var spawnCmd = $"spawn {spawnCount} {spawnCrc}";
        Logger.LogDebug("[SignOn] sending spawn (spawnCount={SpawnCount}, crc=0x{Crc:X8})", spawnCount, spawnCrc);
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
