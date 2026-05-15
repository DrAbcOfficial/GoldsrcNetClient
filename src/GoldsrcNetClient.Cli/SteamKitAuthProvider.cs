using System.Buffers.Binary;
using System.Collections.Concurrent;
using GoldsrcNetClient.Core.Network;
using QRCoder;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;

namespace GoldsrcNetClient.Cli;

public sealed class SteamKitAuthProvider : ISteamAuthProvider, IDisposable
{
    private readonly uint _appId;
    private readonly SteamClient _client;
    private readonly SteamUser _steamUser;
    private readonly SteamApps _steamApps;
    private readonly ConcurrentQueue<byte[]> _gameConnectTokens = new();
    private readonly CancellationTokenSource _cts = new();

    private Task? _callbackLoop;
    private QrAuthSession? _qrSession;
    private AuthPollResult? _loginResult;
    private bool _isLoggedOn;
    private readonly TaskCompletionSource _connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<EResult> _logonTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsAvailable => _isLoggedOn;
    public string? LastError { get; private set; }

    public SteamKitAuthProvider(uint appId = 70)
    {
        _appId = appId;
        var config = SteamConfiguration.Create(b => b.WithDirectoryFetch(true));
        _client = new SteamClient(config);
        _steamUser = _client.GetHandler<SteamUser>()!;
        _steamApps = _client.GetHandler<SteamApps>()!;
    }

    public byte GetAuthProtocol() => 3;

    public string GetRawAuthData()
    {
        if (!_isLoggedOn)
            return "steam";
        return Convert.ToHexString(GetGameAuthBytesInternal() ?? []);
    }

    public byte[] GetRawAuthBytes()
    {
        if (!_isLoggedOn)
            return System.Text.Encoding.UTF8.GetBytes("steam");
        return GetGameAuthBytesInternal() ?? System.Text.Encoding.UTF8.GetBytes("steam");
    }

    public byte[] GetGameAuthBytes(ulong serverSteamId, uint serverIp, ushort serverPort)
    {
        if (!_isLoggedOn)
            return GetRawAuthBytes();

        var result = GetGameAuthBytesInternal();
        return result ?? GetRawAuthBytes();
    }

    public async Task ConnectAsync()
    {
        _callbackLoop = RunCallbackLoopAsync(_cts.Token);
        _client.Connect();
        await _connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public async Task<string> BeginQrLoginAsync()
    {
        var auth = _client.Authentication;
        _qrSession = await auth.BeginAuthSessionViaQRAsync(new AuthSessionDetails
        {
            IsPersistentSession = true,
            PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_SteamClient,
            WebsiteID = "Client",
            DeviceFriendlyName = "GoldsrcNetClient",
            Authenticator = new UserConsoleAuthenticator(),
        });

        _qrSession.ChallengeURLChanged = OnChallengeUrlChanged;

        return RenderQrCode(_qrSession.ChallengeURL);
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

    private void OnChallengeUrlChanged()
    {
        Interlocked.Exchange(ref _qrUrl, RenderQrCode(_qrSession!.ChallengeURL));
    }

    private string? _qrUrl;

    public async Task WaitForLoginAsync()
    {
        if (_qrSession == null)
            throw new InvalidOperationException("BeginQrLoginAsync must be called first.");

        _loginResult = await _qrSession.PollingWaitForResultAsync(CancellationToken.None);

        _steamUser.LogOn(new SteamUser.LogOnDetails
        {
            Username = _loginResult.AccountName,
            AccessToken = _loginResult.RefreshToken,
            ShouldRememberPassword = true,
        });

        var logonResult = await _logonTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        if (logonResult != EResult.OK)
            throw new InvalidOperationException($"Steam logon failed: {logonResult}");
    }

    private async Task RunCallbackLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var cb = await _client.WaitForCallbackAsync(ct);
                await HandleCallback(cb);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LastError = $"Callback loop error: {ex.Message}";
            }
        }
    }

    private Task HandleCallback(CallbackMsg cb)
    {
        switch (cb)
        {
            case SteamClient.ConnectedCallback:
                _connectedTcs.TrySetResult();
                break;

            case SteamClient.DisconnectedCallback dc:
                if (!dc.UserInitiated)
                    LastError = "Disconnected from Steam.";
                break;

            case SteamUser.LoggedOnCallback logon:
                _logonTcs.TrySetResult(logon.Result);
                if (logon.Result == EResult.OK)
                {
                    _isLoggedOn = true;
                }
                else
                {
                    LastError = $"Logon failed: {logon.Result} ({logon.ExtendedResult})";
                }
                break;

            case SteamUser.LoggedOffCallback logoff:
                _isLoggedOn = false;
                LastError ??= "Logged off from Steam.";
                break;

            case SteamApps.GameConnectTokensCallback tokens:
                foreach (var t in tokens.Tokens)
                    _gameConnectTokens.Enqueue(t);
                break;
        }

        return Task.CompletedTask;
    }

    private byte[]? GetGameAuthBytesInternal()
    {
        if (!_isLoggedOn)
            return null;

        if (!_gameConnectTokens.TryDequeue(out var token))
            return null;

        var ticketTask = _steamApps.GetAppOwnershipTicket(_appId).ToTask();
        ticketTask.Wait(TimeSpan.FromSeconds(30));
        if (!ticketTask.IsCompletedSuccessfully)
            return null;

        var ticketResult = ticketTask.Result;
        if (ticketResult.Result != EResult.OK)
            return null;

        var ticket = ticketResult.Ticket;
        return BuildTicketBytes(token, ticket);
    }

    private static byte[] BuildTicketBytes(byte[] token, byte[] ticket)
    {
        using var ms = new MemoryStream();
        var tokenLen = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(tokenLen, token.Length);
        ms.Write(tokenLen);
        ms.Write(token);
        var ticketLen = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(ticketLen, ticket.Length);
        ms.Write(ticketLen);
        ms.Write(ticket);
        return ms.ToArray();
    }

    public void Dispose()
    {
        _isLoggedOn = false;
        _cts.Cancel();
        try { _steamUser.LogOff(); } catch { }
        try { _client.Disconnect(); } catch { }
        _cts.Dispose();
        _callbackLoop = null;
    }
}
