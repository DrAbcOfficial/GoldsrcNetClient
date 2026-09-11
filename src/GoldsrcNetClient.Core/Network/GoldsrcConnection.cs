using GoldsrcNetClient.Core.Handshake;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Netchan;
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
/// Orchestrates the UDP transport, the connection handshake
/// (getchallenge → connect → connected), the per-endpoint sequenced channel
/// (netchan), and connected server-message processing (Munge decryption, delta
/// compression parsing, resource list decoding). Supports both WON and Steam
/// authentication protocols.
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
    /// <summary>Per-endpoint session: session data, sequenced channel, and handshake state.</summary>
    private sealed class Session(IPEndPoint endpoint)
    {
        public ConnectionContext Context { get; } = new()
        {
            ServerIp = BitConverter.ToUInt32(endpoint.Address.GetAddressBytes()),
            ServerPort = (ushort)endpoint.Port
        };

        public required NetchanChannel Channel { get; init; }

        public SessionState State = SessionState.GetChallenge;
    }

    private static readonly IPEndPoint DummyEndpoint = new(0, 0);

    private readonly UdpClient _socket;
    private readonly Dictionary<IPEndPoint, Session> _sessions = [];
    private readonly ISteamAuthProvider _authProvider;
    private readonly IServerMessageHandler _messageHandler;
    private readonly HandshakeNegotiator _handshake;
    private readonly Dictionary<byte, MessageParser> _messageParsers;
    private readonly TaskCompletionSource _connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _keepAliveCts;

    private IPEndPoint? _activeEndpoint;
    private bool _sentContinueLoading;
    private bool _sentSpawn;
    private UserInfoString _userInfo;

    internal readonly ILogger<GoldsrcConnection> Logger;

    /// <summary>Raised when the server disconnects the client, with the server-supplied reason.</summary>
    public event Action<string>? OnServerDisconnect;

    /// <summary>
    /// Raised for every <c>svc_print</c> console message the server sends
    /// (chat for text-only mods, kick/drop notices, rule changes, etc.).
    /// </summary>
    public event Action<string>? OnConsolePrint;

    /// <summary>
    /// Raised for every <c>svc_centerprint</c> message the server sends.
    /// </summary>
    public event Action<string>? OnCenterPrint;

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
        get => _sessions.TryGetValue(_activeEndpoint ?? DummyEndpoint, out var session) ? session.Context.SpawnCount : 0;
        set
        {
            if (_sessions.TryGetValue(_activeEndpoint ?? DummyEndpoint, out var session))
                session.Context.SpawnCount = value;
        }
    }

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
        get => _sessions.TryGetValue(_activeEndpoint ?? DummyEndpoint, out var session) ? session.Context.ResourceListRawBytes : [];
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
    /// <remarks>
    /// <para><c>rate</c> controls how fast the server may transmit the reliable
    /// fragment stream towards us. A low value paces the signon batch one small
    /// fragment per rate-window, which keeps the server's per-client reliable
    /// buffer (<c>netchan.message</c>, ~4 KB) blocked for the whole transfer;
    /// game-DLL reliable messages written during that window then overflow it and
    /// the server drops us with <c>Reliable channel overflowed</c>. Real clients
    /// therefore negotiate a high rate, and the server clamps it to
    /// <c>sv_maxrate</c> anyway.</para>
    /// </remarks>
    public string UserInfo
    {
        get => _userInfo.ToString();
        set => _userInfo = new UserInfoString(value);
    }

    /// <summary>Sets a single key-value pair in the <see cref="UserInfo"/> string.</summary>
    /// <param name="key">The key to set (case-insensitive).</param>
    /// <param name="value">The new value for the key.</param>
    public void SetUserInfo(string key, string value) => _userInfo.Set(key, value);

    /// <summary>Gets a value from the <see cref="UserInfo"/> string by key.</summary>
    /// <param name="key">The key to look up (case-insensitive).</param>
    /// <returns>The value string if found; <c>null</c> otherwise.</returns>
    public string? GetUserInfo(string key) => _userInfo.Get(key);

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
        _handshake = new HandshakeNegotiator(_authProvider, Settings,
            (buffer, target, token) => _socket.SendAsync(buffer, target, token).AsTask(), Logger);
        _messageParsers = BuildMessageParsers();
        _userInfo = new UserInfoString(Settings.DefaultUserInfo);
    }

    /// <summary>
    /// Sends a console command string to the server as a reliable
    /// <see cref="ClientCommandType.StringCmd"/> message (e.g. <c>"say hello"</c>, <c>"status"</c>).
    /// Delivery is guaranteed by the netchan until the server acknowledges it.
    /// </summary>
    /// <param name="cmd">The client command type.</param>
    /// <param name="payload">Null-terminated string payload.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task SendStringCmdAsync(ClientCommandType cmd, string payload, CancellationToken ct = default)
    {
        List<byte> bytes = [(byte)cmd, .. Encoding.UTF8.GetBytes(payload), 0];
        return SendReliableAsync([.. bytes], ct);
    }

    /// <summary>
    /// Sends a raw reliable command with arbitrary data to the connected server.
    /// </summary>
    /// <param name="cmd">The client command type byte.</param>
    /// <param name="data">Raw payload bytes (appended after the command byte).</param>
    /// <param name="ct">Cancellation token.</param>
    public Task SendCommandAsync(ClientCommandType cmd, byte[] data, CancellationToken ct = default)
    {
        byte[] bytes = new byte[data.Length + 1];
        bytes[0] = (byte)cmd;
        data.CopyTo(bytes, 1);
        return SendReliableAsync(bytes, ct);
    }

    /// <summary>
    /// Replies to the server's <c>svc_sendcvarvalue</c> query with a cvar value.
    /// </summary>
    public Task SendCvarValueAsync(string name, string value)
    {
        List<byte> reply = [];
        MessageWriter.WriteString(reply, name);
        MessageWriter.WriteString(reply, value);
        return SendCommandAsync(ClientCommandType.CvarValue, [.. reply]);
    }

    /// <summary>
    /// Replies to the server's <c>svc_sendcvarvalue2</c> query with a cvar value.
    /// </summary>
    public Task SendCvarValue2Async(int requestId, string name, string value)
    {
        List<byte> reply = [];
        MessageWriter.WriteUInt32(reply, (uint)requestId);
        MessageWriter.WriteString(reply, name);
        MessageWriter.WriteString(reply, value);
        return SendCommandAsync(ClientCommandType.CvarValue2, [.. reply]);
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

        var session = new Session(ep)
        {
            Channel = new NetchanChannel(ep,
                (buffer, target, token) => _socket.SendAsync(buffer, target, token).AsTask(), Logger)
        };
        _sessions[ep] = session;

        Logger.LogDebug($"[State] Begin -> GetChallenge. Sending getchallenge (steam={_authProvider.IsAvailable}, authProto={_authProvider.GetAuthProtocol()})");
        await _socket.SendAsync(_handshake.BuildGetChallengePacket(_authProvider.IsAvailable), ep, ct);

        StartKeepAliveTask();

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

            uint header = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4));

            if (header == MessageConstants.ConnectionlessMarker)
            {
                int offset = 4;
                var payload = Encoding.UTF8.GetString(data, offset, len - offset);
                Logger.LogDebug($"connectionless: {payload[..Math.Min(payload.Length, 200)]}");
                session.State = await _handshake.HandleResponseAsync(
                    session.State, ep, appId, _userInfo.ToString(), session.Context, payload, SendStringCmdAsync, ct);
                if (session.State == SessionState.Connected)
                {
                    Logger.LogInformation("[State] -> Connected. Handshake complete.");
                    _connectedTcs.TrySetResult();
                }
            }
            else if (header == MessageConstants.SplitMarker)
            {
                Logger.LogWarning($"[Fragment] received OOB split packet (len={len}), reassembly not yet implemented");
            }
            else
            {
                OnDataPacket?.Invoke(this, data.ToArray());
                session.Channel.ProcessIncoming(data.ToArray(), message => ProcessConnected(ep, message));
            }
        }
    }

    /// <summary>Closes the underlying UDP socket and releases all resources.</summary>
    public void Dispose()
    {
        _keepAliveCts?.Cancel();
        _keepAliveCts?.Dispose();
        _socket.Dispose();
    }

    private Task SendReliableAsync(byte[] payload, CancellationToken ct)
    {
        if (_sessions.TryGetValue(_activeEndpoint ?? DummyEndpoint, out var session))
        {
            session.Channel.SendReliable(payload);
            return Task.CompletedTask;
        }
        throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
    }

    /// <summary>
    /// Background task that transmits a packet at a fixed interval. Each packet carries
    /// acknowledgements for the server, retransmits unacknowledged reliable payload,
    /// and keeps the netchan alive on the server's timeout radar.
    /// </summary>
    private void StartKeepAliveTask()
    {
        if (_keepAliveCts != null) return;
        _keepAliveCts = new CancellationTokenSource();
        var token = _keepAliveCts.Token;
        _ = Task.Run(async () =>
        {
            Logger.LogDebug("[KeepAlive] starting keepalive task");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(Settings.MoveIntervalMs, token);
                    if (_sessions.TryGetValue(_activeEndpoint ?? DummyEndpoint, out var session))
                        session.Channel.SendKeepAlive();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Logger.LogWarning($"[KeepAlive] error: {ex.Message}");
                }
            }
            Logger.LogDebug("[KeepAlive] keepalive task stopped");
        }, token);
    }
}
