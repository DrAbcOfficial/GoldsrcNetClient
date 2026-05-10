using System.Text;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Core.Game;

/// <summary>
/// Abstract base class for game-specific server message handlers.
/// Tracks <see cref="ServerMessageType.NewUserMsg"/> registrations to build
/// a user message registry, then dispatches user messages by name to
/// game-specific handler methods.
/// </summary>
/// <remarks>
/// <para>Engine messages (types below <see cref="ServerMessageType.UserMessageStart"/>)
/// fall through to built-in processing by returning <c>false</c>.</para>
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
    protected readonly UserMessageRegistry Registry = new();

    /// <summary>Raised when an unrecognized user message is received. Provides raw data for custom parsing.</summary>
    public event Action<RawUserMessage>? OnRawUserMessage;

    /// <inheritdoc />
    public bool HandleMessage(GoldsrcConnection connection, byte messageType, MessageReader reader)
    {
        if (messageType == (byte)ServerMessageType.NewUserMsg)
        {
            Registry.Register(reader);
            return false;
        }

        if (messageType >= (byte)ServerMessageType.UserMessageStart)
        {
            if (Registry.TryGetName(messageType, out var name))
            {
                if (DispatchUserMessage(connection, messageType, name!, reader))
                    return true;
            }

            var raw = new RawUserMessage(messageType, name ?? "unknown",
                reader.Data[reader.Offset..reader.Size]);
            OnRawUserMessage?.Invoke(raw);
            reader.Offset = reader.Size;
            return true;
        }

        return false;
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

    /// <summary>Reads a signed 16-bit integer from the reader.</summary>
    protected static short ReadShort(MessageReader reader)
    {
        if (reader.Remaining < 2) return 0;
        short v = BitConverter.ToInt16(reader.Data, reader.Offset);
        reader.Offset += 2;
        return v;
    }

    /// <summary>Reads a signed 32-bit integer from the reader.</summary>
    protected static int ReadInt32(MessageReader reader)
    {
        if (reader.Remaining < 4) return 0;
        int v = BitConverter.ToInt32(reader.Data, reader.Offset);
        reader.Offset += 4;
        return v;
    }

    /// <summary>Resets the message registry. Call when establishing a new connection.</summary>
    public virtual void Reset() => Registry.Clear();
}
