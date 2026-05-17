using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// Per-session connection state for a single server endpoint.
/// Tracks challenge data, sequence numbers, resource info, and fragment reassembly buffers.
/// </summary>
public sealed class ConnectionContext
{
    /// <summary>Challenge token bytes received from the server.</summary>
    public byte[] Challenge = [];

    /// <summary>Authentication protocol negotiated with the server (1=WON, 2=Hash, 3=Steam).</summary>
    public byte AuthProtocol;

    /// <summary>User ID assigned by the server after B approval.</summary>
    public int UserId;

    /// <summary>Local source sequence number for outgoing packets.</summary>
    public uint SrcSequence = 1;

    /// <summary>Remote destination sequence number (last received sequence from server).</summary>
    public uint DstSequence;

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

    /// <summary>Server IP address as a 32-bit integer.</summary>
    public uint ServerIp;

    /// <summary>Server game port.</summary>
    public ushort ServerPort;

    /// <summary>Accumulated incomplete fragment data for message reassembly.</summary>
    public List<byte> IncomingFragment = [];

    /// <summary>Total expected fragment count for the current reassembly stream.</summary>
    public int FragmentTotalCount;

    /// <summary>Whether a fragment stream is currently being reassembled.</summary>
    public bool FragmentActive;

    /// <summary>Accumulated fragment chunks for the current reassembly stream.</summary>
    public readonly List<byte[]> FragmentChunks = [];
}
