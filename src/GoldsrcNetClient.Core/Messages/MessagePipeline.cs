using GoldsrcNetClient.Core.Game;
using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Messages.Parsing;
using GoldsrcNetClient.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoldsrcNetClient.Core.Messages;

/// <summary>
/// Parses one connected (decrypted, reassembled, BZ2-decompressed) message
/// stream into typed <see cref="IServerMessage"/> records and publishes them to
/// the <see cref="MessageHub"/>. Engine messages dispatch through a 256-slot
/// parser table; user messages are framed by their runtime registration
/// (<see cref="UserMessageRegistry"/>) and parsed by name through the game
/// profile's parsers.
/// </summary>
/// <remarks>
/// <para>
/// Error semantics mirror the engine: an unknown engine type byte or a
/// truncated/malformed message discards the remainder of the packet — the
/// stream cannot be trusted past that point. User messages are framed first,
/// so a mis-parsing user-message parser can never desynchronise the stream.
/// </para>
/// <para>
/// User-message framing: a fixed-size registration (1–254) is the payload
/// length; a zero-size registration (0) carries no payload and no length word;
/// a variable-length registration (255) — and any unregistered index — carries
/// a 16-bit little-endian length word before the payload.
/// </para>
/// </remarks>
public sealed class MessagePipeline
{
    private readonly ParserRegistry _parsers;
    private readonly UserMessageRegistry _userMessages;
    private readonly MessageHub _hub;
    private readonly ILogger _logger;

    /// <summary>The runtime user-message registrations learned from svc_newusermsg.</summary>
    public UserMessageRegistry UserMessages => _userMessages;

    /// <summary>The hub every parsed message is published to.</summary>
    public MessageHub Hub => _hub;

    /// <summary>
    /// Creates the pipeline over a frozen parser set. The registry typically
    /// starts empty (the server registers its user messages during signon).
    /// </summary>
    public MessagePipeline(ParserRegistry parsers, UserMessageRegistry userMessages, MessageHub hub, ILogger? logger = null)
    {
        _parsers = parsers;
        _userMessages = userMessages;
        _hub = hub;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Processes one whole message stream (a connected packet payload).</summary>
    public void Process(byte[] data)
    {
        var reader = new BufferReader(data);
        while (reader.Remaining > 0)
        {
            byte type;
            try
            {
                type = reader.ReadUInt8();
            }
            catch (EndOfBufferException)
            {
                _logger.LogWarning("[Pipeline] truncated message type byte; discarding packet tail");
                return;
            }

            IServerMessage message;
            if (type >= (byte)ServerMessageType.UserMessageStart)
            {
                if (!TryFrameUserMessage(type, ref reader, out message))
                    return; // framing failed: stream desynchronised
            }
            else
            {
                var parser = _parsers.GetEngine(type);
                if (parser is null)
                {
                    _logger.LogWarning("[Pipeline] unknown engine message 0x{Type:X2} at offset {Offset}; discarding packet tail",
                        type, reader.BytePosition - 1);
                    return;
                }

                try
                {
                    message = parser(ref reader);
                }
                catch (Exception ex) when (ex is EndOfBufferException or InvalidDataException)
                {
                    _logger.LogWarning("[Pipeline] engine message 0x{Type:X2} malformed: {Message}; discarding packet tail",
                        type, ex.Message);
                    return;
                }
            }

            _hub.Publish(message);
        }
    }

    /// <summary>
    /// Parses one user message whose index byte the caller has already consumed,
    /// and publishes it. Exposed for tests that drive individual messages without
    /// building a whole packet stream.
    /// </summary>
    internal IServerMessage? ProcessUserMessage(byte index, ref BufferReader reader)
    {
        if (!TryFrameUserMessage(index, ref reader, out var message))
            return null;
        _hub.Publish(message);
        return message;
    }

    /// <summary>
    /// Frames and parses one user message. Returns false only when the framing
    /// itself was truncated (stream desynchronised).
    /// </summary>
    private bool TryFrameUserMessage(byte index, ref BufferReader reader, out IServerMessage message)
    {
        _userMessages.TryGetName(index, out var name);

        int payloadLen;
        if (_userMessages.TryGetSize(index, out var fixedSize))
        {
            // Fixed-size registration: the payload directly follows the index byte.
            payloadLen = fixedSize;
        }
        else if (_userMessages.TryGetDeclaredSize(index, out var declared) && declared == 0)
        {
            // Zero-size registration: index byte only — no length byte, no payload.
            payloadLen = 0;
        }
        else
        {
            // Variable-length registration (declared 255) carries a 16-bit LE length
            // word after the index (wire-verified: ServerName 7A 16 00 "Sven Co-op
            // 5.0 server\0" — 0x0016 = 22). Unregistered indices use the same
            // framing as a best effort so the surrounding stream stays in sync.
            try
            {
                payloadLen = reader.ReadUInt16();
            }
            catch (EndOfBufferException)
            {
                _logger.LogWarning("[Pipeline] user message 0x{Index:X2} ({Name}) truncated at the length word", index, name ?? "unknown");
                message = null!;
                return false;
            }
        }

        int take = Math.Min(payloadLen, reader.Remaining);
        var bounded = reader.Slice(take);

        if (name != null && _parsers.TryGetUser(name, out var parser))
        {
            try
            {
                message = parser(ref bounded);
                return true;
            }
            catch (Exception ex) when (ex is EndOfBufferException or InvalidDataException)
            {
                _logger.LogWarning("[Pipeline] user message {Name} (0x{Index:X2}) malformed: {Message}", name, index, ex.Message);
            }
        }

        message = new RawUserMessage(index, name ?? "unknown", bounded.RemainingSpan.ToArray());
        return true;
    }
}
