using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace GoldsrcNetClient.Core.Handshake;

/// <summary>
/// Client side of the GoldSrc connection handshake
/// (getchallenge → connect → approval). Parses challenge responses in both the
/// Steam (<c>A &lt;token&gt; &lt;authProto&gt; &lt;steamId&gt; &lt;vac&gt; [build]</c>)
/// and legacy formats, builds the connect packet for the negotiated auth protocol
/// (WON, hashed CD key, or Steam ticket), and issues the initial <c>new</c>
/// stringcmd once the server approves the connection.
/// </summary>
/// <param name="authProvider">Steam auth provider used to build the ticket blob.</param>
/// <param name="settings">Engine settings (protocol version).</param>
/// <param name="sendPacket">Connectionless transport delegate (typically
/// <see cref="System.Net.Sockets.UdpClient.SendAsync"/>).</param>
/// <param name="logger">Optional logger.</param>
public sealed class HandshakeNegotiator(
    ISteamAuthProvider authProvider,
    GoldsrcEngineSettings settings,
    Func<ReadOnlyMemory<byte>, IPEndPoint, CancellationToken, Task> sendPacket,
    ILogger? logger = null)
{
    /// <summary>Steam auth protocol byte sent in the connect packet.</summary>
    internal const byte AuthProtocolSteam = 3;
    /// <summary>WON auth protocol byte sent in the connect packet.</summary>
    internal const byte AuthProtocolWon = 1;
    /// <summary>Hashed CD key auth protocol byte sent in the connect packet.</summary>
    internal const byte AuthProtocolHashedCdKey = 2;

    private const string FakeHashedCdKey = "12345678901234567890123456789012";
    private const string FakeWonCdKey = "1234567890123";

    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    private static readonly byte[] GetChallengeSteamPacket =
        [0xFF, 0xFF, 0xFF, 0xFF, (byte)'g', (byte)'e', (byte)'t', (byte)'c', (byte)'h', (byte)'a', (byte)'l', (byte)'l', (byte)'e', (byte)'n', (byte)'g', (byte)'e', (byte)' ', (byte)'s', (byte)'t', (byte)'e', (byte)'a', (byte)'m', (byte)'\n'];
    private static readonly byte[] GetChallengePacket =
        [0xFF, 0xFF, 0xFF, 0xFF, (byte)'g', (byte)'e', (byte)'t', (byte)'c', (byte)'h', (byte)'a', (byte)'l', (byte)'l', (byte)'e', (byte)'n', (byte)'g', (byte)'e', (byte)'\n'];

    /// <summary>Builds the OOB getchallenge request; Steam servers expect the "steam" suffix.</summary>
    public byte[] BuildGetChallengePacket(bool steamAvailable) =>
        steamAvailable ? GetChallengeSteamPacket : GetChallengePacket;

    /// <summary>
    /// Handles one connectionless (OOB) response received during the handshake and
    /// returns the next <see cref="SessionState"/>.
    /// </summary>
    /// <param name="state">Current handshake state.</param>
    /// <param name="endpoint">The server endpoint the connect packet is sent to.</param>
    /// <param name="appId">Steam AppId used for the auth ticket request.</param>
    /// <param name="userInfo">Raw userinfo string embedded in the connect packet.</param>
    /// <param name="ctx">Session context filled in from the response.</param>
    /// <param name="payload">OOB response text (after the 0xFFFFFFFF marker).</param>
    /// <param name="sendStringCmd">Reliable stringcmd sender, used for the post-approval <c>new</c> command.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<SessionState> HandleResponseAsync(
        SessionState state,
        IPEndPoint endpoint,
        uint appId,
        string userInfo,
        ConnectionContext ctx,
        string payload,
        Func<ClientCommandType, string, CancellationToken, Task> sendStringCmd,
        CancellationToken ct)
    {
        if (state == SessionState.GetChallenge)
        {
            var parts = payload.Split(' ');

            if (parts.Length >= 2 && parts[0].StartsWith('A'))
            {
                string challengeToken = parts[1];
                ctx.AuthProtocol = parts.Length > 2 && int.TryParse(parts[2], out int ap) ? (byte)ap : AuthProtocolSteam;

                // Steam-auth challenges carry "<serverSteamId> <vacSecure> [build]".
                // Server SteamIDs may carry trailing non-digit characters; parse the
                // leading digits only.
                ulong serverSteamId = parts.Length > 3 ? ParseLeadingDigits(parts[3]) : 0;
                ctx.ServerSteamId = serverSteamId;

                ctx.IsVac2Secure = parts.Length > 4 && parts[4].Length > 0 && parts[4][0] == '1';
                if (parts.Length > 5 && uint.TryParse(parts[5], out uint build))
                    ctx.ServerBuildNumber = build;

                // Steam-authenticated servers always expect an auth ticket blob
                // appended to the connect packet.
                ctx.RequiresGameAuthTicket = ctx.AuthProtocol == AuthProtocolSteam;

                _logger.LogDebug($"[Challenge] challenge={challengeToken}, authProto={ctx.AuthProtocol}, serverSteamId={ctx.ServerSteamId}, vacSecure={ctx.IsVac2Secure}, build={ctx.ServerBuildNumber}");

                ctx.Challenge = Encoding.UTF8.GetBytes(challengeToken);

                var data = BuildConnectPacket(ctx, appId, userInfo);
                _logger.LogDebug($"[Connect] sending connect packet, len={data.Length}");
                await sendPacket(data, endpoint, ct);
                return SessionState.Connect0;
            }

            if (parts.Length >= 2 && char.IsDigit(parts[0][0]))
            {
                string challengeToken = parts[1];
                _logger.LogDebug($"[Challenge] Format legacy: challenge={challengeToken}, parts={parts.Length}");

                ctx.Challenge = Encoding.UTF8.GetBytes(challengeToken);
                var data = BuildConnectPacket(ctx, appId, userInfo);
                _logger.LogDebug($"[Connect] sending connect packet (legacy), len={data.Length}");
                await sendPacket(data, endpoint, ct);
                return SessionState.Connect0;
            }

            _logger.LogWarning($"[Challenge] unexpected response (len={parts.Length}, first={parts[0]}): {payload}");
        }
        else if (state == SessionState.Connect0)
        {
            var parts = payload.Split(' ');
            string msgId = parts[0];

            if (msgId.StartsWith('B'))
            {
                ctx.UserId = parts.Length > 1 && int.TryParse(parts[1], out int uid) ? uid : 0;
                if (parts.Length > 4 && uint.TryParse(parts[4], out uint build))
                    ctx.ServerBuildNumber = build;
                _logger.LogDebug($"[Connect0] Approval (B): userId={ctx.UserId}, build={ctx.ServerBuildNumber}, payload={payload}");
                _logger.LogInformation($"Connection accepted by {endpoint}");

                _logger.LogDebug($"[State] Connect0 -> Connected. Sending 'new' stringcmd");
                await sendStringCmd(ClientCommandType.StringCmd, "new", ct);
                return SessionState.Connected;
            }

            if (msgId.StartsWith('9') || payload.Contains("Bad ", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning($"[Connect0] Server rejected connection: {payload}");
                return SessionState.Connect0;
            }

            _logger.LogWarning($"[Connect0] Generic answer (not B): {payload}");
            return SessionState.Connected;
        }

        return state;
    }

    /// <summary>
    /// Parses the leading decimal digits of a token, ignoring any trailing
    /// characters — the same lenient semantics the engine uses for numeric tokens.
    /// </summary>
    private static ulong ParseLeadingDigits(string token)
    {
        int end = 0;
        while (end < token.Length && char.IsDigit(token[end]))
            end++;
        return end > 0 && ulong.TryParse(token[..end], out ulong value) ? value : 0;
    }

    private byte[] BuildConnectPacket(ConnectionContext ctx, uint appId, string userInfo)
    {
        var challengeStr = Encoding.UTF8.GetString(ctx.Challenge);
        var authProto = ctx.AuthProtocol;

        byte[]? ticketBytes = null;
        string rawValue;
        string cdKeyHash;
        bool useGameTicket = authProvider.IsAvailable && ctx.RequiresGameAuthTicket;

        if (useGameTicket)
        {
            // The ticket binds to the server endpoint. The engine passes the IP as the
            // raw four address bytes and the port in network byte order; both ctx values
            // are converted to that layout here so providers receive Steam-ready values.
            ushort networkOrderPort = BinaryPrimitives.ReverseEndianness(ctx.ServerPort);
            ticketBytes = authProvider.GetGameAuthBytes(appId, ctx.ServerSteamId, ctx.ServerIp, networkOrderPort, ctx.IsVac2Secure);
            _logger.LogDebug($"[Connect] using game auth ticket: serverSteamId={ctx.ServerSteamId}, vacSecure={ctx.IsVac2Secure}, len={ticketBytes.Length}");
            rawValue = "steam";
            cdKeyHash = FakeHashedCdKey;
        }
        else if (authProvider.IsAvailable)
        {
            rawValue = "steam";
            cdKeyHash = FakeHashedCdKey;
            _logger.LogDebug($"[Connect] Steam available but server doesn't require game ticket, using fake hashed key");
        }
        else if (authProto == AuthProtocolHashedCdKey || authProto == AuthProtocolSteam)
        {
            rawValue = FakeHashedCdKey;
            cdKeyHash = rawValue;
            _logger.LogDebug($"[Connect] generated fake hashed key (32 hex chars) for authProto={authProto}");
        }
        else
        {
            rawValue = FakeWonCdKey;
            cdKeyHash = rawValue;
            _logger.LogDebug($"[Connect] generated fake WON CD key for authProto={authProto}");
        }

        var protoInfo = authProto >= AuthProtocolHashedCdKey
            ? $"\\prot\\{authProto}\\unique\\-1\\raw\\{rawValue}\\cdkey\\{cdKeyHash}"
            : $"\\prot\\{authProto}\\unique\\-1\\raw\\{rawValue}";

        _logger.LogDebug($"[Connect] packet: proto={settings.ProtocolVersion}, challenge={challengeStr}, authProto={authProto}, rawAuth={rawValue.Length}, ticket={(ticketBytes != null ? ticketBytes.Length : 0)}");

        var result = BuildRawConnectPacket(
            connectPrefix: $"connect {settings.ProtocolVersion} {challengeStr} ",
            protoInfo: protoInfo,
            userInfo: userInfo,
            ticketBytes: ticketBytes);

        _logger.LogDebug($"[Connect] raw hex: {Convert.ToHexString(result.AsSpan(0, Math.Min(result.Length, 64)))}");
        return result;
    }

    private static byte[] BuildRawConnectPacket(string connectPrefix, string protoInfo, string userInfo, byte[]? ticketBytes)
    {
        using var ms = new MemoryStream();

        ms.Write([0xFF, 0xFF, 0xFF, 0xFF]);
        ms.Write(Encoding.UTF8.GetBytes(connectPrefix));
        ms.WriteByte((byte)'\"');
        ms.Write(Encoding.UTF8.GetBytes(protoInfo));
        ms.WriteByte((byte)'\"');
        ms.WriteByte((byte)' ');
        ms.WriteByte((byte)'\"');
        ms.Write(Encoding.UTF8.GetBytes(userInfo));
        ms.WriteByte((byte)'\"');
        ms.WriteByte((byte)'\n');

        if (ticketBytes != null && ticketBytes.Length > 0)
            ms.Write(ticketBytes);

        return ms.ToArray();
    }
}
