using System.Collections.Concurrent;
using GoldsrcNetClient.Core.Game;
using GoldsrcNetClient.Core.Messages;
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

public sealed class ConnectionManager : IDisposable
{
    private readonly ConcurrentQueue<string> _outputQueue = new();
    private readonly ConcurrentQueue<ConnectionState> _stateQueue = new();
    private GoldsrcConnection? _connection;
    private HalfLifeMessageHandler? _gameHandler;
    private CancellationTokenSource? _cts;
    private ISteamAuthProvider? _authProvider;
    private Task? _connectTask;
    private volatile ConnectionState _state = ConnectionState.Disconnected;
    private ServerConfig? _currentConfig;

    public ConnectionState State => _state;
    public ServerConfig? CurrentConfig => _currentConfig;
    public GoldsrcConnection? Connection => _connection;

    public bool TryDequeueOutput(out string? message) => _outputQueue.TryDequeue(out message);
    public bool TryDequeueState(out ConnectionState state) => _stateQueue.TryDequeue(out state);

    private void SetState(ConnectionState newState)
    {
        if (_state == newState) return;
        _state = newState;
        _stateQueue.Enqueue(newState);
        GlobalLog.Write($"[{DateTime.Now:HH:mm:ss}] State: {newState}");
    }

    private void Emit(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _outputQueue.Enqueue(entry);
        GlobalLog.Write(entry);
    }

    public async Task ConnectAsync(ServerConfig config, ISteamAuthProvider? authProvider, string userInfo)
    {
        await DisconnectAsync();

        _currentConfig = config;
        _authProvider = authProvider;
        _cts = new CancellationTokenSource();
        SetState(ConnectionState.Connecting);

        _gameHandler = config.AppId switch
        {
            10 => new CounterStrikeMessageHandler(),
            225840 => new SvenCoopMessageHandler(),
            _ => new HalfLifeMessageHandler(),
        };

        _gameHandler.SayText += ev => Emit($"[Say] Player #{ev.SenderId}: {ev.Message}");
        _gameHandler.TextMsg += ev => Emit($"[TextMsg] {ev.Message}");
        _gameHandler.HudText += ev => Emit($"[HudText] {ev.TextCode}");

        if (_gameHandler is CounterStrikeMessageHandler cs)
        {
            cs.HudTextArgs += ev => Emit($"[HudTextArgs] {ev.TextCode}: {string.Join(", ", ev.Args)}");
            cs.HudTextPro += ev => Emit($"[HudTextPro] {ev.TextCode}");
        }

        var tuiHandler = new TuiMessageHandler(this);
        _gameHandler.Next = tuiHandler;

        var logger = new GlobalLogger<GoldsrcConnection>();
        var resolvedProvider = _authProvider ?? new NoSteamAuthProvider();
        Emit($"Auth: {resolvedProvider.GetType().Name} (IsAvailable={resolvedProvider.IsAvailable})");
        _connection = new GoldsrcConnection(logger, resolvedProvider, _gameHandler);
        _connection.UserInfo = userInfo;

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
                await _connection.ConnectAsync(config.Host, config.Port, token);
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
            var data = new List<byte>();
            data.AddRange(System.Text.Encoding.UTF8.GetBytes(name));
            data.Add(0);
            data.AddRange(System.Text.Encoding.UTF8.GetBytes(value));
            data.Add(0);
            await _connection.SendCommandAsync(ClientCommandType.CvarValue, data.ToArray(), _cts?.Token ?? default);
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
            var data = new List<byte>();
            data.AddRange(BitConverter.GetBytes(requestId));
            data.AddRange(System.Text.Encoding.UTF8.GetBytes(name));
            data.Add(0);
            data.AddRange(System.Text.Encoding.UTF8.GetBytes(value));
            data.Add(0);
            await _connection.SendCommandAsync(ClientCommandType.CvarValue2, data.ToArray(), _cts?.Token ?? default);
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

    private sealed class TuiMessageHandler : IServerMessageHandler
    {
        private readonly ConnectionManager _manager;

        public TuiMessageHandler(ConnectionManager manager) => _manager = manager;

        public bool HandleMessage(GoldsrcConnection connection, byte messageType, MessageReader reader)
        {
            switch ((ServerMessageType)messageType)
            {
                case ServerMessageType.Print:
                    {
                        var msg = reader.ReadString();
                        _manager.Emit($"[Print] {msg}");
                        return true;
                    }
                case ServerMessageType.CenterPrint:
                    {
                        var msg = reader.ReadString();
                        _manager.Emit($"[CenterPrint] {msg}");
                        return true;
                    }
                case ServerMessageType.Exec:
                    {
                        var msg = reader.ReadString();
                        _manager.Emit($"[Exec] {msg}");
                        return true;
                    }
                case ServerMessageType.Disconnect:
                    {
                        var reason = reader.ReadString();
                        _manager.HandleDisconnect(reason);
                        reader.Offset = reader.Size;
                        return true;
                    }
                case ServerMessageType.SendCvarValue:
                    {
                        var cvarName = reader.ReadString();
                        var value = connection.Settings.GetDefaultCvarValue(cvarName);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await connection.SendCommandAsync(
                                    ClientCommandType.CvarValue,
                                    [.. System.Text.Encoding.UTF8.GetBytes(cvarName), 0, .. System.Text.Encoding.UTF8.GetBytes(value), 0]);
                            }
                            catch { }
                        });
                        return true;
                    }
                case ServerMessageType.SendCvarValue2:
                    {
                        if (reader.Remaining < 4) return true;
                        var requestId = BitConverter.ToInt32(reader.Data, reader.Offset);
                        reader.Offset += 4;
                        var cvarName = reader.ReadString();
                        var value = connection.Settings.GetDefaultCvarValue(cvarName);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var data = new List<byte>();
                                data.AddRange(BitConverter.GetBytes(requestId));
                                data.AddRange(System.Text.Encoding.UTF8.GetBytes(cvarName));
                                data.Add(0);
                                data.AddRange(System.Text.Encoding.UTF8.GetBytes(value));
                                data.Add(0);
                                await connection.SendCommandAsync(ClientCommandType.CvarValue2, data.ToArray());
                            }
                            catch { }
                        });
                        return true;
                    }
                case ServerMessageType.ResourceRequest:
                    {
                        if (reader.Remaining >= 8)
                        {
                            reader.Offset += 4;
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await connection.SendCommandAsync(ClientCommandType.ResourceList, [0]);
                                }
                                catch { }
                            });
                        }
                        return true;
                    }
            }
            return false;
        }
    }
}
