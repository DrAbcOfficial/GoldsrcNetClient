namespace GoldsrcNetClient.Core.Network;

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
    /// <param name="serverSteamId">The server's Steam ID.</param>
    /// <param name="serverIp">The server's IP address as a 32-bit integer.</param>
    /// <param name="serverPort">The server's game port.</param>
    byte[] GetGameAuthBytes(ulong serverSteamId, uint serverIp, ushort serverPort) => GetRawAuthBytes();
}
