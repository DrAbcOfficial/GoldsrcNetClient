using GoldsrcNetClient.Core.Handshake;
using System.Text;

namespace GoldsrcNetClient.SteamProvider;

/// <summary>
/// Shared behavior for the Steam auth providers: the Steam auth protocol byte,
/// the <c>"steam"</c> placeholder used when no ticket is available, the
/// <see cref="IsAvailable"/> flag (settable only by the provider itself), and a
/// no-op <see cref="Dispose"/>. Concrete providers supply the ticket data.
/// </summary>
public abstract class SteamAuthProviderBase : ISteamAuthProvider, IDisposable
{
    /// <summary>Auth protocol byte for Steam-authenticated connections.</summary>
    protected const byte AuthProtocolSteam = 3;

    /// <summary>Placeholder raw auth value sent when no real ticket is available.</summary>
    protected const string PlaceholderAuthData = "steam";

    /// <inheritdoc />
    public bool IsAvailable { get; protected set; }

    /// <inheritdoc />
    public virtual byte GetAuthProtocol() => AuthProtocolSteam;

    /// <inheritdoc />
    public abstract string GetRawAuthData();

    /// <inheritdoc />
    public virtual byte[] GetRawAuthBytes() => Encoding.UTF8.GetBytes(GetRawAuthData());

    /// <inheritdoc />
    public virtual byte[] GetGameAuthBytes(uint appid, ulong serverSteamId, uint serverIp, ushort serverPort, bool vac2Secure)
        => GetRawAuthBytes();

    /// <inheritdoc />
    public virtual void Dispose()
    {
    }
}
