using CliFx.Infrastructure;
using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Cli;

/// <summary>
/// CLI server message handler. In debug mode, traces every engine message the
/// built-in parser receives. Disconnects, console printing, and gameplay replies
/// (resources, cvars) are handled by the built-in Core processing and surfaced
/// through the connection's events.
/// </summary>
/// <param name="console">CLI console for output.</param>
/// <param name="debug">Whether debug output is enabled.</param>
public sealed class CliServerMessageHandler(IConsole console, bool debug) : IServerMessageHandler
{
    /// <inheritdoc />
    public bool HandleMessage(GoldsrcConnection connection, byte messageType, ref BufferReader reader)
    {
        if (!debug)
            return false;

        var typeName = Enum.IsDefined(typeof(ServerMessageType), messageType)
            ? ((ServerMessageType)messageType).ToString()
            : $"0x{messageType:X2}";
        console.Output.WriteLine($"[RECV] {typeName} ({reader.Remaining} bytes)");
        return false;
    }
}
