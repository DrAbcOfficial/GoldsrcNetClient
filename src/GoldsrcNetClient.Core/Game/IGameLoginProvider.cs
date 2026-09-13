using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Core.Game;

/// <summary>
/// Per-game login and behavior profile for GoldSrc-engine games and mods
/// (Half-Life, Counter-Strike, Sven Co-op, ...).
/// </summary>
/// <remarks>
/// <para>This is the extension point for supporting new games: implement the
/// interface (or derive from <see cref="BaseGameLoginProvider"/>) and register
/// it with <see cref="GameLoginProviders.Register"/>. The profile bundles
/// everything the connection needs — Steam AppId, default userinfo, the
/// engine-branch <see cref="IEngineVariant"/> wire dialect, and the user
/// message handler — so the protocol layer never branches on game identities:</para>
///
/// <code>
/// public sealed class MyModLoginProvider : BaseGameLoginProvider
/// {
///     public override string Id => "mymod";
///     public override string DisplayName => "My Mod";
///     public override uint AppId => 123456;
///     public override IEngineVariant EngineVariant => myBranchVariant;
///     public override IServerMessageHandler CreateMessageHandler() => new MyModMessageHandler();
/// }
///
/// GameLoginProviders.Register(new MyModLoginProvider());
/// var profile = GameLoginProviders.GetByAppId(123456);
/// </code>
///
/// <para>Steam login itself is game-agnostic: an <see cref="ISteamAuthProvider"/>
/// produces the auth ticket for the profile's <see cref="AppId"/>.</para>
/// </remarks>
public interface IGameLoginProvider
{
    /// <summary>Short unique identifier, e.g. <c>"hl"</c>, <c>"cstrike"</c>, <c>"svencoop"</c>.</summary>
    string Id { get; }

    /// <summary>Human-readable game name.</summary>
    string DisplayName { get; }

    /// <summary>Steam AppId used for authentication ticket requests and game detection.</summary>
    uint AppId { get; }

    /// <summary>Default userinfo string sent in the connect packet.</summary>
    string DefaultUserInfo { get; }

    /// <summary>
    /// The engine-branch wire dialect (netchan encryption, fragment field widths,
    /// delta/entity bit widths, coordinate encoding, CRC handling) this game's
    /// servers speak. The connection and netchan consume this abstraction.
    /// </summary>
    IEngineVariant EngineVariant { get; }

    /// <summary>
    /// Creates the server message handler for this game. Called once per connection;
    /// a new instance (or a freshly reset one) should be returned per connection.
    /// </summary>
    IServerMessageHandler CreateMessageHandler();
}

/// <summary>Convenience base for game login providers, defaulting to the standard
/// Valve GoldSrc engine branch and userinfo.</summary>
public abstract class BaseGameLoginProvider : IGameLoginProvider
{
    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract string DisplayName { get; }

    /// <inheritdoc />
    public abstract uint AppId { get; }

    /// <inheritdoc />
    public virtual string DefaultUserInfo => GoldsrcEngineSettings.DefaultUserInfoTemplate;

    /// <inheritdoc />
    public virtual IEngineVariant EngineVariant => EngineVariants.Valve;

    /// <inheritdoc />
    public abstract IServerMessageHandler CreateMessageHandler();
}

/// <summary>
/// Registry of known game login providers. Built-in GoldSrc games are pre-registered;
/// additional games (mods, GoldSrc branches) can be registered at startup via
/// <see cref="Register"/> and then resolved by id or AppId.
/// </summary>
public static class GameLoginProviders
{
    private static readonly Dictionary<string, IGameLoginProvider> ById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<uint, IGameLoginProvider> ByAppId = new();

    static GameLoginProviders()
    {
        Register(new HalfLifeLoginProvider());
        Register(new CounterStrikeLoginProvider());
        Register(new ConditionZeroLoginProvider());
        Register(new SvenCoopLoginProvider());
    }

    /// <summary>Registers a provider, replacing any existing provider with the same id or AppId.</summary>
    public static void Register(IGameLoginProvider provider)
    {
        ById[provider.Id] = provider;
        ByAppId[provider.AppId] = provider;
    }

    /// <summary>Gets a provider by its short id (case-insensitive); <c>null</c> if unknown.</summary>
    public static IGameLoginProvider? GetById(string id) =>
        ById.TryGetValue(id, out var p) ? p : null;

    /// <summary>Gets a provider by Steam AppId; <c>null</c> if unknown.</summary>
    public static IGameLoginProvider? GetByAppId(uint appId) =>
        ByAppId.TryGetValue(appId, out var p) ? p : null;

    /// <summary>All registered providers.</summary>
    public static IEnumerable<IGameLoginProvider> All => ById.Values;

    /// <summary>Resolves a provider by id, falling back to AppId, falling back to the Half-Life profile.</summary>
    public static IGameLoginProvider Resolve(string? id, uint? appId)
    {
        if (id != null && GetById(id) is { } byId)
            return byId;
        if (appId != null && GetByAppId(appId.Value) is { } byApp)
            return byApp;
        return GetById("hl")!;
    }
}

/// <summary>Half-Life Deathmatch and other <c>valve</c> gamedir games (AppId 70).</summary>
public sealed class HalfLifeLoginProvider : BaseGameLoginProvider
{
    /// <inheritdoc />
    public override string Id => "hl";
    /// <inheritdoc />
    public override string DisplayName => "Half-Life";
    /// <inheritdoc />
    public override uint AppId => 70;
    /// <inheritdoc />
    public override IServerMessageHandler CreateMessageHandler() => new HalfLifeMessageHandler();
}

/// <summary>Counter-Strike 1.6 (AppId 10).</summary>
public sealed class CounterStrikeLoginProvider : BaseGameLoginProvider
{
    /// <inheritdoc />
    public override string Id => "cstrike";
    /// <inheritdoc />
    public override string DisplayName => "Counter-Strike";
    /// <inheritdoc />
    public override uint AppId => 10;
    /// <inheritdoc />
    public override IServerMessageHandler CreateMessageHandler() => new CounterStrikeMessageHandler();
}

/// <summary>Counter-Strike: Condition Zero (AppId 80).</summary>
public sealed class ConditionZeroLoginProvider : BaseGameLoginProvider
{
    /// <inheritdoc />
    public override string Id => "czero";
    /// <inheritdoc />
    public override string DisplayName => "Counter-Strike: Condition Zero";
    /// <inheritdoc />
    public override uint AppId => 80;
    /// <inheritdoc />
    public override IServerMessageHandler CreateMessageHandler() => new CounterStrikeMessageHandler();
}

/// <summary>Sven Co-op (AppId 225840), a standalone GoldSrc-branch game.</summary>
public sealed class SvenCoopLoginProvider : BaseGameLoginProvider
{
    /// <inheritdoc />
    public override string Id => "svencoop";
    /// <inheritdoc />
    public override string DisplayName => "Sven Co-op";
    /// <inheritdoc />
    public override uint AppId => 225840;

    /// <summary>
    /// The Sven Co-op engine branch: plaintext netchan (the munge functions are
    /// compiled into hw.dll as dead code with zero call sites), 32-bit fragment
    /// fields, 13-bit entity indices, 4-bit delta prefixes, wide coordinates,
    /// plaintext worldmap/spawn CRCs.
    /// </summary>
    /// <inheritdoc />
    public override IEngineVariant EngineVariant => EngineVariants.SvenCoop;

    /// <inheritdoc />
    public override IServerMessageHandler CreateMessageHandler() => new SvenCoopMessageHandler();
}
