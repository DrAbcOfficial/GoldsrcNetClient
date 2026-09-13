using GoldsrcNetClient.Core.Game;

namespace GoldsrcNetClient.Tui.Models;

public sealed class ServerConfig
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 27015;
    public uint AppId { get; set; } = 70;

    public string AppIdLabel =>
        GameLoginProviders.GetByAppId(AppId)?.DisplayName ?? $"Unknown ({AppId})";

    public override string ToString() => $"{Name} ({Host}:{Port}) [{AppIdLabel}]";

    public ServerConfig Clone() => new()
    {
        Name = Name,
        Host = Host,
        Port = Port,
        AppId = AppId
    };
}
