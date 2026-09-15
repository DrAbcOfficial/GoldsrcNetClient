using GoldsrcNetClient.Core.Game;
using GoldsrcNetClient.Core.Handshake;
using GoldsrcNetClient.Core.Network;
using Microsoft.Extensions.DependencyInjection;

namespace GoldsrcNetClient.Core;

/// <summary>
/// Dependency-injection wiring for the client library. Register the library and
/// the game profiles once, then resolve <see cref="IGoldsrcConnectionFactory"/>
/// to create connections. The library only depends on
/// <c>Microsoft.Extensions.DependencyInjection.Abstractions</c> — the application
/// owns the container.
/// </summary>
/// <code>
/// var services = new ServiceCollection();
/// services.AddGoldsrcClient(o => o.LocalPort = 0);
/// services.AddGameProfile&lt;MyModProfile&gt;();
/// using var provider = services.BuildServiceProvider();
///
/// var connection = provider.GetRequiredService&lt;IGoldsrcConnectionFactory&gt;().Create(profile);
/// </code>
public static class GoldsrcServiceCollectionExtensions
{
    /// <summary>
    /// Registers the protocol client: the connection factory, the profile
    /// resolver, and the default transport. Register profiles with
    /// <see cref="AddGameProfile{TProfile}"/> (the built-in GoldSrc games are
    /// added automatically unless <see cref="GoldsrcClientOptions.IncludeBuiltInProfiles"/>
    /// is disabled).
    /// </summary>
    public static IServiceCollection AddGoldsrcClient(
        this IServiceCollection services, Action<GoldsrcClientOptions>? configure = null)
    {
        var options = new GoldsrcClientOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        if (options.IncludeBuiltInProfiles)
        {
            services.AddSingleton<IGameProfile, HalfLifeProfile>();
            services.AddSingleton<IGameProfile, CounterStrikeProfile>();
            services.AddSingleton<IGameProfile, ConditionZeroProfile>();
            services.AddSingleton<IGameProfile, SvenCoopProfile>();
        }

        services.AddSingleton<IGameProfileResolver>(sp =>
            new GameProfileResolver(sp.GetServices<IGameProfile>()));
        services.AddSingleton<IGoldsrcConnectionFactory, GoldsrcConnectionFactory>();
        return services;
    }

    /// <summary>
    /// Registers a game profile. Profiles are additive: registering a profile with
    /// an existing id or AppId overrides the earlier one for that key.
    /// </summary>
    public static IServiceCollection AddGameProfile<TProfile>(this IServiceCollection services)
        where TProfile : class, IGameProfile
    {
        services.AddSingleton<IGameProfile, TProfile>();
        return services;
    }

    /// <summary>Registers an already-constructed profile instance.</summary>
    public static IServiceCollection AddGameProfile(this IServiceCollection services, IGameProfile profile)
    {
        services.AddSingleton(profile);
        return services;
    }

    /// <summary>Replaces the Steam auth provider used for ticket requests.</summary>
    public static IServiceCollection AddSteamAuthProvider<TAuthProvider>(this IServiceCollection services)
        where TAuthProvider : class, ISteamAuthProvider
    {
        services.AddSingleton<ISteamAuthProvider, TAuthProvider>();
        return services;
    }
}

/// <summary>Options for <see cref="GoldsrcServiceCollectionExtensions.AddGoldsrcClient"/>.</summary>
public sealed class GoldsrcClientOptions
{
    /// <summary>Local UDP port to bind for outgoing connections (0 = OS-assigned).</summary>
    public int LocalPort { get; set; }

    /// <summary>Whether to register the built-in GoldSrc game profiles (Half-Life,
    /// Counter-Strike, Condition Zero, Sven Co-op). Default true.</summary>
    public bool IncludeBuiltInProfiles { get; set; } = true;

    /// <summary>Diagnostic stream dump path (see the <c>GOLDSRC_MSGDUMP</c> environment variable).</summary>
    public string? MessageDumpPath { get; set; }
}
