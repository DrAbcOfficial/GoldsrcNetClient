using System.Collections.ObjectModel;
using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.SteamProvider;
using GoldsrcNetClient.Tui.Models;
using GoldsrcNetClient.Tui.Services;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace GoldsrcNetClient.Tui.Views;

/// <summary>
/// The main (and only) application view: server browser on the left, log on the
/// right, command bar at the bottom, status line on top. Replaces the old
/// tabbed Settings/Connection/Console layout.
/// </summary>
public sealed class MainView : View
{
    private readonly AppData _appData;
    private readonly ConnectionManager _connManager;
    private readonly ServerConfigStore _configStore;
    private readonly ServerBrowser _browser;
    private readonly UserInfoStore _userInfoStore;
    private readonly AppSettingsStore _settingsStore;

    private readonly ListView _serverList;
    private readonly TextView _logTv;
    private readonly Label _statusLbl;
    private readonly Button _connectBtn;
    private readonly DropDownList _cmdTypeDd;
    private readonly TextField _inputTf;
    private readonly Button _moveBtn;
    private int _selectedServerIdx = -1;
    private int _lastBrowserVersion = -1;
    private ConnectionState _lastState = ConnectionState.Disconnected;
    private string _lastStatusText = "";

    private static readonly string[] CmdTypes = ["StringCmd", "Move", "CvarValue", "CvarValue2"];

