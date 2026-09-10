using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;
using GoldsrcNetClient.Core.Game;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.SteamProvider;
using Microsoft.Extensions.Logging;
using QRCoder;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoldsrcNetClient.Cli;

[Command("connect", Description = "Connects to a GoldSrc server, shows console/chat messages, and sends chat or commands from the input shell.")]
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

    [CommandOption("game", 'g', Description = "Game profile: hl, cstrike, czero, svencoop, or any registered id. Default: auto-detected from --appid.")]
    public string? Game { get; set; }

    [CommandOption("appid", Description = "Steam AppId for authentication. Default: 70 (Half-Life). Also selects the game profile when --game is omitted.")]
    public uint AppId { get; set; } = 70;

    [CommandOption("name", 'n', Description = "Player name used in userinfo. Default: GoldsrcNetClient.")]
    public string? PlayerName { get; set; }

    [CommandOption("timeout", 't', Description = "Connection timeout in seconds. Default: 5.")]
    public int TimeoutSeconds { get; set; } = 5;

    [CommandOption("reconnect", 'r', Description = "Automatically reconnect after a disconnect. Value = seconds to wait between attempts (0 = exit on disconnect). Default: 0.")]
    public int ReconnectDelaySeconds { get; set; } = 0;

    [CommandOption("userinfo", 'u', Description = "UserInfo key-value pairs: key1=val1&key2=val2... (e.g. name=Player&rate=25000).")]
    public string? UserInfoRaw { get; set; }

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

        // Resolve the game profile (drives message handling, userinfo, and the AppId
        // used for login) — this is the per-game extension point. An explicit --game
        // determines both profile and AppId; otherwise AppId picks the profile.
        var profile = GameLoginProviders.Resolve(Game, AppId);
        uint loginAppId = Game != null ? profile.AppId : AppId;

        console.Output.WriteLine($"Game profile: {profile.DisplayName} (id={profile.Id}, appid={loginAppId})");

        if (UseSteam)
        {
            console.Output.WriteLine($"Initializing Steam (AppID {loginAppId})...");
            var steamAuth = new SteamNetAuthProvider(loginAppId);
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
            }
        }
        else if (UseSteamKit)
        {
            console.Output.WriteLine($"Initializing SteamKit2 (AppID {loginAppId})...");
            var steamKitAuth = new SteamKitAuthProvider();
            authDisposable = steamKitAuth;

            try
            {
                console.Output.WriteLine("Connecting to Steam...");
                await steamKitAuth.ConnectAsync();
                console.Output.WriteLine("Connected to Steam. Starting QR code login...");
                var qrUrl = await steamKitAuth.BeginQrLoginAsync();
                console.Output.WriteLine(RenderQrCode(qrUrl));
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
            }
        }
        else
        {
            if (Debug)
                console.Error.WriteLine("[DEBUG] Steam auth not enabled, using basic auth (raw='steam').");
        }

        console.Output.WriteLine($"Connecting to {Host}:{Port} (timeout: {TimeoutSeconds}s)...");

        var logger = new ConsoleLogger(console, Debug);
        var gameHandler = profile.CreateMessageHandler();
        if (gameHandler is GameMessageHandler gmh)
        {
            gmh.OnRawUserMessage += raw =>
            {
                if (Debug)
                    console.Error.WriteLine($"[UserMsg] {raw.Name} ({raw.Data.Length} bytes) {Convert.ToHexString(raw.Data.AsSpan(0, Math.Min(raw.Data.Length, 24)).ToArray())}");
            };
        }

        var exitCts = new CancellationTokenSource();
        console.RegisterCancellationHandler().Register(() =>
        {
            if (Debug)
                console.Error.WriteLine("[DEBUG] Ctrl+C received, cancelling...");
            exitCts.Cancel();
        });

        while (!exitCts.IsCancellationRequested)
        {
            await RunSessionAsync(console, logger, gameHandler, authProvider, loginAppId, exitCts);

            if (ReconnectDelaySeconds <= 0 || exitCts.IsCancellationRequested)
                break;

            console.Output.WriteLine($"Reconnecting in {ReconnectDelaySeconds}s (Ctrl+C to stop)...");
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(ReconnectDelaySeconds), exitCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        console.Output.WriteLine("Disconnected.");
        authDisposable?.Dispose();
    }

    /// <summary>Runs one connect + interactive session. Exits when the session ends or
    /// <paramref name="exitCts"/> is cancelled (Ctrl+C).</summary>
    private async Task RunSessionAsync(
        IConsole console,
        ILogger<GoldsrcConnection> logger,
        IServerMessageHandler gameHandler,
        ISteamAuthProvider? authProvider,
        uint loginAppId,
        CancellationTokenSource exitCts)
    {
        var userCts = new CancellationTokenSource();
        var cliHandler = new CliServerMessageHandler(console, Debug, userCts);
        if (gameHandler is GameMessageHandler chain)
            chain.Next = cliHandler;

        using var connection = new GoldsrcConnection(logger, authProvider, gameHandler);

        if (!string.IsNullOrEmpty(PlayerName))
            connection.SetUserInfo("name", PlayerName);

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
                connection.SetUserInfo(pair[..eq], pair[(eq + 1)..]);
            }
        }

        connection.OnServerInfo += (conn, info) =>
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
        connection.OnConsolePrint += msg => console.Output.WriteLine($"[Server] {msg.TrimEnd('\n')}");
        connection.OnServerDisconnect += reason =>
        {
            console.Output.WriteLine($"[Disconnect] {reason}");
            userCts.Cancel();
        };
        connection.OnDataPacket += (conn, raw) =>
        {
            if (Debug)
                Emit("raw_packet", new { length = raw.Length, hex = Convert.ToHexString(raw.AsSpan(0, Math.Min(raw.Length, 128)).ToArray()) }, console);
        };

        if (gameHandler is HalfLifeMessageHandler hlHandler)
        {
            hlHandler.SayText += ev => console.Output.WriteLine($"[Chat] {ev.Message.TrimEnd('\n')}");
            hlHandler.TextMsg += ev =>
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
            hlHandler.DeathMsg += ev => console.Output.WriteLine($"[DeathMsg] killer={ev.KillerId} victim={ev.VictimId} weapon=\"{ev.WeaponName}\"");
        }

        using var exitRegistration = exitCts.Token.Register(() => userCts.Cancel());
        using var userRegistration = console.RegisterCancellationHandler().Register(() => userCts.Cancel());

        bool connected = false;
        try
        {
            using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds)))
            {
                if (Debug)
                    console.Error.WriteLine($"[DEBUG] Connecting with timeout={TimeoutSeconds}s...");

                var connectTask = connection.ConnectAsync(loginAppId, Host, Port, userCts.Token);
                var delayTask = Task.Delay(Timeout.Infinite, timeoutCts.Token);
                var completed = await Task.WhenAny(connectTask, connection.Connected, delayTask);

                if (completed == delayTask)
                {
                    userCts.Cancel();
                    console.Error.WriteLine($"Error: Connection timed out after {TimeoutSeconds} seconds.");
                }
                else if (completed == connectTask)
                {
                    await connectTask;
                }
                else
                {
                    connected = true;
                }
            }

            if (connected)
            {
                if (Debug && gameHandler is GameMessageHandler debugGmh)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(5000);
                        foreach (var (idx, msgName, msgSize) in debugGmh.Registry.Entries)
                            console.Output.WriteLine($"[UserMsgReg] {idx} (0x{idx:X2}) -> {msgName} (size={msgSize})");
                    });
                }

                RunInputShell(console, connection, userCts);
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
    }

    /// <summary>
    /// Interactive input shell. Plain text is sent as chat (<c>say &lt;text&gt;</c>);
    /// lines starting with <c>/</c> run as raw console commands on the server
    /// (<c>/say_team</c>, <c>/status</c>, ...); <c>/quit</c> leaves the session.
    /// </summary>
    private void RunInputShell(IConsole console, GoldsrcConnection client, CancellationTokenSource userCts)
    {
        console.Output.WriteLine("Connected. Chat: just type a message. Commands: /say <msg>, /say_team <msg>, /<any console command>, /quit.");
        while (!userCts.Token.IsCancellationRequested)
        {
            console.Output.Write("> ");
            console.Output.Flush();
            var readTask = console.Input.ReadLineAsync();
            try
            {
                var completed = Task.WhenAny(readTask, Task.Delay(-1, userCts.Token)).GetAwaiter().GetResult();
                if (completed != readTask || userCts.Token.IsCancellationRequested)
                    break;
                var input = readTask.GetAwaiter().GetResult();
                if (input == null)
                    break;
                if (input.Length == 0)
                    continue;

                string command;
                if (input.StartsWith('/'))
                {
                    command = input[1..];
                    if (command.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                        command.Equals("disconnect", StringComparison.OrdinalIgnoreCase))
                        break;
                }
                else
                {
                    command = $"say {input}";
                }

                client.SendStringCmdAsync(ClientCommandType.StringCmd, command, userCts.Token)
                    .GetAwaiter().GetResult();
                console.Output.WriteLine($"[Sent] {command}");
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static string RenderQrCode(string url)
    {
        using var qrGenerator = new QRCodeGenerator();
        var qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.L);
        using var qrCode = new AsciiQRCode(qrData);
        return url + Environment.NewLine + Environment.NewLine
            + "Use the Steam Mobile App to sign in via QR code:" + Environment.NewLine
            + qrCode.GetGraphic(1, drawQuietZones: false);
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
