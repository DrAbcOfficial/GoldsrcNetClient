namespace GoldsrcNetClient.Core.Game;

/// <summary>
/// Resolves the <see cref="IGameProfile"/> for a connection target: by short id
/// (e.g. <c>"svencoop"</c>) or by Steam AppId, falling back to Half-Life. The
/// set of profiles comes from dependency injection, not a static registry.
/// </summary>
public interface IGameProfileResolver
{
    /// <summary>All registered profiles.</summary>
    IReadOnlyCollection<IGameProfile> Profiles { get; }

    /// <summary>Gets a profile by short id (case-insensitive); <c>null</c> if unknown.</summary>
    IGameProfile? GetById(string id);

    /// <summary>Gets a profile by Steam AppId; <c>null</c> if unknown.</summary>
    IGameProfile? GetByAppId(uint appId);

    /// <summary>Resolves by id, then AppId, falling back to the Half-Life profile.</summary>
    IGameProfile Resolve(string? id, uint? appId);
}

/// <summary>Default resolver over the profiles registered with the service collection.</summary>
public sealed class GameProfileResolver : IGameProfileResolver
{
    private readonly Dictionary<string, IGameProfile> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, IGameProfile> _byAppId = [];
    private readonly IGameProfile _fallback;

    /// <summary>Creates the resolver over an explicit profile set. The first profile
    /// registered for a given id or AppId wins; duplicates are ignored.</summary>
    public GameProfileResolver(IEnumerable<IGameProfile> profiles)
    {
        var all = profiles.ToList();
        foreach (var profile in all)
        {
            _byId.TryAdd(profile.Id, profile);
            _byAppId.TryAdd(profile.AppId, profile);
        }

        // Fallback: the Half-Life profile if present, else the first registered.
        _fallback = _byId.GetValueOrDefault("hl") ?? all.FirstOrDefault()
            ?? throw new InvalidOperationException("No game profiles were registered.");
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IGameProfile> Profiles => _byId.Values;

    /// <inheritdoc />
    public IGameProfile? GetById(string id) => _byId.GetValueOrDefault(id);

    /// <inheritdoc />
    public IGameProfile? GetByAppId(uint appId) => _byAppId.GetValueOrDefault(appId);

    /// <inheritdoc />
    public IGameProfile Resolve(string? id, uint? appId)
    {
        if (id != null && _byId.TryGetValue(id, out var byId))
            return byId;
        if (appId != null && _byAppId.TryGetValue(appId.Value, out var byApp))
            return byApp;
        return _fallback;
    }
}
