namespace GoldsrcNetClient.Core.Handshake;

/// <summary>
/// Default no-op <see cref="ISteamAuthProvider"/> that reports Steam as unavailable
/// and provides fake authentication data for servers that do not enforce Steam auth.
/// </summary>
public sealed class NoSteamAuthProvider : ISteamAuthProvider
{
    /// <inheritdoc/>
    public bool IsAvailable => false;

    /// <inheritdoc/>
    public byte GetAuthProtocol() => 3;

    /// <inheritdoc/>
    public string GetRawAuthData() => "steam";
}
