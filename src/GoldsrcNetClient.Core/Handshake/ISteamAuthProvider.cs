namespace GoldsrcNetClient.Core.Handshake;

/// <summary>
/// Provides Steam authentication data for the GoldSrc connect handshake.
/// Implement this interface to supply real Steam auth tickets; the
/// default <see cref="NoSteamAuthProvider"/> sends a fake key for
/// servers that do not enforce Steam authentication.
/// </summary>
public interface ISteamAuthProvider
{
    /// <summary>Whether the Steam auth provider is available and ready.</summary>
    bool IsAvailable { get; }

    /// <summary>Returns the auth protocol version (1 = WON, 2 = hashed CD key, 3 = Steam).</summary>
    byte GetAuthProtocol();

    /// <summary>Returns the raw auth data string sent in the connect packet.</summary>
    string GetRawAuthData();

    /// <summary>Returns the raw auth bytes derived from <see cref="GetRawAuthData"/>.</summary>
    byte[] GetRawAuthBytes() => System.Text.Encoding.UTF8.GetBytes(GetRawAuthData());

    /// <summary>
    /// Returns the game auth ticket bytes for the target server.
    /// Defaults to <see cref="GetRawAuthBytes"/> when no game ticket is available.
    /// </summary>
    /// <param name="appid">The Steam AppId to authenticate with.</param>
    /// <param name="serverSteamId">The game server's SteamID64.</param>
    /// <param name="serverIp">The server IP as the raw four address bytes read as a 32-bit integer
    /// (the layout the engine passes to the Steam ticket API).</param>
    /// <param name="serverPort">The server port in network byte order (byte-swapped on little-endian).</param>
    /// <param name="vac2Secure">Whether the server advertised itself as VAC-secured.</param>
    byte[] GetGameAuthBytes(uint appid, ulong serverSteamId, uint serverIp, ushort serverPort, bool vac2Secure) => GetRawAuthBytes();
}
