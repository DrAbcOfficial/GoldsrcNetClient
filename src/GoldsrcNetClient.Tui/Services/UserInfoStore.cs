using System.Text.Json;

namespace GoldsrcNetClient.Tui.Services;

public sealed record UserInfoData
{
    public string Name { get; init; } = "GoldsrcNetClient";
    public string Model { get; init; } = "gordon";
    public string TopColor { get; init; } = "0";
    public string BottomColor { get; init; } = "0";
    public string Rate { get; init; } = "20000";
    public string ClUpdaterate { get; init; } = "60";
}

public sealed class UserInfoStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _lock = new();
    private UserInfoData _data = new();

    public UserInfoData Data
    {
        get { lock (_lock) return _data with { }; }
    }

    public UserInfoStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GoldsrcNetClient",
            "userinfo.json");
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
                    _data = JsonSerializer.Deserialize<UserInfoData>(json) ?? new UserInfoData();
                }
                catch
                {
                    _data = new UserInfoData();
                }
            }
        }
    }

    public void Save(UserInfoData data)
    {
        lock (_lock)
        {
            _data = data;
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_data, JsonOptions);
            File.WriteAllText(_path, json);
        }
    }
}
