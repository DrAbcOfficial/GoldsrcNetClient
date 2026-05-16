using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GoldsrcNetClient.Core.Delta;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Munge;
using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.Core.Util;
using ICSharpCode.SharpZipLib.BZip2;
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
    private IPEndPoint? _activeEndpoint;
    private bool _sentContinueLoading;
    private bool _sentSpawn;
    private CancellationTokenSource? _moveCts;
    private Task? _moveTask;

    /// <summary>
    /// The current spawn count reported by the server.
    /// Updated when the server sends a <see cref="ServerMessageType.ResourceRequest"/>.
    /// </summary>
    public uint SpawnCount
    {
        get => _contexts.TryGetValue(_activeEndpoint ?? DummyEndpoint, out var ctx) ? ctx.SpawnCount : 0;
        set
        {
            if (_contexts.TryGetValue(_activeEndpoint ?? DummyEndpoint, out var ctx))
                ctx.SpawnCount = value;
        }
    }

    private static readonly IPEndPoint DummyEndpoint = new(0, 0);

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
    /// Button bitmask sent with each usercmd (move) packet.
    /// Set to <c>1</c> (<c>IN_ATTACK</c>) to enable respawning in Sven Co-op.
    /// Defaults to <c>0</c> (no buttons pressed).
    /// </summary>
    public ushort MoveButtons { get; set; }

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
    /// Sends a string command to the connected server.
    /// </summary>
    /// <param name="cmd">The client command type.</param>
    /// <param name="payload">Null-terminated string payload.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SendStringCmdAsync(ClientCommandType cmd, string payload, CancellationToken ct = default)
    {
        if (_activeEndpoint == null)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        var cmdBytes = new List<byte> { (byte)cmd };
        cmdBytes.AddRange(Encoding.UTF8.GetBytes(payload));
        cmdBytes.Add(0);
        await SendRawAsync(_activeEndpoint, cmdBytes.ToArray(), ct);
    }

    /// <summary>
    /// Sends a raw command with arbitrary data to the connected server.
    /// </summary>
    /// <param name="cmd">The client command type byte.</param>
    /// <param name="data">Raw payload bytes (appended after the command byte).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SendCommandAsync(ClientCommandType cmd, byte[] data, CancellationToken ct = default)
    {
        if (_activeEndpoint == null)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        var bytes = new List<byte> { (byte)cmd };
        bytes.AddRange(data);
        await SendRawAsync(_activeEndpoint, bytes.ToArray(), ct);
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
        _activeEndpoint = ep;
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
                    StartMoveTask();
                }
            }
            else if (header == MessageConstants.SplitMarker)
            {
                _logger.LogWarning($"[Fragment] received OOB split packet (len={len}), reassembly not yet implemented");
            }
            else
            {
                var ctx = _contexts[ep];
                uint rawHeader = header;
                bool isFragment = (rawHeader & MessageConstants.SequenceModeFragment) != 0;
                bool isCommand = (rawHeader & MessageConstants.SequenceModeCommand) != 0;
                uint srcSeq = rawHeader & MessageConstants.SequenceMask;
                uint dstSeq = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
                offset += 4;

                ctx.DstSequence = srcSeq;

                int payloadLen = len - offset;
                _logger.LogDebug($"connected packet: srcSeq={srcSeq}, dstSeq={dstSeq}, isFragment={isFragment}, isCommand={isCommand}, payload={payloadLen}");
                OnDataPacket?.Invoke(this, data);

                if (payloadLen <= 0) continue;

                byte[] payload = new byte[payloadLen];
                Array.Copy(data, offset, payload, 0, payloadLen);

                if (isFragment && isCommand)
                {
                    int hdr = 0;
                    _logger.LogDebug($"[FragHdr] payloadLen={payloadLen}, firstBytes={BitConverter.ToString(payload, 0, Math.Min(payloadLen, 16))}");
                    uint stream0FragId = 0;
                    ushort stream0FragLen = 0;
                    ushort stream0StartPos = 0;
                    uint stream1FragId = 0;
                    ushort stream1FragLen = 0;
                    ushort stream1StartPos = 0;
                    bool stream0Active = false;
                    bool stream1Active = false;

                    for (int i = 0; i < 2; i++)
                    {
                        if (hdr >= payloadLen) break;
                        byte streamFlag = payload[hdr++];
                        _logger.LogDebug($"[FragHdr] stream[{i}] flag={(int)streamFlag}, hdrPos={hdr - 1}");
                        if (streamFlag != 0)
                        {
                            uint fragId = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(hdr));
                            hdr += 4;
                            ushort startPos = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(hdr));
                            hdr += 2;
                            ushort fragLen = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(hdr));
                            hdr += 2;
                            _logger.LogDebug($"[FragHdr] stream[{i}] fragId=0x{fragId:X8}, startPos={startPos}, fragLen={fragLen}, hdrPos={hdr - 4}");

                            if (i == 0) { stream0Active = true; stream0FragId = fragId; stream0FragLen = fragLen; stream0StartPos = startPos; }
                            else { stream1Active = true; stream1FragId = fragId; stream1FragLen = fragLen; stream1StartPos = startPos; }
                        }
                    }

                    int msgLen = payloadLen - hdr;
                    if (msgLen <= 0) continue;

                    int reliableLen = msgLen;
                    int firstFragDataStart = msgLen;

                    // Only consider streams with valid fragment data (fragId != 0 and fragLen > 0)
                    bool hasStream0Frag = stream0Active && stream0FragLen > 0 && stream0FragId != 0;
                    bool hasStream1Frag = stream1Active && stream1FragLen > 0 && stream1FragId != 0;

                    if (hasStream0Frag)
                    {
                        firstFragDataStart = Math.Min(firstFragDataStart, stream0StartPos);
                    }
                    if (hasStream1Frag)
                    {
                        firstFragDataStart = Math.Min(firstFragDataStart, stream1StartPos);
                    }
                    reliableLen = Math.Min(reliableLen, firstFragDataStart);

                    // Accumulate stream 0 fragment data (normal stream)
                    if (hasStream0Frag)
                    {
                        int fragStart = hdr + stream0StartPos;
                        int fragLen = Math.Min(stream0FragLen, payloadLen - fragStart);
                        if (fragLen > 0)
                        {
                            byte[] fragChunk = new byte[fragLen];
                            Array.Copy(payload, fragStart, fragChunk, 0, fragLen);
                            AccumulateFragment(ctx, stream0FragId, fragChunk, _logger);

                            // Remove fragment data from reliable range if it overlaps
                            if (stream0StartPos == 0)
                                reliableLen = 0;
                        }
                    }

                    // Accumulate stream 1 fragment data (file stream)
                    if (hasStream1Frag)
                    {
                        int fragStart = hdr + stream1StartPos;
                        int fragLen = Math.Min(stream1FragLen, payloadLen - fragStart);
                        if (fragLen > 0)
                        {
                            byte[] fragChunk = new byte[fragLen];
                            Array.Copy(payload, fragStart, fragChunk, 0, fragLen);
                            AccumulateFragment(ctx, stream1FragId, fragChunk, _logger);
                        }
                    }

                    // Process completed fragment messages
                    byte[]? completedData = TryCompleteFragments(ctx, _logger);
                    if (completedData != null)
                    {
                        _ = SendAckAsync(ep);
                        _sessions[ep] = ProcessConnected(ep, ref srcSeq, ref dstSeq, completedData, completedData.Length);
                        continue;
                    }

                    // Process reliable portion of this packet
                    if (reliableLen > 0)
                    {
                        byte[] msgData = new byte[reliableLen];
                        Array.Copy(payload, hdr, msgData, 0, reliableLen);
                        _logger.LogDebug($"[Fragment] parsed {hdr} bytes of headers, {reliableLen} bytes reliable data");
                        _ = SendAckAsync(ep);
                        _sessions[ep] = ProcessConnected(ep, ref srcSeq, ref dstSeq, msgData, reliableLen);
                    }
                    else
                    {
                        _logger.LogDebug($"[Fragment] parsed {hdr} bytes of headers, accumulating fragment data only");
                        _ = SendAckAsync(ep);
                    }
                    continue;
                }

                if (isFragment)
                {
                    _logger.LogWarning($"[Fragment] unexpected fragment-only packet (no command flag), accumulating {payloadLen} bytes");
                    ctx.IncomingFragment.AddRange(payload);
                    _ = SendAckAsync(ep);
                    continue;
                }

                // Not a fragment — if we have accumulated fragment data, append current payload as unreliable data
                byte[] messageData;
                int messageLen;
                if (ctx.IncomingFragment.Count > 0)
                {
                    _logger.LogDebug($"[Fragment] reassembly complete: {ctx.IncomingFragment.Count} + {payloadLen} = {ctx.IncomingFragment.Count + payloadLen} bytes");
                    messageData = [.. ctx.IncomingFragment, .. payload];
                    messageLen = messageData.Length;
                    ctx.IncomingFragment.Clear();
                }
                else
                {
                    messageData = payload;
                    messageLen = payloadLen;
                }

                if (!isCommand && messageLen == 8 && BitConverter.ToUInt64(messageData, 0) == MessageConstants.AckData)
                {
                    _logger.LogDebug($"[Ack] received ack, dstSeq={dstSeq}");
                    continue;
                }

                if (!isCommand)
                {
                    _logger.LogDebug($"[Munge] UnMunge2 message len={messageLen}, seq={(int)(srcSeq & 0xFF)}");
                    MungeEngine.UnMunge2(messageData, messageLen, (int)(srcSeq & 0xFF));
                }
                else
                {
                    _logger.LogDebug($"[Munge] Command packet, skipping UnMunge2 for message len={messageLen}");
                }

                _sessions[ep] = ProcessConnected(ep, ref srcSeq, ref dstSeq, messageData, messageLen);
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
                await SendStringCmdAsync(ClientCommandType.StringCmd, "new", ct);
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

        var reader = new MessageReader(data, size);
        _logger.LogDebug($"[Connected] processing {size} bytes, srcSeq={srcSequence}, dstSeq={dstSequence}");

        while (reader.Remaining > 0)
        {
            byte dataType = reader.Data[reader.Offset++];
            int dataLen = reader.Remaining;
            string typeName = Enum.IsDefined(typeof(ServerMessageType), dataType) ? ((ServerMessageType)dataType).ToString() : $"0x{dataType:X2}";
            _logger.LogDebug($"[Connected] type={typeName} (0x{dataType:X2}), remaining={dataLen}");

            if (_messageHandler.HandleMessage(this, dataType, reader))
                continue;

            if (dataType == (byte)ServerMessageType.Nop) { }
            else if (dataType == (byte)ServerMessageType.Bad)
            {
                _logger.LogWarning("[Bad] server sent bad message, consuming remaining data");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.Disconnect)
            {
                string reason = reader.ReadString();
                _logger.LogWarning($"[Disconnect] server disconnected: reason=\"{reason}\"");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.Print)
            {
                string msg = reader.ReadString();
                _logger.LogDebug($"[Print] msg=\"{msg[..Math.Min(msg.Length, 200)]}\"");
                _logger.LogInformation(msg);
            }
            else if (dataType == (byte)ServerMessageType.CenterPrint)
            {
                string centerMsg = reader.ReadString();
                _logger.LogDebug($"[CenterPrint] msg=\"{centerMsg}\"");
            }
            else if (dataType == (byte)ServerMessageType.ServerInfo)
            {
                int structSize = 33;
                if (reader.Offset + structSize > reader.Size)
                {
                    _logger.LogWarning($"[ServerInfo] buffer overflow: offset={reader.Offset}, need={structSize}, size={reader.Size}");
                    return SessionState.Connected;
                }

                ServerInfoData si;
                unsafe
                {
                    fixed (byte* p = &reader.Data[reader.Offset])
                        si = *(ServerInfoData*)p;
                }

                reader.Offset += structSize;

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
                    reader.ReadString();

                if (reader.Offset + 1 <= reader.Size)
                    reader.Offset++;

                OnServerInfo?.Invoke(this, si);

                if (!_sentContinueLoading)
                {
                    _sentContinueLoading = true;
                    _logger.LogDebug("[SignOn] sending sendres");
                    _ = SendStringCmdAsync(ClientCommandType.StringCmd, "sendres", CancellationToken.None);
                }
            }
            else if (dataType == (byte)ServerMessageType.DeltaDescription)
            {
                string name = reader.ReadString();
                _logger.LogDebug($"[DeltaDescription] deltaName=\"{name}\"");
                var dt = DeltaDefinitions.Find(name);
                if (dt == null)
                {
                    _logger.LogWarning($"[DeltaDescription] unknown delta \"{name}\", skipping");
                    return SessionState.Connected;
                }

                int bitIdx = 0;
                uint fieldCount = 0;
                if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref fieldCount, 16))
                {
                    _logger.LogWarning($"[DeltaDescription] failed reading fieldCount");
                    return SessionState.Connected;
                }
                _logger.LogDebug($"[DeltaDescription] fieldCount={fieldCount}");

                for (uint f = 0; f < fieldCount; f++)
                {
                    int parseBitIdx = 0;
                    if (!ParseDeltaFieldDescriptions(reader.Data, reader.Size, ref parseBitIdx))
                    {
                        _logger.LogWarning($"[DeltaDescription] failed parsing field {f}");
                        return SessionState.Connected;
                    }
                }

                reader.Offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
                _logger.LogDebug($"[DeltaDescription] done, new offset={reader.Offset}");
            }
            else if (dataType == (byte)ServerMessageType.NewMoveVars)
            {
                int mvSize;
                unsafe { mvSize = sizeof(NewMoveVarsData); }
                if (reader.Offset + mvSize > reader.Size)
                {
                    _logger.LogWarning($"[NewMoveVars] buffer overflow: offset={reader.Offset}, need={mvSize}, size={reader.Size}");
                    return SessionState.Connected;
                }

                unsafe
                {
                    fixed (byte* p = &reader.Data[reader.Offset])
                        _ = *(NewMoveVarsData*)p;
                    reader.Offset += mvSize;
                }

                reader.ReadString();
                _logger.LogDebug($"[NewMoveVars] done, structSize={mvSize}");
            }
            else if (dataType == (byte)ServerMessageType.SetView)
            {
                if (reader.Offset + 2 > reader.Size)
                {
                    _logger.LogWarning($"[SetView] buffer overflow: offset={reader.Offset}, size={reader.Size}");
                    return SessionState.Connected;
                }
                reader.Offset += 2;
            }
            else if (dataType == (byte)ServerMessageType.NewUserMsg)
            {
                int msgSize;
                unsafe { msgSize = sizeof(NewUserMsgData); }
                if (reader.Offset + msgSize > reader.Size)
                {
                    _logger.LogWarning($"[NewUserMsg] buffer overflow: offset={reader.Offset}, need={msgSize}, size={reader.Size}");
                    return SessionState.Connected;
                }

                unsafe
                {
                    fixed (byte* p = &reader.Data[reader.Offset])
                        _ = *(NewUserMsgData*)p;
                    reader.Offset += msgSize;
                }
                _logger.LogDebug($"[NewUserMsg] done, structSize={msgSize}");
            }
            else if (dataType == (byte)ServerMessageType.StuffText)
            {
                string st = reader.ReadString();
                _logger.LogDebug($"[StuffText] text=\"{st[..Math.Min(st.Length, 200)]}\"");
            }
            else if (dataType == (byte)ServerMessageType.UpdateUserInfo)
            {
                if (reader.Offset + 1 > reader.Size) { _logger.LogWarning("[UpdateUserInfo] buffer overflow at byte 1"); return SessionState.Connected; }
                reader.Offset += 1;
                if (reader.Offset + 4 > reader.Size) { _logger.LogWarning("[UpdateUserInfo] buffer overflow at byte 4"); return SessionState.Connected; }
                reader.Offset += 4;
                string uui = reader.ReadString();
                UserInfo = uui;
                _logger.LogDebug($"[UpdateUserInfo] userInfo=\"{uui[..Math.Min(uui.Length, 100)]}\"");
                if (reader.Offset + 16 > reader.Size) { _logger.LogWarning("[UpdateUserInfo] buffer overflow at 16"); return SessionState.Connected; }
                reader.Offset += 16;
            }
            else if (dataType == (byte)ServerMessageType.ResourceRequest)
            {
                if (reader.Offset + 4 > reader.Size) { _logger.LogWarning("[ResourceRequest] buffer overflow"); return SessionState.Connected; }
                ctx.SpawnCount = reader.ReadUInt32();
                if (reader.Offset + 4 > reader.Size) { _logger.LogWarning("[ResourceRequest] buffer overflow after spawnCount"); return SessionState.Connected; }
                uint resUnknown = reader.ReadUInt32();
                _logger.LogDebug($"[ResourceRequest] spawnCount={ctx.SpawnCount}, unknown={resUnknown}");
            }
            else if (dataType == (byte)ServerMessageType.ResourceLocation)
            {
                string loc = reader.ReadString();
                _logger.LogDebug($"[ResourceLocation] location=\"{loc}\"");
            }
            else if (dataType == (byte)ServerMessageType.ResourceList)
            {
                int listStart = reader.Offset;
                ProcessResourceList(ctx, reader);
                _logger.LogDebug($"[ResourceList] count={ctx.Resources.Length}, dataBytes={reader.Offset - listStart}");
                OnResourceList?.Invoke(this, ctx.Resources);
            }
            else if (dataType == (byte)ServerMessageType.TempEntity)
            {
                if (reader.Offset + 1 > reader.Size) { _logger.LogWarning("[TempEntity] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 1;
                for (int i = 0; i < 3; i++)
                {
                    if (reader.Offset + 2 > reader.Size) { _logger.LogWarning($"[TempEntity] buffer overflow at coord {i}"); return SessionState.Connected; }
                    reader.Offset += 2;
                }
            }
            else if (dataType == (byte)ServerMessageType.SpawnStaticSound)
            {
                reader.Offset += 14;
            }
            else if (dataType == (byte)ServerMessageType.SendCvarValue2)
            {
                if (reader.Offset + 4 > reader.Size) { _logger.LogWarning("[SendCvarValue2] buffer overflow"); return SessionState.Connected; }
                uint requestId = reader.ReadUInt32();
                string cvarName = reader.ReadString();
                _logger.LogDebug($"[SendCvarValue2] requestId={requestId}, cvar=\"{cvarName}\"");
                var reply = new List<byte>();
                MessageWriter.WriteUInt32(reply, requestId);
                MessageWriter.WriteString(reply, cvarName);
                MessageWriter.WriteString(reply, GetDefaultCvarValue(cvarName));
                _ = SendCommandAsync(ClientCommandType.CvarValue2, reply.ToArray(), CancellationToken.None);
            }
            else if (dataType == (byte)ServerMessageType.SendCvarValue)
            {
                string cvarName = reader.ReadString();
                _logger.LogDebug($"[SendCvarValue] cvar=\"{cvarName}\"");
                var reply = new List<byte>();
                MessageWriter.WriteString(reply, GetDefaultCvarValue(cvarName));
                _ = SendCommandAsync(ClientCommandType.CvarValue, reply.ToArray(), CancellationToken.None);
            }
            else if (dataType == (byte)ServerMessageType.SpawnBaseline)
            {
                int bitIdx = 0;
                int entityCount = 0;
                while (true)
                {
                    uint entityNumber = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref entityNumber, 11))
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
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref entityType, 2))
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

                    ParseDeltaFields(dt, reader.Data, reader.Size, ref bitIdx);
                    entityCount++;
                }

                uint baselineCount = 0;
                BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref baselineCount, 6);
                _logger.LogDebug($"[SpawnBaseline] entities={entityCount}, baselineCount={baselineCount}");
                for (uint ei = 0; ei < baselineCount; ei++)
                    ParseDeltaFields(DeltaDefinitions.EntityState, reader.Data, reader.Size, ref bitIdx);

                reader.Offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
                _logger.LogDebug($"[SpawnBaseline] done, totalBits={bitIdx}, newOffset={reader.Offset}");

                if (!_sentSpawn && _sentContinueLoading)
                {
                    _sentSpawn = true;
                    uint spawnCount = ctx.SpawnCount;
                    int rawCrc = (int)ctx.WorldmapCrc;
                    byte[] crcBytes = BitConverter.GetBytes(rawCrc);
                    int mungeKey = ~(int)spawnCount;
                    _logger.LogDebug($"[SignOn] spawn: rawCrc=0x{rawCrc:X8}, mungeKey=0x{mungeKey:X8} (spawnCount={spawnCount})");
                    MungeEngine.Munge2(crcBytes, 4, mungeKey);
                    int mungedCrc = BitConverter.ToInt32(crcBytes, 0);
                    var spawnCmd = $"spawn {spawnCount} {mungedCrc}";
                    _logger.LogDebug($"[SignOn] sending spawn (spawnCount={spawnCount}, mungedCrc=0x{mungedCrc:X8})");
                    _ = SendStringCmdAsync(ClientCommandType.StringCmd, spawnCmd, CancellationToken.None);
                }
            }
            else if (dataType == (byte)ServerMessageType.Time)
            {
                reader.ReadSingle(out float time);
                _logger.LogDebug($"[Time] time={time:F2}");
            }
            else if (dataType == (byte)ServerMessageType.LightStyle)
            {
                if (reader.Offset + 1 > reader.Size) { _logger.LogWarning("[LightStyle] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 1;
                reader.ReadString();
            }
            else if (dataType == (byte)ServerMessageType.SetAngle)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (reader.Offset + 2 > reader.Size) { _logger.LogWarning($"[SetAngle] buffer overflow at angle {i}"); return SessionState.Connected; }
                    reader.Offset += 2;
                }
            }
            else if (dataType == (byte)ServerMessageType.ClientData)
            {
                int bitIdx = 0;
                uint haveDeltaSeq = 0;
                if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref haveDeltaSeq, 1)) return SessionState.Connected;
                if (haveDeltaSeq != 0)
                {
                    uint deltaSeq = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref deltaSeq, 8)) return SessionState.Connected;
                    _logger.LogDebug($"[ClientData] deltaSeq={deltaSeq}");
                }
                ParseDeltaFields(DeltaDefinitions.ClientData, reader.Data, reader.Size, ref bitIdx);

                int weaponCount = 0;
                while (true)
                {
                    uint haveDelta = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref haveDelta, 1)) return SessionState.Connected;
                    if (haveDelta == 0) break;
                    uint index = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref index, 6)) return SessionState.Connected;
                    ParseDeltaFields(DeltaDefinitions.WeaponData, reader.Data, reader.Size, ref bitIdx);
                    weaponCount++;
                }
                _logger.LogDebug($"[ClientData] done, weaponDeltas={weaponCount}");
                reader.Offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
            }
            else if (dataType == (byte)ServerMessageType.SignOnNum)
            {
                if (reader.Offset + 1 > reader.Size) { _logger.LogWarning("[SignOnNum] buffer overflow"); return SessionState.Connected; }
                byte signOn = reader.Data[reader.Offset++];
                _logger.LogDebug($"[SignOnNum] value={signOn}");
                if (signOn == 1)
                {
                    _logger.LogInformation("[SignOn] signon=1 received, signon sequence complete. Sending sendents.");
                    _logger.LogDebug("[SignOn] sending sendents (final handshake step)");
                    _ = SendStringCmdAsync(ClientCommandType.StringCmd, "sendents", CancellationToken.None);
                    StartMoveTask();
                }
                else
                {
                    _logger.LogDebug($"[SignOn] signon={signOn} received (not 1, no action)");
                }
            }
            else if (dataType == (byte)ServerMessageType.VoiceInit)
            {
                string codec = reader.ReadString();
                if (reader.Offset + 1 > reader.Size) { _logger.LogWarning("[VoiceInit] buffer overflow"); return SessionState.Connected; }
                byte quality = reader.Data[reader.Offset++];
                _logger.LogDebug($"[VoiceInit] codec=\"{codec}\", quality={quality}");
            }
            else if (dataType == (byte)ServerMessageType.Sound)
            {
                int bitIdx = 0;
                uint fieldMask = 0;
                if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref fieldMask, 9)) return SessionState.Connected;

                if ((fieldMask & SoundFlags.Volume) != 0)
                {
                    uint vol = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref vol, 8)) return SessionState.Connected;
                }
                if ((fieldMask & SoundFlags.Attenuation) != 0)
                {
                    uint attn = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref attn, 8)) return SessionState.Connected;
                }

                uint channel = 0;
                if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref channel, 3)) return SessionState.Connected;
                uint entity = 0;
                if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref entity, 11)) return SessionState.Connected;
                uint soundNum = 0;
                int snBits = (fieldMask & SoundFlags.LargeIndex) != 0 ? 16 : 8;
                if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref soundNum, snBits)) return SessionState.Connected;

                float ox = 0, oy = 0, oz = 0;
                uint xf = 0, yf = 0, zf = 0;
                BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref xf, 1);
                BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref yf, 1);
                BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref zf, 1);
                if (xf != 0) BitReader.ReadBitCoord(reader.Data, ref bitIdx, reader.Size, ref ox);
                if (yf != 0) BitReader.ReadBitCoord(reader.Data, ref bitIdx, reader.Size, ref oy);
                if (zf != 0) BitReader.ReadBitCoord(reader.Data, ref bitIdx, reader.Size, ref oz);

                if ((fieldMask & SoundFlags.Pitch) != 0)
                {
                    uint pitch = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref pitch, 8)) return SessionState.Connected;
                }
                _logger.LogDebug($"[Sound] channel={channel}, entity={entity}, soundNum={soundNum}, fieldMask=0x{fieldMask:X4}");
                reader.Offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
            }
            else if (dataType == (byte)ServerMessageType.Customization)
            {
                if (reader.Offset + 1 > reader.Size) { _logger.LogWarning("[Customization] buffer overflow at 1"); return SessionState.Connected; }
                byte playerSlot = reader.Data[reader.Offset++];
                if (reader.Offset + 1 > reader.Size) { _logger.LogWarning("[Customization] buffer overflow at 2"); return SessionState.Connected; }
                byte resourceType = reader.Data[reader.Offset++];
                string resourceName = reader.ReadString();
                if (reader.Offset + 2 > reader.Size) { _logger.LogWarning("[Customization] buffer overflow at 3"); return SessionState.Connected; }
                reader.Offset += 2;
                if (reader.Offset + 4 > reader.Size) { _logger.LogWarning("[Customization] buffer overflow at 4"); return SessionState.Connected; }
                reader.Offset += 4;
                if (reader.Offset + 1 > reader.Size) { _logger.LogWarning("[Customization] buffer overflow at 5"); return SessionState.Connected; }
                reader.Offset += 1;
                _logger.LogDebug($"[Customization] player={playerSlot}, type={resourceType}, name=\"{resourceName}\"");
            }
            else if (dataType == (byte)ServerMessageType.Choke) { }
            else if (dataType == (byte)ServerMessageType.Event)
            {
                _logger.LogDebug("[Event] game event received, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.Version)
            {
                if (reader.Offset + 4 > reader.Size) { _logger.LogWarning("[Version] buffer overflow"); return SessionState.Connected; }
                uint version = reader.ReadUInt32();
                _logger.LogDebug($"[Version] protocol={version}");
            }
            else if (dataType == (byte)ServerMessageType.StopSound)
            {
                if (reader.Offset + 2 > reader.Size) { _logger.LogWarning("[StopSound] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 2;
            }
            else if (dataType == (byte)ServerMessageType.Pings)
            {
                int bitIdx = 0;
                for (int i = 0; i < 32; i++)
                {
                    uint hasEntry = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref hasEntry, 1))
                        break;
                    if (hasEntry == 0) break;
                    uint slot = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref slot, 5)) return SessionState.Connected;
                    uint ping = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref ping, 12)) return SessionState.Connected;
                    uint loss = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref loss, 7)) return SessionState.Connected;
                }
                reader.Offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
            }
            else if (dataType == (byte)ServerMessageType.Particle)
            {
                _logger.LogDebug("[Particle] particle effect, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.Damage)
            {
                if (reader.Offset + 8 > reader.Size) { _logger.LogWarning("[Damage] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 8;
                if (reader.Offset + 3 > reader.Size) { _logger.LogWarning("[Damage] buffer overflow at coords"); return SessionState.Connected; }
                reader.Offset += 3;
            }
            else if (dataType == (byte)ServerMessageType.SpawnStatic)
            {
                _logger.LogDebug("[SpawnStatic] static entity, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.EventReliable)
            {
                _logger.LogDebug("[EventReliable] reliable event, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.SetPause)
            {
                uint paused = 0;
                int bitIdx = 0;
                BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref paused, 1);
                reader.Offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
                _logger.LogDebug($"[SetPause] paused={paused}");
            }
            else if (dataType == (byte)ServerMessageType.KilledMonster) { }
            else if (dataType == (byte)ServerMessageType.FoundSecret) { }
            else if (dataType == (byte)ServerMessageType.Intermission)
            {
                _logger.LogDebug("[Intermission] intermission started");
            }
            else if (dataType == (byte)ServerMessageType.Finale)
            {
                string finaleStr = reader.ReadString();
                _logger.LogDebug($"[Finale] text=\"{finaleStr}\"");
            }
            else if (dataType == (byte)ServerMessageType.CdTrack)
            {
                if (reader.Offset + 2 > reader.Size) { _logger.LogWarning("[CdTrack] buffer overflow"); return SessionState.Connected; }
                byte track = reader.ReadByte();
                byte loopTrack = reader.ReadByte();
                _logger.LogDebug($"[CdTrack] track={track}, loop={loopTrack}");
            }
            else if (dataType == (byte)ServerMessageType.Restore)
            {
                _logger.LogDebug("[Restore] restore game state, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.Cutscene)
            {
                string cutscene = reader.ReadString();
                _logger.LogDebug($"[Cutscene] name=\"{cutscene}\"");
            }
            else if (dataType == (byte)ServerMessageType.WeaponAnim)
            {
                if (reader.Offset + 2 > reader.Size) { _logger.LogWarning("[WeaponAnim] buffer overflow"); return SessionState.Connected; }
                byte anim = reader.ReadByte();
                byte body = reader.ReadByte();
                _logger.LogDebug($"[WeaponAnim] anim={anim}, body={body}");
            }
            else if (dataType == (byte)ServerMessageType.DecalName)
            {
                if (reader.Offset + 2 > reader.Size) { _logger.LogWarning("[DecalName] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 2;
            }
            else if (dataType == (byte)ServerMessageType.RoomType)
            {
                if (reader.Offset + 2 > reader.Size) { _logger.LogWarning("[RoomType] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 2;
            }
            else if (dataType == (byte)ServerMessageType.AddAngle)
            {
                if (reader.Offset + 2 > reader.Size) { _logger.LogWarning("[AddAngle] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 2;
            }
            else if (dataType == (byte)ServerMessageType.PacketEntities)
            {
                _logger.LogDebug("[PacketEntities] full entity packet, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.DeltaPacketEntities)
            {
                _logger.LogDebug("[DeltaPacketEntities] delta entity packet, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.CrosshairAngle)
            {
                if (reader.Offset + 2 > reader.Size) { _logger.LogWarning("[CrosshairAngle] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 2;
            }
            else if (dataType == (byte)ServerMessageType.SoundFade)
            {
                if (reader.Offset + 4 > reader.Size) { _logger.LogWarning("[SoundFade] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 4;
            }
            else if (dataType == (byte)ServerMessageType.FileTxferFailed)
            {
                string failName = reader.ReadString();
                _logger.LogDebug($"[FileTxferFailed] file=\"{failName}\"");
            }
            else if (dataType == (byte)ServerMessageType.Hltv)
            {
                _logger.LogDebug("[Hltv] HLTV data, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.Director)
            {
                _logger.LogDebug("[Director] director command, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.VoiceData)
            {
                _logger.LogDebug("[VoiceData] voice data, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.SendExtraInfo)
            {
                _logger.LogDebug("[SendExtraInfo] extra info, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.TimeScale)
            {
                reader.ReadSingle(out float timeScale);
                _logger.LogDebug($"[TimeScale] scale={timeScale:F2}");
            }
            else if (dataType == (byte)ServerMessageType.Exec)
            {
                string execCmd = reader.ReadString();
                _logger.LogDebug($"[Exec] cmd=\"{execCmd}\"");
            }
            else if (dataType == 'B' && reader.Offset + 2 < reader.Size && reader.Data[reader.Offset] == 'Z' && reader.Data[reader.Offset + 1] == '2' && reader.Data[reader.Offset + 2] == 0)
            {
                _logger.LogWarning("[BZ2] compressed data detected, skipping");
                _logger.LogWarning("BZ2 compressed data received (decompression not yet supported)");
                reader.Offset = reader.Size;
            }
            else if (ScanForBz2(reader.Data, reader.Offset, reader.Size))
            {
                _logger.LogWarning("[BZ2] compressed data detected at offset, skipping");
                reader.Offset = reader.Size;
            }
            else
            {
                reader.Offset--;
                _logger.LogWarning($"[Connected] Unknown data type: 0x{dataType:X2} at offset={reader.Offset}, remaining={reader.Remaining}");
                return SessionState.Connected;
            }
        }

        _logger.LogDebug($"[Connected] processed all {reader.Size} bytes successfully");
        return SessionState.Connected;
    }

    private async Task SendRawAsync(IPEndPoint ep, byte[] data, CancellationToken ct)
    {
        var ctx = _contexts[ep];
        uint srcSeq = ctx.SrcSequence++;

        var payload = new byte[data.Length + MessageConstants.ConnectedHeadSize];
        BitConverter.GetBytes(srcSeq | MessageConstants.SequenceModeCommand).CopyTo(payload, 0);
        BitConverter.GetBytes(ctx.DstSequence & MessageConstants.SequenceMask).CopyTo(payload, 4);
        data.CopyTo(payload, MessageConstants.ConnectedHeadSize);

        _logger.LogDebug($"[SendRaw] srcSeq={srcSeq}, dstSeq={ctx.DstSequence}, totalLen={payload.Length}");
        await _socket.SendAsync(new ReadOnlyMemory<byte>(payload), ep, ct);
    }

    private async Task SendAckAsync(IPEndPoint ep)
    {
        var ctx = _contexts[ep];
        uint srcSeq = ctx.SrcSequence++;
        var ackPacket = new byte[MessageConstants.ConnectedHeadSize];
        BitConverter.GetBytes(srcSeq | MessageConstants.SequenceModeCommand).CopyTo(ackPacket, 0);
        BitConverter.GetBytes(ctx.DstSequence & MessageConstants.SequenceMask).CopyTo(ackPacket, 4);
        _logger.LogDebug($"[SendAck] srcSeq={srcSeq}, dstSeq={ctx.DstSequence}");
        await _socket.SendAsync(new ReadOnlyMemory<byte>(ackPacket), ep, CancellationToken.None);
    }

    private void StartMoveTask()
    {
        if (_moveCts != null) return;
        _moveCts = new CancellationTokenSource();
        var token = _moveCts.Token;
        _moveTask = Task.Run(async () =>
        {
            _logger.LogDebug("[Move] starting move task");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await SendMoveAsync(token);
                    await Task.Delay(100, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[Move] error: {ex.Message}");
                }
            }
            _logger.LogDebug("[Move] move task stopped");
        }, token);
    }

    private async Task SendMoveAsync(CancellationToken ct)
    {
        if (_activeEndpoint == null) return;
        var payload = BuildMovePayload();
        await SendCommandAsync(ClientCommandType.Move, payload, ct);
    }

    private byte[] BuildMovePayload()
    {
        var data = new byte[32];
        int bitIdx = 0;
        int destSize = data.Length;

        // Number of backup commands
        BitWriter.WriteBits(1u, 8, data, ref bitIdx, destSize);

        // First (and only) usercmd: delta from nullcmd
        BitWriter.WriteBits(1u, 1, data, ref bitIdx, destSize); // hasdata = true

        // All fields of usercmd_t in wire format:
        BitWriter.WriteBits(0u, 9, data, ref bitIdx, destSize);  // lerp_msec
        BitWriter.WriteBits(0u, 8, data, ref bitIdx, destSize);  // msec
        BitWriter.WriteBits(0u, 16, data, ref bitIdx, destSize); // viewangles[0] (pitch)
        BitWriter.WriteBits(0u, 16, data, ref bitIdx, destSize); // viewangles[1] (yaw)
        BitWriter.WriteBits(0u, 16, data, ref bitIdx, destSize); // viewangles[2] (roll)
        BitWriter.WriteBits(0u, 12, data, ref bitIdx, destSize); // forwardmove (signed, 0)
        BitWriter.WriteBits(0u, 12, data, ref bitIdx, destSize); // sidemove (signed, 0)
        BitWriter.WriteBits(0u, 12, data, ref bitIdx, destSize); // upmove (signed, 0)
        BitWriter.WriteBits((uint)MoveButtons, 16, data, ref bitIdx, destSize); // buttons
        BitWriter.WriteBits(0u, 8, data, ref bitIdx, destSize);  // impulse
        BitWriter.WriteBits(0u, 8, data, ref bitIdx, destSize);  // lightlevel

        // Final light level byte (used by server for lighting)
        int byteSize = bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
        if (byteSize < data.Length) data[byteSize] = 0;
        byteSize++;

        var result = new byte[byteSize];
        Array.Copy(data, result, byteSize);
        return result;
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

    private static void ProcessResourceList(ConnectionContext ctx, MessageReader reader)
    {
        int bitIdx = 0;
        uint resourceCount = 0;
        if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref resourceCount, 12)) return;

        ctx.Resources = new ResourceInfo[resourceCount];
        for (uint i = 0; i < resourceCount; i++)
        {
            var r = new ResourceInfo();
            ctx.Resources[i] = r;

            uint type = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref type, 4)) return;

            if (!BitReader.ReadBitString(reader.Data, ref bitIdx, reader.Size, out r.Name)) return;
            if (r.Name.Length > 64) return;

            uint resIdx = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref resIdx, 12)) return;
            uint dlSize = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref dlSize, 24)) return;
            uint flag = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref flag, 3)) return;
            r.Flag = (byte)flag;

            if ((r.Flag & (byte)ResourceFlag.Custom) != 0)
            {
                byte[] md5 = new byte[16];
                for (int b = 0; b < 16; b++)
                {
                    uint bb = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref bb, 8)) return;
                    md5[b] = (byte)bb;
                }
                r.Md5 = md5;
            }

            uint hasReserved = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref hasReserved, 1)) return;
            if (hasReserved != 0)
            {
                byte[] reserved = new byte[32];
                for (int b = 0; b < 32; b++)
                {
                    uint bb = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref bb, 8)) return;
                    reserved[b] = (byte)bb;
                }
                r.Reserved = reserved;
            }

            r.NeedConsistency = false;
        }

        uint hasConsistency = 0;
        if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref hasConsistency, 1)) return;

        if (hasConsistency != 0)
        {
            int lastIndex = 0;
            while (true)
            {
                uint haveFile = 0;
                if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref haveFile, 1)) return;
                if (haveFile == 0) break;

                uint indexOrDiff = 0;
                if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref indexOrDiff, 1)) return;

                if (indexOrDiff == 0)
                {
                    lastIndex = 0;
                    uint idx = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref idx, 10)) return;
                    lastIndex = (int)idx;
                }
                else
                {
                    uint diff = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref diff, 5)) return;
                    lastIndex += (int)diff;
                }

                if (lastIndex < ctx.Resources.Length)
                    ctx.Resources[lastIndex].NeedConsistency = true;
            }
        }

        reader.Offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
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

    private static string GetDefaultCvarValue(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "cl_lc" or "cl_lw" or "cl_updaterate" => "1",
            "rate" => "20000",
            "name" => "GoldsrcNetClient",
            "topcolor" or "bottomcolor" => "0",
            "model" => "gordon",
            "_cl_autowepswitch" => "1",
            "cl_dlmax" => "80",
            "hltv" => "0",
            _ => "0"
        };
    }

    /// <summary>Closes the underlying UDP socket and releases all resources.</summary>
    public void Dispose()
    {
        _moveCts?.Cancel();
        _moveCts?.Dispose();
        _socket.Dispose();
    }

    private static void AccumulateFragment(ConnectionContext ctx, uint fragId, byte[] data, ILogger logger)
    {
        int count = (int)(fragId & 0xFFFF);
        int id = (int)((fragId >> 16) & 0xFFFF);

        if (!ctx.FragmentActive || ctx.FragmentTotalCount != count)
        {
            ctx.FragmentActive = true;
            ctx.FragmentTotalCount = count;
            ctx.FragmentChunks.Clear();
            logger.LogDebug($"[FragAccum] new message: totalFragments={count}");
        }

        ctx.FragmentChunks.Add(data);
        logger.LogDebug($"[FragAccum] stored fragment id={id}/{count}, received={ctx.FragmentChunks.Count}/{ctx.FragmentTotalCount}, size={data.Length}");
    }

    private static byte[]? TryCompleteFragments(ConnectionContext ctx, ILogger logger)
    {
        if (!ctx.FragmentActive || ctx.FragmentChunks.Count < ctx.FragmentTotalCount)
            return null;

        logger.LogDebug($"[FragAccum] all {ctx.FragmentChunks.Count} fragments received, reassembling...");

        int totalSize = 0;
        foreach (var c in ctx.FragmentChunks)
            totalSize += c.Length;

        byte[] assembled = new byte[totalSize];
        int pos = 0;
        foreach (var c in ctx.FragmentChunks)
        {
            Array.Copy(c, 0, assembled, pos, c.Length);
            pos += c.Length;
        }

        ctx.FragmentActive = false;
        ctx.FragmentTotalCount = 0;
        ctx.FragmentChunks.Clear();

        if (totalSize > 4 && assembled[0] == 'B' && assembled[1] == 'Z' && assembled[2] == '2' && assembled[3] == 0)
        {
            logger.LogDebug($"[BZ2] decompressing {totalSize} bytes of sign-on data...");
            try
            {
                using var msIn = new MemoryStream(assembled, 4, totalSize - 4);
                using var msOut = new MemoryStream();
                BZip2.Decompress(msIn, msOut, false);
                var decompressed = msOut.ToArray();
                logger.LogDebug($"[BZ2] decompressed to {decompressed.Length} bytes");
                return decompressed;
            }
            catch (Exception ex)
            {
                logger.LogWarning($"[BZ2] decompression failed: {ex.Message}");
                return null;
            }
        }

        return assembled;
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
        public List<byte> IncomingFragment = [];

        // Fragment reassembly state
        public int FragmentTotalCount;
        public bool FragmentActive;
        public readonly List<byte[]> FragmentChunks = [];
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
