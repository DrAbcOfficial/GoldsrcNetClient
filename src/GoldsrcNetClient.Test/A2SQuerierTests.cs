using System.Text;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Query;
using System.Net;

namespace GoldsrcNetClient.Test;

/// <summary>
/// Byte fixtures are built to match the wire layouts of the reference server
/// implementations: ReHLDS SVC_Info (engine/sv_main.cpp), Xash3D
/// SV_SourceQuery_Info (engine/server/sv_query.c), and the Source A2S_INFO format.
/// </summary>
public class A2SQuerierTests
{
    private static byte[] Header() => [0xFF, 0xFF, 0xFF, 0xFF];

    private static byte[] Str(string s, Encoding? encoding = null) =>
    [
        .. (encoding ?? Encoding.UTF8).GetBytes(s),
        0x00
    ];

    [Fact]
    public void Parse_RehldsStyleInfo_ReturnsNameFromHostname()
    {
        // ReHLDS SVC_Info: m, address, hostname, map, gamedir, gamedesc, players, max, protocol
        byte[] packet =
        [
            .. Header(),
            (byte)'m',
            .. Str("111.229.5.113:27019"),
            .. Str("零点CS1.6服"),
            .. Str("de_dust2"),
            .. Str("cstrike"),
            .. Str("Counter-Strike"),
            0x03, // players
            0x20, // max = 32
            0x30, // protocol 48
        ];

        A2SInfo? info = A2SQuerier.Parse(packet, pingMs: 25);

        Assert.NotNull(info);
        Assert.Equal("零点CS1.6服", info.ServerName);
        Assert.Equal("de_dust2", info.Map);
        Assert.Equal(3, info.Players);
        Assert.Equal(32, info.MaxPlayers);
        Assert.Equal("cstrike", info.GameDir);
        Assert.Equal("Counter-Strike", info.GameDescription);
        Assert.Equal(25, info.PingMs);
    }

    [Fact]
    public void Parse_RehldsLoopbackAddress_IsDetected()
    {
        byte[] packet =
        [
            .. Header(),
            (byte)'m',
            .. Str("LOOPBACK"),
            .. Str("Half-Life Dedicated"),
            .. Str("map01"),
            .. Str("valve"),
            .. Str("Half-Life"),
            0x00, 0x08, 0x30,
        ];

        A2SInfo? info = A2SQuerier.Parse(packet, 1);

        Assert.NotNull(info);
        Assert.Equal("Half-Life Dedicated", info.ServerName);
        Assert.Equal(0, info.Players);
        Assert.Equal(8, info.MaxPlayers);
    }

    [Fact]
    public void Parse_XashStyleInfo_SkipsAppidShort()
    {
        // Xash3D: m, protocol byte, hostname, map, gamefolder, gamedesc, short appid, players, max, ...
        byte[] packet =
        [
            .. Header(),
            (byte)'m',
            0x30, // protocol 48 as raw byte
            .. Str("Xash Server"),
            .. Str("crossfire"),
            .. Str("valve"),
            .. Str("Half-Life"),
            0x00, 0x00, // appid short
            0x05, // players
            0x10, // max = 16
            0x00, // bots
            (byte)'d',
            (byte)'w',
        ];

        A2SInfo? info = A2SQuerier.Parse(packet, 9);

        Assert.NotNull(info);
        Assert.Equal("Xash Server", info.ServerName);
        Assert.Equal("crossfire", info.Map);
        Assert.Equal(5, info.Players);
        Assert.Equal(16, info.MaxPlayers);
        Assert.Equal("valve", info.GameDir);
    }

    [Fact]
    public void Parse_SourceInfo_ParsesNameAndCounts()
    {
        // Source A2S_INFO 'I': protocol, name, map, folder, game, short appid, players, max, bots...
        byte[] packet =
        [
            .. Header(),
            (byte)'I',
            0x11, // protocol 17
            .. Str("A Source Server"),
            .. Str("de_nuke"),
            .. Str("cstrike"),
            .. Str("Counter-Strike: Source"),
            0x50, 0x00, // appid 80
            0x07, // players
            0x18, // max = 24
            0x01, // bots
            (byte)'d',
            (byte)'l',
            0x00, // visibility
            0x01, // vac
            .. Str("1.0.0.34"),
        ];

        A2SInfo? info = A2SQuerier.Parse(packet, 40);

        Assert.NotNull(info);
        Assert.Equal("A Source Server", info.ServerName);
        Assert.Equal("de_nuke", info.Map);
        Assert.Equal(7, info.Players);
        Assert.Equal(24, info.MaxPlayers);
    }

