using GoldsrcNetClient.Core.Handshake;
using GoldsrcNetClient.SteamProvider;

namespace GoldsrcNetClient.Tui;

public sealed class AppData
{
    public string UserInfo { get; set; } = "\\name\\GoldsrcNetClient\\protocol\\48\\cl_lc\\1\\cl_lw\\1\\cl_updaterate\\60\\rate\\20000\\hltv\\0";
    public LoginMethod LoginMethod { get; set; } = LoginMethod.NoSteam;

    /// <summary>Steam API (steam_api.dll) provider. Initialized on demand; may be null or unavailable.</summary>
    public SteamNetAuthProvider? SteamApiProvider { get; set; }

    /// <summary>SteamKit (QR login) provider. Initialized when the user completes a QR login.</summary>
    public SteamKitAuthProvider? SteamKitProvider { get; set; }

    /// <summary>The provider matching the selected login method, or null for NoSteam.</summary>
    public ISteamAuthProvider? AuthProvider => LoginMethod switch
    {
        LoginMethod.SteamApi => SteamApiProvider,
        LoginMethod.SteamKit => SteamKitProvider,
        _ => null
    };

    public string? SteamUsername { get; set; }
    public ulong? SteamId { get; set; }

    /// <summary>Disposes every provider; called at application shutdown.</summary>
    public void DisposeProviders()
    {
        SteamApiProvider?.Dispose();
        SteamApiProvider = null;
        SteamKitProvider?.Dispose();
        SteamKitProvider = null;
    }
}
