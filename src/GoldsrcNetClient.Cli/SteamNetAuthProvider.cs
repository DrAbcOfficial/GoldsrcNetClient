using GoldsrcNetClient.Core.Network;
using Steamworks;

namespace GoldsrcNetClient.Cli;

public sealed class SteamNetAuthProvider : ISteamAuthProvider, IDisposable
{
    private byte[] _ticketData = [];

    public bool IsAvailable { get; private set; }
    public string? LastError { get; private set; }

    public SteamNetAuthProvider(uint appId = 70)
    {
        try
        {
            Environment.SetEnvironmentVariable("SteamAppId", appId.ToString());
            if (!SteamAPI.Init())
            {
                LastError = $"SteamAPI.Init() returned false. Ensure Steam client is running and you own AppID {appId}.";
                return;
            }

            IsAvailable = true;
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
        if (_ticketData.Length > 0)
            return Convert.ToHexString(_ticketData).ToLowerInvariant();

        return "steam";
    }

    public byte[] GetRawAuthBytes()
    {
        if (_ticketData.Length > 0)
            return _ticketData;

        return System.Text.Encoding.UTF8.GetBytes("steam");
    }

    public byte[] GetGameAuthBytes(ulong serverSteamId, uint serverIp, ushort serverPort)
    {
        if (!IsAvailable)
            return GetRawAuthBytes();

        try
        {
            var blob = new byte[4096];
            var steamId = new CSteamID(serverSteamId);
            int resultLen = SteamUser.InitiateGameConnection_DEPRECATED(
                blob, blob.Length, steamId, serverIp, serverPort, false);

            if (resultLen > 0)
            {
                _ticketData = blob[..resultLen];
                return _ticketData;
            }

            LastError = $"InitiateGameConnection returned {resultLen} (expected > 0).";
        }
        catch (Exception ex)
        {
            LastError = $"InitiateGameConnection_DEPRECATED failed: {ex.GetType().Name}: {ex.Message}";
        }

        return GetRawAuthBytes();
    }

    public void Dispose()
    {
        _ticketData = [];
        IsAvailable = false;
        try { SteamAPI.Shutdown(); } catch { }
    }
}
