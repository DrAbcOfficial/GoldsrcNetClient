using System.Collections.ObjectModel;
using System.Text;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.SteamProvider;
using GoldsrcNetClient.Tui.Services;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace GoldsrcNetClient.Tui.Views;

public sealed class SettingsView : View
{
    private readonly AppData _appData;
    private readonly UserInfoStore _userInfoStore;
    private readonly TextField _nameTf;
    private readonly TextField _modelTf;
    private readonly TextField _topColorTf;
    private readonly TextField _bottomColorTf;
    private readonly TextField _rateTf;
    private readonly TextField _updaterateTf;
    private readonly DropDownList _loginMethodDd;
    private readonly Button _loginBtn;
    private readonly FrameView _qrFrame;
    private readonly TextView _qrText;
    private readonly Button _qrBtn;
    private readonly Label _qrStatus;
    private SteamKitAuthProvider? _steamKitAuth;
    private bool _qrLoggedIn;

    public event Action? LoggedIn;

    public SettingsView(AppData appData, UserInfoStore userInfoStore)
    {
        _appData = appData;
        _userInfoStore = userInfoStore;
        userInfoStore.Load();
        var d = userInfoStore.Data;
        Width = Dim.Fill();
        Height = Dim.Fill();

        Add(new Label { Text = "── User Info ──", X = 1, Y = 0 });

        void AddField(string label, int y, out Label lbl, out TextField tf, string value)
        {
            lbl = new Label { Text = label, X = 1, Y = y + 1 };
            tf = new TextField { Text = value, X = 16, Y = y + 1, Width = 22 };
            Add(lbl, tf);
        }

        AddField("name:", 0, out _, out _nameTf, d.Name);
        AddField("model:", 1, out _, out _modelTf, d.Model);
        AddField("topcolor:", 2, out _, out _topColorTf, d.TopColor);
        AddField("bottomcolor:", 3, out _, out _bottomColorTf, d.BottomColor);
        AddField("rate:", 4, out _, out _rateTf, d.Rate);
        AddField("cl_updaterate:", 5, out _, out _updaterateTf, d.ClUpdaterate);

        FrameView loginFrame = new FrameView
        {
            Title = "Login Method",
            X = 1, Y = 14, Width = 40, Height = 8
        };

        List<string> loginMethods = new List<string> { "No Steam", "Steam API", "SteamKit (QR Code)" };
        _loginMethodDd = new DropDownList
        {
            X = 1, Y = 0,
            Width = 38,
            Source = new ListWrapper<string>(new ObservableCollection<string>(loginMethods)),
            ReadOnly = true,
            Value = loginMethods[0]
        };
        loginFrame.Add(_loginMethodDd);

        _loginBtn = new Button
        {
            Text = "Login",
            X = 1, Y = 2,
            Visible = false
        };
        _loginBtn.Accepting += (s, e) => StartSteamKitLogin();
        loginFrame.Add(_loginBtn);
        Add(loginFrame);

        Button goBtn = new Button
        {
            Text = "Proceed to Connection",
            X = 1, Y = 23, Width = 40
        };
        goBtn.Accepting += (s, e) => LoggedIn?.Invoke();
        Add(goBtn);

        _qrFrame = new FrameView
        {
            Title = "SteamKit QR Code",
            X = 43, Y = 0, Width = Dim.Fill(2), Height = Dim.Fill(1),
            Visible = false
        };
        _qrText = new TextView
        {
            X = 1, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(3),
            ReadOnly = true
        };
        _qrStatus = new Label { Text = "", X = 1, Y = Pos.AnchorEnd(2) };
        _qrBtn = new Button { Text = "Generate QR Code", X = 1, Y = Pos.AnchorEnd(0) };
        _qrBtn.Accepting += (s, e) => StartSteamKitLogin();
        _qrFrame.Add(_qrText, _qrStatus, _qrBtn);
        Add(_qrFrame);

        _loginMethodDd.ValueChanged += (s, e) =>
        {
            string? val = _loginMethodDd.Value?.ToString();
            int idx = loginMethods.IndexOf(val ?? "");
            if (idx < 0) return;
            _qrFrame.Visible = idx == 2;
            _loginBtn.Visible = idx == 2;
            _appData.LoginMethod = (LoginMethod)idx;
        };
    }

