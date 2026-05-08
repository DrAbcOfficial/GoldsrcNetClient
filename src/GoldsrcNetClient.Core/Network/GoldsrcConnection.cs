using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GoldsrcNetClient.Core.Delta;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Munge;
using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.Core.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// Main entry point for the GoldSrc (Half-Life 1) engine network client.
/// Handles the full connection handshake (getchallenge → connect → connected),
/// server message processing, packet Munge/UnMunge encryption, delta compression parsing,
/// and resource list decoding. Supports both WON and Steam authentication protocols.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// var conn = new GoldsrcConnection(logger, authProvider);
/// conn.OnServerInfo += (c, info) => Console.WriteLine($"Player #{info.PlayerNumber}");
/// await conn.ConnectAsync("127.0.0.1", 27015);
/// await conn.Connected;  // wait for handshake completion
/// </code>
/// </remarks>
public class GoldsrcConnection : IDisposable
{
    private static readonly byte[] GetChallengeSteamPacket =
        [0xFF, 0xFF, 0xFF, 0xFF, (byte)'g', (byte)'e', (byte)'t', (byte)'c', (byte)'h', (byte)'a', (byte)'l', (byte)'l', (byte)'e', (byte)'n', (byte)'g', (byte)'e', (byte)' ', (byte)'s', (byte)'t', (byte)'e', (byte)'a', (byte)'m', (byte)'\n'];
    private static readonly byte[] GetChallengePacket =
        [0xFF, 0xFF, 0xFF, 0xFF, (byte)'g', (byte)'e', (byte)'t', (byte)'c', (byte)'h', (byte)'a', (byte)'l', (byte)'l', (byte)'e', (byte)'n', (byte)'g', (byte)'e', (byte)'\n'];

    private const byte AuthProtocolSteam = 3;
    private const byte AuthProtocolWon = 1;
    private const byte AuthProtocolHashedCdKey = 2;
    private const int ProtocolVersion = 48;

    private readonly UdpClient _socket;
    private readonly Dictionary<IPEndPoint, SessionState> _sessions = [];
    private readonly Dictionary<IPEndPoint, ConnectionContext> _contexts = [];
    private readonly ISteamAuthProvider _authProvider;
    private readonly IServerMessageHandler _messageHandler;
    private readonly ILogger<GoldsrcConnection> _logger;
    private readonly TaskCompletionSource _connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Delegate for <see cref="OnServerInfo"/> events.</summary>
    /// <param name="conn">The connection that received the server info.</param>
    /// <param name="info">Parsed server info data.</param>
    public delegate void ServerInfoHandler(GoldsrcConnection conn, ServerInfoData info);

    /// <summary>
    /// Raised when the server sends its <see cref="ServerMessageType.ServerInfo"/> block.
    /// Contains protocol version, spawn count, worldmap CRC, player slot, and more.
    /// </summary>
    public event ServerInfoHandler? OnServerInfo;

    /// <summary>Delegate for <see cref="OnResourceList"/> events.</summary>
    /// <param name="conn">The connection that received the resource list.</param>
    /// <param name="resources">Array of resource descriptors (maps, models, sounds).</param>
    public delegate void ResourceListHandler(GoldsrcConnection conn, ResourceInfo[] resources);

    /// <summary>
    /// Raised when the server sends its <see cref="ServerMessageType.ResourceList"/>.
    /// Lists all resources the server expects the client to have.
    /// </summary>
    public event ResourceListHandler? OnResourceList;

    /// <summary>Delegate for <see cref="OnDataPacket"/> events.</summary>
    /// <param name="conn">The connection that received the data.</param>
    /// <param name="data">Raw packet bytes (including header) for debugging or external processing.</param>
    public delegate void DataPacketHandler(GoldsrcConnection conn, byte[] data);

    /// <summary>Raised for every connected (sequenced) packet received, before decryption.</summary>
    public event DataPacketHandler? OnDataPacket;

    /// <summary>A task that completes when the connection handshake reaches <see cref="SessionState.Connected"/>.</summary>
    public Task Connected => _connectedTcs.Task;

    /// <summary>
    /// The current userinfo string sent during the connect handshake.
    /// Uses the GoldSrc backslash-delimited key-value format:
    /// <c>\name\PlayerName\protocol\48\...</c>.
    /// Updated automatically when the server sends <see cref="ServerMessageType.UpdateUserInfo"/>.
    /// </summary>
    public string UserInfo { get; set; } = "\\name\\GoldsrcNetClient\\protocol\\48\\cl_lc\\1\\cl_lw\\1\\cl_updaterate\\60\\rate\\20000\\hltv\\0";

    /// <summary>Sets a single key-value pair in the <see cref="UserInfo"/> string.</summary>
    /// <param name="key">The key to set (case-insensitive).</param>
    /// <param name="value">The new value for the key.</param>
    public void SetUserInfo(string key, string value)
    {
        var current = UserInfo;
        var parts = current.Split('\\');
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i + 1 < parts.Length; i += 2)
            dict[parts[i]] = parts[i + 1];

        dict[key] = value;

