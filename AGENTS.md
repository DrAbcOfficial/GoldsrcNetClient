# AGENTS.md — GoldSrc NetClient

## Build & Test

```bash
dotnet build   # from repo root; solution uses .slnx format (XML)
dotnet test    # xunit + coverlet, single test project: src/GoldsrcNetClient.Test
```

Single test execution:

```bash
dotnet test --filter "FullyQualifiedName~MungeTests"
```

Target framework is `net10.0` (C# 14) — requires .NET 10 SDK.

## Project Layout

```
src/
  GoldsrcNetClient.Core/           # protocol library (NuGet package, AllowUnsafeBlocks)
  GoldsrcNetClient.Cli/            # CLI tool (entrypoint: Program.cs, command: ConnectCommand)
  GoldsrcNetClient.SteamProvider/  # Steam auth providers (Steamworks.NET + SteamKit2)
  GoldsrcNetClient.Tui/            # terminal UI front-end
  GoldsrcNetClient.Test/           # xunit tests
```

- **Core** has no external dependencies (except SharpZipLib for BZ2) — a pure C# UDP
  client library for the GoldSrc (HL1) engine network protocol.
- **Cli** references Core + SteamProvider; uses CliFx for CLI parsing.
- **Test** only references Core (sees `internal` members via `InternalsVisibleTo`).

## Core Architecture

Dependencies point one way: `Io` → `Protocol`/`Delta` → `Messages` → `Network` →
`Game`/profiles; the application (CLI/TUI) is the composition root.

```
Core/
  Io/                               # byte/bit codec (ref structs over spans)
    BufferReader.cs                 #   unified byte+bit reader (engine bf_read), one bit position
    BufferWriter.cs                 #   growable byte+bit writer
    EndOfBufferException.cs         #   underflow -> discard rest of packet
    HexExtensions.cs                #   ToHexPreview for logs
  Messages/
    IServerMessage.cs               # marker for every parsed message (engine + user)
    MessageHub.cs                   # typed pub/sub: Subscribe<T>(Action<T>)
    MessagePipeline.cs              # packet -> parser -> hub; user-message framing
    RawUserMessage.cs               # user message with no registered parser
    Engine/EngineMessages.cs        # one record per svc_* (Print, ServerInfo, ...)
    Users/                          # one record per user message + the game message sets
      UserMessages.cs               #   all user-message records (HL/CS/Sven)
      HalfLifeMessages.cs           #   Register(builder) - shared base vocabulary
      CounterStrikeMessages.cs      #   Register(builder) - CS on top of HL
      SvenCoopMessages.cs           #   Register(builder) - Sven on top of HL (73 parsers)
    Parsing/ParserRegistry.cs       # 256-slot engine table + name-keyed user parsers (replace semantics)
    Parsing/EngineMessageParsers.cs #   the built-in svc_* parsers (branch-aware)
  Network/
    GoldsrcConnection.cs            # UDP receive loop, keepalive, send API, Messages hub
    GoldsrcConnection.Behaviors.cs  #   protocol-internal replies (userinfo, cvars, registry feed)
    GoldsrcConnectionFactory.cs     # IGoldsrcConnectionFactory (from DI)
    SignonController.cs             #   signon state machine (sendres -> spawn -> sendents)
    HandshakeState.cs / SessionData.cs  # handshake vs connected-session state
    Transport.cs                    # ITransport + UdpTransport (socket seam for tests)
    SplitPacketReassembler.cs / TimerResolution.cs
  Handshake/                        # getchallenge -> connect -> approval
  Netchan/                          # sequenced channel: reliability, fragments, Munge2
  Delta/                            # delta definitions, reader, meta field descriptions
  Protocol/                         # enums, wire structs, EngineVariant dialect, UserInfoString, UserCmd
  Game/
    IGameProfile.cs                 # per-mod profile: Id/AppId/dialect + RegisterMessages + AttachSession
    GameProfileResolver.cs          # id/appId resolution over the DI-registered profiles
  DependencyInjection/GoldsrcServiceCollectionExtensions.cs  # AddGoldsrcClient / AddGameProfile
  Munge/ Query/                     # Munge ciphers; A2S_INFO query
```

### Messages and DI (the extension seam)

- **Parsing**: each message is a record implementing `IServerMessage`. Engine messages
  live in `Messages/Engine/`, user messages in `Messages/Users/`. A parser is
  `static (ref BufferReader) => new XMessage(...)`, registered through
  `ParserRegistry.Builder` — `AddEngine(typeByte, parser)` for `svc_*`,
  `AddUser(name, parser)` for server-registered user messages. **Re-registering a
  slot replaces it**, which is exactly how Sven Co-op overrides the shared
  Half-Life layouts with its own wider wire formats.
- **Consuming**: subscribe by type — `connection.Subscribe<SayTextMessage>(m => ...)`
  (or `connection.Messages.Subscribe<...>`). There are no per-message C# events and
  no god handler classes; adding a mod message is one record plus one `AddUser` line.
  Unregistered user messages arrive as `RawUserMessage` (raw bytes preserved).
- **Error semantics** (engine-faithful): an unknown engine type byte, or a
  truncated/malformed message (`EndOfBufferException`/`InvalidDataException`),
  discards the remainder of the packet. User messages are framed first, so a
  mis-parsing user-message parser can never desynchronise the stream.
- **Profiles**: `IGameProfile` bundles `Id`, `DisplayName`, `AppId`,
  `DefaultUserInfo`, `EngineVariant`, `RegisterMessages(builder)`, and optional
  `AttachSession(connection)` (session behavior such as Half-Life's `VModEnable 1`
  answer to `ReqState`). Built-ins: `HalfLifeProfile`, `CounterStrikeProfile`,
  `ConditionZeroProfile`, `SvenCoopProfile`. Register with `AddGameProfile<T>()`;
  resolve through `IGameProfileResolver` (no static registry).
- **DI**: Core references only `Microsoft.Extensions.DependencyInjection.Abstractions`.
  `AddGoldsrcClient(o => ...)` registers the built-in profiles, the resolver, and
  `IGoldsrcConnectionFactory`; the application owns the container
  (`BuildServiceProvider()`). Services the library does not assume are optional
  (`ILogger`, `ISteamAuthProvider`, `ITransport`).

### Engine variants

`IEngineVariant` abstracts every engine-branch wire difference (netchan encryption,
fragment field widths, delta/entity/resource bit widths, coordinate encoding, which
CRCs travel plaintext). The protocol layer consumes the abstraction only — it never
branches on game names. Built-ins: `EngineVariants.Valve` and
`EngineVariants.SvenCoop`; new branches are `new EngineVariant { ... }` overrides
returned from a profile.

### Encoding details

- **Io**: `BufferReader`/`BufferWriter` are ref structs over spans with a single
  absolute bit position, mirroring the engine's `bf_read`/`Sizebuf`. Byte and bit
  reads mix freely (byte reads take an aligned fast path and fall back to the bit
  path when unaligned) — there is no manual `bitIdx = reader.Offset * 8` bridging.
  All reads throw `EndOfBufferException` on underflow; there are no 0-on-overflow
  primitives.
- **Struct marshalling**: `BufferReader.ReadStruct<T>()` overlays blittable
  `StructLayout(LayoutKind.Sequential, Pack=1)` structs via `MemoryMarshal`. Core has
  `AllowUnsafeBlocks=true`.
- **Encryption**: Munge2 (connected packets, key = seq & 0xFF, applied to the whole
  body including svc_nop padding), Munge3 (worldmap CRC, 8-bit key
  `(-1 - playernum) & 0xFF`; the spawn echo re-munges with `(-1 - spawnCount) & 0xFF`).
  Valve branches only — variants with plaintext CRCs skip both passes.
- **Delta compression**: predefined delta types in `Delta/DeltaDefinitions.cs` are the
  fallbacks; the live tables received via svc_deltadescription (stored in
  `SessionData.DeltaTables`) always win, which is what makes Sven Co-op's divergent
  structures work without protocol changes.
- **Strings** are UTF-8 throughout (the original protocol is raw bytes; UTF-8
  preserves non-ASCII names and CJK chat).

## Commands

```bash
dotnet run --project src/GoldsrcNetClient.Cli -- connect <host> [options]

Options:
  -p, --port <port>        Server port (default 27015)
  -s, --steam              Steam authentication via SteamAPI
      --steamkit           Steam authentication via SteamKit2 (QR)
  -g, --game <id>          Game profile (hl, cstrike, czero, svencoop, ...)
  -n, --name <name>        Player name
  -r, --reconnect <sec>    Auto-reconnect after disconnect
  -u, --userinfo <kv>      Extra userinfo: key1=val1&key2=val2
  -t, --timeout <sec>      Connect timeout (default 5)
  -d, --debug              Verbose output
      --appid <id>         Steam AppId (default 70)
```

## UserInfo API

`GoldsrcConnection` exposes the client's userinfo string (`\key\value\...` format):

- `conn.UserInfo` — read/write the full userinfo string directly.
- `conn.SetUserInfo(key, value)` / `conn.GetUserInfo(key)` — single-key access.
- Server-initiated userinfo updates (`UpdateUserInfo` message) are automatically applied.
- Reuse `Core/Protocol/UserInfoString.cs` for parsing/building userinfo strings
  outside the connection (the TUI does).

## Network Encoding

All network string encoding in Core is **UTF8**. The original GoldSrc protocol uses
raw byte strings, but UTF8 preserves non-ASCII characters (e.g. player names, CJK
chat text) correctly.

## XML Documentation

All public types, methods, properties, events, enums, and structs in Core carry
`///` XML doc comments.
