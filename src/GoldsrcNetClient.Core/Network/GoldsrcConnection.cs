using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Munge;
using GoldsrcNetClient.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

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
public partial class GoldsrcConnection : IDisposable
{
    private static readonly byte[] GetChallengeSteamPacket =
        [0xFF, 0xFF, 0xFF, 0xFF, (byte)'g', (byte)'e', (byte)'t', (byte)'c', (byte)'h', (byte)'a', (byte)'l', (byte)'l', (byte)'e', (byte)'n', (byte)'g', (byte)'e', (byte)' ', (byte)'s', (byte)'t', (byte)'e', (byte)'a', (byte)'m', (byte)'\n'];
    private static readonly byte[] GetChallengePacket =
        [0xFF, 0xFF, 0xFF, 0xFF, (byte)'g', (byte)'e', (byte)'t', (byte)'c', (byte)'h', (byte)'a', (byte)'l', (byte)'l', (byte)'e', (byte)'n', (byte)'g', (byte)'e', (byte)'\n'];

    internal const byte AuthProtocolSteam = 3;
    internal const byte AuthProtocolWon = 1;
    internal const byte AuthProtocolHashedCdKey = 2;

    private readonly UdpClient _socket;
    private readonly Dictionary<IPEndPoint, SessionState> _sessions = [];
    private readonly Dictionary<IPEndPoint, ConnectionContext> _contexts = [];
    private readonly ISteamAuthProvider _authProvider;
    private readonly IServerMessageHandler _messageHandler;
    internal readonly ILogger<GoldsrcConnection> Logger;
    private readonly TaskCompletionSource _connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IPEndPoint? _activeEndpoint;
    private bool _sentContinueLoading;
    private bool _sentSpawn;
    private CancellationTokenSource? _moveCts;
    private Task? _moveTask;

    /// <summary>
    /// Configurable engine behavior settings. Modify before or during a connection
    /// to customize protocol version, move interval, cvar defaults, and more.
    /// </summary>
    public GoldsrcEngineSettings Settings { get; } = new();

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

    /// <summary>
    /// Raw bit-packed payload of the most recent <c>svc_resourcelist</c> from the server
    /// (the data after the 0x2B type byte). Used to echo back identical data when the
    /// server sends a <see cref="ServerMessageType.ResourceRequest"/>.
    /// Returns an empty array if no resource list has been received yet.
    /// </summary>
    public byte[] ResourceListRawBytes
    {
        get => _contexts.TryGetValue(_activeEndpoint ?? DummyEndpoint, out var ctx) ? ctx.ResourceListRawBytes : [];
    }

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

        var sb = new StringBuilder();
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
        Logger = logger ?? NullLogger<GoldsrcConnection>.Instance;
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
    /// <param name="appId">Server appId.</param>
    /// <param name="host">Server hostname or IP address.</param>
    /// <param name="port">Server UDP port (default 27015).</param>
    /// <param name="ct">Cancellation token to stop the receive loop.</param>
    public async Task ConnectAsync(uint appId, string host, int port = 27015, CancellationToken ct = default)
    {
        Logger.LogDebug($"[State] Begin -> resolving {host}:{port}");
        var addresses = await Dns.GetHostAddressesAsync(host, ct);
        var ip = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];
        var ep = new IPEndPoint(ip, port);
        _activeEndpoint = ep;
        Logger.LogDebug($"[DNS] resolved {host} -> {ep}");
        _sessions[ep] = SessionState.GetChallenge;
        _contexts[ep] = new ConnectionContext { ServerIp = BitConverter.ToUInt32(ep.Address.GetAddressBytes()), ServerPort = (ushort)ep.Port };

        Logger.LogDebug($"[State] Begin -> GetChallenge. Sending getchallenge (steam={_authProvider.IsAvailable}, authProto={_authProvider.GetAuthProtocol()})");
        var challengePacket = _authProvider.IsAvailable ? GetChallengeSteamPacket : GetChallengePacket;
        await _socket.SendAsync(new ReadOnlyMemory<byte>(challengePacket), ep, ct);

        Logger.LogDebug("[Loop] entering receive loop");
        while (!ct.IsCancellationRequested)
        {
            Logger.LogTrace("[Loop] waiting for packet...");
            var result = await _socket.ReceiveAsync(ct);
            var data = result.Buffer;
            var len = data.Length;
            var from = result.RemoteEndPoint;

            Logger.LogDebug($"[Loop] received {len} bytes from {from}");
            if (!ep.Equals(from)) continue;
            if (len < 4) continue;

            int offset = 0;
            uint header = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4));
            offset += 4;

            if (header == MessageConstants.ConnectionlessMarker)
            {
                var payload = Encoding.UTF8.GetString(data, offset, len - offset);
                Logger.LogDebug($"connectionless: {payload[..Math.Min(payload.Length, 200)]}");
                _sessions[ep] = await ProcessConnectionless(ep, payload, appId, ct);
                if (_sessions[ep] == SessionState.Connected)
                {
                    Logger.LogInformation("[State] -> Connected. Handshake complete.");
                    _connectedTcs.TrySetResult();
                    StartMoveTask();
                }
            }
            else if (header == MessageConstants.SplitMarker)
            {
                Logger.LogWarning($"[Fragment] received OOB split packet (len={len}), reassembly not yet implemented");
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
                Logger.LogDebug($"connected packet: srcSeq={srcSeq}, dstSeq={dstSeq}, isFragment={isFragment}, isCommand={isCommand}, payload={payloadLen}");
                OnDataPacket?.Invoke(this, data);

                if (payloadLen <= 0) continue;

                byte[] payload = new byte[payloadLen];
                Array.Copy(data, offset, payload, 0, payloadLen);

                if (isFragment && isCommand)
                {
                    ProcessFragmentCommand(ctx, ep, payload, payloadLen, ref srcSeq, ref dstSeq);
                    continue;
                }

                if (isFragment)
                {
                    Logger.LogWarning($"[Fragment] unexpected fragment-only packet (no command flag), accumulating {payloadLen} bytes");
                    ctx.IncomingFragment.AddRange(payload);
                    _ = SendAckAsync(ep);
                    continue;
                }

                byte[] messageData;
                int messageLen;
                if (ctx.IncomingFragment.Count > 0)
                {
                    Logger.LogDebug($"[Fragment] reassembly complete: {ctx.IncomingFragment.Count} + {payloadLen} = {ctx.IncomingFragment.Count + payloadLen} bytes");
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
                    Logger.LogDebug($"[Ack] received ack, dstSeq={dstSeq}");
                    continue;
                }

                if (!isCommand)
                {
                    Logger.LogDebug($"[Munge] UnMunge2 message len={messageLen}, seq={(int)(srcSeq & 0xFF)}");
                    MungeEngine.UnMunge2(messageData, messageLen, (int)(srcSeq & 0xFF));
                }
                else
                {
                    Logger.LogDebug($"[Munge] Command packet, skipping UnMunge2 for message len={messageLen}");
                }

                _sessions[ep] = ProcessConnected(ep, ref srcSeq, ref dstSeq, messageData, messageLen);
            }
        }
    }

    /// <summary>Closes the underlying UDP socket and releases all resources.</summary>
    public void Dispose()
    {
        _moveCts?.Cancel();
        _moveCts?.Dispose();
        _socket.Dispose();
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
