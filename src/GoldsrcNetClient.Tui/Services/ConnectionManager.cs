using GoldsrcNetClient.Core.Game;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Handshake;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.Tui.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoldsrcNetClient.Tui.Services;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}

/// <summary>
/// Owns the active <see cref="GoldsrcConnection"/> and reports progress through
/// <see cref="GlobalLog"/>; the UI polls <see cref="State"/> on its timer.
/// </summary>
public sealed class ConnectionManager : IDisposable
{
    private GoldsrcConnection? _connection;
    private HalfLifeMessageHandler? _gameHandler;    private CancellationTokenSource? _cts;
    private ISteamAuthProvider? _authProvider;
    private Task? _connectTask;
    private volatile ConnectionState _state = ConnectionState.Disconnected;
    private ServerConfig? _currentConfig;

    public ConnectionState State => _state;
    public ServerConfig? CurrentConfig => _currentConfig;
    public GoldsrcConnection? Connection => _connection;

    private void SetState(ConnectionState newState)
    {
        if (_state == newState) return;
        _state = newState;
        GlobalLog.Write($"[{DateTime.Now:HH:mm:ss}] State: {newState}");
    }

    private void Emit(string message)
    {
        GlobalLog.Write($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    public async Task ConnectAsync(ServerConfig config, ISteamAuthProvider? authProvider, string userInfo)
    {
        await DisconnectAsync();

        _currentConfig = config;
        _authProvider = authProvider;
        _cts = new CancellationTokenSource();
        SetState(ConnectionState.Connecting);

        // The game profile drives message handling, the engine-variant wire
        // dialect, and the AppId used for login — the per-game extension point.
        IGameLoginProvider profile = GameLoginProviders.GetByAppId(config.AppId) ?? GameLoginProviders.Resolve(null, config.AppId);
        IServerMessageHandler gameHandler = profile.CreateMessageHandler();
        _gameHandler = gameHandler as HalfLifeMessageHandler;

        if (_gameHandler is { } hlHandler)
        {
            hlHandler.SayText += ev => Emit($"[Say] Player #{ev.SenderId}: {ev.Message}");
            hlHandler.TextMsg += ev => Emit($"[TextMsg] {ev.Message}");
            hlHandler.HudText += ev => Emit($"[HudText] {ev.TextCode}");

            if (hlHandler is CounterStrikeMessageHandler cs)
            {
                cs.HudTextArgs += ev => Emit($"[HudTextArgs] {ev.TextCode}: {string.Join(", ", ev.Args)}");
                cs.HudTextPro += ev => Emit($"[HudTextPro] {ev.TextCode}");
            }
        }

        var logger = new GlobalLogger<GoldsrcConnection>();
        var resolvedProvider = _authProvider ?? new NoSteamAuthProvider();
        Emit($"Auth: {resolvedProvider.GetType().Name} (IsAvailable={resolvedProvider.IsAvailable})");
        _connection = new GoldsrcConnection(logger, resolvedProvider, gameHandler, profile.EngineVariant);
        _connection.UserInfo = userInfo;

        // Console output, center prints, and server-initiated disconnects are handled
        // by the built-in Core processing and surfaced here via events.
        _connection.OnConsolePrint += msg => Emit($"[Print] {msg}");
        _connection.OnCenterPrint += msg => Emit($"[CenterPrint] {msg}");
        _connection.OnServerDisconnect += HandleDisconnect;

        _connection.OnServerInfo += (conn, info) =>
        {
            Emit($"ServerInfo: protocol={info.ProtocolVersion}, maxClients={info.MaxClients}, playerSlot={info.PlayerNumber}, spawnCount={info.SpawnCount}");
        };

        var token = _cts.Token;
        _connectTask = Task.Run(async () =>
        {
            try
            {
                Emit($"Connecting to {config.Host}:{config.Port}...");
                await _connection.ConnectAsync(config.AppId, config.Host, config.Port, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Emit($"[Error] {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    SetState(ConnectionState.Disconnected);
                }
            }
        }, token);

        _ = _connection.Connected.ContinueWith(_ =>
        {
            if (!token.IsCancellationRequested)
            {
                SetState(ConnectionState.Connected);
            }
        }, TaskContinuationOptions.NotOnFaulted);

        _ = Task.Run(async () =>
        {
            try
            {
                await _connection.Connected.WaitAsync(TimeSpan.FromSeconds(10), token);
            }
            catch
            {
                if (!token.IsCancellationRequested)
                {
                    Emit("Connection timed out (no response from server)");
                    _cts?.Cancel();
                    SetState(ConnectionState.Disconnected);
                }
            }
        }, token);
    }

    public async Task DisconnectAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        if (_connectTask != null)
        {
            try { await _connectTask; }
            catch { }
            _connectTask = null;
        }

        _connection?.Dispose();
        _connection = null;
        _gameHandler = null;
        _currentConfig = null;
        SetState(ConnectionState.Disconnected);
    }

    public void HandleDisconnect(string reason)
    {
        Emit($"[Disconnect] {reason}");

        bool isMapChange = reason.Contains("shutdown", StringComparison.OrdinalIgnoreCase)
                        || reason.Contains("changing levels", StringComparison.OrdinalIgnoreCase)
                        || reason.Contains("loading", StringComparison.OrdinalIgnoreCase);

        if (isMapChange && _currentConfig != null)
        {
            SetState(ConnectionState.Reconnecting);
            var config = _currentConfig;
            var auth = _authProvider;
            var userInfo = _connection?.UserInfo ?? "";

            _ = Task.Run(async () =>
            {
                try
                {
                    Emit("Map change detected. Reconnecting in 3 seconds...");
                    await Task.Delay(3000);

                    if (_cts != null)
                    {
                        _cts.Cancel();
                        _cts.Dispose();
                    }

                    _connection?.Dispose();
                    _connection = null;

                    await ConnectAsync(config, auth, userInfo);
                }
                catch (Exception ex)
                {
                    Emit($"[Reconnect Error] {ex.Message}");
                    SetState(ConnectionState.Disconnected);
                }
            });
        }
        else
        {
            SetState(ConnectionState.Disconnected);
        }
    }

    public async Task SendStringCmdAsync(string cmd)
    {
        if (_connection == null || _state != ConnectionState.Connected) return;
        try
        {
            await _connection.SendStringCmdAsync(ClientCommandType.StringCmd, cmd, _cts?.Token ?? default);
        }
        catch (Exception ex)
        {
            Emit($"[Send Error] {ex.Message}");
        }
    }

    public async Task SendCvarValueAsync(string name, string value)
    {
        if (_connection == null || _state != ConnectionState.Connected) return;
        try
        {
            await _connection.SendCvarValueAsync(name, value);
        }
        catch (Exception ex)
        {
            Emit($"[Cvar Error] {ex.Message}");
        }
    }

    public async Task SendCvarValue2Async(int requestId, string name, string value)
    {
        if (_connection == null || _state != ConnectionState.Connected) return;
        try
        {
            await _connection.SendCvarValue2Async(requestId, name, value);
        }
        catch (Exception ex)
        {
            Emit($"[Cvar2 Error] {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _connection?.Dispose();
    }
}
