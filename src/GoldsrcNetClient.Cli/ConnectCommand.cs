using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;

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

    [CommandOption("appid", Description = "Steam AppId for authentication. Default: 70 (Half-Life).")]
    public uint AppId { get; set; } = 70;

    [CommandOption("timeout", 't', Description = "Connection timeout in seconds. Default: 5.")]
    public int TimeoutSeconds { get; set; } = 5;

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

        if (UseSteam)
        {
            console.Output.WriteLine($"Initializing Steam (AppID {AppId})...");
            var steamAuth = new FacepunchSteamAuthProvider(AppId);
            authDisposable = steamAuth;

            if (steamAuth.IsAvailable)
            {
                var authLen = steamAuth.GetRawAuthData().Length;
                console.Output.WriteLine($"Steam authentication enabled (ticket: {authLen} bytes).");
                authProvider = steamAuth;
            }
            else
            {
                console.Error.WriteLine("Steam init failed. Falling back to basic auth.");
                steamAuth.Dispose();
                authDisposable = null;
            }
        }
        else
        {
            if (Debug)
                console.Error.WriteLine("[DEBUG] Steam auth not enabled, using basic auth (raw='steam').");
        }

        console.Output.WriteLine($"Connecting to {Host}:{Port} (timeout: {TimeoutSeconds}s)...");

        using var client = new GoldsrcConnection(authProvider);
        client.OnPrint += msg => Emit("print", new { text = msg }, console);
        client.OnServerInfo += (conn, info) => Emit("serverinfo", info, console);
        client.OnResourceList += (conn, resources) => Emit("resourcelist", new { count = resources.Length, resources }, console);
        client.OnDataPacket += (conn, raw) =>
        {
            if (Debug)
                Emit("raw_packet", new { length = raw.Length, hex = Convert.ToHexString(raw[..Math.Min(raw.Length, 128)]) }, console);
        };
        client.OnDebug += msg =>
        {
            if (Debug)
                console.Error.WriteLine($"[DEBUG] {msg}");
        };

        var userCts = new CancellationTokenSource();
        console.RegisterCancellationHandler().Register(() =>
        {
            if (Debug)
                console.Error.WriteLine("[DEBUG] Ctrl+C received, cancelling...");
            userCts.Cancel();
        });

        try
        {
            using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds)))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(userCts.Token, timeoutCts.Token))
            {
                if (Debug)
                    console.Error.WriteLine($"[DEBUG] Connecting with timeout={TimeoutSeconds}s, using linked cancellation token.");

                await client.ConnectAsync(Host, Port, linkedCts.Token);
            }

            console.Output.WriteLine("Connected. Press Ctrl+C to disconnect.");
            await Task.Delay(Timeout.Infinite, userCts.Token);
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
}
