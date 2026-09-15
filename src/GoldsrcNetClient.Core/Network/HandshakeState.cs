namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// Connection-handshake state for one server endpoint: everything negotiated
/// during getchallenge → connect → approval (challenge token, auth protocol,
/// server Steam ID) plus the endpoint values the auth ticket binds to. Written
/// by <see cref="GoldsrcNetClient.Core.Handshake.HandshakeNegotiator"/>; the
/// connected-session state lives in <see cref="SessionState"/>.
/// </summary>
public sealed class HandshakeState
{
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
