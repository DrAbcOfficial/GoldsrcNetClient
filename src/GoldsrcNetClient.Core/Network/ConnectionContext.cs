using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// Per-session data for a single server endpoint: challenge and auth values from
/// the handshake plus the session state (spawn count, worldmap CRC, resources)
/// exchanged over the connected channel. Sequencing and reliability state lives in
/// <see cref="GoldsrcNetClient.Core.Netchan.NetchanChannel"/>.
/// </summary>
public sealed class ConnectionContext
{
    /// <summary>
    /// Delta-compression field tables received from this server via
    /// <c>svc_deltadescription</c>, keyed by struct name (e.g. "entity_state_t").
    /// The engine registers every table before sending any delta-compressed
    /// payload (they are written inside SV_SendServerinfo), so consumers can
    /// always resolve the live definition instead of the compiled Valve layout —
    /// required for Sven Co-op whose structures differ from stock GoldSrc.
    /// </summary>
    public Dictionary<string, DeltaType> DeltaTables { get; } = new(StringComparer.Ordinal);

    /// <summary>Challenge token bytes received from the server.</summary>
    public byte[] Challenge { get; set; } = [];

    /// <summary>Authentication protocol negotiated with the server (1=WON, 2=Hash, 3=Steam).</summary>
    public byte AuthProtocol { get; set; }

    /// <summary>User ID assigned by the server after B approval.</summary>
    public int UserId { get; set; }

    /// <summary>True when the server flags this client as VAC-secured in the challenge response.</summary>
    public bool IsVac2Secure { get; set; }

    /// <summary>Build number reported by the server in the challenge/approval response (0 if absent).</summary>
    public uint ServerBuildNumber { get; set; }

    /// <summary>Current spawn count reported by the server.</summary>
    public uint SpawnCount { get; set; }

    /// <summary>Decrypted worldmap CRC value.</summary>
    public uint WorldmapCrc { get; set; }

    /// <summary>Maximum number of clients the server allows.</summary>
    public byte MaxClients { get; set; } = 32;

    /// <summary>This client's player slot index (0-based).</summary>
    public byte PlayerNumber { get; set; }

    /// <summary>Parsed resource list from the server.</summary>
    public ResourceInfo[] Resources { get; set; } = [];

    /// <summary>Raw bit-packed payload of the server's <c>svc_resourcelist</c> message (after the 0x2B type byte).</summary>
    public byte[] ResourceListRawBytes { get; set; } = [];

    /// <summary>Server's Steam ID (0 if not applicable).</summary>
    public ulong ServerSteamId { get; set; }

    /// <summary>Whether the server requires a game auth ticket.</summary>
    public bool RequiresGameAuthTicket { get; set; }

    /// <summary>Server IP address as a 32-bit integer (raw big-endian bytes, read as little-endian uint —
    /// the same layout the engine passes to the Steam ticket API).</summary>
    public uint ServerIp { get; set; }

    /// <summary>Server game port.</summary>
    public ushort ServerPort { get; set; }
}
