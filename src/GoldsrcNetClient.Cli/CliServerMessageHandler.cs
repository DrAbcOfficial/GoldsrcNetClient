using CliFx.Infrastructure;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.Core.Util;

namespace GoldsrcNetClient.Cli;

/// <summary>
/// CLI server message handler: prints notable messages, responds to server requests
/// with generated data, and exits on disconnect.
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
                HandleDisconnect(reader);
                return true;

            case ServerMessageType.CenterPrint:
                HandleCenterPrint(reader);
                return true;

            case ServerMessageType.Print:
                HandlePrint(reader);
                return true;

            case ServerMessageType.ResourceRequest:
                HandleResourceRequest(connection, reader);
                return true;

            case ServerMessageType.SendCvarValue:
                HandleSendCvarValue(connection, reader);
                return true;

            case ServerMessageType.SendCvarValue2:
                HandleSendCvarValue2(connection, reader);
                return true;

            default:
                if (!_debug)
                    return false;

                var typeName = Enum.IsDefined(typeof(ServerMessageType), messageType)
                    ? ((ServerMessageType)messageType).ToString()
                    : $"0x{messageType:X2}";
                _console.Output.WriteLine($"[RECV] {typeName} ({reader.Remaining} bytes)");
                return false;
        }
    }

    private void HandleDisconnect(MessageReader reader)
    {
        string reason = reader.ReadString();
        _console.Output.WriteLine($"Disconnected by server: {reason}");
        _disconnectCts.Cancel();
    }

    private void HandleCenterPrint(MessageReader reader)
    {
        string msg = reader.ReadString();
        _console.Output.WriteLine($"[CenterPrint] {msg}");
    }

    private void HandlePrint(MessageReader reader)
    {
        string msg = reader.ReadString();
        _console.Output.WriteLine($"[Server] {msg}");
    }

    private void HandleResourceRequest(GoldsrcConnection connection, MessageReader reader)
    {
        uint spawnCount = reader.ReadUInt32();
        reader.Offset += 4; // skip unknown second long

        FireAndForget(SendResourceListReply(connection));
        if (_debug)
            _console.Output.WriteLine($"[ResourceRequest] spawnCount={spawnCount}, sent empty resource list");
    }

    private void HandleSendCvarValue(GoldsrcConnection connection, MessageReader reader)
    {
        string cvarName = reader.ReadString();
        string randomValue = GenerateRandomCvarValue(cvarName);
        FireAndForget(SendCvarValueReply(connection, cvarName, randomValue));
        if (_debug)
            _console.Output.WriteLine($"[SendCvarValue] cvar=\"{cvarName}\", replied with \"{randomValue}\"");
    }

    private void HandleSendCvarValue2(GoldsrcConnection connection, MessageReader reader)
    {
        uint requestId = reader.ReadUInt32();
        string cvarName = reader.ReadString();
        string randomValue = GenerateRandomCvarValue(cvarName);
        FireAndForget(SendCvarValue2Reply(connection, requestId, cvarName, randomValue));
        if (_debug)
            _console.Output.WriteLine($"[SendCvarValue2] requestId={requestId}, cvar=\"{cvarName}\", replied with \"{randomValue}\"");
    }

    private static string GenerateRandomCvarValue(string name)
    {
        var rng = Random.Shared;
        return name.ToLowerInvariant() switch
        {
            "cl_lc" or "cl_lw" or "cl_updaterate" => "1",
            "rate" => rng.Next(20000, 100000).ToString(),
            "name" => "GoldsrcNetClient",
            "topcolor" or "bottomcolor" => rng.Next(0, 256).ToString(),
            "model" => "gordon",
            "_cl_autowepswitch" => "1",
            _ => rng.Next(0, 1000).ToString()
        };
    }

    private static async Task SendResourceListReply(GoldsrcConnection connection)
    {
        var data = new byte[2];
        int bitIdx = 0;
        BitWriter.WriteBits(0u, 12, data, ref bitIdx, 2);
        BitWriter.WriteBits(0u, 1, data, ref bitIdx, 2);
        await connection.SendCommandAsync(ClientCommandType.ResourceList, data);
    }

    private static async Task SendCvarValueReply(GoldsrcConnection connection, string name, string value)
    {
        var data = new List<byte>();
        MessageWriter.WriteString(data, name);
        MessageWriter.WriteString(data, value);
        await connection.SendCommandAsync(ClientCommandType.CvarValue, data.ToArray());
    }

    private static async Task SendCvarValue2Reply(GoldsrcConnection connection, uint requestId, string name, string value)
    {
        var data = new List<byte>();
        MessageWriter.WriteUInt32(data, requestId);
        MessageWriter.WriteString(data, name);
        MessageWriter.WriteString(data, value);
        await connection.SendCommandAsync(ClientCommandType.CvarValue2, data.ToArray());
    }

    private static void FireAndForget(Task task)
    {
        _ = task;
    }
}
