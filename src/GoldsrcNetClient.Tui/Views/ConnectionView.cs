using System.Collections.ObjectModel;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.SteamProvider;
using GoldsrcNetClient.Tui.Models;
using GoldsrcNetClient.Tui.Services;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace GoldsrcNetClient.Tui.Views;

public sealed class ConnectionView : View
{
    private readonly AppData _appData;
    private readonly ConnectionManager _connManager;
    private readonly ServerConfigStore _configStore;
    private readonly SettingsView _settingsView;

    private readonly ListView _serverList;
    private readonly TextView _outputTv;
    private readonly FrameView _outputFrame;
    private readonly FrameView _serverFrame;
    private readonly FrameView _userInfoFrame;
    private readonly Dictionary<string, TextField> _userInfoFields = new();
    private readonly DropDownList _cmdTypeDd;
    private readonly TextField _inputTf;
    private readonly Button _sendBtn;
    private readonly Button _moveBtn;
    private readonly Button _connectBtn;
    private readonly Button _addBtn;
    private readonly Button _editBtn;
    private readonly Button _deleteBtn;
    private readonly Button _upBtn;
    private readonly Button _downBtn;
    private readonly Label _statusLbl;
    private int _selectedServerIdx = -1;
    private bool _autoScroll = true;

    private static readonly string[] CmdTypes = ["StringCmd", "Move", "CvarValue", "CvarValue2"];

    public ConnectionView(AppData appData, ConnectionManager connManager, ServerConfigStore configStore, SettingsView settingsView)
    {
        _appData = appData;
        _connManager = connManager;
        _configStore = configStore;
        _settingsView = settingsView;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _serverFrame = new FrameView
        {
            Title = "Server Configurations",
            X = 1, Y = 1, Width = 36, Height = Dim.Fill(5)
        };
        _serverList = new ListView
        {
            X = 1, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(3)
        };
        _serverList.ValueChanged += (s, e) => _selectedServerIdx = _serverList.SelectedItem.GetValueOrDefault();
        _serverFrame.Add(_serverList);

        _addBtn = new Button { Text = "Add", X = 1, Y = Pos.AnchorEnd(2) };
        _addBtn.Accepting += (s, e) => AddServer();
        _editBtn = new Button { Text = "Edit", X = Pos.Right(_addBtn) + 1, Y = Pos.AnchorEnd(2) };
        _editBtn.Accepting += (s, e) => EditServer();
        _deleteBtn = new Button { Text = "Del", X = Pos.Right(_editBtn) + 1, Y = Pos.AnchorEnd(2) };
        _deleteBtn.Accepting += (s, e) => DeleteServer();
        _upBtn = new Button { Text = "Up", X = Pos.Right(_deleteBtn) + 1, Y = Pos.AnchorEnd(2) };
        _upBtn.Accepting += (s, e) => MoveServerUp();
        _downBtn = new Button { Text = "Down", X = Pos.Right(_upBtn) + 1, Y = Pos.AnchorEnd(2) };
        _downBtn.Accepting += (s, e) => MoveServerDown();
        _serverFrame.Add(_addBtn, _editBtn, _deleteBtn, _upBtn, _downBtn);

        View connFrame = new View
        {
            X = 1, Y = Pos.AnchorEnd(3), Width = 36, Height = 3
        };
        _connectBtn = new Button { Text = "Connect", X = 1, Y = 0 };
        _connectBtn.Accepting += (s, e) => ToggleConnection();
        _statusLbl = new Label { Text = "Disconnected", X = Pos.Right(_connectBtn) + 2, Y = 0 };
        connFrame.Add(_connectBtn, _statusLbl);

        Add(_serverFrame, connFrame);

        _outputFrame = new FrameView
        {
            Title = "Server Output",
            X = 38, Y = 1, Width = Dim.Fill(), Height = Dim.Fill(17)
        };
        _outputTv = new TextView
        {
            X = 1, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(),
            ReadOnly = true
        };
        _outputFrame.Add(_outputTv);
        Add(_outputFrame);

        _userInfoFrame = new FrameView
        {
            Title = "User Info (Connected)",
            X = 38, Y = Pos.AnchorEnd(15), Width = Dim.Fill(), Height = 12
        };
        BuildUserInfoEditor();
        Add(_userInfoFrame);

        View inputFrame = new View
        {
            X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(), Height = 2
        };

        List<string> cmdTypeItems = CmdTypes.ToList();
        _cmdTypeDd = new DropDownList
        {
            X = 1, Y = 0, Width = 14,
            Source = new ListWrapper<string>(new ObservableCollection<string>(cmdTypeItems)),
            ReadOnly = true,
            Value = CmdTypes[0]
        };
        _inputTf = new TextField { Text = "", X = 16, Y = 0, Width = Dim.Fill(22) };

        _sendBtn = new Button { Text = "Send", X = Pos.AnchorEnd(19), Y = 0 };
        _sendBtn.Accepting += (s, e) => SendCommand();

        _moveBtn = new Button { Text = "Move...", X = Pos.AnchorEnd(11), Y = 0 };
        _moveBtn.Accepting += (s, e) => OpenMoveEditor();

        _cmdTypeDd.ValueChanged += (s, e) =>
        {
            string? val = _cmdTypeDd.Value?.ToString();
            int idx = Array.IndexOf(CmdTypes, val ?? "");
            if (idx < 0) return;
            string type = CmdTypes[idx];
            _moveBtn.Visible = type == "Move";
            _inputTf.Width = type == "Move" ? Dim.Fill(30) : Dim.Fill(22);
        };

        inputFrame.Add(_cmdTypeDd, _inputTf, _sendBtn, _moveBtn);
        Add(inputFrame);

        RefreshServerList();

        if (AppHolder.App is IApplication app)
        {
            app.AddTimeout(TimeSpan.FromMilliseconds(50), () =>
            {
                FlushOutput();
                FlushState();
                return true;
            });
        }
    }