    public MainView(AppData appData, ConnectionManager connManager, ServerConfigStore configStore,
        ServerBrowser browser, UserInfoStore userInfoStore, AppSettingsStore settingsStore)
    {
        _appData = appData;
        _connManager = connManager;
        _configStore = configStore;
        _browser = browser;
        _userInfoStore = userInfoStore;
        _settingsStore = settingsStore;

        Width = Dim.Fill();
        Height = Dim.Fill();

        _statusLbl = new Label { X = 0, Y = 0, Width = Dim.Fill() };
        Add(_statusLbl);

        // ── Left: server browser ────────────────────────────────────────────
        FrameView serverFrame = new FrameView
        {
            Title = "Servers (Enter=connect, F5=refresh)",
            X = 0, Y = 1,
            Width = 55,
            Height = Dim.Fill(2)
        };
        _serverList = new ListView
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1)
        };
        _serverList.ValueChanged += (s, e) => _selectedServerIdx = _serverList.SelectedItem ?? -1;
        // Enter (default Accept binding) and double-click on a server connect to it.
        _serverList.Accepting += (s, e) =>
        {
            if (_serverList.HasFocus)
            {
                ToggleConnection();
                e.Handled = true;
            }
        };
        serverFrame.Add(_serverList);

        Button connectBtn = new() { Text = "Connect", X = 0, Y = Pos.AnchorEnd(1) };
        connectBtn.Accepting += (s, e) => ToggleConnection();
        Button refreshBtn = new() { Text = "Refresh", X = Pos.Right(connectBtn) + 1, Y = Pos.AnchorEnd(1) };
        refreshBtn.Accepting += (s, e) => _browser.RefreshAll();
        Button addBtn = new() { Text = "Add", X = Pos.Right(refreshBtn) + 1, Y = Pos.AnchorEnd(1) };
        addBtn.Accepting += (s, e) => AddServer();
        Button editBtn = new() { Text = "Edit", X = Pos.Right(addBtn) + 1, Y = Pos.AnchorEnd(1) };
        editBtn.Accepting += (s, e) => EditServer();
        Button delBtn = new() { Text = "Del", X = Pos.Right(editBtn) + 1, Y = Pos.AnchorEnd(1) };
        delBtn.Accepting += (s, e) => DeleteServer();
        _connectBtn = connectBtn;
        serverFrame.Add(connectBtn, refreshBtn, addBtn, editBtn, delBtn);
        Add(serverFrame);

        // ── Right: log ──────────────────────────────────────────────────────
        FrameView logFrame = new FrameView
        {
            Title = "Log",
            X = 55, Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(2)
        };
        _logTv = new TextView
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true
        };
        logFrame.Add(_logTv);
        Add(logFrame);

        // ── Bottom: command bar ─────────────────────────────────────────────
        View inputBar = new View
        {
            X = 0, Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1
        };

        _cmdTypeDd = new DropDownList
        {
            X = 0, Y = 0,
            Width = 12,
            Source = new ListWrapper<string>(new ObservableCollection<string>(CmdTypes)),
            ReadOnly = true,
            Value = CmdTypes[0]
        };
        _inputTf = new TextField { X = 13, Y = 0, Width = Dim.Fill(42) };

        Button sendBtn = new() { Text = "Send", X = Pos.AnchorEnd(41), Y = 0 };
        sendBtn.Accepting += (s, e) => SendCommand();

        _moveBtn = new Button { Text = "Move...", X = Pos.AnchorEnd(31), Y = 0 };
        _moveBtn.Accepting += (s, e) => OpenMoveEditor();

        Button settingsBtn = new() { Text = "Settings (F9)", X = Pos.AnchorEnd(18), Y = 0 };
        settingsBtn.Accepting += (s, e) => OpenSettings();

        _cmdTypeDd.ValueChanged += (s, e) =>
        {
            _moveBtn.Visible = _cmdTypeDd.Value == "Move";
        };

        _inputTf.KeyDown += (s, e) =>
        {
            if (e == Key.Enter)
            {
                SendCommand();
                e.Handled = true;
            }
        };

        inputBar.Add(_cmdTypeDd, _inputTf, sendBtn, _moveBtn, settingsBtn);
        Add(inputBar);

        // Hotkeys
        KeyDown += (s, e) =>
        {
            if (e == Key.F5)
            {
                _browser.RefreshAll();
                e.Handled = true;
            }
            else if (e == Key.F9)
            {
                OpenSettings();
                e.Handled = true;
            }
            else if (e == Key.F2)
            {
                ToggleConnection();
                e.Handled = true;
            }
        };

        RefreshServerList();
        _browser.RefreshAll();

        if (AppHolder.App is IApplication app)
        {
            app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
            {
                try { Tick(); } catch { }
                return true;
            });
        }
    }

    // ── UI tick: poll state, log, browser updates ────────────────────────────

    private void Tick()
    {
        FlushLog();

        if (_browser.Version != _lastBrowserVersion)
        {
            _lastBrowserVersion = _browser.Version;
            RefreshServerList();
        }

        if (_connManager.State != _lastState)
        {
            _lastState = _connManager.State;
            OnStateChanged();
        }

        UpdateStatusLine(force: false);

        // Steam API needs periodic callbacks; SteamKit pumps its own loop.
        if (_appData.SteamApiProvider is SteamNetAuthProvider steamApi)
            steamApi.PumpCallbacks();
    }

    private void FlushLog()
    {
        var sb = new System.Text.StringBuilder();
        while (GlobalLog.TryRead(out string? entry))
        {
            if (entry != null)
                sb.Append(entry).Append('\n');
        }
        if (sb.Length > 0)
        {
            _logTv.Text += sb.ToString();
            const int maxLen = 100000;
            if (_logTv.Text.Length > maxLen)
                _logTv.Text = _logTv.Text[^maxLen..];
            _logTv.MoveEnd();
        }
    }

    private void OnStateChanged()
    {
        _connectBtn.Text = _connManager.State == ConnectionState.Disconnected ? "Connect" : "Disconnect";

        if (_connManager.State == ConnectionState.Connected)
            GlobalLog.Write($"[{DateTime.Now:HH:mm:ss}] Connected. Use the command bar to send commands.");
    }

    private void UpdateStatusLine(bool force)
    {
        string state = _connManager.State switch
        {
            ConnectionState.Disconnected => "Disconnected",
            ConnectionState.Connecting => $"Connecting to {_connManager.CurrentConfig?.Host}:{_connManager.CurrentConfig?.Port}…",
            ConnectionState.Connected => $"Connected to {_connManager.CurrentConfig?.Host}:{_connManager.CurrentConfig?.Port}",
            ConnectionState.Reconnecting => "Reconnecting…",
            _ => ""
        };

        string steam = _appData.LoginMethod switch
        {
            LoginMethod.SteamApi => _appData.SteamUsername != null
                ? $"Steam: {_appData.SteamUsername}" + (_appData.SteamId.HasValue ? $" ({_appData.SteamId.Value})" : "")
                : "Steam: not initialized",
            LoginMethod.SteamKit => _appData.SteamUsername != null
                ? $"SteamKit: {_appData.SteamUsername}" + (_appData.SteamId.HasValue ? $" ({_appData.SteamId.Value})" : "")
                : "SteamKit: not logged in",
            _ => "Auth: No Steam"
        };

        string serverDetail = "";
        ServerEntry? entry = _browser.Entries.ElementAtOrDefault(_selectedServerIdx);
        if (entry != null)
        {
            string detail = entry.Info != null
                ? $"{entry.Config.Host}:{entry.Config.Port} · {entry.Info.Map} · {entry.Info.Players}/{entry.Info.MaxPlayers}"
                : $"{entry.Config.Host}:{entry.Config.Port}";
            serverDetail = $"  │  {detail}";
        }

        string text = $"State: {state}  │  {steam}{serverDetail}";
        if (force || text != _lastStatusText)
        {
            _lastStatusText = text;
            _statusLbl.Text = text;
        }
    }

    // ── Server list management ───────────────────────────────────────────────

    private void RefreshServerList()
    {
        IReadOnlyList<ServerEntry> entries = _browser.Entries;
        List<string> items = entries.Select(e => e.Row).ToList();
        if (items.Count == 0) items.Add("(no saved servers — press Add)");
        _serverList.Source = new ListWrapper<string>(new ObservableCollection<string>(items));
        _serverList.SelectedItem = _selectedServerIdx >= 0 && _selectedServerIdx < entries.Count
            ? _selectedServerIdx
            : 0;
        if (_selectedServerIdx < 0 && entries.Count > 0)
            _selectedServerIdx = 0;
        UpdateStatusLine(force: true);
    }

    private void AddServer()
    {
        RunModal(new ServerConfigDialog(), dialog =>
        {
            _configStore.Add(dialog.GetConfig());
            _browser.Reload();
        });
    }

    private void EditServer()
    {
        IReadOnlyList<ServerConfig> configs = _configStore.Configs;
        if (_selectedServerIdx < 0 || _selectedServerIdx >= configs.Count) return;
        var existing = configs[_selectedServerIdx];
        RunModal(new ServerConfigDialog(existing), dialog =>
        {
            _configStore.Update(_selectedServerIdx, dialog.GetConfig());
            _browser.Reload();
        });
    }

    private void DeleteServer()
    {
        IReadOnlyList<ServerConfig> configs = _configStore.Configs;
        if (_selectedServerIdx < 0 || _selectedServerIdx >= configs.Count) return;
        _configStore.Remove(_selectedServerIdx);
        _selectedServerIdx = Math.Min(_selectedServerIdx, Math.Max(0, configs.Count - 2));
        _browser.Reload();
    }

    // ── Connection ───────────────────────────────────────────────────────────

    private void ToggleConnection()
    {
        if (_connManager.State == ConnectionState.Disconnected)
        {
            Connect();
        }
        else
        {
            _ = _connManager.DisconnectAsync();
        }
    }

    private async void Connect()
    {
        IReadOnlyList<ServerConfig> configs = _configStore.Configs;
        if (_selectedServerIdx < 0 || _selectedServerIdx >= configs.Count)
        {
            GlobalLog.Write($"[{DateTime.Now:HH:mm:ss}] [Error] No server selected.");
            return;
        }
        ServerConfig config = configs[_selectedServerIdx];

        switch (_appData.LoginMethod)
        {
            case LoginMethod.SteamApi:
            {
                SteamNetAuthProvider? provider = _appData.SteamApiProvider;
                if (provider == null || !provider.IsAvailable || provider.AppId != config.AppId)
                {
                    provider?.Dispose();
                    provider = new SteamNetAuthProvider(config.AppId);
                    _appData.SteamApiProvider = provider;
                    if (provider.IsAvailable)
                    {
                        _appData.SteamUsername = provider.GetSteamName();
                        _appData.SteamId = provider.GetSteamID();
                    }
                }

                if (!provider.IsAvailable)
                {
                    GlobalLog.Write($"[{DateTime.Now:HH:mm:ss}] [Steam] Login failed: {provider.LastError}");
                    UpdateStatusLine(force: true);
                    return;
                }
                break;
            }
            case LoginMethod.SteamKit:
            {
                if (_appData.SteamKitProvider is not { IsAvailable: true })
                {
                    GlobalLog.Write($"[{DateTime.Now:HH:mm:ss}] [SteamKit] Not logged in. Open Settings (F9) and complete the QR code login first.");
                    return;
                }
                break;
            }
        }

        UpdateStatusLine(force: true);
        await _connManager.ConnectAsync(config, _appData.AuthProvider, _appData.UserInfo);
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    private void SendCommand()
    {
        if (_connManager.State != ConnectionState.Connected)
        {
            GlobalLog.Write($"[{DateTime.Now:HH:mm:ss}] Not connected — nothing sent.");
            return;
        }

        string cmdType = _cmdTypeDd.Value ?? CmdTypes[0];
        if (!CmdTypes.Contains(cmdType)) cmdType = CmdTypes[0];
        string text = _inputTf.Text ?? "";

        switch (cmdType)
        {
            case "StringCmd":
                if (!string.IsNullOrEmpty(text))
                    _ = _connManager.SendStringCmdAsync(text);
                break;
            case "Move":
                GlobalLog.Write($"[{DateTime.Now:HH:mm:ss}] Use the Move… button to edit and send a move command.");
                return;
            case "CvarValue":
            {
                string[] parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                    _ = _connManager.SendCvarValueAsync(parts[0], parts[1]);
                break;
            }
            case "CvarValue2":
            {
                string[] parts = text.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && int.TryParse(parts[0], out int reqId))
                    _ = _connManager.SendCvarValue2Async(reqId, parts[1], parts[2]);
                break;
            }
        }

        _inputTf.Text = "";
    }

    private void OpenMoveEditor()
    {
        if (_connManager.State != ConnectionState.Connected || _connManager.Connection == null) return;

        RunModal(new MoveEditorDialog(), dialog =>
        {
            byte[] payload = UserCmd.Encode(
                dialog.ForwardMove, dialog.SideMove, dialog.UpMove,
                dialog.Buttons, dialog.Impulse);
            _ = _connManager.Connection.SendCommandAsync(ClientCommandType.Move, payload);
        });
    }

    // ── Settings ─────────────────────────────────────────────────────────────

    private void OpenSettings()
    {
        // Steam API logins from Settings target the selected server's AppId, so
        // the login is immediately usable for Connect instead of being re-initialized.
        uint selectedAppId = (uint)(_configStore.Configs.ElementAtOrDefault(_selectedServerIdx)?.AppId ?? 70);
        RunModal(new SettingsDialog(_appData, _userInfoStore, _connManager, () => selectedAppId, _settingsStore), _ =>
        {
            UpdateStatusLine(force: true);
        });
    }

    // ── Modal helper ─────────────────────────────────────────────────────────

    private void RunModal<T>(T dialog, Action<T> onConfirmed) where T : Window
    {
        if (AppHolder.App is not IApplication app) return;
        app.Run(dialog);
        if (dialog is IConfirmed confirmed && confirmed.Confirmed)
            onConfirmed(dialog);
    }
}

/// <summary>Implemented by dialogs that report whether the user accepted them.</summary>
public interface IConfirmed
{
    bool Confirmed { get; }
}
