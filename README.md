# GoldSrc NetClient

GoldSrc (Half-Life 1) engine network protocol client library for .NET.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Quick Start

```bash
dotnet build
dotnet test
dotnet run --project src/GoldsrcNetClient.Cli -- connect <host> [--port 27015] [--debug] [--steam]
```

## Project Structure

| Project | Description |
|---------|-------------|
| `GoldsrcNetClient.Core` | Protocol library — UDP client, packet encryption (Munge), delta compression |
| `GoldsrcNetClient.Cli` | CLI tool using CliFx; optional Steam auth via Facepunch.Steamworks |
| `GoldsrcNetClient.Test` | xunit tests |

## Features

- Connection handshake (challenge/connect/approval)
- Munge2/Munge3 XOR encryption for connected packets and worldmap CRC
- Delta-compressed entity state, player state, clientdata, and weapon data parsing
- Packet fragmentation and reassembly
- Resource list and file consistency processing
