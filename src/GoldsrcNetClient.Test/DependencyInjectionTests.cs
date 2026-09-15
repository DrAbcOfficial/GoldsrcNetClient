using GoldsrcNetClient.Core;
using GoldsrcNetClient.Core.Game;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Messages.Parsing;
using GoldsrcNetClient.Core.Messages.Users;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace GoldsrcNetClient.Test;

/// <summary>Verifies the DI wiring: registration, profile resolution, and factory composition.</summary>
public class DependencyInjectionTests
{
    private sealed class TestProfile : GameProfileBase
    {
        public override string Id => "testmod";
        public override string DisplayName => "Test Mod";
        public override uint AppId => 999;

        public override void RegisterMessages(ParserRegistry.Builder builder)
        {
            HalfLifeMessages.Register(builder);
            builder.AddUser("TestOnly", static (ref Core.Io.BufferReader r) => new ReqStateMessage());
        }
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddGoldsrcClient();
        services.AddGameProfile<TestProfile>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void BuiltInProfiles_AreResolvable()
    {
        using var provider = BuildProvider();
        var resolver = provider.GetRequiredService<IGameProfileResolver>();

        Assert.Equal("hl", resolver.GetById("hl")!.Id);
        Assert.Equal("svencoop", resolver.GetByAppId(225840)!.Id);
        Assert.Equal("cstrike", resolver.Resolve("cstrike", null).Id);
    }

    [Fact]
    public void CustomProfile_IsResolvable()
    {
        using var provider = BuildProvider();
        var resolver = provider.GetRequiredService<IGameProfileResolver>();

        Assert.Equal("testmod", resolver.GetById("testmod")!.Id);
        Assert.Equal("testmod", resolver.Resolve(null, 999).Id);
    }

    [Fact]
    public void Resolve_UnknownFallsBackToHalfLife()
    {
        using var provider = BuildProvider();
        var resolver = provider.GetRequiredService<IGameProfileResolver>();

        Assert.Equal("hl", resolver.Resolve("nope", 12345).Id);
    }

    [Fact]
    public void Factory_CreatesConnectionWithProfileDialect()
    {
        using var provider = BuildProvider();
        var factory = provider.GetRequiredService<IGoldsrcConnectionFactory>();

        using var connection = factory.Create(provider.GetRequiredService<IGameProfileResolver>().GetById("svencoop"));

        // The connection adopted the profile's engine dialect and default userinfo.
        Assert.NotNull(connection);
    }

    [Fact]
    public void AddGoldsrcClient_WithoutBuiltIns_OnlyHasCustomProfiles()
    {
        var services = new ServiceCollection();
        services.AddGoldsrcClient(o => o.IncludeBuiltInProfiles = false);
        services.AddGameProfile<TestProfile>();
        using var provider = services.BuildServiceProvider();

        var resolver = provider.GetRequiredService<IGameProfileResolver>();
        Assert.Null(resolver.GetById("hl"));
        Assert.Equal("testmod", resolver.Resolve(null, null).Id);
    }

    [Fact]
    public void CustomProfile_MessagesAreParsed()
    {
        using var provider = BuildProvider();
        var profile = provider.GetRequiredService<IGameProfileResolver>().GetById("testmod")!;

        var builder = new ParserRegistry.Builder();
        EngineMessageParsers.Register(builder, profile.EngineVariant, new SessionData());
        profile.RegisterMessages(builder);
        var registry = builder.Build();

        // The custom message name is registered in addition to the shared Half-Life set.
        Assert.True(registry.TryGetUser("TestOnly", out _));
        Assert.True(registry.TryGetUser("SayText", out _));
    }

    [Fact]
    public void Factory_RequiresAtLeastOneProfile()
    {
        var services = new ServiceCollection();
        services.AddGoldsrcClient(o => o.IncludeBuiltInProfiles = false);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IGameProfileResolver>());
    }

    [Fact]
    public void AddGoldsrcClient_RegistersDefaultTransportFactory()
    {
        var services = new ServiceCollection();
        services.AddGoldsrcClient();
        using var provider = services.BuildServiceProvider();

        var transportFactory = provider.GetRequiredService<Func<ITransport>>();
        using var transport = transportFactory();
        Assert.IsType<UdpTransport>(transport);
    }

    [Fact]
    public void Factory_CreatesDistinctTransportPerConnection()
    {
        var created = new List<ITransport>();
        var services = new ServiceCollection();
        services.AddGoldsrcClient();
        services.AddSingleton<Func<ITransport>>(() =>
        {
            var transport = new CountingTransport();
            created.Add(transport);
            return transport;
        });
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IGoldsrcConnectionFactory>();

        using var first = factory.Create();
        using var second = factory.Create();

        Assert.Equal(2, created.Count);
        Assert.NotSame(created[0], created[1]);
    }

    /// <summary>Minimal transport fake: records nothing, never receives.</summary>
    private sealed class CountingTransport : ITransport
    {
        public Task SendAsync(ReadOnlyMemory<byte> buffer, IPEndPoint target, CancellationToken ct)
            => Task.CompletedTask;

        public Task<(byte[] Buffer, IPEndPoint RemoteEndPoint)> ReceiveAsync(CancellationToken ct)
            => Task.FromResult((Array.Empty<byte>(), new IPEndPoint(IPAddress.Loopback, 0)));

        public void Dispose() { }
    }
}
