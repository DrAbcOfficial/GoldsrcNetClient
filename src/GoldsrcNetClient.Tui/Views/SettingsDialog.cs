using System.Collections.ObjectModel;
using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.SteamProvider;
using GoldsrcNetClient.Tui.Services;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace GoldsrcNetClient.Tui.Views;

/// <summary>
/// Modal settings dialog: user info editing, login method selection, and the
/// Steam login flows (steam_api init + SteamKit QR code).
/// </summary>
public sealed class SettingsDialog : Window, IConfirmed
{
    private readonly AppData _appData;
    private readonly UserInfoStore _userInfoStore;
    private readonly ConnectionManager _connManager;
    private readonly Func<uint> _appIdProvider;

    private readonly TextField _nameTf;
    private readonly TextField _modelTf;
    private readonly TextField _topColorTf;
    private readonly TextField _bottomColorTf;
    private readonly TextField _rateTf;
    private readonly TextField _updaterateTf;
    private readonly DropDownList _loginMethodDd;
    private readonly Label _steamStatus;
    private readonly Button _steamApiBtn;
    private readonly Button _qrBtn;
    private readonly FrameView _qrFrame;
    private readonly TextView _qrText;
    private readonly Label _qrStatus;
    private bool _qrLoggedIn;
    private bool _qrInFlight;

    private static readonly string[] LoginMethods = ["No Steam", "Steam API", "SteamKit (QR Code)"];

    public bool Confirmed { get; private set; }

    public SettingsDialog(AppData appData, UserInfoStore userInfoStore, ConnectionManager connManager)
        : this(appData, userInfoStore, connManager, () => 70)
    {
    }

    public SettingsDialog(AppData appData, UserInfoStore userInfoStore, ConnectionManager connManager,
        Func<uint> appIdProvider)
    {
        _appData = appData;
        _userInfoStore = userInfoStore;
        _connManager = connManager;
        _appIdProvider = appIdProvider;

        Title = "Settings";
        Width = 62;
        Height = 27;

        // ── User info ──────────────────────────────────────────────────────
        UserInfoData d = userInfoStore.Data;

        Add(new Label { Text = "User Info", X = 1, Y = 0 });

        Add(new Label { Text = "name:", X = 1, Y = 2 });
        _nameTf = new TextField { Text = d.Name, X = 15, Y = 2, Width = 40 };
        Add(_nameTf);

        Add(new Label { Text = "model:", X = 1, Y = 3 });
        _modelTf = new TextField { Text = d.Model, X = 15, Y = 3, Width = 20 };
        Add(_modelTf);

        Add(new Label { Text = "topcolor:", X = 1, Y = 4 });
        _topColorTf = new TextField { Text = d.TopColor, X = 15, Y = 4, Width = 6 };
        Add(_topColorTf);

        Add(new Label { Text = "bottomcolor:", X = 25, Y = 4 });
        _bottomColorTf = new TextField { Text = d.BottomColor, X = 40, Y = 4, Width = 6 };
        Add(_bottomColorTf);

        Add(new Label { Text = "rate:", X = 1, Y = 5 });
        _rateTf = new TextField { Text = d.Rate, X = 15, Y = 5, Width = 12 };
        Add(_rateTf);

        Add(new Label { Text = "cl_updaterate:", X = 30, Y = 5 });
        _updaterateTf = new TextField { Text = d.ClUpdaterate, X = 45, Y = 5, Width = 6 };
        Add(_updaterateTf);

        // ── Login method ───────────────────────────────────────────────────
        Add(new Label { Text = "Login Method", X = 1, Y = 7 });

        _loginMethodDd = new DropDownList
        {
            X = 1, Y = 8,
            Width = 26,
            Source = new ListWrapper<string>(new ObservableCollection<string>(LoginMethods)),
            ReadOnly = true,
            Value = LoginMethods[(int)appData.LoginMethod]
        };
        Add(_loginMethodDd);

        _steamStatus = new Label { Text = BuildSteamStatus(), X = 1, Y = 10, Width = Dim.Fill() };
        Add(_steamStatus);

        _steamApiBtn = new Button { Text = "Login via Steam API", X = 1, Y = 11 };
        _steamApiBtn.Accepting += (s, e) => StartSteamApiLogin();
        Add(_steamApiBtn);

        _qrBtn = new Button { Text = "Login via QR Code", X = 24, Y = 11 };
        _qrBtn.Accepting += (s, e) => StartSteamKitLogin();
        Add(_qrBtn);

        _qrFrame = new FrameView
        {
            Title = "SteamKit QR Code",
            X = 1, Y = 13,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
            Visible = false
        };
        _qrText = new TextView
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            ReadOnly = true
        };
        _qrStatus = new Label { Text = "", X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill() };
        _qrFrame.Add(_qrText, _qrStatus);
        Add(_qrFrame);

