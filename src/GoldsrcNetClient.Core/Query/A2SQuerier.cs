using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Network;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GoldsrcNetClient.Core.Query;

/// <summary>
/// Result of an A2S_INFO query against a GoldSrc (protocol 48) or Source server.
/// </summary>
/// <param name="ServerName">Server hostname as reported by the server.</param>
/// <param name="Map">Current map.</param>
/// <param name="Players">Current number of players.</param>
/// <param name="MaxPlayers">Maximum number of players.</param>
/// <param name="GameDir">Mod/game directory (e.g. cstrike).</param>
/// <param name="GameDescription">Human readable game description.</param>
/// <param name="PingMs">Query round-trip time in milliseconds.</param>
public sealed record A2SInfo(
    string ServerName,
    string Map,
    int Players,
    int MaxPlayers,
    string GameDir,
    string GameDescription,
    int PingMs);

/// <summary>
/// Minimal A2S_INFO client for the Valve server query protocol. Speaks both the
/// legacy GoldSrc reply (header <c>'m'</c>) and the Source reply (header <c>'I'</c>),
/// and handles the challenge round-trip that some servers require before answering.
/// The UDP transport is a seam: <see cref="A2SQuerier(Func{ITransport})"/> accepts a
/// factory (one fresh transport per query, disposed afterwards) so tests can script
/// replies without network I/O; the static convenience overload uses the default.
/// </summary>
public sealed class A2SQuerier
{
    private static readonly byte[] InfoRequestSuffix = "Source Engine Query\0"u8.ToArray();
    private static readonly byte[] InfoRequest = BuildRequest([]);

    private readonly Func<ITransport>? _transportFactory;

    /// <summary>Creates a querier over an optional transport factory. Each query
    /// invokes the factory once and disposes the transport it returns; when absent,
    /// a real <see cref="UdpTransport"/> bound to an OS-assigned port is used.</summary>
    public A2SQuerier(Func<ITransport>? transportFactory = null)
    {
        _transportFactory = transportFactory;
    }

    /// <summary>Convenience wrapper using the default transport; see <see cref="QueryAsync"/>.</summary>
    public static Task<A2SInfo?> QueryInfoAsync(
        string host, int port, CancellationToken ct = default, TimeSpan? timeout = null)
        => new A2SQuerier().QueryAsync(host, port, ct, timeout);

    /// <summary>
    /// Queries <paramref name="host"/>:<paramref name="port"/> for server info.
    /// </summary>
    /// <returns>The parsed info, or null when the query timed out or the reply could not be parsed.</returns>
    public async Task<A2SInfo?> QueryAsync(
        string host, int port, CancellationToken ct = default, TimeSpan? timeout = null)
    {
        TimeSpan wait = timeout ?? TimeSpan.FromSeconds(2);
        using var link = CancellationTokenSource.CreateLinkedTokenSource(ct);
        link.CancelAfter(wait);

        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, link.Token);
            IPAddress? ip = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                            ?? addresses.FirstOrDefault();
            if (ip == null)
                return null;

            var endpoint = new IPEndPoint(ip, port);

            using ITransport transport = _transportFactory?.Invoke() ?? new UdpTransport(0);

            Stopwatch sw = Stopwatch.StartNew();
            byte[] reply = await ExchangeAsync(transport, endpoint, InfoRequest, link.Token);
            for (int round = 0; round < 2 && reply.Length >= 5 && reply[4] == (byte)'A'; round++)
            {
                // Challenge response: resend the request with the challenge appended.
                byte[] withChallenge = BuildRequest(reply[5..]);
                reply = await ExchangeAsync(transport, endpoint, withChallenge, link.Token);
            }
            sw.Stop();

