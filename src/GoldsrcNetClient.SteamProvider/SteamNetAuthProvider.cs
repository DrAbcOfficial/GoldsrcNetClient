using Steamworks;

namespace GoldsrcNetClient.SteamProvider;

/// <summary>
/// Steam authentication provider using Facepunch Steamworks.NET.
/// Requires the Steam client to be running and the user to own the specified AppId.
/// </summary>
public sealed class SteamNetAuthProvider : SteamBaseAuthProvider
{
    private byte[] _ticketData = [];

    /// <summary>Whether the Steam provider initialized successfully.</summary>
    public override bool IsAvailable { get; set; } = true;

    /// <summary>The last error message if initialization or auth failed.</summary>
    public string? LastError { get; private set; }

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
        try
        {
            Environment.SetEnvironmentVariable("SteamAppId", appId.ToString());
            if (!SteamAPI.Init())
            {
                LastError = $"SteamAPI.Init() returned false. Ensure Steam client is running and you own AppID {appId}.";
                IsAvailable = false;
                return [];
            }
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
        catch (DllNotFoundException ex)
        {
            LastError = $"Native Steam library not found: {ex.Message}. Ensure steam_api64.dll is present in the output directory.";
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
}
