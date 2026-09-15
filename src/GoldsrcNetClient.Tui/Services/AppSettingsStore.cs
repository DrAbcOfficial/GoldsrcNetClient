namespace GoldsrcNetClient.Tui.Services;

public sealed record AppSettingsData
{
    public LoginMethod LoginMethod { get; init; } = LoginMethod.NoSteam;
}

/// <summary>Persists app-level settings (login method) to settings.json. Live Steam
/// sessions are not persisted — reconnecting re-initializes them on demand.</summary>
public sealed class AppSettingsStore(string? path = null) : JsonStore<AppSettingsData>(path, "settings.json")
{
    protected override AppSettingsData InitialValue => new();

    /// <summary>A defensive copy of the stored settings.</summary>
    public AppSettingsData Data => Read() with { };

    public void Save(AppSettingsData data)
    {
        Assign(data);
        Save();
    }
}
