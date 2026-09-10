using CliFx.Infrastructure;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Cli;

/// <summary>
/// CLI server message handler: reports server-initiated disconnects and, in debug
/// mode, traces every received engine message. Gameplay replies (resources, cvars)
/// and console printing are handled by the built-in Core processing.
/// </summary>
public sealed class CliServerMessageHandler : IServerMessageHandler
{
    private readonly IConsole _console;
    private readonly bool _debug;
    private readonly CancellationTokenSource _disconnectCts;

    /// <summary>
    /// Creates the CLI handler.
    /// </summary>
    /// <param name="console">CLI console for output.</param>
    /// <param name="debug">Whether debug output is enabled.</param>
    /// <param name="disconnectCts">Cancelled when SVC_DISCONNECT is received to terminate the session.</param>
    public CliServerMessageHandler(IConsole console, bool debug, CancellationTokenSource disconnectCts)
    {
        _console = console;
        _debug = debug;
        _disconnectCts = disconnectCts;
    }

    /// <inheritdoc />
    public bool HandleMessage(GoldsrcConnection connection, byte messageType, MessageReader reader)
    {
        switch ((ServerMessageType)messageType)
        {
            case ServerMessageType.Disconnect:
                string reason = reader.ReadString();
                _console.Output.WriteLine($"Disconnected by server: {reason}");
                _disconnectCts.Cancel();
                return true;

            case ServerMessageType.CenterPrint:
                string center = reader.ReadString();
                _console.Output.WriteLine($"[CenterPrint] {center}");
                return true;

            default:
                if (_debug)
                {
                    var typeName = Enum.IsDefined(typeof(ServerMessageType), messageType)
                        ? ((ServerMessageType)messageType).ToString()
                        : $"0x{messageType:X2}";
                    _console.Output.WriteLine($"[RECV] {typeName} ({reader.Remaining} bytes)");
                }
                return false;
        }
    }
}