        // ── Buttons ────────────────────────────────────────────────────────
        Button saveBtn = new() { Text = "Save", X = Pos.Center() - 10, Y = Pos.AnchorEnd(1) };
        saveBtn.Accepting += (s, e) =>
        {
            Confirmed = true;
            ApplyAndClose();
        };
        Add(saveBtn);

        Button cancelBtn = new() { Text = "Cancel", X = Pos.Right(saveBtn) + 2, Y = Pos.AnchorEnd(1) };
        cancelBtn.Accepting += (s, e) =>
        {
            Confirmed = false;
            RequestStop();
        };
        Add(cancelBtn);

        _loginMethodDd.ValueChanged += (s, e) =>
        {
            string? val = _loginMethodDd.Value;
            int idx = Array.IndexOf(LoginMethods, val ?? "");
            if (idx < 0) return;
            _steamStatus.Text = idx switch
            {
                1 => DescribeSteamApiState(),
                2 => "SteamKit: press 'Login via QR Code' and scan with the Steam mobile app.",
                _ => "No Steam: connect with fake auth data (works on non-secured servers)."
            };
        };
    }

    // ── Steam API login ──────────────────────────────────────────────────────

    private void StartSteamApiLogin()
    {
        uint appId = _appIdProvider();
        _steamApiBtn.Enabled = false;
        _steamStatus.Text = $"Initializing Steam API (AppID {appId})…";

        Task.Run(() =>
        {
            try
            {
                SteamNetAuthProvider? old = _appData.SteamApiProvider;
                if (old != null && old.AppId == appId && old.IsAvailable)
                {
                    // Already logged in with this AppId.
                    OnUiThread(() =>
                    {
                        _steamApiBtn.Enabled = true;
                        _steamStatus.Text = DescribeSteamApiState();
                    });
                    return;
                }

                old?.Dispose();

                var provider = new SteamNetAuthProvider(appId);
                OnUiThread(() =>
                {
                    _appData.SteamApiProvider = provider;
                    if (provider.IsAvailable)
                    {
                        _appData.SteamUsername = provider.GetSteamName();
                        _appData.SteamId = provider.GetSteamID();
                    }
                    _steamApiBtn.Enabled = true;
                    _steamStatus.Text = DescribeSteamApiState();
                });
            }
            catch (Exception ex)
            {
                OnUiThread(() =>
                {
                    _steamApiBtn.Enabled = true;
                    _steamStatus.Text = $"Steam API error: {ex.Message}";
                });
            }
        });
    }

    private static void OnUiThread(Action action)
    {
        if (AppHolder.App is IApplication app)
            app.Invoke(action);
        else
            action();
    }

    private string DescribeSteamApiState()
    {
        SteamNetAuthProvider? provider = _appData.SteamApiProvider;
        if (provider == null)
            return "Steam API: not initialized. Click 'Login via Steam API' (requires a running Steam client).";
        if (provider.IsAvailable)
            return $"Steam API: {_appData.SteamUsername ?? "(unknown)"}" +
                   (_appData.SteamId.HasValue ? $" (SteamID {_appData.SteamId.Value})" : "");
        return $"Steam API login failed: {provider.LastError}";
    }

    private static string BuildSteamStatus() =>
        "Steam API: not initialized. Click 'Login via Steam API' (requires a running Steam client).";

    // ── SteamKit QR login ────────────────────────────────────────────────────

    private void StartSteamKitLogin()
    {
        if (_qrLoggedIn || _qrInFlight)
            return;

        _qrInFlight = true;
        _qrBtn.Enabled = false;
        _qrFrame.Visible = true;
        _qrStatus.Text = "Connecting to Steam…";
        _qrText.Text = "";

        Task.Run(async () =>
        {
            try
            {
                SteamKitAuthProvider authProvider = new();
                await authProvider.ConnectAsync();
                OnUiThread(() => _qrStatus.Text = "Generating QR code…");

                string qrUrl = await authProvider.BeginQrLoginAsync();
                string qrString = RenderQrCode(qrUrl);

                OnUiThread(() =>
                {
                    _qrText.Text = qrString;
                    _qrStatus.Text = "Scan the QR code with the Steam mobile app";
                });

                await authProvider.WaitForLoginAsync();

                OnUiThread(() =>
                {
                    _qrStatus.Text = "Logged in!";
                    _qrBtn.Text = "Logged In";
                    _qrLoggedIn = true;
                    _qrInFlight = false;
                    _appData.SteamKitProvider?.Dispose();
                    _appData.SteamKitProvider = authProvider;
                    _appData.SteamUsername = authProvider.SteamUsername;
                    _appData.SteamId = authProvider.SteamId;
                    _steamStatus.Text = "SteamKit: logged in.";
                });
            }
            catch (Exception ex)
            {
                OnUiThread(() =>
                {
                    _qrStatus.Text = $"Error: {ex.Message}";
                    _qrBtn.Enabled = true;
                    _qrInFlight = false;
                });
            }
        });
    }

    private static string RenderQrCode(string url)
    {
        try
        {
            using QRCoder.QRCodeGenerator qrGenerator = new();
            QRCoder.QRCodeData qrData = qrGenerator.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.L);
            using QRCoder.AsciiQRCode qrCode = new(qrData);
            return url + "\n\n" + qrCode.GetGraphic(1, drawQuietZones: false);
        }
        catch
        {
            return url + "\n\n(Could not render QR code)";
        }
    }

    // ── Apply / close ────────────────────────────────────────────────────────

    private void ApplyAndClose()
    {
        ApplyUserInfo();
        _appData.LoginMethod = Array.IndexOf(LoginMethods, _loginMethodDd.Value ?? "") switch
        {
            1 => LoginMethod.SteamApi,
            2 => LoginMethod.SteamKit,
            _ => LoginMethod.NoSteam
        };
        RequestStop();
    }

    private void ApplyUserInfo()
    {
        UserInfoData data = new()
        {
            Name = _nameTf.Text,
            Model = _modelTf.Text,
            TopColor = string.IsNullOrWhiteSpace(_topColorTf.Text) ? "0" : _topColorTf.Text,
            BottomColor = string.IsNullOrWhiteSpace(_bottomColorTf.Text) ? "0" : _bottomColorTf.Text,
            Rate = string.IsNullOrWhiteSpace(_rateTf.Text) ? "20000" : _rateTf.Text,
            ClUpdaterate = string.IsNullOrWhiteSpace(_updaterateTf.Text) ? "60" : _updaterateTf.Text,
        };
        _userInfoStore.Save(data);

        UserInfoString info = new("\\protocol\\48\\cl_lc\\1\\cl_lw\\1\\hltv\\0");
        info.Set("name", data.Name);
        info.Set("model", data.Model);
        info.Set("topcolor", data.TopColor);
        info.Set("bottomcolor", data.BottomColor);
        info.Set("rate", data.Rate);
        info.Set("cl_updaterate", data.ClUpdaterate);

        string updated = info.ToString();
        PushChangedUserinfo(_connManager.Connection?.UserInfo, updated);
        _appData.UserInfo = updated;
    }

    /// <summary>When connected, sends setinfo for every userinfo value that changed.</summary>
    private void PushChangedUserinfo(string? current, string updated)
    {
        if (_connManager.State != ConnectionState.Connected || _connManager.Connection == null)
            return;

        var before = new UserInfoString(current);
        var after = new UserInfoString(updated);
        foreach (var (key, value) in after.Values)
        {
            if (key == "protocol") continue;
            if (!string.Equals(before.Get(key), value, StringComparison.Ordinal))
            {
                _connManager.Connection.SetUserInfo(key, value);
                _ = _connManager.SendStringCmdAsync($"setinfo \"{key}\" \"{value}\"");
            }
        }
    }
}