    [Fact]
    public void Parse_GbkEncodedHostname_DecodesCorrectly()
    {
        // Legacy Chinese HLDS servers send GBK-encoded hostnames that are not
        // valid UTF-8; the parser must fall back to codepage 936.
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        byte[] hostnameGbk = Encoding.GetEncoding(936).GetBytes("传奇服务器");

        byte[] packet =
        [
            .. Header(),
            (byte)'m',
            .. Str("1.2.3.4:27015"),
            .. hostnameGbk,
            0x00, // null terminator of the hostname string
            .. Str("de_inferno"),
            .. Str("cstrike"),
            .. Str("Counter-Strike"),
            0x02, 0x10, 0x30,
        ];

        A2SInfo? info = A2SQuerier.Parse(packet, 15);

        Assert.Null(A2SQuerier.LegacyEncodingError);
        Assert.NotNull(info);
        Assert.Equal("传奇服务器", info.ServerName);
    }

    [Fact]
    public void Parse_TruncatedOrInvalidPackets_ReturnNull()
    {
        Assert.Null(A2SQuerier.Parse([], 1));
        Assert.Null(A2SQuerier.Parse([0xFF, 0xFF, 0xFF], 1));
        Assert.Null(A2SQuerier.Parse([0xFF, 0xFF, 0xFF, 0xFF, (byte)'X', 0x00], 1));
        Assert.Null(A2SQuerier.Parse([0x00, 0x00, 0x00, 0x00, (byte)'m'], 1));

        // unterminated string
        Assert.Null(A2SQuerier.Parse([0xFF, 0xFF, 0xFF, 0xFF, (byte)'m', (byte)'a', (byte)'b'], 1));
    }

    [Fact]
    public async Task QueryAsync_InjectedTransport_ParsesReplyAndFiltersForeignDatagrams()
    {
        byte[] reply =
        [
            .. Header(),
            (byte)'m',
            .. Str("127.0.0.1:27015"),
            .. Str("Seam Server"),
            .. Str("crossfire"),
            .. Str("valve"),
            .. Str("Half-Life"),
            0x01, // players
            0x10, // max = 16
        ];

        // A datagram from a different endpoint arrives first; the querier must
        // ignore it and keep waiting for the reply from the queried endpoint.
        var transport = new ScriptedTransport();
        transport.Replies.Enqueue((new IPEndPoint(IPAddress.Loopback, 40000), [0x01, 0x02, 0x03]));
        transport.Replies.Enqueue((new IPEndPoint(IPAddress.Loopback, 27015), reply));

        var querier = new A2SQuerier(() => transport);
        A2SInfo? info = await querier.QueryAsync("127.0.0.1", 27015);

        Assert.NotNull(info);
        Assert.Equal("Seam Server", info.ServerName);
        Assert.Equal(1, info.Players);
        Assert.Equal(16, info.MaxPlayers);
        Assert.Single(transport.Requests); // one request: the reply was not a challenge
        transport.Dispose();
    }

    /// <summary>Transport fake replaying queued (endpoint, datagram) pairs and
    /// recording every request — no network I/O.</summary>
    private sealed class ScriptedTransport : ITransport
    {
        public Queue<(IPEndPoint From, byte[] Buffer)> Replies { get; } = new();
        public List<byte[]> Requests { get; } = [];

        public Task SendAsync(ReadOnlyMemory<byte> buffer, IPEndPoint target, CancellationToken ct)
        {
            Requests.Add(buffer.ToArray());
            return Task.CompletedTask;
        }

        public Task<(byte[] Buffer, IPEndPoint RemoteEndPoint)> ReceiveAsync(CancellationToken ct)
        {
            if (Replies.Count > 0)
            {
                var (from, buffer) = Replies.Dequeue();
                return Task.FromResult((buffer, from));
            }
            return Task.FromResult((Array.Empty<byte>(), new IPEndPoint(IPAddress.Any, 0)));
        }

        public void Dispose() { }
    }
}
