namespace GoldsrcNetClient.Core.Game;

/// <summary>
/// The built-in GoldSrc game profiles: the single source used by the DI
/// registration (<see cref="GoldsrcServiceCollectionExtensions.AddGoldsrcClient"/>)
/// and by container-less consumers constructing a <see cref="GameProfileResolver"/>
/// directly. Profiles are immutable and safe to share.
/// </summary>
public static class GoldsrcProfiles
{
    /// <summary>Half-Life, Counter-Strike, Condition Zero, and Sven Co-op.</summary>
    public static IReadOnlyList<IGameProfile> BuiltIn { get; } =
    [
        new HalfLifeProfile(),
        new CounterStrikeProfile(),
        new ConditionZeroProfile(),
        new SvenCoopProfile(),
    ];
}
