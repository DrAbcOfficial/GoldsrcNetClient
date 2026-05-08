using GoldsrcNetClient.Core.Network;
using Steamworks;

namespace GoldsrcNetClient.Cli;

public sealed class FacepunchSteamAuthProvider : ISteamAuthProvider, IDisposable
{
    private AuthTicket? _ticket;

    public bool IsAvailable => _ticket?.Data is { Length: > 0 };

    public FacepunchSteamAuthProvider(uint appId = 70)
    {
        try
        {
            SteamClient.Init(appId);
            _ticket = SteamUser.GetAuthSessionTicket();
        }
        catch (Exception)
        {
            _ticket = null;
        }
    }

    public byte GetAuthProtocol() => 3;

    public string GetRawAuthData()
    {
        if (_ticket?.Data is { Length: > 0 } data)
            return Convert.ToHexString(data).ToLowerInvariant();

        return "steam";
    }

    public void Dispose()
    {
        _ticket?.Cancel();
        _ticket = null;
        try { SteamClient.Shutdown(); } catch { }
    }
}