        var sb = new System.Text.StringBuilder();
        foreach (var kv in dict)
        {
            sb.Append('\\');
            sb.Append(kv.Key);
            sb.Append('\\');
            sb.Append(kv.Value);
        }
        UserInfo = sb.ToString();
    }

    /// <summary>Gets a value from the <see cref="UserInfo"/> string by key.</summary>
    /// <param name="key">The key to look up (case-insensitive).</param>
    /// <returns>The value string if found; <c>null</c> otherwise.</returns>
    public string? GetUserInfo(string key)
    {
        var parts = UserInfo.Split('\\');
        for (int i = 1; i + 1 < parts.Length; i += 2)
        {
            if (string.Equals(parts[i], key, StringComparison.OrdinalIgnoreCase))
                return parts[i + 1];
        }
        return null;
    }

    /// <summary>
    /// Creates a new GoldSrc connection.
    /// </summary>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger{GoldsrcConnection}"/>.</param>
    /// <param name="authProvider">Steam auth provider; defaults to <see cref="NoSteamAuthProvider"/> which sends a fake key.</param>
    /// <param name="messageHandler">Optional server message handler. Called for each message type in connected packets
    /// before built-in processing. Return <c>true</c> to consume the message; <c>false</c> to fall through to the default parser.
    /// Defaults to <see cref="DefaultServerMessageHandler"/> which always delegates to built-in logic.</param>
    /// <param name="localPort">Local UDP port to bind (0 = OS-assigned).</param>
    public GoldsrcConnection(ILogger<GoldsrcConnection>? logger = null, ISteamAuthProvider? authProvider = null, IServerMessageHandler? messageHandler = null, int localPort = 0)
    {
        _logger = logger ?? NullLogger<GoldsrcConnection>.Instance;
        _authProvider = authProvider ?? new NoSteamAuthProvider();
        _messageHandler = messageHandler ?? new DefaultServerMessageHandler();
        _socket = new UdpClient(localPort);
    }

    /// <summary>
    /// Resolves the hostname and begins the connection handshake.
    /// This method blocks until the <paramref name="ct"/> cancellation token is triggered.
    /// Use <see cref="Connected"/> to await handshake completion.
    /// </summary>
    /// <param name="host">Server hostname or IP address.</param>
    /// <param name="port">Server UDP port (default 27015).</param>
    /// <param name="ct">Cancellation token to stop the receive loop.</param>
    public async Task ConnectAsync(string host, int port = 27015, CancellationToken ct = default)
    {
        _logger.LogDebug($"[State] Begin -> resolving {host}:{port}");
        var addresses = await Dns.GetHostAddressesAsync(host, ct);
        var ip = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];
        var ep = new IPEndPoint(ip, port);
        _logger.LogDebug($"[DNS] resolved {host} -> {ep}");
        _sessions[ep] = SessionState.GetChallenge;
        _contexts[ep] = new ConnectionContext { ServerIp = BitConverter.ToUInt32(ep.Address.GetAddressBytes()), ServerPort = (ushort)ep.Port };

        _logger.LogDebug($"[State] Begin -> GetChallenge. Sending getchallenge (steam={_authProvider.IsAvailable}, authProto={_authProvider.GetAuthProtocol()})");
        var challengePacket = _authProvider.IsAvailable ? GetChallengeSteamPacket : GetChallengePacket;
        await _socket.SendAsync(new ReadOnlyMemory<byte>(challengePacket), ep, ct);

        _logger.LogDebug("[Loop] entering receive loop");
        while (!ct.IsCancellationRequested)
        {
            _logger.LogTrace("[Loop] waiting for packet...");
            var result = await _socket.ReceiveAsync(ct);
            var data = result.Buffer;
            var len = data.Length;
            var from = result.RemoteEndPoint;

            _logger.LogDebug($"[Loop] received {len} bytes from {from}");
            if (!ep.Equals(from)) continue;
            if (len < 4) continue;

            int offset = 0;
            uint header = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4));
            offset += 4;

            if (header == MessageConstants.ConnectionlessMarker)
            {
                var payload = Encoding.UTF8.GetString(data, offset, len - offset);
                _logger.LogDebug($"connectionless: {payload[..Math.Min(payload.Length, 200)]}");
                _sessions[ep] = await ProcessConnectionless(ep, payload, ct);
                if (_sessions[ep] == SessionState.Connected)
                {
                    _logger.LogInformation("[State] -> Connected. Handshake complete.");
                    _connectedTcs.TrySetResult();
                }
            }
            else if (header == MessageConstants.SplitMarker)
            {
                _logger.LogWarning($"[Fragment] received split packet header (len={len}), fragment reassembly not yet implemented");
            }
            else
            {
                var ctx = _contexts[ep];
                uint srcSeq = header & MessageConstants.SequenceMask;
                uint dstSeq = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
                offset += 4;

                ctx.DstSequence = srcSeq;

                int payloadLen = len - offset;
                _logger.LogDebug($"connected packet: srcSeq={srcSeq}, dstSeq={dstSeq}, payload={payloadLen}");
                OnDataPacket?.Invoke(this, data);
                if (payloadLen > 0)
                {
                    byte[] payload = new byte[payloadLen];
                    Array.Copy(data, offset, payload, 0, payloadLen);

                    bool isCommand = (header & MessageConstants.SequenceModeCommand) != 0;
                    if (!isCommand && payloadLen == 8 && BitConverter.ToUInt64(payload, 0) == MessageConstants.AckData)
                    {
                        _logger.LogDebug($"[Ack] received ack, dstSeq={dstSeq}");
                        continue;
                    }

                    if (!isCommand)
                    {
                        _logger.LogDebug($"[Munge] UnMunge2 payload len={payloadLen}, seq={(int)(srcSeq & 0xFF)}");
                        MungeEngine.UnMunge2(payload, payloadLen, (int)(srcSeq & 0xFF));
                    }
                    else
                    {
                        _logger.LogDebug($"[Munge] Command packet, skipping UnMunge2 for payload len={payloadLen}");
                    }

                    _sessions[ep] = ProcessConnected(ep, ref srcSeq, ref dstSeq, payload, payloadLen);
                }
            }
        }
    }

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
                _logger.LogDebug($"[Challenge] Format A: markerHex={challengeFromMarker}, field2={challengeFromField}, authProto={ctx.AuthProtocol}, serverSteamId={ctx.ServerSteamId}, requiresTicket={ctx.RequiresGameAuthTicket}, parts={parts.Length}");

                string challengeToken = challengeFromField;
                _logger.LogDebug($"[Challenge] using field2 as challenge: {challengeToken}");

                ctx.Challenge = Encoding.UTF8.GetBytes(challengeToken);

                _logger.LogDebug($"[Challenge] parsed: challenge={challengeToken}, authProto={ctx.AuthProtocol}");
                var data = BuildConnectPacket(ep);
                _logger.LogDebug($"[Connect] sending connect packet, len={data.Length}");
                await _socket.SendAsync(new ReadOnlyMemory<byte>(data), ep, ct);
                return SessionState.Connect0;
            }

            if (parts.Length >= 2 && char.IsDigit(parts[0][0]))
            {
                string challengeToken = parts[1];
                _logger.LogDebug($"[Challenge] Format legacy: challenge={challengeToken}, parts={parts.Length}");

                ctx.Challenge = Encoding.UTF8.GetBytes(challengeToken);
                var data = BuildConnectPacket(ep);
                _logger.LogDebug($"[Connect] sending connect packet (legacy), len={data.Length}");
                await _socket.SendAsync(new ReadOnlyMemory<byte>(data), ep, ct);
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
                _logger.LogDebug($"[Connect0] Approval (B): userId={ctx.UserId}, parts={parts.Length}, payload={payload}");
                _logger.LogInformation($"Connection accepted by {ep}");

                _logger.LogDebug($"[State] Connect0 -> Connected. Sending 'new' stringcmd");
                await SendConnectedAsync(ep, ClientCommandType.StringCmd, "new", ct);
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
            _logger.LogDebug($"[Connect] using game auth ticket: serverSteamId={ctx.ServerSteamId}, len={ticketBytes.Length}");
            rawValue = "steam";
            cdKeyHash = "12345678901234567890123456789012";
        }
        else if (_authProvider.IsAvailable)
        {
            rawValue = "steam";
            cdKeyHash = "12345678901234567890123456789012";
            _logger.LogDebug($"[Connect] Steam available but server doesn't require game ticket, using fake hashed key");
        }
        else if (authProto == 2 || authProto == 3)
        {
            rawValue = "12345678901234567890123456789012";
            cdKeyHash = rawValue;
            _logger.LogDebug($"[Connect] generated fake hashed key (32 hex chars) for authProto={authProto}");
        }
        else
        {
            rawValue = "1234567890123";
            cdKeyHash = rawValue;
            _logger.LogDebug($"[Connect] generated fake WON CD key for authProto={authProto}");
        }

        var protoInfo = authProto >= 2
            ? $"\\prot\\{authProto}\\unique\\-1\\raw\\{rawValue}\\cdkey\\{cdKeyHash}"
            : $"\\prot\\{authProto}\\unique\\-1\\raw\\{rawValue}";

        var userInfo = UserInfo;
        _logger.LogDebug($"[Connect] packet: proto={ProtocolVersion}, challenge={challengeStr}, authProto={authProto}, rawAuth={rawValue.Length}, ticket={(ticketBytes != null ? ticketBytes.Length : 0)}");

        var result = BuildRawConnectPacket(
            connectPrefix: $"connect {ProtocolVersion} {challengeStr} ",
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

    private SessionState ProcessConnected(IPEndPoint ep, ref uint srcSequence, ref uint dstSequence,
        byte[] data, int size)
    {
        var ctx = _contexts[ep];

        _logger.LogDebug($"[Connected] processing {size} bytes, srcSeq={srcSequence}, dstSeq={dstSequence}");

        int offset = 0;
        while (offset < size)
        {
            byte dataType = data[offset++];
            int dataLen = size - offset;
            string typeName = Enum.IsDefined(typeof(ServerMessageType), dataType) ? ((ServerMessageType)dataType).ToString() : $"0x{dataType:X2}";
            _logger.LogDebug($"[Connected] type={typeName} (0x{dataType:X2}), remaining={dataLen}");

            if (_messageHandler.HandleMessage(this, dataType, data, ref offset, size))
                continue;

            if (dataType == (byte)ServerMessageType.Nop) { }
            else if (dataType == (byte)ServerMessageType.Bad)
            {
                _logger.LogWarning("[Bad] server sent bad message, consuming remaining data");
                offset = size;
            }
            else if (dataType == (byte)ServerMessageType.Disconnect)
            {
                string reason = MessageReader.ReadString(ref data, ref offset, size);
                _logger.LogWarning($"[Disconnect] server disconnected: reason=\"{reason}\"");
                offset = size;
            }
            else if (dataType == (byte)ServerMessageType.Print)
            {
                string msg = MessageReader.ReadString(ref data, ref offset, size);
                _logger.LogDebug($"[Print] msg=\"{msg[..Math.Min(msg.Length, 200)]}\"");
                _logger.LogInformation(msg);
            }
            else if (dataType == (byte)ServerMessageType.ServerInfo)
            {
                int structSize = 33;
                if (offset + structSize > size)
                {
                    _logger.LogWarning($"[ServerInfo] buffer overflow: offset={offset}, need={structSize}, size={size}");
                    return SessionState.Connected;
                }

                ServerInfoData si;
                unsafe
                {
                    fixed (byte* p = &data[offset])
                        si = *(ServerInfoData*)p;
                }

                offset += structSize;

                ctx.MaxClients = si.MaxClients;
                ctx.PlayerNumber = si.PlayerNumber;

                byte[] crcBytes = new byte[4];
                BitConverter.GetBytes(si.Munge3WorldmapCrc).CopyTo(crcBytes, 0);
                uint unmungeSeq = (uint)((-1 - ctx.PlayerNumber) & 0xFF);
                _logger.LogDebug($"[ServerInfo] proto={si.ProtocolVersion}, spawnCount={si.SpawnCount}, maxClients={si.MaxClients}, playerNum={si.PlayerNumber}, worldmapCrcRaw=0x{si.Munge3WorldmapCrc:X8}, unmungeSeq={unmungeSeq}");
                MungeEngine.UnMunge3(crcBytes, 4, (int)((-1 - ctx.PlayerNumber) & 0xFF));
                ctx.WorldmapCrc = BitConverter.ToUInt32(crcBytes);
                _logger.LogDebug($"[ServerInfo] worldmapCrcUnMunaged=0x{ctx.WorldmapCrc:X8}");

                for (int i = 0; i < 4; i++)
                    MessageReader.ReadString(ref data, ref offset, size);

                if (offset + 1 <= size)
                    offset++;

                OnServerInfo?.Invoke(this, si);
            }
            else if (dataType == (byte)ServerMessageType.DeltaDescription)
            {
                string name = MessageReader.ReadString(ref data, ref offset, size);
                _logger.LogDebug($"[DeltaDescription] deltaName=\"{name}\"");
                var dt = DeltaDefinitions.Find(name);
                if (dt == null)
                {
                    _logger.LogWarning($"[DeltaDescription] unknown delta \"{name}\", skipping");
                    return SessionState.Connected;
                }

                int bitIdx = 0;
                uint fieldCount = 0;
                if (!BitReader.ReadBits(data, ref bitIdx, size, ref fieldCount, 16))
                {
                    _logger.LogWarning($"[DeltaDescription] failed reading fieldCount");
                    return SessionState.Connected;
                }
                _logger.LogDebug($"[DeltaDescription] fieldCount={fieldCount}");

                for (uint f = 0; f < fieldCount; f++)
                {
                    int parseBitIdx = 0;
                    if (!ParseDeltaFieldDescriptions(data, size, ref parseBitIdx))
                    {
                        _logger.LogWarning($"[DeltaDescription] failed parsing field {f}");
                        return SessionState.Connected;
                    }
                }

                offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
                _logger.LogDebug($"[DeltaDescription] done, new offset={offset}");
            }
            else if (dataType == (byte)ServerMessageType.NewMoveVars)
            {
                int mvSize;
                unsafe { mvSize = sizeof(NewMoveVarsData); }
                if (offset + mvSize > size)
                {
                    _logger.LogWarning($"[NewMoveVars] buffer overflow: offset={offset}, need={mvSize}, size={size}");
                    return SessionState.Connected;
                }

                unsafe
                {
                    fixed (byte* p = &data[offset])
                        _ = *(NewMoveVarsData*)p;
                    offset += mvSize;
                }

                MessageReader.ReadString(ref data, ref offset, size);
                _logger.LogDebug($"[NewMoveVars] done, structSize={mvSize}");
            }
            else if (dataType == (byte)ServerMessageType.SetView)
            {
                if (offset + 2 > size)
                {
                    _logger.LogWarning($"[SetView] buffer overflow: offset={offset}, size={size}");
                    return SessionState.Connected;
                }
                offset += 2;
            }
            else if (dataType == (byte)ServerMessageType.NewUserMsg)
            {
                int msgSize;
                unsafe { msgSize = sizeof(NewUserMsgData); }
                if (offset + msgSize > size)
                {
                    _logger.LogWarning($"[NewUserMsg] buffer overflow: offset={offset}, need={msgSize}, size={size}");
                    return SessionState.Connected;
                }

                unsafe
                {
                    fixed (byte* p = &data[offset])
                        _ = *(NewUserMsgData*)p;
                    offset += msgSize;
                }
                _logger.LogDebug($"[NewUserMsg] done, structSize={msgSize}");
            }
            else if (dataType == (byte)ServerMessageType.StuffText)
            {
                string st = MessageReader.ReadString(ref data, ref offset, size);
                _logger.LogDebug($"[StuffText] text=\"{st[..Math.Min(st.Length, 200)]}\"");
            }
            else if (dataType == (byte)ServerMessageType.UpdateUserInfo)
            {
                if (offset + 1 > size) { _logger.LogWarning("[UpdateUserInfo] buffer overflow at byte 1"); return SessionState.Connected; }
                offset += 1;
                if (offset + 4 > size) { _logger.LogWarning("[UpdateUserInfo] buffer overflow at byte 4"); return SessionState.Connected; }
                offset += 4;
                string uui = MessageReader.ReadString(ref data, ref offset, size);
                UserInfo = uui;
                _logger.LogDebug($"[UpdateUserInfo] userInfo=\"{uui[..Math.Min(uui.Length, 100)]}\"");
                if (offset + 16 > size) { _logger.LogWarning("[UpdateUserInfo] buffer overflow at 16"); return SessionState.Connected; }
                offset += 16;
            }
            else if (dataType == (byte)ServerMessageType.ResourceRequest)
            {
                if (offset + 4 > size) { _logger.LogWarning("[ResourceRequest] buffer overflow"); return SessionState.Connected; }
                ctx.SpawnCount = BitConverter.ToUInt32(data, offset);
                offset += 4;
                if (offset + 4 > size) { _logger.LogWarning("[ResourceRequest] buffer overflow after spawnCount"); return SessionState.Connected; }
                uint resUnknown = BitConverter.ToUInt32(data, offset);
                offset += 4;
                _logger.LogDebug($"[ResourceRequest] spawnCount={ctx.SpawnCount}, unknown={resUnknown}");
            }
            else if (dataType == (byte)ServerMessageType.ResourceLocation)
            {
                string loc = MessageReader.ReadString(ref data, ref offset, size);
                _logger.LogDebug($"[ResourceLocation] location=\"{loc}\"");
            }
            else if (dataType == (byte)ServerMessageType.ResourceList)
            {
                int listStart = offset;
                ProcessResourceList(ctx, ref data, ref offset, size);
                _logger.LogDebug($"[ResourceList] count={ctx.Resources.Length}, dataBytes={offset - listStart}");
                OnResourceList?.Invoke(this, ctx.Resources);
            }
            else if (dataType == (byte)ServerMessageType.TempEntity)
            {
                if (offset + 1 > size) { _logger.LogWarning("[TempEntity] buffer overflow"); return SessionState.Connected; }
                offset += 1;
                for (int i = 0; i < 3; i++)
                {
                    if (offset + 2 > size) { _logger.LogWarning($"[TempEntity] buffer overflow at coord {i}"); return SessionState.Connected; }
                    offset += 2;
                }
            }
            else if (dataType == (byte)ServerMessageType.SpawnStaticSound)
            {
                offset += 14;
            }
            else if (dataType == (byte)ServerMessageType.SendCvarValue2)
            {
                if (offset + 4 > size) { _logger.LogWarning("[SendCvarValue2] buffer overflow"); return SessionState.Connected; }
                uint requestId = BitConverter.ToUInt32(data, offset);
                offset += 4;
                string cvarName = MessageReader.ReadString(ref data, ref offset, size);
                _logger.LogDebug($"[SendCvarValue2] requestId={requestId}, cvar=\"{cvarName}\"");
            }
            else if (dataType == (byte)ServerMessageType.SpawnBaseline)
            {
                int bitIdx = 0;
                int entityCount = 0;
                while (true)
                {
                    uint entityNumber = 0;
                    if (!BitReader.ReadBits(data, ref bitIdx, size, ref entityNumber, 11))
                    {
                        _logger.LogWarning($"[SpawnBaseline] failed reading entityNumber at count={entityCount}");
                        return SessionState.Connected;
                    }

                    int maxEntity = (1 << 11) - 1;
                    if (entityNumber == maxEntity)
                    {
                        bitIdx += 5;
                        break;
                    }

                    uint entityType = 0;
                    if (!BitReader.ReadBits(data, ref bitIdx, size, ref entityType, 2))
                    {
                        _logger.LogWarning($"[SpawnBaseline] failed reading entityType at entity={entityNumber}");
                        return SessionState.Connected;
                    }

                    DeltaType dt;
                    if ((entityType & 1) != 0)
                    {
                        bool isPlayer = entityNumber >= 1 && entityNumber <= ctx.MaxClients;
                        dt = isPlayer ? DeltaDefinitions.EntityStatePlayer : DeltaDefinitions.EntityState;
                    }
                    else
                    {
                        dt = DeltaDefinitions.CustomEntityState;
                    }

                    ParseDeltaFields(dt, data, size, ref bitIdx);
                    entityCount++;
                }

                uint baselineCount = 0;
                BitReader.ReadBits(data, ref bitIdx, size, ref baselineCount, 6);
                _logger.LogDebug($"[SpawnBaseline] entities={entityCount}, baselineCount={baselineCount}");
                for (uint ei = 0; ei < baselineCount; ei++)
                    ParseDeltaFields(DeltaDefinitions.EntityState, data, size, ref bitIdx);

                offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
                _logger.LogDebug($"[SpawnBaseline] done, totalBits={bitIdx}, newOffset={offset}");
            }
            else if (dataType == (byte)ServerMessageType.Time)
            {
                float time = BitConverter.ToSingle(data, offset);
                offset += 4;
                _logger.LogDebug($"[Time] time={time:F2}");
            }
            else if (dataType == (byte)ServerMessageType.LightStyle)
            {
                if (offset + 1 > size) { _logger.LogWarning("[LightStyle] buffer overflow"); return SessionState.Connected; }
                offset += 1;
                MessageReader.ReadString(ref data, ref offset, size);
            }
            else if (dataType == (byte)ServerMessageType.SetAngle)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (offset + 2 > size) { _logger.LogWarning($"[SetAngle] buffer overflow at angle {i}"); return SessionState.Connected; }
                    offset += 2;
                }
            }
            else if (dataType == (byte)ServerMessageType.ClientData)
            {
                int bitIdx = 0;
                uint haveDeltaSeq = 0;
                if (!BitReader.ReadBits(data, ref bitIdx, size, ref haveDeltaSeq, 1)) return SessionState.Connected;
                if (haveDeltaSeq != 0)
                {
                    uint deltaSeq = 0;
                    if (!BitReader.ReadBits(data, ref bitIdx, size, ref deltaSeq, 8)) return SessionState.Connected;
                    _logger.LogDebug($"[ClientData] deltaSeq={deltaSeq}");
                }
                ParseDeltaFields(DeltaDefinitions.ClientData, data, size, ref bitIdx);

                int weaponCount = 0;
                while (true)
                {
                    uint haveDelta = 0;
                    if (!BitReader.ReadBits(data, ref bitIdx, size, ref haveDelta, 1)) return SessionState.Connected;
                    if (haveDelta == 0) break;
                    uint index = 0;
                    if (!BitReader.ReadBits(data, ref bitIdx, size, ref index, 6)) return SessionState.Connected;
                    ParseDeltaFields(DeltaDefinitions.WeaponData, data, size, ref bitIdx);
                    weaponCount++;
                }
                _logger.LogDebug($"[ClientData] done, weaponDeltas={weaponCount}");
                offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
            }
            else if (dataType == (byte)ServerMessageType.SignOnNum)
            {
                if (offset + 1 > size) { _logger.LogWarning("[SignOnNum] buffer overflow"); return SessionState.Connected; }
                byte signOn = data[offset++];
                _logger.LogDebug($"[SignOnNum] value={signOn}");
            }
            else if (dataType == (byte)ServerMessageType.VoiceInit)
            {
                string codec = MessageReader.ReadString(ref data, ref offset, size);
                if (offset + 1 > size) { _logger.LogWarning("[VoiceInit] buffer overflow"); return SessionState.Connected; }
                byte quality = data[offset++];
                _logger.LogDebug($"[VoiceInit] codec=\"{codec}\", quality={quality}");
            }
            else if (dataType == (byte)ServerMessageType.Sound)
            {
                int bitIdx = 0;
                uint fieldMask = 0;
                if (!BitReader.ReadBits(data, ref bitIdx, size, ref fieldMask, 9)) return SessionState.Connected;

                if ((fieldMask & SoundFlags.Volume) != 0)
                {
                    uint vol = 0;
                    if (!BitReader.ReadBits(data, ref bitIdx, size, ref vol, 8)) return SessionState.Connected;
                }
                if ((fieldMask & SoundFlags.Attenuation) != 0)
                {
                    uint attn = 0;
                    if (!BitReader.ReadBits(data, ref bitIdx, size, ref attn, 8)) return SessionState.Connected;
                }

                uint channel = 0;
                if (!BitReader.ReadBits(data, ref bitIdx, size, ref channel, 3)) return SessionState.Connected;
                uint entity = 0;
                if (!BitReader.ReadBits(data, ref bitIdx, size, ref entity, 11)) return SessionState.Connected;
                uint soundNum = 0;
                int snBits = (fieldMask & SoundFlags.LargeIndex) != 0 ? 16 : 8;
                if (!BitReader.ReadBits(data, ref bitIdx, size, ref soundNum, snBits)) return SessionState.Connected;

                float ox = 0, oy = 0, oz = 0;
                uint xf = 0, yf = 0, zf = 0;
                BitReader.ReadBits(data, ref bitIdx, size, ref xf, 1);
                BitReader.ReadBits(data, ref bitIdx, size, ref yf, 1);
                BitReader.ReadBits(data, ref bitIdx, size, ref zf, 1);
                if (xf != 0) BitReader.ReadBitCoord(data, ref bitIdx, size, ref ox);
                if (yf != 0) BitReader.ReadBitCoord(data, ref bitIdx, size, ref oy);
                if (zf != 0) BitReader.ReadBitCoord(data, ref bitIdx, size, ref oz);

                if ((fieldMask & SoundFlags.Pitch) != 0)
                {
                    uint pitch = 0;
                    if (!BitReader.ReadBits(data, ref bitIdx, size, ref pitch, 8)) return SessionState.Connected;
                }
                _logger.LogDebug($"[Sound] channel={channel}, entity={entity}, soundNum={soundNum}, fieldMask=0x{fieldMask:X4}");
                offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
            }
            else if (dataType == (byte)ServerMessageType.Customization)
            {
                if (offset + 1 > size) { _logger.LogWarning("[Customization] buffer overflow at 1"); return SessionState.Connected; }
                offset += 1;
                if (offset + 1 > size) { _logger.LogWarning("[Customization] buffer overflow at 2"); return SessionState.Connected; }
                offset += 1;
                MessageReader.ReadString(ref data, ref offset, size);
                if (offset + 2 > size) { _logger.LogWarning("[Customization] buffer overflow at 3"); return SessionState.Connected; }
                offset += 2;
                if (offset + 4 > size) { _logger.LogWarning("[Customization] buffer overflow at 4"); return SessionState.Connected; }
                offset += 4;
                if (offset + 1 > size) { _logger.LogWarning("[Customization] buffer overflow at 5"); return SessionState.Connected; }
                offset += 1;
            }
            else if (dataType == (byte)ServerMessageType.Choke) { }
            else if (dataType == 'B' && offset + 2 < size && data[offset] == 'Z' && data[offset + 1] == '2' && data[offset + 2] == 0)
            {
                _logger.LogWarning("[BZ2] compressed data detected, skipping");
                _logger.LogWarning("BZ2 compressed data received (decompression not yet supported)");
                offset = size;
            }
            else if (ScanForBz2(data, offset, size))
            {
                _logger.LogWarning("[BZ2] compressed data detected at offset, skipping");
                offset = size;
            }
            else
            {
                offset--;
                _logger.LogWarning($"[Connected] Unknown data type: 0x{dataType:X2} at offset={offset}, remaining={size - offset}");
                return SessionState.Connected;
            }
        }

        _logger.LogDebug($"[Connected] processed all {size} bytes successfully");
        return SessionState.Connected;
    }

    private async Task SendConnectedAsync(IPEndPoint ep, ClientCommandType cmd, string str, CancellationToken ct)
    {
        var ctx = _contexts[ep];
        uint srcSeq = ctx.SrcSequence++;

        var cmdBytes = new List<byte> { (byte)cmd };
        cmdBytes.AddRange(Encoding.UTF8.GetBytes(str));
        cmdBytes.Add(0);

        var payload = new byte[cmdBytes.Count + MessageConstants.ConnectedHeadSize];
        BitConverter.GetBytes(srcSeq | MessageConstants.SequenceModeCommand).CopyTo(payload, 0);
        BitConverter.GetBytes(ctx.DstSequence & MessageConstants.SequenceMask).CopyTo(payload, 4);
        cmdBytes.CopyTo(payload, MessageConstants.ConnectedHeadSize);

        _logger.LogDebug($"[SendConnected] cmd={cmd}(0x{(byte)cmd:X2}), str=\"{str}\", srcSeq={srcSeq}, dstSeq={ctx.DstSequence}, mungeKey={srcSeq & 0xFF}, totalLen={payload.Length}");
        await _socket.SendAsync(new ReadOnlyMemory<byte>(payload), ep, ct);
    }

    private static List<byte> MungeBytes(List<byte> data, int seq)
    {
        var arr = data.ToArray();
        MungeEngine.Munge2(arr, arr.Length, seq);
        return [.. arr];
    }

    private bool ParseDeltaFields(DeltaType dt, byte[] data, int size, ref int bitIdx)
    {
        uint byteCount = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref byteCount, 3))
            return false;

        if (byteCount > dt.FieldAmount / 8 + (dt.FieldAmount % 8 != 0 ? 1 : 0))
            return false;

        ulong markArray = 0;
        for (uint i = 0; i < byteCount; i++)
        {
            uint b = 0;
            if (!BitReader.ReadBits(data, ref bitIdx, size, ref b, 8))
                return false;
            markArray |= (ulong)b << (int)(i * 8);
        }

        uint toIterate = byteCount * 8;
        if (toIterate > dt.FieldAmount) toIterate = dt.FieldAmount;

        for (uint i = 0; i < toIterate; i++)
        {
            if ((markArray & 1) != 0)
            {
                var field = dt.Fields[i];
                if ((field.FieldFlag & DeltaFieldFlag.StringField) != 0)
                {
                    for (int s = 0; s < 32; s++)
                    {
                        uint ch = 0;
                        if (!BitReader.ReadBits(data, ref bitIdx, size, ref ch, 8))
                            return false;
                        if (ch == 0) break;
                    }
                }
                else
                {
                    uint filler = 0;
                    if (!BitReader.ReadBits(data, ref bitIdx, size, ref filler, field.Bits))
                        return false;
                }
            }
            markArray >>= 1;
        }

        return true;
    }

    private bool ParseDeltaFieldDescriptions(byte[] data, int size, ref int bitIdx)
    {
        uint fieldType = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref fieldType, 32)) return false;
        if (!BitReader.ReadBitString(data, ref bitIdx, size, out _)) return false;
        uint fieldOffset = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref fieldOffset, 16)) return false;
        uint fieldSize = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref fieldSize, 8)) return false;
        uint sigBits = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref sigBits, 8)) return false;
        uint preMul = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref preMul, 32)) return false;
        uint postMul = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref postMul, 32)) return false;
        return true;
    }

    private static void ProcessResourceList(ConnectionContext ctx, ref byte[] data, ref int offset, int size)
    {
        int bitIdx = 0;
        uint resourceCount = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref resourceCount, 12)) return;

        ctx.Resources = new ResourceInfo[resourceCount];
        for (uint i = 0; i < resourceCount; i++)
        {
            var r = new ResourceInfo();
            ctx.Resources[i] = r;

            uint type = 0;
            if (!BitReader.ReadBits(data, ref bitIdx, size, ref type, 4)) return;

            if (!BitReader.ReadBitString(data, ref bitIdx, size, out r.Name)) return;
            if (r.Name.Length > 64) return;

            uint resIdx = 0;
            if (!BitReader.ReadBits(data, ref bitIdx, size, ref resIdx, 12)) return;
            uint dlSize = 0;
            if (!BitReader.ReadBits(data, ref bitIdx, size, ref dlSize, 24)) return;
            uint flag = 0;
            if (!BitReader.ReadBits(data, ref bitIdx, size, ref flag, 3)) return;
            r.Flag = (byte)flag;

            if ((r.Flag & (byte)ResourceFlag.Custom) != 0)
            {
                byte[] md5 = new byte[16];
                for (int b = 0; b < 16; b++)
                {
                    uint bb = 0;
                    if (!BitReader.ReadBits(data, ref bitIdx, size, ref bb, 8)) return;
                    md5[b] = (byte)bb;
                }
                r.Md5 = md5;
            }

            uint hasReserved = 0;
            if (!BitReader.ReadBits(data, ref bitIdx, size, ref hasReserved, 1)) return;
            if (hasReserved != 0)
            {
                byte[] reserved = new byte[32];
                for (int b = 0; b < 32; b++)
                {
                    uint bb = 0;
                    if (!BitReader.ReadBits(data, ref bitIdx, size, ref bb, 8)) return;
                    reserved[b] = (byte)bb;
                }
                r.Reserved = reserved;
            }

            r.NeedConsistency = false;
        }

        uint hasConsistency = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref hasConsistency, 1)) return;

        if (hasConsistency != 0)
        {
            int lastIndex = 0;
            while (true)
            {
                uint haveFile = 0;
                if (!BitReader.ReadBits(data, ref bitIdx, size, ref haveFile, 1)) return;
                if (haveFile == 0) break;

                uint indexOrDiff = 0;
                if (!BitReader.ReadBits(data, ref bitIdx, size, ref indexOrDiff, 1)) return;

                if (indexOrDiff == 0)
                {
                    lastIndex = 0;
                    uint idx = 0;
                    if (!BitReader.ReadBits(data, ref bitIdx, size, ref idx, 10)) return;
                    lastIndex = (int)idx;
                }
                else
                {
                    uint diff = 0;
                    if (!BitReader.ReadBits(data, ref bitIdx, size, ref diff, 5)) return;
                    lastIndex += (int)diff;
                }

                if (lastIndex < ctx.Resources.Length)
                    ctx.Resources[lastIndex].NeedConsistency = true;
            }
        }

        offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
    }

    private static bool ScanForBz2(byte[] data, int offset, int size)
    {
        for (int i = offset; i <= size - 4; i++)
        {
            if (data[i] == 'B' && data[i + 1] == 'Z' && data[i + 2] == '2' && data[i + 3] == 0)
                return true;
        }
        return false;
    }

    /// <summary>Closes the underlying UDP socket and releases all resources.</summary>
    public void Dispose()
    {
        _socket.Dispose();
    }

    private class ConnectionContext
    {
        public byte[] Challenge = [];
        public byte AuthProtocol;
        public int UserId;
        public uint SrcSequence = 1;
        public uint DstSequence;
        public uint SpawnCount;
        public uint WorldmapCrc;
        public byte MaxClients = 32;
        public byte PlayerNumber;
        public ResourceInfo[] Resources = [];
        public List<UserMessage> UserMessages = [];
        public ulong ServerSteamId;
        public bool RequiresGameAuthTicket;
        public uint ServerIp;
        public ushort ServerPort;
    }
}

/// <summary>
/// Default no-op <see cref="ISteamAuthProvider"/> that reports Steam as unavailable
/// and provides fake authentication data for servers that do not enforce Steam auth.
/// </summary>
public sealed class NoSteamAuthProvider : ISteamAuthProvider
{
    /// <inheritdoc/>
    public bool IsAvailable => false;

    /// <inheritdoc/>
    public byte GetAuthProtocol() => 3;

    /// <inheritdoc/>
    public string GetRawAuthData() => "steam";
}