    private void BuildUserInfoEditor()
    {
        string defaultUserInfo = _appData.UserInfo;
        string[] parts = defaultUserInfo.Split('\\');
        int y = 0;
        for (int i = 1; i + 1 < parts.Length; i += 2)
        {
            if (parts[i] == "protocol") continue;
            if (y >= 9) break;

            Label lbl = new Label { Text = $"{parts[i]}:", X = 1, Y = y };
            TextField tf = new TextField { Text = parts[i + 1], X = 18, Y = y, Width = 20 };
            string key = parts[i];
            tf.TextChanged += (s, args) =>
            {
                if (_connManager.Connection != null && _connManager.State == ConnectionState.Connected)
                {
                    _connManager.Connection.SetUserInfo(key, tf.Text ?? "");
                    _ = _connManager.SendStringCmdAsync($"setinfo \"{key}\" \"{tf.Text}\"");
                }
            };
            _userInfoFields[key] = tf;
            _userInfoFrame.Add(lbl, tf);
            y += 1;
        }

        Button saveBtn = new Button { Text = "Update All", X = 1, Y = 10 };
        saveBtn.Accepting += (s, e) =>
        {
            if (_connManager.Connection == null || _connManager.State != ConnectionState.Connected) return;
            foreach (KeyValuePair<string, TextField> kv in _userInfoFields)
            {
                _connManager.Connection.SetUserInfo(kv.Key, kv.Value.Text ?? "");
                _ = _connManager.SendStringCmdAsync($"setinfo \"{kv.Key}\" \"{kv.Value.Text}\"");
            }
        };
        _userInfoFrame.Add(saveBtn);
    }

    private void RefreshUserInfoFields()
    {
        if (_connManager.Connection == null) return;
        string userInfo = _connManager.Connection.UserInfo;
        string[] parts = userInfo.Split('\\');
        for (int i = 1; i + 1 < parts.Length; i += 2)
        {
            if (_userInfoFields.TryGetValue(parts[i], out TextField? tf))
                tf.Text = parts[i + 1];
        }
    }

    private void RefreshServerList()
    {
        IReadOnlyList<ServerConfig> configs = _configStore.Configs;
        List<string> items = configs.Select(c => c.ToString()).ToList();
        if (items.Count == 0) items.Add("(no saved servers)");
        _serverList.Source = new ListWrapper<string>(new ObservableCollection<string>(items));
        _serverList.SelectedItem = _selectedServerIdx >= 0 && _selectedServerIdx < configs.Count
            ? _selectedServerIdx : 0;
    }

    private void AddServer()
    {
        ServerConfigDialog dialog = new ServerConfigDialog();
        if (AppHolder.App is IApplication app)
        {
            app.Run(dialog);
            if (dialog.Confirmed)
            {
                _configStore.Add(dialog.GetConfig());
                RefreshServerList();
            }
        }
    }

