using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoldsrcNetClient.Core.Game;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace GoldsrcNetClient.Cli;

[Command("connect", Description = "Connects to a GoldSrc server and prints received messages.")]
public partial class ConnectCommand : ICommand
{
    [CommandParameter(0, Name = "host", Description = "Target server hostname or IP.")]
    public required string Host { get; set; }

    [CommandOption("port", 'p', Description = "Target server port.")]
    public int Port { get; set; } = 27015;

    [CommandOption("debug", 'd', Description = "Enable debug/verbose output.")]
    public bool Debug { get; set; }

    [CommandOption("steam", 's', Description = "Use Steam authentication (requires Steam client running).")]
    public bool UseSteam { get; set; }

    [CommandOption("steamkit", Description = "Use SteamKit2 authentication (requires Steam mobile app for QR code scan). Mutually exclusive with --steam.")]
    public bool UseSteamKit { get; set; }

    [CommandOption("appid", Description = "Steam AppId for authentication. Default: 70 (Half-Life).")]
    public uint AppId { get; set; } = 70;

    [CommandOption("timeout", 't', Description = "Connection timeout in seconds. Default: 5.")]
    public int TimeoutSeconds { get; set; } = 5;

    [CommandOption("userinfo", 'u', Description = "UserInfo key-value pairs: key1=val1&key2=val2... (e.g. name=Player&rate=25000).")]
    public string? UserInfoRaw { get; set; }

