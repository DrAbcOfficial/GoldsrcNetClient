using GoldsrcNetClient.Tui.Models;

namespace GoldsrcNetClient.Tui.Services;

public sealed class ServerConfigStore(string? path = null) : JsonStore<List<ServerConfig>>(path, "servers.json")
{
    protected override List<ServerConfig> InitialValue => [];

    /// <summary>A snapshot of the saved server configurations.</summary>
    public IReadOnlyList<ServerConfig> Configs => [.. Read()];

    public void Add(ServerConfig config)
    {
        Update(configs => configs.Add(config));
        Save();
    }

    public void Update(int index, ServerConfig config)
    {
        Update(configs => { if ((uint)index < configs.Count) configs[index] = config; });
        Save();
    }

    public void Remove(int index)
    {
        Update(configs => { if ((uint)index < configs.Count) configs.RemoveAt(index); });
        Save();
    }

    public void MoveUp(int index)
    {
        Update(configs =>
        {
            if (index > 0 && index < configs.Count)
                (configs[index - 1], configs[index]) = (configs[index], configs[index - 1]);
        });
        Save();
    }

    public void MoveDown(int index)
    {
        Update(configs =>
        {
            if (index >= 0 && index < configs.Count - 1)
                (configs[index], configs[index + 1]) = (configs[index + 1], configs[index]);
        });
        Save();
    }
}
