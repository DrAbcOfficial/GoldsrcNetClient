using GoldsrcNetClient.Core.Game;
using GoldsrcNetClient.Core.Handshake;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// Creates configured <see cref="GoldsrcConnection"/> instances. Resolve it from
/// dependency injection so the container supplies the logger, auth provider, and
/// transport; connections themselves are short-lived and must be disposed.
/// </summary>
public interface IGoldsrcConnectionFactory
{
    /// <summary>
    /// Creates a connection. The profile supplies the wire dialect, message
    /// parsers, default userinfo, and session behavior.
    /// </summary>
    /// <param name="profile">Game profile; defaults to the container's fallback (Half-Life).</param>
    /// <param name="authProvider">Steam auth provider; defaults to the container's, then a no-steam stub.</param>
    /// <param name="transport">UDP transport; defaults to the container's, then a real socket.</param>
    GoldsrcConnection Create(IGameProfile? profile = null, ISteamAuthProvider? authProvider = null, ITransport? transport = null);
}

/// <inheritdoc />
public sealed class GoldsrcConnectionFactory(
    IServiceProvider services,
    IGameProfileResolver resolver,
    GoldsrcClientOptions options) : IGoldsrcConnectionFactory
{
    private readonly IGameProfileResolver _resolver = resolver;
    private readonly GoldsrcClientOptions _options = options;
    private readonly ILogger<GoldsrcConnection> _logger =
        services.GetService<ILogger<GoldsrcConnection>>() ?? NullLogger<GoldsrcConnection>.Instance;
    private readonly ISteamAuthProvider? _authProvider = services.GetService<ISteamAuthProvider>();
    private readonly ITransport? _transport = services.GetService<ITransport>();

    /// <inheritdoc />
    public GoldsrcConnection Create(
        IGameProfile? profile = null,
        ISteamAuthProvider? authProvider = null,
        ITransport? transport = null)
    {
        var connection = new GoldsrcConnection(
            _logger,
            authProvider ?? _authProvider,
            profile ?? _resolver.Resolve(null, null),
            _options.LocalPort,
            transport ?? _transport);
        if (_options.MessageDumpPath is not null)
            connection.Settings.MessageDumpPath = _options.MessageDumpPath;
        return connection;
    }
}
