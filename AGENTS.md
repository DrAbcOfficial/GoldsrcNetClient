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
  GoldsrcNetClient.Core/    # protocol library (NuGet package, AllowUnsafeBlocks)
  GoldsrcNetClient.Cli/     # CLI tool (entrypoint: Program.cs, commands: ConnectCommand)
  GoldsrcNetClient.Test/    # xunit tests
```

- **Core** has no external dependencies — it is a pure C# UDP client library for the GoldSrc (HL1) engine network protocol.
- **Cli** references Core and `Steamworks.NET.AnyCPU`; uses CliFx for CLI parsing, `SteamNetAuthProvider.cs` for optional Steam auth.
- **Test** only references Core (not Cli).

## Key Architecture

- Namespace convention: `GoldsrcNetClient.{Core|Cli|Test}.{Subsystem}`
- File-scoped namespaces, `ImplicitUsings`, `Nullable=enable` on all projects.
- `GoldsrcConnection.cs` (Core/Network/) is the main entrypoint — contains the full connection handshake and server message processing loop.
- Struct marshalling: uses `fixed` pointers and `StructLayout(LayoutKind.Sequential, Pack=1)` to overlay packets onto structs. Core has `AllowUnsafeBlocks=true` — required.
- Encryption: `Munge2` (connected packets) and `Munge3` (worldmap CRC) are XOR-based. Munge/UnMunge are inverse operations using different lookup tables.
- Delta compression: predefined delta types in `DeltaDefinitions.cs`; entity state, player state, clientdata, weapon data, etc.

## Commands

```bash
dotnet run --project src/GoldsrcNetClient.Cli -- connect <host> [--port 27015] [--debug] [--steam] [--appid 70]
```

No build/publish scripts, no CI/CD, no lint or formatter config exists in this repo.

## Network Encoding

All network string encoding in Core is **UTF8** (`Encoding.UTF8`). The original GoldSrc protocol uses raw byte strings, but UTF8 preserves non-ASCII characters (e.g. player names) correctly.

## UserInfo API

`GoldsrcConnection` exposes the client's userinfo string (`\key\value\...` format):

- `conn.UserInfo` — read/write the full userinfo string directly.
- `conn.SetUserInfo(key, value)` — set a single key (case-insensitive), rebuilding the string.
- `conn.GetUserInfo(key)` — get a single value by key; returns `null` if not found.

Server-initiated userinfo updates (`UpdateUserInfo` message) are automatically applied to `conn.UserInfo`.

## XML Documentation

All public types, methods, properties, events, enums, and structs in Core carry `///` XML doc comments. Generated docs are usable from IDE IntelliSense.
