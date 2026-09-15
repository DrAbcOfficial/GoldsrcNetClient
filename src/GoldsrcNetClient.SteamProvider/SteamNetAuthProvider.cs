using Steamworks;

namespace GoldsrcNetClient.SteamProvider;

/// <summary>
/// Steam authentication provider using Facepunch Steamworks.NET.
/// Requires the Steam client to be running and the user to own the specified AppId.
/// </summary>
public sealed class SteamNetAuthProvider : SteamAuthProviderBase
{
    private readonly uint _appId = 70;
    private byte[] _ticketData = [];
    private bool _initialized;

    /// <summary>Whether the Steam provider initialized successfully.</summary>
    public bool IsInitialized => IsAvailable;

    /// <summary>The last error message if initialization or auth failed.</summary>
    public string? LastError { get; private set; }

    /// <summary>The AppId this provider was initialized with.</summary>
    public uint AppId => _appId;

    /// <summary>
    /// Initialize steam API
    /// </summary>
    /// <param name="appId">start appId</param>
    public SteamNetAuthProvider(uint appId)
    {
        try
        {
            _appId = appId;
            // The native steam_api reads this before Init; setting it lets the
            // process initialize outside of a Steam launch.
            Environment.SetEnvironmentVariable("SteamAppId", _appId.ToString());

            ESteamAPIInitResult result = SteamAPI.InitEx(out string errMsg);
            if (result == ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                _initialized = true;
                IsAvailable = true;
            }
            else
            {
                IsAvailable = false;
                LastError = $"SteamAPI.Init failed ({result}): {errMsg}. " +
                            $"Ensure the Steam client is running and you own AppID {_appId}.";
            }
        }
        catch (DllNotFoundException ex)
        {
            IsAvailable = false;
            LastError = $"Native Steam library not found: {ex.Message}. Ensure steam_api64.dll is present in the output directory.";
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            LastError = $"SteamAPI.Init failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <inheritdoc />
    public override string GetRawAuthData() => _ticketData.Length > 0
        ? Convert.ToHexString(_ticketData).ToLowerInvariant()
        : PlaceholderAuthData;

    /// <inheritdoc />
    public override byte[] GetRawAuthBytes()
    {
        if (_ticketData.Length > 0)
            return _ticketData;

        return System.Text.Encoding.UTF8.GetBytes("steam");
    }

    /// <inheritdoc />
    public override byte[] GetGameAuthBytes(uint appId, ulong serverSteamId, uint serverIp, ushort serverPort, bool vac2Secure)
    {
        if (appId != _appId)
        {
            LastError = $"appId {appId} dismatch with this provider appId {_appId}";
            return [];
        }
        try
        {
            var blob = new byte[4096];
            var steamId = new CSteamID(serverSteamId);
            int resultLen = SteamUser.InitiateGameConnection_DEPRECATED(
                blob, blob.Length, steamId, serverIp, serverPort, vac2Secure);

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
        if (_initialized)
        {
            _initialized = false;
            try { SteamAPI.Shutdown(); } catch { }
        }
    }

    /// <summary>
    /// Gets the logged-in user's persona name, or null when Steam is not initialized.
    /// </summary>
    public string? GetSteamName()
    {
        if (!_initialized)
            return null;
        try
        {
            string? name = SteamFriends.GetPersonaName();
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (Exception ex)
        {
            LastError = $"GetPersonaName failed: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// Gets the logged-in user's SteamID, or null when Steam is not initialized.
    /// </summary>
    public ulong? GetSteamID()
    {
        if (!_initialized)
            return null;
        try
        {
            return SteamUser.GetSteamID().m_SteamID;
        }
        catch (Exception ex)
        {
            LastError = $"GetSteamID failed: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// Pumps the Steam callback queue. Call periodically (e.g. from a UI timer)
    /// while the provider is initialized so Steam client state stays fresh.
    /// </summary>
    public void PumpCallbacks()
    {
        if (!_initialized)
            return;
        try { SteamAPI.RunCallbacks(); } catch { }
    }
}
