using Steamworks;

namespace GoldsrcNetClient.SteamProvider;

/// <summary>
/// Steam authentication provider using Facepunch Steamworks.NET.
/// Requires the Steam client to be running and the user to own the specified AppId.
/// </summary>
public sealed class SteamNetAuthProvider : SteamBaseAuthProvider
{
    private readonly uint _appId = 70;
    private byte[] _ticketData = [];

    /// <summary>Whether the Steam provider initialized successfully.</summary>
    public override bool IsAvailable { get; set; } = true;

    /// <summary>The last error message if initialization or auth failed.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Initialize steam API
    /// </summary>
    /// <param name="appId">start appId</param>
    public SteamNetAuthProvider(uint appId)
    {
        try
        {
            _appId = appId;
            Environment.SetEnvironmentVariable("SteamAppId", _appId.ToString());
            if (!SteamAPI.Init())
            {
                LastError = $"SteamAPI.Init() returned false. Ensure Steam client is running and you own AppID {_appId}.";
                IsAvailable = false;
            }
        }
        catch (DllNotFoundException ex)
        {
            LastError = $"Native Steam library not found: {ex.Message}. Ensure steam_api64.dll is present in the output directory.";
        }
        catch (Exception ex)
        {
            LastError = $"SteamAPI.Init() failed: {ex.GetType().Name}: {ex.Message}";
        }

    }

    /// <inheritdoc />
    public override byte GetAuthProtocol() => 3;

    /// <inheritdoc />
    public override string GetRawAuthData()
    {
        if (_ticketData.Length > 0)
            return Convert.ToHexString(_ticketData).ToLowerInvariant();

        return "steam";
    }

    /// <inheritdoc />
    public override byte[] GetRawAuthBytes()
    {
        if (_ticketData.Length > 0)
            return _ticketData;

        return System.Text.Encoding.UTF8.GetBytes("steam");
    }

    /// <inheritdoc />
    public override byte[] GetGameAuthBytes(uint appId, ulong serverSteamId, uint serverIp, ushort serverPort)
    {
        if(appId != _appId)
        {
            LastError = $"appId {appId} dismatch with this provider appId {_appId}";
            return [];
        }
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

    /// <inheritdoc />
    public override void Dispose()
    {
        _ticketData = [];
        IsAvailable = false;
        try { SteamAPI.Shutdown(); } catch { }
    }

    /// <summary>
    /// Get current user's SteamID
    /// </summary>
    /// <returns>SteamID</returns>
    public static ulong GetSteamID()
    {
        return SteamUser.GetSteamID().m_SteamID;
    }

    /// <summary>
    /// Get current user's personal name
    /// </summary>
    /// <returns>Steam personal name</returns>
    public static string GetSteamName()
    {
        return SteamFriends.GetPersonaName();
    }
}
