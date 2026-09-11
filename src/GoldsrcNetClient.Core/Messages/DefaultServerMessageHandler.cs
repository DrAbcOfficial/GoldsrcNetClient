namespace GoldsrcNetClient.Core.Messages;

/// <summary>
/// Default no-op handler that delegates all messages to built-in processing.
/// Equivalent to not providing an <see cref="IServerMessageHandler"/> to the connection.
/// </summary>
public sealed class DefaultServerMessageHandler : IServerMessageHandler
{
    /// <inheritdoc />
    public bool HandleMessage(GoldsrcNetClient.Core.Network.GoldsrcConnection connection, byte messageType, MessageReader reader) => false;
}
