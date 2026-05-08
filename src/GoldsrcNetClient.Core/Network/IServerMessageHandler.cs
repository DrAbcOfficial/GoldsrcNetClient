using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// Dependency injection interface for custom server message parsing.
/// Implement this to override or extend how individual <see cref="ServerMessageType"/>
/// values (including user-registered messages at or above <see cref="ServerMessageType.UserMessageStart"/>)
/// are processed during the connected session.
/// </summary>
/// <remarks>
/// <para>Register your implementation with the <see cref="GoldsrcConnection"/> constructor.
/// For each message byte in a connected packet, the handler is called <em>before</em> built-in
/// processing. Return <c>true</c> to consume the message (built-in logic is skipped);
/// return <c>false</c> to fall through to the default parser.</para>
///
/// <para>The handler must advance <see cref="MessageReader.Offset"/> past all bytes consumed
/// from the message. Use the reader's methods (e.g. <see cref="MessageReader.ReadString"/>,
/// <see cref="MessageReader.ReadUInt32"/>) or read raw bytes via
/// <see cref="MessageReader.Data"/>.</para>
///
/// <code>
/// public class MyHandler : IServerMessageHandler
/// {
///     public bool HandleMessage(GoldsrcConnection conn, byte messageType, MessageReader reader)
///     {
///         if (messageType == (byte)ServerMessageType.Print)
///         {
///             string msg = reader.ReadString();
///             Console.WriteLine($"Server: {msg}");
///             return true;
///         }
///         // Handle user messages >= 0x40
///         if (messageType >= (byte)ServerMessageType.UserMessageStart)
///         {
///             // Custom parsing with reader...
///             return true;
///         }
///         return false; // let default processing handle it
///     }
/// }
/// </code>
/// </remarks>
public interface IServerMessageHandler
{
    /// <summary>
    /// Called for each server message in a connected packet.
    /// </summary>
    /// <param name="connection">The connection that received the message. Provides access to
    /// <see cref="GoldsrcConnection.UserInfo"/>, session state, and send methods.</param>
    /// <param name="messageType">The raw message type byte. Known types correspond to
    /// <see cref="ServerMessageType"/> values; values at or above <see cref="ServerMessageType.UserMessageStart"/>
    /// (0x40) are server-registered user messages.</param>
    /// <param name="reader">A <see cref="MessageReader"/> positioned immediately after the type byte.
    /// Advance <see cref="MessageReader.Offset"/> past all bytes consumed — the connection
    /// will use the updated position for the next message.</param>
    /// <returns><c>true</c> if the message was consumed; <c>false</c> to let the built-in parser handle it.</returns>
    bool HandleMessage(GoldsrcConnection connection, byte messageType, MessageReader reader);
}

/// <summary>
/// Default no-op handler that delegates all messages to built-in processing.
/// Equivalent to not providing an <see cref="IServerMessageHandler"/> to the connection.
/// </summary>
public sealed class DefaultServerMessageHandler : IServerMessageHandler
{
    /// <inheritdoc />
    public bool HandleMessage(GoldsrcConnection connection, byte messageType, MessageReader reader) => false;
}
