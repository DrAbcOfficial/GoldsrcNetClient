using GoldsrcNetClient.Core.Network;
using Steamworks;

namespace GoldsrcNetClient.SteamProvider;

/// <summary>
/// Steam authentication provider using Facepunch Steamworks.NET.
/// Requires the Steam client to be running and the user to own the specified AppId.
/// </summary>
public sealed class SteamNetAuthProvider : ISteamAuthProvider, IDisposable
{
    private byte[] _ticketData = [];

    /// <summary>Whether the Steam provider initialized successfully.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>The last error message if initialization or auth failed.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Initializes the Steamworks.NET provider for the given AppId.
    /// </summary>
    /// <param name="appId">Steam AppId to authenticate with. Default 70 (Half-Life).</param>
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

    /// <inheritdoc />
    public byte GetAuthProtocol() => 3;

    /// <inheritdoc />
    public string GetRawAuthData()
    {
        if (_ticketData.Length > 0)
            return Convert.ToHexString(_ticketData).ToLowerInvariant();

        return "steam";
    }

    /// <inheritdoc />
    public byte[] GetRawAuthBytes()
    {
        if (_ticketData.Length > 0)
            return _ticketData;

        return System.Text.Encoding.UTF8.GetBytes("steam");
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public void Dispose()
    {
        _ticketData = [];
        IsAvailable = false;
        try { SteamAPI.Shutdown(); } catch { }
    }
}
