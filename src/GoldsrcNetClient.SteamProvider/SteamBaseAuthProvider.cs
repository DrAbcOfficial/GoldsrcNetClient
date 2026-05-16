using GoldsrcNetClient.Core.Network;

namespace GoldsrcNetClient.SteamProvider;

/// <summary>
/// Base Steam authentication provider
/// </summary>
public abstract class SteamBaseAuthProvider : ISteamAuthProvider, IDisposable
{
    /// <inheritdoc />
    public abstract bool IsAvailable { get; set; }
    /// <inheritdoc />
    public abstract void Dispose();
    /// <inheritdoc />
    public abstract byte GetAuthProtocol();
    /// <inheritdoc />
    public abstract string GetRawAuthData();
    /// <inheritdoc />
    public abstract byte[] GetRawAuthBytes();
    /// <inheritdoc />
    public abstract byte[] GetGameAuthBytes(uint appid, ulong serverSteamId, uint serverIp, ushort serverPort);
}
