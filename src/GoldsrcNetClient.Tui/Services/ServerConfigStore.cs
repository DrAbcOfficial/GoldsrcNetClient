using System.Text.Json;
using GoldsrcNetClient.Tui.Models;

namespace GoldsrcNetClient.Tui.Services;

public sealed class ServerConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _lock = new();
    private List<ServerConfig> _configs = [];

    public IReadOnlyList<ServerConfig> Configs
    {
        get { lock (_lock) return _configs.ToList(); }
    }

    public ServerConfigStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GoldsrcNetClient",
            "servers.json");
    }

    public void Load()
    {
        lock (_lock)
        {
            if (File.Exists(_path))
            {
                try
                {
                    var json = File.ReadAllText(_path);
                    _configs = JsonSerializer.Deserialize<List<ServerConfig>>(json) ?? [];
                }
                catch
                {
                    _configs = [];
                }
            }
            else
            {
                _configs = [];
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_configs, JsonOptions);
            File.WriteAllText(_path, json);
        }
    }

    public void Add(ServerConfig config)
    {
        lock (_lock) { _configs.Add(config); }
        Save();
    }

    public void Update(int index, ServerConfig config)
    {
        lock (_lock)
        {
            if (index >= 0 && index < _configs.Count)
                _configs[index] = config;
        }
        Save();
    }

    public void Remove(int index)
    {
        lock (_lock)
        {
            if (index >= 0 && index < _configs.Count)
                _configs.RemoveAt(index);
        }
        Save();
    }

    public void MoveUp(int index)
    {
        lock (_lock)
        {
            if (index > 0 && index < _configs.Count)
                (_configs[index - 1], _configs[index]) = (_configs[index], _configs[index - 1]);
        }
        Save();
    }

    public void MoveDown(int index)
    {
        lock (_lock)
        {
            if (index >= 0 && index < _configs.Count - 1)
                (_configs[index], _configs[index + 1]) = (_configs[index + 1], _configs[index]);
        }
        Save();
    }
}
