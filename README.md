# GoldSrc NetClient

GoldSrc (Half-Life 1) engine network protocol client library for .NET.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Quick Start

```bash
dotnet build
dotnet test
dotnet run --project src/GoldsrcNetClient.Cli -- connect <host> [--port 27015] [--steam] [--name Bot] [--reconnect 5]
```

Interactive shell after connecting: plain text is sent as chat (`say <text>`),
`/command` runs a raw console command on the server (`/status`, `/say_team hi`, ...),
`/quit` leaves the session. Incoming chat (`SayText`), console messages (`svc_print`)
and death notices are printed live.

## Project Structure

| Project | Description |
|---------|-------------|
| `GoldsrcNetClient.Core` | Protocol library — sequenced channel (netchan) with reliable delivery, Munge encryption, delta compression |
| `GoldsrcNetClient.Cli` | CLI tool using CliFx; chat shell; optional Steam auth (Steamworks.NET or SteamKit2) |
| `GoldsrcNetClient.SteamProvider` | Steam auth providers: SteamAPI ticket pipeline and SteamKit2 QR-login |
| `GoldsrcNetClient.Tui` | Terminal UI front-end |
| `GoldsrcNetClient.Test` | xunit tests |

### Core layout (by responsibility)

```
Core/
  GoldsrcConnection.cs        orchestrator: UDP socket, receive loop, events, send API
  Handshake/                  connection establishment (challenge, connect packet, auth ticket interface)
  Netchan/                    sequenced channel: reliability, retransmission, fragments, Munge2
  Messages/                   message reader/writer, constants, IServerMessageHandler hook
  Delta/                      delta-compression definitions and bitstream reader
  Protocol/                   enums, structs, settings, UserInfoString, UserCmd encoder
  Game/                       per-game login profiles and user-message handlers
  Munge/                      Munge1/2/3 ciphers
  Util/                       bit-level reader/writer
```

## Features

- Connection handshake (getchallenge → connect → approval) with Steam auth ticket
  (`ISteamUser::InitiateGameConnection` blob, server SteamID + VAC flag parsed from
  the challenge response)
- Sequenced channel (netchan): Munge2 encryption both ways, duplicate/out-of-order
  suppression, reliable-message acknowledgement and retransmission, two-stream
  fragment reassembly, BZ2 decompression, keepalive/ack cadence, sub-16-byte
  packet padding
- Full signon flow: serverinfo, delta descriptions (meta-delta field encoding),
  movevars, user-message registration, resourcelist (bit-packed parsing),
  resourcerequest reply, `spawn <count> <munged CRC>`, `sendents`
- Console (`svc_print`), chat (`SayText`), `TextMsg`, `DeathMsg` events
- `ReqState` user message answered with `VModEnable 1` (voice-manager handshake)
- Auto-reconnect (`--reconnect <seconds>`) — with the SteamAPI pipeline reconnects
  are seamless (no re-login)
- Per-game login profiles (`IGameLoginProvider` registry): Half-Life, Counter-Strike,
  Condition Zero, Sven Co-op — each supplies AppId, default userinfo and a message
  handler; register custom providers for other GoldSrc-branch games

## Commands

```bash
dotnet run --project src/GoldsrcNetClient.Cli -- connect <host> [options]

Options:
  -p, --port <port>        Server port (default 27015)
  -s, --steam              Steam authentication via SteamAPI (requires running Steam client)
      --steamkit           Steam authentication via SteamKit2 (QR login)
  -g, --game <id>          Game profile: hl, cstrike, czero, svencoop, ...
  -n, --name <name>        Player name
  -r, --reconnect <sec>    Auto-reconnect after disconnect (0 = off)
  -u, --userinfo <kv>      Extra userinfo: key1=val1&key2=val2
  -t, --timeout <sec>      Connect timeout (default 5)
  -d, --debug              Verbose output
      --appid <id>         Steam AppId (default 70)
```