    private void EditServer()
    {
        IReadOnlyList<ServerConfig> configs = _configStore.Configs;
        if (_selectedServerIdx < 0 || _selectedServerIdx >= configs.Count) return;
        ServerConfigDialog dialog = new ServerConfigDialog(configs[_selectedServerIdx]);
        if (AppHolder.App is IApplication app)
        {
            app.Run(dialog);
            if (dialog.Confirmed)
            {
                _configStore.Update(_selectedServerIdx, dialog.GetConfig());
                RefreshServerList();
            }
        }
    }

    private void DeleteServer()
    {
        IReadOnlyList<ServerConfig> configs = _configStore.Configs;
        if (_selectedServerIdx < 0 || _selectedServerIdx >= configs.Count) return;
        _configStore.Remove(_selectedServerIdx);
        _selectedServerIdx = Math.Min(_selectedServerIdx, configs.Count - 2);
        RefreshServerList();
    }

    private void MoveServerUp()
    {
        _configStore.MoveUp(_selectedServerIdx);
        if (_selectedServerIdx > 0) _selectedServerIdx--;
        RefreshServerList();
    }

    private void MoveServerDown()
    {
        _configStore.MoveDown(_selectedServerIdx);
        int count = _configStore.Configs.Count;
        if (_selectedServerIdx < count - 1) _selectedServerIdx++;
        RefreshServerList();
    }

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
        if (_selectedServerIdx < 0 || _selectedServerIdx >= configs.Count) return;

        _settingsView.ApplyUserInfo();

        ISteamAuthProvider? authProvider = _appData.LoginMethod switch
        {
            LoginMethod.SteamApi or LoginMethod.SteamKit => _settingsView.GetAuthProvider(_appData.LoginMethod),
            _ => null
        };
        if (_appData.LoginMethod == LoginMethod.SteamApi && authProvider == null)
        {
            SteamNetAuthProvider netAuth = new SteamNetAuthProvider(_appData.SteamAppId);
            if (netAuth.IsAvailable)
            {
                _appData.AuthDisposable?.Dispose();
                _appData.AuthDisposable = netAuth;
                authProvider = netAuth;
            }
            else
            {
                netAuth.Dispose();
            }
        }
        if (_appData.LoginMethod != LoginMethod.NoSteam && authProvider == null)
        {
            _statusLbl.Text = "Auth provider not ready. Complete Steam login first.";
            return;
        }

        _connectBtn.Enabled = false;
        _statusLbl.Text = "Connecting...";

        await _connManager.ConnectAsync(configs[_selectedServerIdx], authProvider, _appData.UserInfo);
        RefreshUserInfoFields();
        _connectBtn.Enabled = true;
    }

    private void FlushOutput()
    {
        while (_connManager.TryDequeueOutput(out string? msg))
        {
            if (msg == null) continue;
            _outputTv.Text += msg + "\n";
        }
        if (_autoScroll)
        {
            _outputTv.MoveEnd();
        }
    }

    private void FlushState()
    {
        while (_connManager.TryDequeueState(out ConnectionState state))
        {
            _statusLbl.Text = state switch
            {
                ConnectionState.Disconnected => "Disconnected",
                ConnectionState.Connecting => "Connecting...",
                ConnectionState.Connected => "Connected",
                ConnectionState.Reconnecting => "Reconnecting...",
                _ => ""
            };
            _connectBtn.Text = state == ConnectionState.Disconnected ? "Connect" : "Disconnect";

            if (state == ConnectionState.Connected)
            {
                RefreshUserInfoFields();
            }
        }
    }

    private void SendCommand()
    {
        if (_connManager.State != ConnectionState.Connected) return;

        string? val = _cmdTypeDd.Value?.ToString();
        int cmdIdx = Array.IndexOf(CmdTypes, val ?? "");
        if (cmdIdx < 0 || cmdIdx >= CmdTypes.Length) return;
        string cmdType = CmdTypes[cmdIdx];
        string text = _inputTf.Text ?? "";

        switch (cmdType)
        {
            case "StringCmd":
                if (!string.IsNullOrEmpty(text))
                    _ = _connManager.SendStringCmdAsync(text);
                break;
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
        if (_connManager.State != ConnectionState.Connected) return;
        if (_connManager.Connection == null) return;

        MoveEditorDialog dialog = new MoveEditorDialog();
        if (AppHolder.App is IApplication app)
        {
            app.Run(dialog);
            if (dialog.Confirmed)
            {
                byte[] payload = MovePayloadBuilder.Build(
                    dialog.ForwardMove, dialog.SideMove, dialog.UpMove,
                    dialog.Buttons, dialog.Impulse);
                _ = _connManager.Connection.SendCommandAsync(ClientCommandType.Move, payload);
            }
        }
    }
}
