using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace GoldsrcNetClient.Core.Game;

/// <summary>
/// Abstract base class for game-specific server message handlers.
/// Tracks <see cref="ServerMessageType.NewUserMsg"/> registrations to build
/// a user message registry, then dispatches user messages by name to
/// game-specific handler methods.
/// </summary>
/// <remarks>
/// <para>Engine messages (types below <see cref="ServerMessageType.UserMessageStart"/>)
/// fall through to <see cref="Next"/> (if set) then to built-in processing.</para>
///
/// <para>User messages (types at or above <see cref="ServerMessageType.UserMessageStart"/>)
/// are dispatched by name. If the handler recognizes the message name, it parses and
/// consumes it. Otherwise, a <see cref="OnRawUserMessage"/> event is raised with the
/// raw data for custom parsing.</para>
///
/// <para>To use:
/// <code>
/// var handler = new CounterStrikeMessageHandler();
/// handler.Money += (amount, flashes) => Console.WriteLine($"Money: ${amount}");
/// var conn = new GoldsrcConnection(logger, authProvider, handler);
/// await conn.ConnectAsync("127.0.0.1", 27015);
/// </code>
/// </para>
/// </remarks>
public abstract class GameMessageHandler : IServerMessageHandler
{
    /// <summary>Registry of user message indices mapped to their names.</summary>
    public readonly UserMessageRegistry Registry = new();

    /// <summary>
    /// Optional next handler in the chain. Called for engine messages that this
    /// handler does not consume (returns <c>false</c> from <see cref="HandleMessage"/>).
    /// Useful when combining game-specific user message handling with CLI interaction
    /// (e.g. replying to <see cref="ServerMessageType.ResourceRequest"/>).
    /// </summary>
    public IServerMessageHandler? Next { get; set; }

    /// <summary>Raised when an unrecognized user message is received. Provides raw data for custom parsing.</summary>
    public event Action<RawUserMessage>? OnRawUserMessage;

    /// <inheritdoc />
    public bool HandleMessage(GoldsrcConnection connection, byte messageType, MessageReader reader)
    {
        if (messageType == (byte)ServerMessageType.NewUserMsg)
        {
            Registry.Register(reader);
            return true;
        }

        if (messageType >= (byte)ServerMessageType.UserMessageStart)
        {
            Registry.TryGetName(messageType, out var name);

            if (Registry.TryGetSize(messageType, out var fixedSize))
            {
                // Fixed-size registration: the payload directly follows the index byte.
                DispatchUserMessageRange(connection, messageType, name, reader, fixedSize, out var handled);
                if (!handled)
                    LogUnknownUserMessage(connection, messageType, name);
                return true;
            }

            int payloadLen;
            if (Registry.TryGetDeclaredSize(messageType, out var declared) && declared == 0)
            {
                // Zero-size registration: index byte only — no length byte, no payload.
                payloadLen = 0;
            }
            else
            {
                // Variable-length registration (declared 255) carries a 16-bit
                // little-endian length word after the index (wire-verified:
                // ServerName 7A 16 00 "Sven Co-op 5.0 server\0" — 0x0016 = 22).
                // Unregistered indices use the same framing as a best effort so
                // the surrounding stream stays in sync.
                uint lo = reader.Offset < reader.Size ? reader.Data[reader.Offset++] : 0u;
                uint hi = reader.Offset < reader.Size ? reader.Data[reader.Offset++] : 0u;
                payloadLen = (int)(lo | (hi << 8));
            }

            DispatchUserMessageRange(connection, messageType, name, reader, payloadLen, out var handledVariable);
            if (!handledVariable)
                LogUnknownUserMessage(connection, messageType, name);
            return true;
        }

        if (Next != null && Next.HandleMessage(connection, messageType, reader))
            return true;

        return false;
    }

    /// <summary>
    /// Dispatches the next <paramref name="payloadLen"/> bytes of the stream as one
    /// user message over a bounded reader, then consumes exactly those bytes. The
    /// bounded view guarantees a mis-parsing handler can never desynchronise the
    /// surrounding message stream.
    /// </summary>
    private void DispatchUserMessageRange(
        GoldsrcConnection connection,
        byte index,
        string? name,
        MessageReader reader,
        int payloadLen,
        out bool handled)
    {
        int payloadEnd = Math.Min(reader.Offset + payloadLen, reader.Size);
        var bounded = new MessageReader(reader.Data[reader.Offset..payloadEnd]);

        handled = name != null && DispatchUserMessage(connection, index, name, bounded);
        if (!handled)
        {
            var raw = new RawUserMessage(index, name ?? "unknown", bounded.Data[bounded.Offset..bounded.Size].ToArray());
            OnRawUserMessage?.Invoke(raw);
        }

        reader.Offset = payloadEnd;
    }

    private static void LogUnknownUserMessage(GoldsrcConnection connection, byte index, string? name)
    {
        connection.Logger.LogDebug("[UserMsg] {Name} (0x{Index:X2}) not handled by game handler", name ?? "unknown", index);
    }

    /// <summary>
    /// Dispatches a user message by name. Override this to handle game-specific messages.
    /// The base implementation returns <c>false</c> for all messages.
    /// </summary>
    /// <returns><c>true</c> if the message was consumed; <c>false</c> to raise <see cref="OnRawUserMessage"/>.</returns>
    protected virtual bool DispatchUserMessage(GoldsrcConnection connection, byte index, string name, MessageReader reader) => false;

    /// <summary>Reads a GoldSrc coordinate (16-bit signed fixed-point /8).</summary>
    protected static float ReadCoord(MessageReader reader)
    {
        if (reader.Remaining < 2) return 0f;
        short raw = BitConverter.ToInt16(reader.Data, reader.Offset);
        reader.Offset += 2;
        return raw / 8.0f;
    }

    /// <summary>Reads a signed 16-bit integer from the reader (0 on overflow).</summary>
    protected static short ReadShort(MessageReader reader)
    {
        reader.ReadInt16(out short value);
        return value;
    }

    /// <summary>Reads a signed 32-bit integer from the reader (0 on overflow).</summary>
    protected static int ReadInt32(MessageReader reader)
    {
        reader.ReadInt32(out int value);
        return value;
    }

    /// <summary>Resets the message registry. Call when establishing a new connection.</summary>
    public virtual void Reset() => Registry.Clear();
}
