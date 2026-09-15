using GoldsrcNetClient.Core.Io;

namespace GoldsrcNetClient.Core.Messages.Parsing;

/// <summary>
/// Parses one engine (<c>svc_*</c>) message from the stream. The parser owns
/// the reader: it must advance past exactly its own payload. A malformed or
/// truncated payload surfaces as <see cref="EndOfBufferException"/> /
/// <see cref="InvalidDataException"/>, which the pipeline turns into
/// "discard the rest of the packet" — the engine's malformed-stream behaviour.
/// </summary>
/// <param name="reader">Reader positioned after the message type byte.</param>
public delegate IServerMessage EngineMessageParser(ref BufferReader reader);

/// <summary>
/// Parses one user-registered message payload (already framed by the pipeline:
/// the reader is bounded to exactly the announced payload length).
/// </summary>
/// <param name="reader">Bounded reader over the payload.</param>
public delegate IServerMessage UserMessageParser(ref BufferReader reader);

/// <summary>
/// Frozen, connection-scoped parser lookup: engine messages by type byte
/// (256-slot array — no hashing on the hot path), user messages by wire name.
/// Build one per connection from a <see cref="ParserRegistryBuilder"/>; register
/// game-profile parsers before building.
/// </summary>
public sealed class ParserRegistry
{
    private readonly EngineMessageParser?[] _engine;
    private readonly IReadOnlyDictionary<string, UserMessageParser> _users;

    private ParserRegistry(EngineMessageParser?[] engine, IReadOnlyDictionary<string, UserMessageParser> users)
    {
        _engine = engine;
        _users = users;
    }

    /// <summary>Parser for an engine message type byte, or null when unknown
    /// (the pipeline discards the rest of the packet, matching the engine).</summary>
    public EngineMessageParser? GetEngine(byte type) => _engine[type];

    /// <summary>Parser registered for a user message name (case-sensitive wire name).</summary>
    public bool TryGetUser(string name, out UserMessageParser parser) => _users.TryGetValue(name, out parser!);

    /// <summary>All user message names with a registered parser.</summary>
    public IEnumerable<string> UserNames => _users.Keys;

    /// <summary>Collects parser registrations, then freezes into a <see cref="ParserRegistry"/>.
    /// Re-registering the same slot replaces the previous parser — the mechanism
    /// game profiles use to override shared message layouts (e.g. Sven Co-op's
    /// wider CurWeapon).</summary>
    public sealed class Builder
    {
        private readonly EngineMessageParser?[] _engine = new EngineMessageParser?[256];
        private readonly Dictionary<string, UserMessageParser> _users = new(StringComparer.Ordinal);

        /// <summary>Registers (or replaces) the parser for an engine message type byte.</summary>
        public Builder AddEngine(byte type, EngineMessageParser parser)
        {
            _engine[type] = parser;
            return this;
        }

        /// <summary>Registers (or replaces) the parser for a user message wire name.</summary>
        public Builder AddUser(string name, UserMessageParser parser)
        {
            _users[name] = parser;
            return this;
        }

        /// <summary>Copies every registration from another contributor (e.g. the shared
        /// Half-Life message set) — later <c>Add</c> calls still win.</summary>
        public Builder AddFrom(Action<Builder> register)
        {
            register(this);
            return this;
        }

        /// <summary>Freezes the registrations into a lookup.</summary>
        public ParserRegistry Build() => new(_engine, _users);
    }
}
