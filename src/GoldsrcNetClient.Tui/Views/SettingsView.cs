using System.Collections.ObjectModel;
using System.Text;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.SteamProvider;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace GoldsrcNetClient.Tui.Views;

public sealed class SettingsView : View
{
    private readonly AppData _appData;
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
    private readonly DropDownList _appIdDd;
    private readonly FrameView _appIdFrame;
    private SteamKitAuthProvider? _steamKitAuth;
    private bool _qrLoggedIn;

    public event Action? LoggedIn;

    private static readonly (string Label, uint AppId)[] AppIdOptions =
    [
        ("Half-Life", 70),
        ("Counter-Strike", 10),
        ("Sven Co-op", 225840),
    ];

    public SettingsView(AppData appData)
    {
        _appData = appData;
        Width = Dim.Fill();
        Height = Dim.Fill();

        View userInfoFrame = new View
        {
            X = 1, Y = 0, Width = 40, Height = 13
        };
        userInfoFrame.Add(new Label { Text = "── User Info ──", X = 0, Y = 0 });

        void AddField(string label, int y, out Label lbl, out TextField tf, string value)
        {
            lbl = new Label { Text = label, X = 1, Y = y + 1 };
            tf = new TextField { Text = value, X = 16, Y = y + 1, Width = 22 };
            userInfoFrame.Add(lbl, tf);
        }

        AddField("name:", 0, out _, out _nameTf, "GoldsrcNetClient");
        AddField("model:", 1, out _, out _modelTf, "gordon");
        AddField("topcolor:", 2, out _, out _topColorTf, "0");
        AddField("bottomcolor:", 3, out _, out _bottomColorTf, "0");
        AddField("rate:", 4, out _, out _rateTf, "20000");
        AddField("cl_updaterate:", 5, out _, out _updaterateTf, "60");
        Add(userInfoFrame);

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

        List<string> appIdLabels = AppIdOptions.Select(a => a.Label).ToList();
        _appIdFrame = new FrameView
        {
            Title = "Steam AppID",
            X = 1, Y = 23, Width = 40, Height = 4
        };
        _appIdDd = new DropDownList
        {
            X = 1, Y = 0, Width = 36,
            Source = new ListWrapper<string>(new ObservableCollection<string>(appIdLabels)),
            ReadOnly = true,
            Value = appIdLabels[0]
        };
        _appIdFrame.Add(_appIdDd);
        Add(_appIdFrame);

        Button goBtn = new Button
        {
            Text = "Proceed to Connection",
            X = 1, Y = 28, Width = 40
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
            _appIdFrame.Visible = idx != 0;
            _loginBtn.Visible = idx == 2;
            _appData.LoginMethod = (LoginMethod)idx;
            if (_appData.LoginMethod != LoginMethod.NoSteam)
                _appData.SteamAppId = GetSelectedAppId();
        };
    }

    public void FocusFirstField() => _nameTf.SetFocus();

    private uint GetSelectedAppId()
    {
        string? label = _appIdDd.Value?.ToString();
        int idx = Array.FindIndex(AppIdOptions, a => a.Label == label);
        return idx >= 0 ? AppIdOptions[idx].AppId : 70u;
    }

    private void StartSteamKitLogin()
    {
        if (_qrLoggedIn) return;

        uint appId = GetSelectedAppId();

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
        StringBuilder sb = new StringBuilder();
        sb.Append("\\name\\").Append(_nameTf.Text);
        sb.Append("\\model\\").Append(_modelTf.Text);
        sb.Append("\\topcolor\\").Append(_topColorTf.Text);
        sb.Append("\\bottomcolor\\").Append(_bottomColorTf.Text);
        sb.Append("\\rate\\").Append(_rateTf.Text);
        sb.Append("\\cl_updaterate\\").Append(_updaterateTf.Text);
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
                uint appId = GetSelectedAppId();
                SteamNetAuthProvider auth = new SteamNetAuthProvider(appId);
                _appData.AuthDisposable = auth;
                if (auth.IsAvailable)
                    return auth;
                auth.Dispose();
                return null;
            }
            case LoginMethod.SteamKit:
                return _appData.AuthProvider;
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