    public void FocusFirstField() => _nameTf.SetFocus();

    private static uint DefaultAppId => 70;

    private void StartSteamKitLogin()
    {
        if (_qrLoggedIn) return;

        uint appId = DefaultAppId;

        _qrBtn.Enabled = false;
        _qrStatus.Text = "Connecting to Steam...";
        _qrText.Text = "";

        _appData.AuthDisposable?.Dispose();
        _steamKitAuth = new SteamKitAuthProvider(appId);
        _appData.AuthDisposable = _steamKitAuth;

        Task.Run(async () =>
        {
            try
            {
                await _steamKitAuth.ConnectAsync();

                _qrStatus.Text = "Generating QR code...";

                string qrUrl = await _steamKitAuth.BeginQrLoginAsync();
                string qrString = RenderQrCode(qrUrl);

                _qrText.Text = qrString;
                _qrStatus.Text = "Scan the QR code with the Steam mobile app";

                await _steamKitAuth.WaitForLoginAsync();

                _qrStatus.Text = "Logged in!";
                _qrBtn.Text = "Logged In";
                _qrLoggedIn = true;
                _appData.AuthProvider = _steamKitAuth;
                _appData.LoginMethod = LoginMethod.SteamKit;
                _appData.SteamAppId = appId;
                _appData.SteamUsername = _steamKitAuth.SteamUsername;
                _appData.SteamId = _steamKitAuth.SteamId;
            }
            catch (Exception ex)
            {
                _qrStatus.Text = $"Error: {ex.Message}";
                _qrBtn.Enabled = true;
            }
        });
    }

    public void ApplyUserInfo()
    {
        var data = new UserInfoData
        {
            Name = _nameTf.Text ?? "",
            Model = _modelTf.Text ?? "",
            TopColor = _topColorTf.Text ?? "0",
            BottomColor = _bottomColorTf.Text ?? "0",
            Rate = _rateTf.Text ?? "20000",
            ClUpdaterate = _updaterateTf.Text ?? "60",
        };
        _userInfoStore.Save(data);

        StringBuilder sb = new StringBuilder();
        sb.Append("\\name\\").Append(data.Name);
        sb.Append("\\model\\").Append(data.Model);
        sb.Append("\\topcolor\\").Append(data.TopColor);
        sb.Append("\\bottomcolor\\").Append(data.BottomColor);
        sb.Append("\\rate\\").Append(data.Rate);
        sb.Append("\\cl_updaterate\\").Append(data.ClUpdaterate);
        sb.Append("\\protocol\\48\\cl_lc\\1\\cl_lw\\1\\hltv\\0");
        _appData.UserInfo = sb.ToString();
    }

    public ISteamAuthProvider? GetAuthProvider(LoginMethod method)
    {
        switch (method)
        {
            case LoginMethod.NoSteam:
                return null;
            case LoginMethod.SteamApi:
            {
                SteamNetAuthProvider auth = new SteamNetAuthProvider(DefaultAppId);
                _appData.AuthDisposable = auth;
                if (auth.IsAvailable)
                    return auth;
                auth.Dispose();
                return null;
            }
            case LoginMethod.SteamKit:
                if (_steamKitAuth == null || !_steamKitAuth.IsAvailable)
                    return null;
                return _steamKitAuth;
            default:
                return null;
        }
    }

    private static string RenderQrCode(string url)
    {
        try
        {
            using QRCoder.QRCodeGenerator qrGenerator = new QRCoder.QRCodeGenerator();
            QRCoder.QRCodeData qrData = qrGenerator.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.L);
            using QRCoder.AsciiQRCode qrCode = new QRCoder.AsciiQRCode(qrData);
            return url + "\n\n" + qrCode.GetGraphic(1, drawQuietZones: false);
        }
        catch
        {
            return url + "\n\n(Could not render QR code)";
        }
    }
}