            return Parse(reply, (int)sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // timed out
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[] BuildRequest(byte[] suffix)
    {
        byte[] request = new byte[4 + 1 + InfoRequestSuffix.Length + suffix.Length];
        request[0] = 0xFF; request[1] = 0xFF; request[2] = 0xFF; request[3] = 0xFF;
        request[4] = (byte)'T';
        InfoRequestSuffix.CopyTo(request, 5);
        suffix.CopyTo(request, 5 + InfoRequestSuffix.Length);
        return request;
    }

    private static async Task<byte[]> ExchangeAsync(ITransport transport, IPEndPoint endpoint, byte[] request, CancellationToken ct)
    {
        await transport.SendAsync(request, endpoint, ct);
        while (true)
        {
            // The transport socket is bound but not connected, so it sees datagrams
            // from anywhere; replies are filtered to the queried endpoint.
            (byte[] buffer, IPEndPoint from) = await transport.ReceiveAsync(ct);
            if (from.Equals(endpoint))
                return buffer;
        }
    }

    internal static A2SInfo? Parse(byte[] data, int pingMs)
    {
        if (data.Length < 6)
            return null;

        uint marker = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (marker != 0xFFFFFFFFu)
            return null;

        try
        {
            return data[4] switch
            {
                (byte)'m' => ParseGoldSrc(data, pingMs),
                (byte)'I' => ParseSource(data, pingMs),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// GoldSrc reply (header <c>'m'</c>). Two wire variants exist:
    /// <list type="bullet">
    /// <item>HLDS/ReHLDS: <c>m address name map folder game players max protocol</c>.</item>
    /// <item>Xash3D: <c>m protocol name map folder game (short)appid players max ...</c> (no address).</item>
    /// </list>
    /// The first field disambiguates: HLDS begins with the server address string
    /// (<c>ip:port</c> or <c>LOOPBACK</c>), Xash begins with the protocol byte followed
    /// by the hostname string.
    /// </summary>
    private static A2SInfo ParseGoldSrc(byte[] data, int pingMs)
    {
        var reader = new BufferReader(data) { BytePosition = 5 };
        string first = Decode(reader.ReadStringBytes());

        if (LooksLikeAddress(first))
        {
            string name = Decode(reader.ReadStringBytes());
            string map = Decode(reader.ReadStringBytes());
            string folder = Decode(reader.ReadStringBytes());
            string game = Decode(reader.ReadStringBytes());
            int players = reader.ReadUInt8();
            int maxPlayers = reader.ReadUInt8();
            return new A2SInfo(name, map, players, maxPlayers, folder, game, pingMs);
        }
        else
        {
            // Xash3D: the protocol version byte (ASCII digit) precedes the hostname.
            string name = first.Length > 1 ? first[1..] : first;
            string map = Decode(reader.ReadStringBytes());
            string folder = Decode(reader.ReadStringBytes());
            string game = Decode(reader.ReadStringBytes());
            reader.ReadInt16(); // appid
            int players = reader.ReadUInt8();
            int maxPlayers = reader.ReadUInt8();
            return new A2SInfo(name, map, players, maxPlayers, folder, game, pingMs);
        }
    }

    private static bool LooksLikeAddress(string s) =>
        s == "LOOPBACK" || System.Text.RegularExpressions.Regex.IsMatch(
            s, @"^\d{1,3}(\.\d{1,3}){3}:\d{1,5}$");

    /// <summary>Source reply: <c>I protocol name map folder game (short)id players max bots ...</c>.</summary>
    private static A2SInfo ParseSource(byte[] data, int pingMs)
    {
        var reader = new BufferReader(data) { BytePosition = 5 };
        reader.ReadUInt8(); // protocol version
        string name = Decode(reader.ReadStringBytes());
        string map = Decode(reader.ReadStringBytes());
        string folder = Decode(reader.ReadStringBytes());
        string game = Decode(reader.ReadStringBytes());
        reader.ReadInt16(); // appid
        int players = reader.ReadUInt8();
        int maxPlayers = reader.ReadUInt8();

        return new A2SInfo(name, map, players, maxPlayers, folder, game, pingMs);
    }

    private static Encoding? _legacyEncoding;

    /// <summary>Populated when legacy encoding initialization fails; for diagnostics.</summary>
    public static string? LegacyEncodingError { get; private set; }

    /// <summary>
    /// Encoding used when the payload is not valid UTF-8. Real GoldSrc servers
    /// commonly send non-UTF-8 text (e.g. GBK-encoded Chinese server names).
    /// </summary>
    private static Encoding LegacyEncoding
    {
        get
        {
            if (_legacyEncoding == null)
            {
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    _legacyEncoding = Encoding.GetEncoding(936);
                }
                catch (Exception ex)
                {
                    LegacyEncodingError = $"{ex.GetType().Name}: {ex.Message}";
                    _legacyEncoding = Encoding.Latin1;
                }
            }
            return _legacyEncoding;
        }
    }

    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
            return "";
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch
        {
            try { return LegacyEncoding.GetString(bytes); }
            catch { return Encoding.Latin1.GetString(bytes); }
        }
    }
}
