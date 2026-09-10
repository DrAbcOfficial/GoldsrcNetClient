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

Target framework is `net10.0` — requires .NET 10 SDK.

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
- **Test** only references Core.

## Key Architecture

- Namespace convention: `GoldsrcNetClient.{Core|Cli|SteamProvider}.{Subsystem}`
- File-scoped namespaces, `ImplicitUsings`, `Nullable=enable` on all projects.
- `GoldsrcConnection` (Core/Network/) is the main entrypoint:
  - `GoldsrcConnection.cs` — handshake + receive loop
  - `GoldsrcConnection.Netchan.cs` — sequenced channel: header flags (bit31 =
    reliable, bit30 = fragments), unconditional Munge2 encrypt on send / decrypt on
    receive, reliable-message ack + retransmission, fragment reassembly + BZ2
    - Reliable ack bit semantics: the server toggles its counter once per NEW
      reliable message; our ack bit mirrors the toggle per received reliable packet.
      Retransmissions of a stuck message are throttled (500 ms) and a parity
      re-sync heuristic repairs phase drift.
  - `GoldsrcConnection.Handshake.cs` — challenge parse (server SteamID uses
    leading-digit parsing; trailing chars like `m` are legal), connect packet,
    Steam ticket blob (network-byte-order port, VAC flag)
  - `GoldsrcConnection.Connected.cs` — svc message parsers
  - `GoldsrcConnection.Sending.cs` — keepalive task
- Struct marshalling: `fixed` pointers + `StructLayout(LayoutKind.Sequential, Pack=1)`.
  Core has `AllowUnsafeBlocks=true`.
- Encryption: Munge2 (connected packets, key = seq & 0xFF, applied to the whole body
  including svc_nop padding), Munge3 (worldmap CRC, full-int key `~playerNum` /
  `~spawnCount`).
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

## Network Encoding

All network string encoding in Core is **UTF8**. The original GoldSrc protocol uses
raw byte strings, but UTF8 preserves non-ASCII characters (e.g. player names, CJK
chat text) correctly.

## XML Documentation

All public types, methods, properties, events, enums, and structs in Core carry
`///` XML doc comments.
