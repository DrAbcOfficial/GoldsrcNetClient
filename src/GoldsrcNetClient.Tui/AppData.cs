using GoldsrcNetClient.Core.Network;

namespace GoldsrcNetClient.Tui;

public sealed class AppData
{
    public string UserInfo { get; set; } = "\\name\\GoldsrcNetClient\\protocol\\48\\cl_lc\\1\\cl_lw\\1\\cl_updaterate\\60\\rate\\20000\\hltv\\0";
    public ISteamAuthProvider? AuthProvider { get; set; }
    public LoginMethod LoginMethod { get; set; } = LoginMethod.NoSteam;
    public IDisposable? AuthDisposable { get; set; }
    public uint SteamAppId { get; set; } = 70;
}
