using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Messages.Parsing;
using Microsoft.Extensions.Logging;
using GoldsrcNetClient.Core.Messages.Users;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Core.Game;

/// <summary>
/// Per-game profile for a GoldSrc-engine game or mod (Half-Life, Counter-Strike,
/// Sven Co-op, ...). Bundles the Steam AppId, default userinfo, engine-branch
/// wire dialect, the user-message parser registrations, and optional per-session
/// behavior — so the protocol layer never branches on game identities.
/// </summary>
/// <remarks>
/// <para>
/// Register profiles with
/// <c>AddGameProfile&lt;TProfile&gt;()</c>
/// and resolve them through <see cref="IGameProfileResolver"/> — no static
/// registry. Supporting a new mod means writing one profile class:
/// </para>
/// <code>
/// public sealed class MyModProfile : IGameProfile
/// {
///     public string Id => "mymod";
///     public string DisplayName => "My Mod";
///     public uint AppId => 123456;
///     public IEngineVariant EngineVariant => EngineVariants.Valve;
///     public void RegisterMessages(ParserRegistry.Builder builder)
///     {
///         HalfLifeMessages.Register(builder);
///         builder.AddUser("MyMessage", static (ref BufferReader r) => new MyMessage(r.ReadUInt8()));
///     }
/// }
/// </code>
/// </remarks>
public interface IGameProfile
{
    /// <summary>Short unique identifier, e.g. <c>"hl"</c>, <c>"cstrike"</c>, <c>"svencoop"</c>.</summary>
    string Id { get; }

    /// <summary>Human-readable game name.</summary>
    string DisplayName { get; }

    /// <summary>Steam AppId used for authentication ticket requests and game detection.</summary>
    uint AppId { get; }

    /// <summary>Default userinfo string sent in the connect packet.</summary>
    string DefaultUserInfo => GoldsrcEngineSettings.DefaultUserInfoTemplate;

    /// <summary>
    /// The engine-branch wire dialect (netchan encryption, fragment field widths,
    /// delta/entity bit widths, coordinate encoding, CRC handling) this game's
    /// servers speak. The connection and netchan consume this abstraction.
    /// </summary>
    IEngineVariant EngineVariant => EngineVariants.Valve;

    /// <summary>
    /// Registers the user message parsers for this game. The profile decides the
    /// vocabulary: typically it calls a shared set (e.g.
    /// <see cref="HalfLifeMessages.Register"/>) and then adds or overrides its own
    /// names. Later registrations replace earlier ones for the same name.
    /// </summary>
    void RegisterMessages(ParserRegistry.Builder builder);

    /// <summary>
    /// Attaches per-session behavior to a connection (e.g. answering a game-specific
    /// request message). Called once per session, after the built-in protocol
    /// behaviors are wired and before <c>ConnectAsync</c>.
    /// </summary>
    void AttachSession(GoldsrcConnection connection)
    {
    }
}

/// <summary>Base class for profiles with sensible GoldSrc defaults (Valve dialect, standard userinfo).</summary>
public abstract class GameProfileBase : IGameProfile
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
    public abstract void RegisterMessages(ParserRegistry.Builder builder);

    /// <inheritdoc />
    public virtual void AttachSession(GoldsrcConnection connection)
    {
    }
}

/// <summary>Half-Life Deathmatch and other <c>valve</c> gamedir games (AppId 70).</summary>
public sealed class HalfLifeProfile : GameProfileBase
{
    /// <inheritdoc />
    public override string Id => "hl";
    /// <inheritdoc />
    public override string DisplayName => "Half-Life";
    /// <inheritdoc />
    public override uint AppId => 70;

    /// <inheritdoc />
    public override void RegisterMessages(ParserRegistry.Builder builder) => HalfLifeMessages.Register(builder);

    /// <inheritdoc />
    /// <remarks>ReqState: the game DLL's voice manager asks for the client's voice
    /// state; the vanilla client replies with <c>VModEnable 1</c>. Unanswered polls
    /// keep the server re-queueing state until its reliable channel overflows.</remarks>
    public override void AttachSession(GoldsrcConnection connection)
    {
        connection.Subscribe<ReqStateMessage>(message =>
        {
            _ = message;
            connection.Logger.LogDebug("[ReqState] replying VModEnable 1");
            _ = connection.SendStringCmdAsync(ClientCommandType.StringCmd, "VModEnable 1");
        });
    }
}

/// <summary>Counter-Strike 1.6 (AppId 10).</summary>
public class CounterStrikeProfile : GameProfileBase
{
    /// <inheritdoc />
    public override string Id => "cstrike";
    /// <inheritdoc />
    public override string DisplayName => "Counter-Strike";
    /// <inheritdoc />
    public override uint AppId => 10;

    /// <inheritdoc />
    public override void RegisterMessages(ParserRegistry.Builder builder) => CounterStrikeMessages.Register(builder);

    /// <inheritdoc />
    public override void AttachSession(GoldsrcConnection connection) => new HalfLifeProfile().AttachSession(connection);
}

/// <summary>Counter-Strike: Condition Zero (AppId 80).</summary>
public sealed class ConditionZeroProfile : CounterStrikeProfile
{
    /// <inheritdoc />
    public override string Id => "czero";
    /// <inheritdoc />
    public override string DisplayName => "Counter-Strike: Condition Zero";
    /// <inheritdoc />
    public override uint AppId => 80;
}

/// <summary>Sven Co-op (AppId 225840), a standalone GoldSrc-branch game.</summary>
public sealed class SvenCoopProfile : GameProfileBase
{
    /// <inheritdoc />
    public override string Id => "svencoop";
    /// <inheritdoc />
    public override string DisplayName => "Sven Co-op";
    /// <inheritdoc />
    public override uint AppId => 225840;

    /// <summary>
    /// The Sven Co-op engine branch: netchan munge compiled to dead code, 32-bit
    /// fragment fields, 13-bit entity indices, 4-bit delta prefixes, wide
    /// coordinates, plaintext worldmap/spawn CRCs.
    /// </summary>
    /// <inheritdoc />
    public override IEngineVariant EngineVariant => EngineVariants.SvenCoop;

    /// <inheritdoc />
    public override void RegisterMessages(ParserRegistry.Builder builder) => SvenCoopMessages.Register(builder);

    /// <inheritdoc />
    public override void AttachSession(GoldsrcConnection connection) => new HalfLifeProfile().AttachSession(connection);
}
