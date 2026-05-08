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
- **Cli** references Core; uses CliFx for CLI parsing, Facepunch.Steamworks for optional Steam auth.
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
