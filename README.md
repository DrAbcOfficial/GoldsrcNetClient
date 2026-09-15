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
  Io/            byte/bit codec: BufferReader/BufferWriter (one position, span-based)
  Messages/      typed message records, MessageHub (Subscribe<T>), MessagePipeline,
                 ParserRegistry, Engine/ + Users/ message sets
  Network/       GoldsrcConnection (receive loop, send API, hub), SignonController,
                 connection factory, transport seam, split-packet reassembly
  Handshake/     connection establishment (challenge, connect packet, auth ticket)
  Netchan/       sequenced channel: reliability, fragments, Munge2
  Delta/         delta-compression definitions and reader
  Protocol/      enums, wire structs, EngineVariant dialects, UserInfoString, UserCmd
  Game/          IGameProfile + GameProfileResolver (per-mod profiles)
  DependencyInjection/  AddGoldsrcClient / AddGameProfile
  Munge/ Query/  Munge ciphers; A2S_INFO query
```

## Features

- Connection handshake (getchallenge → connect → approval) with Steam auth ticket
  (`ISteamUser::InitiateGameConnection` blob, server SteamID + VAC flag parsed from
  the challenge response)
- Sequenced channel (netchan): Munge2 encryption both ways, duplicate/out-of-order
  suppression, reliable-message acknowledgement with engine-exact evidence-based
  retransmission, two-stream fragment reassembly, UDP split-packet reassembly,
  BZ2 decompression, keepalive/ack cadence with 1 ms timer resolution, sub-16-byte
  packet padding
- Full signon flow: serverinfo, delta descriptions (meta-delta field encoding),
  movevars, user-message registration, resourcelist (bit-packed parsing),
  resourcerequest reply, `spawn <count> <munged CRC>`, `sendents`
- Typed message hub (`connection.Subscribe<TMessage>()`) for every engine and user
  message; `Print`/`SayText`/`TextMsg`/`DeathMsg` and the full Sven Co-op set included
- `ReqState` user message answered with `VModEnable 1` (voice-manager handshake)
- Auto-reconnect (`--reconnect <seconds>`) — with the SteamAPI pipeline reconnects
  are seamless (no re-login)
- Per-game profiles (`IGameProfile`, DI-registered): Half-Life, Counter-Strike,
  Condition Zero, Sven Co-op — each supplies AppId, default userinfo, an
  `IEngineVariant` wire dialect, and its message parsers; register custom profiles for
  other GoldSrc-branch games without touching the protocol code

## Engine variants and mod support

Every engine-branch wire difference (netchan encryption, fragment field widths,
delta/entity/resource bit widths, coordinate encoding, plaintext vs munged CRCs) is
captured in the `IEngineVariant` abstraction (`Protocol/EngineVariant.cs`). The
protocol machinery consumes the abstraction; built-in dialects are
`EngineVariants.Valve` and `EngineVariants.SvenCoop`.

Supporting a new mod or engine branch means writing one profile class — no protocol
changes, no event declarations, no dispatch switch:

```csharp
public sealed class MyModProfile : GameProfileBase
{
    public override string Id => "mymod";
    public override string DisplayName => "My Mod";
    public override uint AppId => 123456;
    public override IEngineVariant EngineVariant => new EngineVariant
    {
        NetchanEncryption = false,
        LongFragmentFields = true,
        EntityIndexBits = 13,
        // ...only what differs
    };

    public override void RegisterMessages(ParserRegistry.Builder builder)
    {
        HalfLifeMessages.Register(builder);          // inherit the shared vocabulary
        builder.AddUser("MyMessage", static (ref BufferReader r) => new MyMessage(r.ReadUInt8()));
    }
}

// Composition root
services.AddGoldsrcClient().AddGameProfile<MyModProfile>();

// Consume typed messages
connection.Subscribe<MyMessage>(m => Console.WriteLine(m.Value));
```

### Consuming messages

Parsed messages are published by type through `connection.Messages`:

```csharp
using var chat = connection.Subscribe<SayTextMessage>(m => Console.WriteLine($"[Chat] {m.Message}"));
using var print = connection.Subscribe<PrintMessage>(m => Console.WriteLine(m.Text));
```

Unregistered user messages arrive as `RawUserMessage` with their bytes intact, so a
consumer can still parse mod messages the profile does not know about.

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
