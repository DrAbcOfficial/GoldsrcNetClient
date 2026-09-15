using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;
using GoldsrcNetClient.Core;
using GoldsrcNetClient.Core.Game;
using GoldsrcNetClient.Core.Handshake;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Messages.Engine;
using GoldsrcNetClient.Core.Messages.Users;
using GoldsrcNetClient.SteamProvider;
using Microsoft.Extensions.DependencyInjection;
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

        // Composition root: the CLI owns a ServiceProvider and resolves the game
        // profile and connection factory from it. Registering more profiles (mods)
        // is a one-line addition here.
        using var provider = new ServiceCollection()
            .AddGoldsrcClient()
            .AddGameProfile<HalfLifeProfile>()
            .AddGameProfile<CounterStrikeProfile>()
            .AddGameProfile<ConditionZeroProfile>()
            .AddGameProfile<SvenCoopProfile>()
            .BuildServiceProvider();

        // Resolve the game profile (drives message parsing, userinfo, and the AppId
        // used for login) — this is the per-game extension point. An explicit --game
        // determines both profile and AppId; otherwise AppId picks the profile.
        var profile = provider.GetRequiredService<IGameProfileResolver>().Resolve(Game, AppId);
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

        var exitCts = new CancellationTokenSource();
        console.RegisterCancellationHandler().Register(() =>
        {
            if (Debug)
                console.Error.WriteLine("[DEBUG] Ctrl+C received, cancelling...");
            exitCts.Cancel();
        });

        while (!exitCts.IsCancellationRequested)
        {
            await RunSessionAsync(console, logger, provider.GetRequiredService<IGoldsrcConnectionFactory>(), authProvider, loginAppId, profile, exitCts);

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
        IGoldsrcConnectionFactory factory,
        ISteamAuthProvider? authProvider,
        uint loginAppId,
        IGameProfile profile,
        CancellationTokenSource exitCts)
    {
        var userCts = new CancellationTokenSource();
        using var connection = factory.Create(profile, authProvider);
        connection.UserInfo = profile.DefaultUserInfo;

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

        connection.Subscribe<ServerInfoMessage>(info =>
        {
            unsafe
            {
                var d = info.Data;
                console.Output.WriteLine("─────────────────────────────────────");
                console.Output.WriteLine($"  Protocol:      {d.ProtocolVersion}");
                console.Output.WriteLine($"  Max Clients:   {d.MaxClients}");
                console.Output.WriteLine($"  Player Slot:   {d.PlayerNumber}");
                console.Output.WriteLine($"  Spawn Count:   {d.SpawnCount}");
                console.Output.WriteLine($"  Worldmap CRC:  0x{d.Munge3WorldmapCrc:X8} (encrypted)");
                console.Output.WriteLine($"  ClientDLL MD5: {new ReadOnlySpan<byte>(d.Md5ClientDll, 16).ToHexPreview(16)}");
                console.Output.WriteLine("─────────────────────────────────────");
            }
        });
        connection.Subscribe<PrintMessage>(m => console.Output.WriteLine($"[Server] {m.Text.TrimEnd('\n')}"));
        connection.Subscribe<CenterPrintMessage>(m => console.Output.WriteLine($"[CenterPrint] {m.Text}"));
        connection.Subscribe<DisconnectMessage>(m =>
        {
            console.Output.WriteLine($"[Disconnect] {m.Reason}");
            userCts.Cancel();
        });
        if (Debug)
            connection.Messages.SubscribeAll(m => Emit("message", new { type = m.GetType().Name }, console));

        // Typed message subscriptions replace the old per-handler events: the CLI
        // reacts to the message types it cares about, whatever the game profile.
        connection.Subscribe<SayTextMessage>(m => console.Output.WriteLine($"[Chat] {m.Message.TrimEnd('\n')}"));
        connection.Subscribe<TextMsgMessage>(m =>
        {
            var dest = m.MsgDest switch
            {
                1 => "console",
                2 => "chat",
                3 => "center",
                4 => "centernostay",
                _ => $"?{m.MsgDest}"
            };
            console.Output.WriteLine($"[TextMsg] ({dest}) {m.Message}");
        });
        connection.Subscribe<DeathMsgMessage>(m =>
            console.Output.WriteLine($"[DeathMsg] killer={m.KillerId} victim={m.VictimId} weapon=\"{m.WeaponName}\""));
        connection.Subscribe<ScTextMsgMessage>(m => console.Output.WriteLine($"[SC TextMsg] ({m.MsgDest}) {m.Message}"));
        SubscribeSvenCoopMessages(connection, console);
        if (Debug)
        {
            connection.Subscribe<NewUserMsgMessage>(m =>
                console.Output.WriteLine($"[UserMsgReg] {m.Index} (0x{m.Index:X2}) -> {m.Name} (size={m.DeclaredSize})"));
            connection.Messages.Subscribe<RawUserMessage>(m =>
                console.Error.WriteLine($"[UserMsg] {m.Name} ({m.Data.Length} bytes) {m.Data.AsSpan().ToHexPreview(24)}"));
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
                RunInputShell(console, connection, userCts);
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

    /// <summary>Subscribes the Sven Co-op message types the CLI prints.</summary>
    private static void SubscribeSvenCoopMessages(GoldsrcConnection connection, IConsole console)
    {
        connection.Subscribe<ScServerNameMessage>(m => console.Output.WriteLine($"[SC] ServerName: {m.ServerName}"));
        connection.Subscribe<ScServerVersionMessage>(m => console.Output.WriteLine($"[SC] ServerVer: {m.Version}"));
        connection.Subscribe<ScServerBuildMessage>(m => console.Output.WriteLine($"[SC] ServerBuild: {m.Build}"));
        connection.Subscribe<ScNextMapMessage>(m => console.Output.WriteLine($"[SC] NextMap: {m.MapName}"));
        connection.Subscribe<ScMotdMessage>(m => console.Output.WriteLine($"[SC] MOTD (final={m.IsFinal}): {m.Text}"));
        connection.Subscribe<ScTeamNamesMessage>(m => console.Output.WriteLine($"[SC] TeamNames: {m.Teams.Length} teams"));
        connection.Subscribe<ScTeamScoreMessage>(m => console.Output.WriteLine($"[SC] TeamScore: {m.TeamName} = {m.Score}/{m.Score2}"));
        connection.Subscribe<ScScoreInfoMessage>(m => console.Output.WriteLine($"[SC] ScoreInfo: player={m.PlayerIndex} score={m.Score}"));
        connection.Subscribe<ScMapListMessage>(m => console.Output.WriteLine(m.IsClose
            ? "[SC] MapList: close"
            : m.IsReset
                ? $"[SC] MapList: reset ({m.TotalMaps} maps)"
                : $"[SC] MapList: update [{m.StartIndex}..{m.EndIndex}): {string.Join(", ", m.MapNames)}"));
        connection.Subscribe<ScVoteMenuMessage>(m => console.Output.WriteLine($"[SC] VoteMenu: id={m.VoteId} \"{m.Question}\" [{m.YesLabel}] vs [{m.NoLabel}]"));
        connection.Subscribe<ScClExtrasInfoMessage>(m => console.Output.WriteLine($"[SC] ClExtrasInfo: plain={m.PlainLength} iv={m.Iv.Length} enc={m.EncryptedData.Length} digest={m.EncryptedDigest.Length}"));
        connection.Subscribe<ScClServerInfoMessage>(m => console.Output.WriteLine($"[SC] ClServerInfo: flag={m.Flag} num={m.Value} key={m.Key}"));
        connection.Subscribe<ScCdAudioMessage>(m => console.Output.WriteLine($"[SC] CdAudio: track={m.Track}"));
        connection.Subscribe<ScPlaylistMessage>(m => console.Output.WriteLine($"[SC] Playlist: {m.Playlist}"));
        connection.Subscribe<ScTimeEndMessage>(m => console.Output.WriteLine($"[SC] TimeEnd: {m.Seconds}"));
        connection.Subscribe<ScOnTankMessage>(m => console.Output.WriteLine($"[SC] OnTank: {m.OnTank}"));
        connection.Subscribe<ScViewModeMessage>(m => console.Output.WriteLine($"[SC] ViewMode: {(m.ThirdPerson ? "thirdperson" : "firstperson")}"));
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
