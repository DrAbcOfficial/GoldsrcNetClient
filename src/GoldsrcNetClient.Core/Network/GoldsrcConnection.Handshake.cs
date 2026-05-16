using System.Net;
using System.Text;
using GoldsrcNetClient.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace GoldsrcNetClient.Core.Network;

public partial class GoldsrcConnection
{
    private async Task<SessionState> ProcessConnectionless(IPEndPoint ep, string payload, CancellationToken ct)
    {
        var state = _sessions[ep];
        var ctx = _contexts[ep];

        if (state == SessionState.GetChallenge)
        {
            var parts = payload.Split(' ');

            if (parts.Length >= 2 && parts[0].StartsWith('A'))
            {
                string challengeFromMarker = parts[0][1..];
                string challengeFromField = parts[1];

                ctx.AuthProtocol = parts.Length > 2 && int.TryParse(parts[2], out int ap) ? (byte)ap : AuthProtocolSteam;
                if (parts.Length > 3 && ulong.TryParse(parts[3], out ulong sid))
                    ctx.ServerSteamId = sid;
                if (parts.Length > 4 && int.TryParse(parts[4], out int reqTicket))
                    ctx.RequiresGameAuthTicket = reqTicket != 0;
                Logger.LogDebug($"[Challenge] Format A: markerHex={challengeFromMarker}, field2={challengeFromField}, authProto={ctx.AuthProtocol}, serverSteamId={ctx.ServerSteamId}, requiresTicket={ctx.RequiresGameAuthTicket}, parts={parts.Length}");

                string challengeToken = challengeFromField;
                Logger.LogDebug($"[Challenge] using field2 as challenge: {challengeToken}");

                ctx.Challenge = Encoding.UTF8.GetBytes(challengeToken);

                Logger.LogDebug($"[Challenge] parsed: challenge={challengeToken}, authProto={ctx.AuthProtocol}");
                var data = BuildConnectPacket(ep);
                Logger.LogDebug($"[Connect] sending connect packet, len={data.Length}");
                await _socket.SendAsync(new ReadOnlyMemory<byte>(data), ep, ct);
                return SessionState.Connect0;
            }

            if (parts.Length >= 2 && char.IsDigit(parts[0][0]))
            {
                string challengeToken = parts[1];
                Logger.LogDebug($"[Challenge] Format legacy: challenge={challengeToken}, parts={parts.Length}");

                ctx.Challenge = Encoding.UTF8.GetBytes(challengeToken);
                var data = BuildConnectPacket(ep);
                Logger.LogDebug($"[Connect] sending connect packet (legacy), len={data.Length}");
                await _socket.SendAsync(new ReadOnlyMemory<byte>(data), ep, ct);
                return SessionState.Connect0;
            }

            Logger.LogWarning($"[Challenge] unexpected response (len={parts.Length}, first={parts[0]}): {payload}");
        }
        else if (state == SessionState.Connect0)
        {
            var parts = payload.Split(' ');
            string msgId = parts[0];

            if (msgId.StartsWith('B'))
            {
                ctx.UserId = parts.Length > 1 && int.TryParse(parts[1], out int uid) ? uid : 0;
                Logger.LogDebug($"[Connect0] Approval (B): userId={ctx.UserId}, parts={parts.Length}, payload={payload}");
                Logger.LogInformation($"Connection accepted by {ep}");

                Logger.LogDebug($"[State] Connect0 -> Connected. Sending 'new' stringcmd");
                await SendStringCmdAsync(ClientCommandType.StringCmd, "new", ct);
                return SessionState.Connected;
            }

            if (msgId.StartsWith('9') || payload.Contains("Bad ", StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogWarning($"[Connect0] Server rejected connection: {payload}");
                return SessionState.Connect0;
            }

            Logger.LogWarning($"[Connect0] Generic answer (not B): {payload}");
            return SessionState.Connected;
        }

        return state;
    }

    private byte[] BuildConnectPacket(IPEndPoint ep)
    {
        var ctx = _contexts[ep];
        var challengeStr = Encoding.UTF8.GetString(ctx.Challenge);
        var authProto = ctx.AuthProtocol;

        byte[]? ticketBytes = null;
        string rawValue;
        string cdKeyHash;
        bool useGameTicket = _authProvider.IsAvailable && ctx.RequiresGameAuthTicket;

        if (useGameTicket)
        {
            ticketBytes = _authProvider.GetGameAuthBytes(ctx.ServerSteamId, ctx.ServerIp, ctx.ServerPort);
            Logger.LogDebug($"[Connect] using game auth ticket: serverSteamId={ctx.ServerSteamId}, len={ticketBytes.Length}");
            rawValue = "steam";
            cdKeyHash = "12345678901234567890123456789012";
        }
        else if (_authProvider.IsAvailable)
        {
            rawValue = "steam";
            cdKeyHash = "12345678901234567890123456789012";
            Logger.LogDebug($"[Connect] Steam available but server doesn't require game ticket, using fake hashed key");
        }
        else if (authProto == 2 || authProto == 3)
        {
            rawValue = "12345678901234567890123456789012";
            cdKeyHash = rawValue;
            Logger.LogDebug($"[Connect] generated fake hashed key (32 hex chars) for authProto={authProto}");
        }
        else
        {
            rawValue = "1234567890123";
            cdKeyHash = rawValue;
            Logger.LogDebug($"[Connect] generated fake WON CD key for authProto={authProto}");
        }

        var protoInfo = authProto >= 2
            ? $"\\prot\\{authProto}\\unique\\-1\\raw\\{rawValue}\\cdkey\\{cdKeyHash}"
            : $"\\prot\\{authProto}\\unique\\-1\\raw\\{rawValue}";

        var userInfo = UserInfo;
        Logger.LogDebug($"[Connect] packet: proto={Settings.ProtocolVersion}, challenge={challengeStr}, authProto={authProto}, rawAuth={rawValue.Length}, ticket={(ticketBytes != null ? ticketBytes.Length : 0)}");

        var result = BuildRawConnectPacket(
            connectPrefix: $"connect {Settings.ProtocolVersion} {challengeStr} ",
            protoInfo: protoInfo,
            userInfo: userInfo,
            ticketBytes: ticketBytes);

        Logger.LogDebug($"[Connect] raw hex: {Convert.ToHexString(result.AsSpan(0, Math.Min(result.Length, 64)))}");
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
