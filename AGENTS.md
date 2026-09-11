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

Code is organized by responsibility into subfolders:

```
Core/
  GoldsrcConnection.cs        # orchestrator: UDP socket, receive loop, events, send API
  Handshake/                  # connection establishment
    HandshakeNegotiator.cs    #   challenge parse, connect packet build, approval → "new"
    ISteamAuthProvider.cs     #   auth ticket abstraction (+ NoSteamAuthProvider default)
  Netchan/
    NetchanChannel.cs         # sequenced channel: flags, ack/retransmit, Munge2, BZ2
    FragmentStream.cs         # per-stream fragment reassembly
  Messages/
    MessageReader.cs          #   byte-level reads
    MessageWriter.cs          #   byte-level writes
    MessageConstants.cs       #   header markers, sequence flags/mask
    IServerMessageHandler.cs  #   DI hook (+ DefaultServerMessageHandler no-op)
  Delta/
    DeltaDefinitions.cs       #   predefined delta types (incl. meta delta_description_t)
    DeltaReader.cs            #   delta record skipping on a bitstream
  Protocol/                   # wire-format vocabulary
    Enums.cs / Structs.cs / GoldsrcEngineSettings.cs / UserMessageType.cs
    UserInfoString.cs         #   \key\value userinfo parse/build
    UserCmd.cs                #   clc_move payload encoder
  Game/                       # per-game profiles + user-message handlers
    IGameLoginProvider.cs, GameMessageHandler.cs,
    HalfLifeMessageHandler.cs, CounterStrikeMessageHandler.cs, SvenCoopMessageHandler.cs,
    GameEventData.cs, UserMessageRegistry.cs
  Munge/Munge.cs              # Munge1/2/3 XOR ciphers
  Util/BitReader.cs, BitWriter.cs
```

- Namespace convention: `GoldsrcNetClient.Core.{Folder}`; `GoldsrcConnection` and
  `ConnectionContext` stay in `.Network`.
- File-scoped namespaces, `ImplicitUsings`, `Nullable=enable` on all projects.
  Style: C# 14 (primary constructors, collection expressions, `System.Threading.Lock`).

### GoldsrcConnection (Core/Network/)

The connection is a thin orchestrator; each responsibility lives in its own class:

- `HandshakeNegotiator` — the whole getchallenge → connect → approval state machine.
- `NetchanChannel` (one per server endpoint) — owns sequencing/reliability state AND
  behavior (the engine's `netchan_t`): duplicate suppression, reliable-message ack +
  500 ms throttled retransmission, fragment reassembly, unconditional Munge2
  encrypt/decrypt, svc_nop padding, ack-per-packet cadence.
  - Reliable ack bit semantics: the server toggles its counter once per NEW
    reliable message; our ack bit mirrors the toggle per received reliable packet.
    Retransmissions are throttled (500 ms) and a parity re-sync heuristic repairs
    phase drift.
- `GoldsrcConnection.Messages.cs` — table-driven dispatch
  (`Dictionary<byte, MessageParser>`): each svc message has one parser entry;
  `false` return discards the rest of the packet (buffer overflow).
- `GoldsrcConnection.Resources.cs` — svc_resourcelist bit parsing; the raw payload
  is echoed back on svc_resourcerequest (real-client behavior).
- Per-message observation goes through events (`OnConsolePrint`, `OnCenterPrint`,
  `OnServerInfo`, `OnServerDisconnect`, ...). Consumers should subscribe to events
  instead of intercepting built-in-handled messages in an `IServerMessageHandler`.

### Handshake / auth

- `ISteamAuthProvider` produces the ticket; `NoSteamAuthProvider` sends a fake key.
- Challenge parsing tolerates trailing chars on numeric tokens (server SteamID).

### Session data

`ConnectionContext` holds only session data (challenge, spawn count, worldmap CRC,
resources, SteamID...). All netchan/sequencing state lives in `NetchanChannel`.

### Encoding details

- Struct marshalling: `fixed` pointers + `StructLayout(LayoutKind.Sequential, Pack=1)`.
  Core has `AllowUnsafeBlocks=true`.
- Encryption: Munge2 (connected packets, key = seq & 0xFF, applied to the whole body
  including svc_nop padding), Munge3 (worldmap CRC, 8-bit key `(-1 - playernum) & 0xFF`;
  the spawn echo re-munges with `(-1 - spawnCount) & 0xFF`).
- Delta compression: predefined delta types in `Delta/DeltaDefinitions.cs`
  (including the `delta_description_t` meta type used by svc_deltadescription).
  svc_deltadescription fields are themselves delta-encoded against the meta type.
- Bit readers (`BitReader`) use ABSOLUTE bit indexes into the buffer — handlers must
  seed `bitIdx = reader.Offset * 8` and convert back with `reader.Offset = (bitIdx + 7) / 8`.

## Game Profiles

`Core/Game/IGameLoginProvider.cs` — registry of per-game login profiles
(`GameLoginProviders.Register/Resolve`). Built-ins: hl (70), cstrike (10),
czero (80), svencoop (225840). Each profile supplies AppId, default userinfo and a
message-handler factory; the CLI's `--game` / `--appid` options resolve through it.
Add new GoldSrc-branch games by registering an `IGameLoginProvider` — the Steam
login path (`ISteamAuthProvider`) is game-agnostic and takes the profile's AppId.

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
