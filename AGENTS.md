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

Layering (dependencies point one way): `Protocol` ← `Messages`/`Netchan`/`Delta` ←
`Network` ← `Game`; composition roots (Cli, Tui) resolve profiles and wire everything.

```
Core/
  Network/                          # the connection orchestrator (one class, partial files)
    GoldsrcConnection.cs            #   UDP socket, receive loop, keepalive, public send API, events
    GoldsrcConnection.Dispatch.cs   #   message loop + table-driven svc dispatch + simple parsers
    GoldsrcConnection.Signon.cs     #   serverinfo → sendres → spawn <count> <crc> → sendents flow
    GoldsrcConnection.Entities.cs   #   bitstream parsers: delta descriptions, baselines, clientdata, events, sounds
    GoldsrcConnection.Resources.cs  #   svc_resourcelist bit parsing (+ echo-back payload)
    ConnectionContext.cs            #   per-session data (challenge, spawn count, CRC, resources)
    SplitPacketReassembler.cs       #   UDP-level split (NET_GetLong) reassembly
    TimerResolution.cs              #   winmm timeBeginPeriod(1) ref-counted helper
  Handshake/                        # connection establishment
    HandshakeNegotiator.cs          #   challenge parse, connect packet build, approval → "new"
    ISteamAuthProvider.cs           #   auth ticket abstraction (+ NoSteamAuthProvider default)
  Netchan/
    NetchanChannel.cs               # sequenced channel: flags, ack/evidence-based retransmit, BZ2
    FragmentStream.cs               # per-stream fragment reassembly
  Messages/
    MessageReader.cs                #   byte-level reads (+ ReadStruct<T> overlay helper)
    MessageWriter.cs                #   byte-level writes
    MessageConstants.cs             #   header markers, sequence flags/mask
    IServerMessageHandler.cs        #   DI hook (+ DefaultServerMessageHandler no-op)
  Delta/
    DeltaTypes.cs                   #   DeltaField/DeltaType/DeltaFieldDescription vocabulary
    DeltaDefinitions.cs             #   predefined delta types (fallbacks for svc_deltadescription)
    DeltaReader.cs                  #   delta record skipping + meta field description reader
  Protocol/                         # wire-format vocabulary (knows no game identities)
    EngineVariant.cs                #   IEngineVariant dialect abstraction + Valve/SvenCoop instances
    Enums.cs / Structs.cs / GoldsrcEngineSettings.cs
    UserInfoString.cs               #   \key\value userinfo parse/build
    UserCmd.cs                      #   clc_move payload encoder
    TempEntityParser.cs             #   svc_tempentity bitstream skipper
  Game/                             # per-game profiles + user-message handlers
    IGameLoginProvider.cs           #   profile interface + registry + built-in providers
    GameMessageHandler.cs, HalfLifeMessageHandler.cs,
    CounterStrikeMessageHandler.cs, SvenCoopMessageHandler.cs,
    GameEventData.cs, UserMessageRegistry.cs
  Munge/Munge.cs                    # Munge1/2/3 XOR ciphers (one shared transform core)
  Query/A2SQuerier.cs               # A2S_INFO server query (GoldSrc + Source replies)
  Util/BitReader.cs, BitWriter.cs, ByteSpanExtensions.cs
```

- Namespace convention: `GoldsrcNetClient.Core.{Folder}`.
- File-scoped namespaces, `ImplicitUsings`, `Nullable=enable` on all projects.
  Style: C# 14 (primary constructors, collection expressions, `System.Threading.Lock`,
  `extension` members — see `ByteSpanExtensions`, null-conditional assignment).

### GoldsrcConnection (Core/Network/)

The connection is a thin orchestrator; each responsibility lives in its own class or
partial file:

- `HandshakeNegotiator` — the whole getchallenge → connect → approval state machine.
- `NetchanChannel` (one per server endpoint) — owns sequencing/reliability state AND
  behavior (the engine's `netchan_t`): duplicate suppression, reliable-message ack +
  evidence-based retransmission, fragment reassembly, unconditional Munge2
  encrypt/decrypt (per variant), svc_nop padding, ack-per-packet cadence.
  - Reliable ack bit semantics: the server toggles its counter once per NEW
    reliable message; our ack bit mirrors the toggle per received reliable packet.
    Retransmission fires only on evidence of loss (later packet acked, parity still
    mismatched), matching the engine's `Netchan_Transmit`.
- `GoldsrcConnection.Dispatch.cs` — table-driven dispatch
  (`Dictionary<byte, MessageParser>`): each svc message has one parser entry;
  `false` return discards the rest of the packet (buffer overflow).
- `GoldsrcConnection.Resources.cs` — svc_resourcelist bit parsing; the raw payload
  is echoed back on svc_resourcerequest (real-client behavior).
- Per-message observation goes through events (`OnConsolePrint`, `OnCenterPrint`,
  `OnServerInfo`, `OnServerDisconnect`, ...). Consumers should subscribe to events
  instead of intercepting built-in-handled messages in an `IServerMessageHandler`.

### Engine variants & game profiles (the extension seam)

- `Protocol/IEngineVariant` abstracts every engine-branch wire difference: netchan
  Munge2 on/off, fragment field widths, delta byte-count prefix (3/4 bits), entity
  index bits (11/13), resource/consistency index bits, coordinate encoding, and
  which CRCs travel plaintext. The protocol layer consumes the abstraction only —
  it never branches on game names.
- Built-in dialects: `EngineVariants.Valve` and `EngineVariants.SvenCoop`. New
  branches: `new EngineVariant { ... }` overrides.
- `Game/IGameLoginProvider` is the per-mod profile: `Id`, `DisplayName`, `AppId`,
  `DefaultUserInfo`, `EngineVariant`, and a `CreateMessageHandler()` factory.
  `GameLoginProviders.Register/Resolve/GetByAppId` is the registry; the CLI's
  `--game` / `--appid` options resolve through it, and Cli/Tui pass
  `profile.EngineVariant` into the connection. Supporting a new mod = registering a
  provider — no protocol changes. The Steam login path (`ISteamAuthProvider`) is
  game-agnostic and takes the profile's AppId.

### Handshake / auth

- `ISteamAuthProvider` produces the ticket; `NoSteamAuthProvider` sends a fake key.
- Challenge parsing tolerates trailing chars on numeric tokens (server SteamID).

### Session data

`ConnectionContext` holds only session data (challenge, spawn count, worldmap CRC,
resources, SteamID...). All netchan/sequencing state lives in `NetchanChannel`.

### Encoding details

- Struct marshalling: `MessageReader.ReadStruct<T>()` overlays blittable
  `StructLayout(LayoutKind.Sequential, Pack=1)` structs on the wire bytes. Core has
  `AllowUnsafeBlocks=true`.
- Encryption: Munge2 (connected packets, key = seq & 0xFF, applied to the whole body
  including svc_nop padding), Munge3 (worldmap CRC, 8-bit key `(-1 - playernum) & 0xFF`;
  the spawn echo re-munges with `(-1 - spawnCount) & 0xFF`). Valve branches only —
  variants with plaintext CRCs skip both passes.
- Delta compression: predefined delta types in `Delta/DeltaDefinitions.cs` are the
  fallbacks; the live tables received via svc_deltadescription (stored in
  `ConnectionContext.DeltaTables`) always win, which is what makes Sven Co-op's
  divergent structures work without protocol changes.
- Bit readers (`BitReader`) use ABSOLUTE bit indexes into the buffer — handlers must
  seed `bitIdx = reader.Offset * 8` and convert back with `reader.Offset = (bitIdx + 7) / 8`.

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
