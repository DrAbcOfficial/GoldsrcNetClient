using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Messages.Engine;
using GoldsrcNetClient.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// Protocol-internal behavior: everything the client DOES in response to a
/// parsed server message (as opposed to how it parses it — that lives in
/// <see cref="Messages.Parsing.EngineMessageParsers"/>). The signon sequence is
/// delegated to <see cref="SignonController"/>; what stays here is the
/// connection-scoped bookkeeping (userinfo adoption, cvar answers, the runtime
/// user-message registry feed) and the diagnostics dump.
/// </summary>
public partial class GoldsrcConnection
{
    /// <summary>Session of this connection, if <see cref="GoldsrcConnection.ConnectAsync"/> has built one.</summary>
    private Session? ActiveSession => _session;

    /// <summary>
    /// Raw bit-packed payload of the most recent <c>svc_resourcelist</c> from the server
    /// (the data after the 0x2B type byte). Used to echo back identical data when the
    /// server sends a <see cref="ServerMessageType.ResourceRequest"/>.
    /// Returns an empty array if no resource list has been received yet.
    /// </summary>
    public byte[] ResourceListRawBytes => ActiveSession?.Data.ResourceListRawBytes ?? [];

    /// <summary>
    /// Subscribes the protocol-internal behaviors. Runs once in the constructor;
    /// every handler resolves the active session so a fresh
    /// <see cref="ConnectAsync"/> on the same connection sees fresh state.
    /// </summary>
    private void AttachProtocolBehaviors()
    {
        Messages.Subscribe<NewUserMsgMessage>(m =>
            ActiveSession?.Pipeline.UserMessages.Register(m.Index, m.Name, m.DeclaredSize));

        Messages.Subscribe<ServerInfoMessage>(m =>
        {
            if (ActiveSession is not { } session) return;
            session.Signon.OnServerInfo(session.Data, m);
        });

        Messages.Subscribe<DeltaDescriptionMessage>(m =>
        {
            if (ActiveSession is not { } session) return;
            session.Signon.OnDeltaDescription(session.Data, m);
        });

        Messages.Subscribe<ResourceRequestMessage>(m =>
        {
            if (ActiveSession is not { } session) return;
            session.Signon.OnResourceRequest(session.Data, m);
        });

        Messages.Subscribe<ResourceListMessage>(m =>
        {
            if (ActiveSession is not { } session) return;
            session.Signon.OnResourceList(session.Data, m);
        });

        Messages.Subscribe<SpawnBaselineMessage>(_ =>
        {
            if (ActiveSession is not { } session) return;
            session.Signon.OnSpawnBaseline(session.Data);
        });

        Messages.Subscribe<SignOnNumMessage>(m => ActiveSession?.Signon.OnSignOnNum(m));

        Messages.Subscribe<UpdateUserInfoMessage>(m =>
        {
            // The server broadcasts this message for every player. Only adopt the
            // server-normalized copy of OUR userinfo — other slots belong to other
            // clients and must never overwrite the local settings.
            if (ActiveSession is { Data.PlayerNumber: var slot } && m.Slot == slot)
                UserInfo = m.UserInfo;
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
    /// Diagnostics: with <see cref="GoldsrcEngineSettings.MessageDumpPath"/> set (the
    /// factory bridges <see cref="GoldsrcClientOptions.MessageDumpPath"/> into it),
    /// appends every message stream to a binary file (length-prefixed records).
    /// Far cheaper than console logging, so it does not destabilise the receive loop.
    /// </summary>
    private void DumpMessageStream(byte[] data)
    {
        var dumpStream = GetMessageDumpStream();
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

    /// <summary>Opens the dump stream lazily from <see cref="GoldsrcEngineSettings.MessageDumpPath"/>
    /// so the factory can assign the option after construction. An open failure disables
    /// dumping for this connection instead of throwing into the receive path.</summary>
    private FileStream? GetMessageDumpStream()
    {
        if (_messageDumpStream != null)
            return _messageDumpStream;
        if (_messageDumpUnavailable)
            return null;

        var path = Settings.MessageDumpPath;
        if (string.IsNullOrEmpty(path))
        {
            _messageDumpUnavailable = true;
            return null;
        }

        try
        {
            _messageDumpStream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            return _messageDumpStream;
        }
        catch (Exception ex)
        {
            Logger.LogWarning("[Dump] could not open message dump file {Path}: {Message}", path, ex.Message);
            _messageDumpUnavailable = true;
            return null;
        }
    }
}
