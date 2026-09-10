using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// Per-session connection state for a single server endpoint.
/// Tracks challenge data, netchan sequence/reliable state, resource info,
/// and fragment reassembly buffers.
/// </summary>
public sealed class ConnectionContext
{
    /// <summary>Challenge token bytes received from the server.</summary>
    public byte[] Challenge = [];

    /// <summary>Authentication protocol negotiated with the server (1=WON, 2=Hash, 3=Steam).</summary>
    public byte AuthProtocol;

    /// <summary>User ID assigned by the server after B approval.</summary>
    public int UserId;

    // ------------------------------------------------------------------
    // Netchan state (mirrors the engine's per-channel bookkeeping)
    // ------------------------------------------------------------------

    /// <summary>Next outgoing sequence number (low 30 bits).</summary>
    public uint SrcSequence = 1;

    /// <summary>Last received packet sequence number from the server (low 30 bits).</summary>
    public uint IncomingSequence;

    /// <summary>Last received acknowledgement of our outgoing sequences (low 30 bits).</summary>
    public uint IncomingAcknowledged;

    /// <summary>Toggles every time the server sends a reliable message. Echoed back in our ack field
    /// so the server knows its reliable data arrived.</summary>
    public uint IncomingReliableSequence;

    /// <summary>Fingerprint of the last new inline reliable payload received, used to detect
    /// the server's retransmissions (which do not toggle its reliable counter).</summary>
    public long LastInlineReliableFingerprint;

    /// <summary>Consecutive retransmissions of the same inline reliable payload observed.</summary>
    public int InlineResendCount;

    /// <summary>Timestamp (Environment.TickCount64) of the last transmission that carried
    /// the pending reliable payload.</summary>
    public long LastReliableTransmitMs;

    /// <summary>Toggles every time we start sending a new reliable message. The server echoes it back
    /// in its ack field; a match means our reliable payload was received.</summary>
    public uint OutgoingReliableSequence;

    /// <summary>Outgoing sequence number that first carried the current reliable payload.</summary>
    public uint LastReliableSequence;

    /// <summary>Reliable payload awaiting acknowledgement from the server; re-sent with every
    /// transmitted packet until acknowledged. <c>null</c> when nothing is pending.</summary>
    public byte[]? PendingReliable;

    /// <summary>Queue of reliable payloads waiting for the current <see cref="PendingReliable"/> to be acked.</summary>
    public readonly Queue<byte[]> ReliableQueue = new();

    // ------------------------------------------------------------------
    // Fragment reassembly
    // ------------------------------------------------------------------

    /// <summary>Per-stream fragment reassembly state (index 0 = normal stream, 1 = file stream).</summary>
    public readonly FragmentStreamState[] FragmentStreams = [new(), new()];

    /// <summary>True when the server flags this client as VAC-secured in the challenge response.</summary>
    public bool IsVac2Secure;

    /// <summary>Build number reported by the server in the challenge/approval response (0 if absent).</summary>
    public uint ServerBuildNumber;

    // ------------------------------------------------------------------
    // Session data
    // ------------------------------------------------------------------

    /// <summary>Current spawn count reported by the server.</summary>
    public uint SpawnCount;

    /// <summary>Decrypted worldmap CRC value.</summary>
    public uint WorldmapCrc;

    /// <summary>Maximum number of clients the server allows.</summary>
    public byte MaxClients = 32;

    /// <summary>This client's player slot index (0-based).</summary>
    public byte PlayerNumber;

    /// <summary>Parsed resource list from the server.</summary>
    public ResourceInfo[] Resources = [];

    /// <summary>Raw bit-packed payload of the server's <c>svc_resourcelist</c> message (after the 0x2B type byte).</summary>
    public byte[] ResourceListRawBytes = [];

    /// <summary>Registered user message types from the server.</summary>
    public List<UserMessage> UserMessages = [];

    /// <summary>Server's Steam ID (0 if not applicable).</summary>
    public ulong ServerSteamId;

    /// <summary>Whether the server requires a game auth ticket.</summary>
    public bool RequiresGameAuthTicket;

    /// <summary>Server IP address as a 32-bit integer (raw big-endian bytes, read as little-endian uint —
    /// the same layout the engine passes to the Steam ticket API).</summary>
    public uint ServerIp;

    /// <summary>Server game port.</summary>
    public ushort ServerPort;

    /// <summary>Accumulated incomplete fragment data for message reassembly.</summary>
    public List<byte> IncomingFragment = [];
}

/// <summary>
/// Fragment reassembly state for a single netchan stream.
/// GoldSrc fragments must arrive with sequentially increasing buffer ids;
/// out-of-order fragments invalidate the whole transfer.
/// </summary>
public sealed class FragmentStreamState
{
    /// <summary>Whether a fragment transfer is currently being reassembled on this stream.</summary>
    public bool Active;

    /// <summary>Id of the current transfer (high 16 bits of the fragid field).</summary>
    public int TransferId;

    /// <summary>Total number of fragments expected for the current transfer.</summary>
    public int TotalCount;

    /// <summary>Fragments received so far, in arrival order (should equal id order).</summary>
    public readonly List<byte[]> Chunks = [];

    /// <summary>True when <see cref="Chunks"/> holds all <see cref="TotalCount"/> pieces.</summary>
    public bool IsComplete => TotalCount > 0 && Chunks.Count >= TotalCount;

    /// <summary>Clears the reassembly state.</summary>
    public void Reset()
    {
        Active = false;
        TransferId = 0;
        TotalCount = 0;
        Chunks.Clear();
    }
}
