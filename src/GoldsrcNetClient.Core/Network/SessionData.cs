using GoldsrcNetClient.Core.Delta;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// Connected-session state for one server endpoint: the signon facts learned
/// from server messages (delta tables, spawn count, worldmap CRC, player slot,
/// resource list) that the message parsers and the
/// <see cref="SignonController"/> read and write during the session.
/// Handshake-phase state lives in <see cref="HandshakeState"/>; netchan
/// sequencing state lives in <see cref="GoldsrcNetClient.Core.Netchan.NetchanChannel"/>.
/// </summary>
public sealed class SessionData
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
}