    [CommandOption("buttons", 'b', Description = "Button bitmask sent with move packets. 1=attack (respawn), 2=jump, 4=duck. Default: 0.")]
    public ushort MoveButtons { get; set; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public async ValueTask ExecuteAsync(IConsole console)
    {
        ISteamAuthProvider? authProvider = null;
        IDisposable? authDisposable = null;

        if (UseSteam && UseSteamKit)
        {
            console.Error.WriteLine("Error: --steam and --steamkit are mutually exclusive.");
            return;
        }

        if (UseSteam)
        {
            console.Output.WriteLine($"Initializing Steam (AppID {AppId})...");
            var steamAuth = new SteamNetAuthProvider(AppId);
            authDisposable = steamAuth;

            if (steamAuth.IsAvailable)
            {
                console.Output.WriteLine($"Steam authentication enabled.");
                authProvider = steamAuth;
            }
            else
            {
                console.Error.WriteLine($"Steam init failed: {steamAuth.LastError ?? "unknown error"}");
                console.Error.WriteLine("Falling back to basic auth.");
                steamAuth.Dispose();
                authDisposable = null;
            }
        }
        else if (UseSteamKit)
        {
            console.Output.WriteLine($"Initializing SteamKit2 (AppID {AppId})...");
            var steamKitAuth = new SteamKitAuthProvider(AppId);
            authDisposable = steamKitAuth;

            try
            {
                console.Output.WriteLine("Connecting to Steam...");
                await steamKitAuth.ConnectAsync();
                console.Output.WriteLine("Connected to Steam. Starting QR code login...");
                var qrDisplay = await steamKitAuth.BeginQrLoginAsync();
                console.Output.WriteLine(qrDisplay);
                console.Output.WriteLine("Waiting for authentication...");
                await steamKitAuth.WaitForLoginAsync();
                console.Output.WriteLine("SteamKit2 authentication successful.");
                authProvider = steamKitAuth;
            }
            catch (Exception ex)
            {
                console.Error.WriteLine($"SteamKit2 init failed: {ex.Message}");
                if (Debug) console.Error.WriteLine($"[DEBUG] {ex}");
                steamKitAuth.Dispose();
                authDisposable = null;
            }
        }
        else
        {
            if (Debug)
                console.Error.WriteLine("[DEBUG] Steam auth not enabled, using basic auth (raw='steam').");
        }

        console.Output.WriteLine($"Connecting to {Host}:{Port} (timeout: {TimeoutSeconds}s)...");

        var userCts = new CancellationTokenSource();
        var logger = new ConsoleLogger(console, Debug);

        var cliHandler = new CliServerMessageHandler(console, Debug, userCts);
        var gameHandler = AppId switch
        {
            10 => new CounterStrikeMessageHandler(),
            225840 => new SvenCoopMessageHandler(),
            _ => new HalfLifeMessageHandler(),
        };
        gameHandler.Next = cliHandler;
        var messageHandler = gameHandler;

        using var client = new GoldsrcConnection(logger, authProvider, messageHandler);

        client.MoveButtons = MoveButtons;

        if (!string.IsNullOrEmpty(UserInfoRaw))
        {
            foreach (var pair in UserInfoRaw.Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0 || eq >= pair.Length - 1)
                {
                    if (Debug)
                        console.Error.WriteLine($"[DEBUG] Skipping invalid userinfo pair: \"{pair}\"");
                    continue;
                }
                var key = pair[..eq];
                var value = pair[(eq + 1)..];
                client.SetUserInfo(key, value);
                if (Debug)
                    console.Error.WriteLine($"[DEBUG] Set userinfo: \\{key}\\{value}");
            }
        }

        client.OnServerInfo += (conn, info) =>
        {
            unsafe
            {
                var md5Bytes = new ReadOnlySpan<byte>(info.Md5ClientDll, 16);
                console.Output.WriteLine("─────────────────────────────────────");
                console.Output.WriteLine($"  Protocol:      {info.ProtocolVersion}");
                console.Output.WriteLine($"  Max Clients:   {info.MaxClients}");
                console.Output.WriteLine($"  Player Slot:   {info.PlayerNumber}");
                console.Output.WriteLine($"  Spawn Count:   {info.SpawnCount}");
                console.Output.WriteLine($"  Worldmap CRC:  0x{info.Munge3WorldmapCrc:X8} (encrypted)");
                console.Output.WriteLine($"  ClientDLL MD5: {Convert.ToHexString(md5Bytes)}");
                console.Output.WriteLine("─────────────────────────────────────");
            }
        };
        client.OnResourceList += (conn, resources) => Emit("resourcelist", new { count = resources.Length, resources }, console);
        client.OnDataPacket += (conn, raw) =>
        {
            if (Debug)
                Emit("raw_packet", new { length = raw.Length, hex = Convert.ToHexString(raw[..Math.Min(raw.Length, 128)]) }, console);
        };

        gameHandler.SayText += ev => console.Output.WriteLine($"[Say] player={ev.SenderId} \"{ev.Message}\"");
        gameHandler.TextMsg += ev =>
        {
            var dest = ev.MsgDest switch
            {
                1 => "console",
                2 => "chat",
                3 => "center",
                4 => "centernostay",
                _ => $"?{ev.MsgDest}"
            };
            console.Output.WriteLine($"[TextMsg] ({dest}) {ev.Message}");
        };
        gameHandler.HudText += ev =>
        {
            if (Debug)
                console.Error.WriteLine($"[HudText] code=\"{ev.TextCode}\" style={ev.Style}");
        };

        if (gameHandler is CounterStrikeMessageHandler csHandler)
        {
            csHandler.HudTextArgs += ev =>
            {
                if (Debug)
                    console.Error.WriteLine($"[HudTextArgs] code=\"{ev.TextCode}\" style={ev.Style} args=[{string.Join(", ", ev.Args)}]");
            };
            csHandler.HudTextPro += ev =>
            {
                if (Debug)
                    console.Error.WriteLine($"[HudTextPro] code=\"{ev.TextCode}\" style={ev.Style}");
            };
        }

        console.RegisterCancellationHandler().Register(() =>
        {
            if (Debug)
                console.Error.WriteLine("[DEBUG] Ctrl+C received, cancelling...");
            userCts.Cancel();
        });

        try
        {
            using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds)))
            {
                if (Debug)
                    console.Error.WriteLine($"[DEBUG] Connecting with timeout={TimeoutSeconds}s...");

                var connectTask = client.ConnectAsync(Host, Port, userCts.Token);
                var delayTask = Task.Delay(Timeout.Infinite, timeoutCts.Token);
                var completed = await Task.WhenAny(connectTask, client.Connected, delayTask);

                if (completed == delayTask)
                {
                    userCts.Cancel();
                    console.Error.WriteLine($"Error: Connection timed out after {TimeoutSeconds} seconds.");
                    return;
                }

                if (completed == connectTask)
                {
                    await connectTask;
                    return;
                }
            }

            console.Output.WriteLine("Connected. Type a command and press Enter to send (clc_stringcmd). Press Ctrl+C to disconnect.");
            while (!userCts.Token.IsCancellationRequested)
            {
                console.Output.Write("> ");
                console.Output.Flush();
                var readTask = console.Input.ReadLineAsync();
                try
                {
                    var completed = await Task.WhenAny(readTask, Task.Delay(-1, userCts.Token));
                    if (completed != readTask || userCts.Token.IsCancellationRequested)
                        break;
                    var input = await readTask;
                    if (input == null)
                        break;
                    await client.SendStringCmdAsync(ClientCommandType.StringCmd, input, userCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!userCts.IsCancellationRequested)
        {
            console.Error.WriteLine($"Error: Connection timed out after {TimeoutSeconds} seconds.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            console.Error.WriteLine($"Error: {ex.GetType().Name}: {ex.Message}");
            if (Debug)
                console.Error.WriteLine($"[DEBUG] Stack trace: {ex}");
        }

        console.Output.WriteLine("Disconnected.");
        authDisposable?.Dispose();
    }

    private void Emit(string type, object data, IConsole console)
    {
        var msg = new ProtocolMessage(type, data);
        console.Output.WriteLine(JsonSerializer.Serialize(msg, JsonOpts));
    }

    private record ProtocolMessage(string Type, object Data)
    {
        public string Timestamp { get; init; } = DateTime.UtcNow.ToString("O");
    }

    private sealed class ConsoleLogger(IConsole console, bool debug) : ILogger<GoldsrcConnection>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => debug || logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            var prefix = logLevel switch
            {
                LogLevel.Trace => "[TRCE]",
                LogLevel.Debug => "[DBG ]",
                LogLevel.Information => "[INFO]",
                LogLevel.Warning => "[WARN]",
                LogLevel.Error => "[ERRO]",
                LogLevel.Critical => "[CRIT]",
                _ => "[????]"
            };

            if (logLevel >= LogLevel.Warning)
                console.Error.WriteLine($"{prefix} {message}");
            else
                console.Output.WriteLine($"{prefix} {message}");
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
