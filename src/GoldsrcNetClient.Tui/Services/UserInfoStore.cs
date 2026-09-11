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

public sealed class UserInfoStore(string? path = null) : JsonStore<UserInfoData>(path, "userinfo.json")
{
    protected override UserInfoData InitialValue => new();

    /// <summary>A defensive copy of the stored userinfo.</summary>
    public UserInfoData Data => Read() with { };

    public void Save(UserInfoData data)
    {
        Assign(data);
        Save();
    }
}
