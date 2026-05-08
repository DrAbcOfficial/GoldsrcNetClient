using GoldsrcNetClient.Core.Network;
using Steamworks;

namespace GoldsrcNetClient.Cli;

public sealed class FacepunchSteamAuthProvider : ISteamAuthProvider, IDisposable
{
    private AuthTicket? _ticket;

    public bool IsAvailable => _ticket?.Data is { Length: > 0 };
    public string? LastError { get; private set; }

    public FacepunchSteamAuthProvider(uint appId = 70)
    {
        try
        {
            SteamClient.Init(appId);
            if (!SteamClient.IsValid)
            {
                LastError = $"SteamClient.Init({appId}) succeeded but SteamClient.IsValid is false. Ensure Steam client is running and you own AppID {appId}.";
                return;
            }

            _ticket = SteamUser.GetAuthSessionTicketAsync().GetAwaiter().GetResult();
            if (_ticket?.Data is not { Length: > 0 })
            {
                LastError = "SteamUser.GetAuthSessionTicketAsync() returned null or empty ticket.";
                _ticket = null;
            }
        }
        catch (DllNotFoundException ex)
        {
            LastError = $"Native Steam library not found: {ex.Message}. Ensure steam_api64.dll is present in the output directory.";
        }
        catch (Exception ex)
        {
            LastError = $"Steam init exception: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public byte GetAuthProtocol() => 3;

    public string GetRawAuthData()
    {
        if (_ticket?.Data is { Length: > 0 } data)
            return Convert.ToHexString(data).ToLowerInvariant();

        return "steam";
    }

    public byte[] GetRawAuthBytes()
    {
        if (_ticket?.Data is { Length: > 0 })
            return _ticket!.Data;

        return System.Text.Encoding.ASCII.GetBytes("steam");
    }

    public void Dispose()
    {
        _ticket?.Cancel();
        _ticket = null;
        try { SteamClient.Shutdown(); } catch { }
    }
}
