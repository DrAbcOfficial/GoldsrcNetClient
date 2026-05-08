using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GoldsrcNetClient.Core.Delta;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Munge;
using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.Core.Util;

namespace GoldsrcNetClient.Core.Network;

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

    public event Action<string>? OnPrint;
    public delegate void ServerInfoHandler(GoldsrcConnection conn, ServerInfoData info);
    public event ServerInfoHandler? OnServerInfo;
    public delegate void ResourceListHandler(GoldsrcConnection conn, ResourceInfo[] resources);
    public event ResourceListHandler? OnResourceList;
    public delegate void DataPacketHandler(GoldsrcConnection conn, byte[] data);
    public event DataPacketHandler? OnDataPacket;
    public event Action<string>? OnDebug;

    public GoldsrcConnection(ISteamAuthProvider? authProvider = null, int localPort = 0)
    {
        _authProvider = authProvider ?? new NoSteamAuthProvider();
        _socket = new UdpClient(localPort);
    }

    public async Task ConnectAsync(string host, int port = 27015, CancellationToken ct = default)
    {
        OnDebug?.Invoke($"[State] Begin -> resolving {host}:{port}");
        var addresses = await Dns.GetHostAddressesAsync(host, ct);
        var ep = new IPEndPoint(addresses[0], port);
        OnDebug?.Invoke($"[DNS] resolved {host} -> {ep}");
        _sessions[ep] = SessionState.GetChallenge;
        _contexts[ep] = new ConnectionContext();

        OnDebug?.Invoke($"[State] Begin -> GetChallenge. Sending getchallenge (steam={_authProvider.IsAvailable}, authProto={_authProvider.GetAuthProtocol()})");
        var challengePacket = _authProvider.IsAvailable ? GetChallengeSteamPacket : GetChallengePacket;
        await _socket.SendAsync(new ReadOnlyMemory<byte>(challengePacket), ep, ct);

        bool done = false;

        OnDebug?.Invoke("[Loop] entering receive loop");
        while (!done && !ct.IsCancellationRequested)
        {
            OnDebug?.Invoke("[Loop] waiting for packet...");
            var result = await _socket.ReceiveAsync(ct);
            var data = result.Buffer;
            var len = data.Length;
            var from = result.RemoteEndPoint;

            OnDebug?.Invoke($"[Loop] received {len} bytes from {from}");
            if (!ep.Equals(from)) continue;
            if (len < 4) continue;

            int offset = 0;
            uint header = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4));
            offset += 4;

            if (header == MessageConstants.ConnectionlessMarker)
            {
                var payload = Encoding.ASCII.GetString(data, offset, len - offset);
                OnDebug?.Invoke($"connectionless: {payload[..Math.Min(payload.Length, 200)]}");
                _sessions[ep] = await ProcessConnectionless(ep, payload, ct);
                if (_sessions[ep] == SessionState.Connected)
                {
                    OnDebug?.Invoke("[State] -> Connected. Handshake complete.");
                    done = true;
                }
            }
            else if (header == MessageConstants.SplitMarker)
            {
                OnDebug?.Invoke($"[Fragment] received split packet header (len={len}), fragment reassembly not yet implemented");
            }
            else
            {
                var ctx = _contexts[ep];
                uint srcSeq = header & MessageConstants.SequenceMask;
                uint dstSeq = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
                offset += 4;

                ctx.DstSequence = srcSeq;

                int payloadLen = len - offset;
                OnDebug?.Invoke($"connected packet: srcSeq={srcSeq}, dstSeq={dstSeq}, payload={payloadLen}");
                OnDataPacket?.Invoke(this, data);
                if (payloadLen > 0)
                {
                    byte[] payload = new byte[payloadLen];
                    Array.Copy(data, offset, payload, 0, payloadLen);
                    OnDebug?.Invoke($"[Munge] UnMunge2 payload len={payloadLen}, seq={(int)(srcSeq & 0xFF)}");
                    MungeEngine.UnMunge2(payload, payloadLen, (int)(srcSeq & 0xFF));

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
                OnDebug?.Invoke($"[Challenge] Format A: markerHex={challengeFromMarker}, field2={challengeFromField}, authProto={ctx.AuthProtocol}, parts={parts.Length}");

                string challengeToken = challengeFromField;
                OnDebug?.Invoke($"[Challenge] using field2 as challenge: {challengeToken}");

                ctx.Challenge = Encoding.ASCII.GetBytes(challengeToken);

                OnDebug?.Invoke($"[Challenge] parsed: challenge={challengeToken}, authProto={ctx.AuthProtocol}");
                var data = BuildConnectPacket(ep);
                OnDebug?.Invoke($"[Connect] sending connect packet, len={data.Length}");
                await _socket.SendAsync(new ReadOnlyMemory<byte>(data), ep, ct);
                return SessionState.Connect0;
            }

            if (parts.Length >= 2 && char.IsDigit(parts[0][0]))
            {
                string challengeToken = parts[1];
                OnDebug?.Invoke($"[Challenge] Format legacy: challenge={challengeToken}, parts={parts.Length}");

                ctx.Challenge = Encoding.ASCII.GetBytes(challengeToken);
                var data = BuildConnectPacket(ep);
                OnDebug?.Invoke($"[Connect] sending connect packet (legacy), len={data.Length}");
                await _socket.SendAsync(new ReadOnlyMemory<byte>(data), ep, ct);
                return SessionState.Connect0;
            }

            OnDebug?.Invoke($"[Challenge] unexpected response (len={parts.Length}, first={parts[0]}): {payload}");
        }
        else if (state == SessionState.Connect0)
        {
            var parts = payload.Split(' ');
            string msgId = parts[0];

            if (msgId.StartsWith('B'))
            {
                ctx.UserId = parts.Length > 1 && int.TryParse(parts[1], out int uid) ? uid : 0;
                OnDebug?.Invoke($"[Connect0] Approval (B): userId={ctx.UserId}, parts={parts.Length}, payload={payload}");
                OnPrint?.Invoke($"Connection accepted by {ep}");

                OnDebug?.Invoke($"[State] Connect0 -> Connected. Sending 'new' stringcmd");
                await SendConnectedAsync(ep, ClientCommandType.StringCmd, "new", ct);
                return SessionState.Connected;
            }

            if (msgId.StartsWith('9') || payload.Contains("Bad ", StringComparison.OrdinalIgnoreCase))
            {
                OnDebug?.Invoke($"[Connect0] Server rejected connection: {payload}");
                OnPrint?.Invoke($"Server rejected connection: {payload}");
                return SessionState.Connect0;
            }

            OnDebug?.Invoke($"[Connect0] Generic answer (not B): {payload}");
            return SessionState.Connected;
        }

        return state;
    }

    private byte[] BuildConnectPacket(IPEndPoint ep)
    {
        var ctx = _contexts[ep];
        var challengeStr = Encoding.ASCII.GetString(ctx.Challenge);
        var connectPrefix = $"connect {ProtocolVersion} {challengeStr} ";

        var authProto = ctx.AuthProtocol;
        var rawAuth = _authProvider.GetRawAuthData();
        if (rawAuth == "steam" && !_authProvider.IsAvailable)
        {
            if (authProto == 2)
            {
                rawAuth = "00000000000000000000000000000000";
                OnDebug?.Invoke($"[Connect] generated fake hashed CD key (32 zero hex chars) for authProto=2");
            }
            else if (authProto == 1)
            {
                rawAuth = "0000000000000";
                OnDebug?.Invoke($"[Connect] generated fake WON CD key for authProto=1");
            }
        }
        var protoInfo = $"\\prot\\{authProto}\\unique\\-1\\raw\\{rawAuth}";
        var userInfo = "\\name\\GoldsrcNetClient\\protocol\\48\\cl_lc\\1\\cl_lw\\1\\cl_updaterate\\60\\rate\\20000";

        var full = $"{connectPrefix}\"{protoInfo}\" \"{userInfo}\"\n";
        OnDebug?.Invoke($"[Connect] packet: proto={ProtocolVersion}, challenge={challengeStr}, authProto={authProto}, rawAuth=[{rawAuth.Length} bytes], protoInfo={protoInfo}");

        var result = new byte[4 + full.Length];
        result[0] = 0xFF;
        result[1] = 0xFF;
        result[2] = 0xFF;
        result[3] = 0xFF;
        Encoding.ASCII.GetBytes(full, 0, full.Length, result, 4);
        OnDebug?.Invoke($"[Connect] raw hex: {Convert.ToHexString(result.AsSpan(0, Math.Min(result.Length, 64)))}");
        return result;
    }

    private SessionState ProcessConnected(IPEndPoint ep, ref uint srcSequence, ref uint dstSequence,
        byte[] data, int size)
    {
        var ctx = _contexts[ep];

        OnDebug?.Invoke($"[Connected] processing {size} bytes, srcSeq={srcSequence}, dstSeq={dstSequence}");

        int offset = 0;
        while (offset < size)
        {
            byte dataType = data[offset++];
            int dataLen = size - offset;
            string typeName = Enum.IsDefined(typeof(ServerMessageType), dataType) ? ((ServerMessageType)dataType).ToString() : $"0x{dataType:X2}";
            OnDebug?.Invoke($"[Connected] type={typeName} (0x{dataType:X2}), remaining={dataLen}");

            if (dataType == (byte)ServerMessageType.Nop) { }
            else if (dataType == (byte)ServerMessageType.Print)
            {
                string msg = MessageReader.ReadString(ref data, ref offset, size);
                OnDebug?.Invoke($"[Print] msg=\"{msg[..Math.Min(msg.Length, 200)]}\"");
                OnPrint?.Invoke(msg);
            }
            else if (dataType == (byte)ServerMessageType.ServerInfo)
            {
                int structSize = 33;
                if (offset + structSize > size)
                {
                    OnDebug?.Invoke($"[ServerInfo] buffer overflow: offset={offset}, need={structSize}, size={size}");
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
                OnDebug?.Invoke($"[ServerInfo] proto={si.ProtocolVersion}, spawnCount={si.SpawnCount}, maxClients={si.MaxClients}, playerNum={si.PlayerNumber}, worldmapCrcRaw=0x{si.Munge3WorldmapCrc:X8}, unmungeSeq={unmungeSeq}");
                MungeEngine.UnMunge3(crcBytes, 4, (int)((-1 - ctx.PlayerNumber) & 0xFF));
                ctx.WorldmapCrc = BitConverter.ToUInt32(crcBytes);
                OnDebug?.Invoke($"[ServerInfo] worldmapCrcUnMunaged=0x{ctx.WorldmapCrc:X8}");

                for (int i = 0; i < 4; i++)
                    MessageReader.ReadString(ref data, ref offset, size);

                if (offset + 1 <= size)
                    offset++;

                OnServerInfo?.Invoke(this, si);
            }
            else if (dataType == (byte)ServerMessageType.DeltaDescription)
            {
                string name = MessageReader.ReadString(ref data, ref offset, size);
                OnDebug?.Invoke($"[DeltaDescription] deltaName=\"{name}\"");
                var dt = DeltaDefinitions.Find(name);
                if (dt == null)
                {
                    OnDebug?.Invoke($"[DeltaDescription] unknown delta \"{name}\", skipping");
                    return SessionState.Connected;
                }

                int bitIdx = 0;
                uint fieldCount = 0;
                if (!BitReader.ReadBits(data, ref bitIdx, size, ref fieldCount, 16))
                {
                    OnDebug?.Invoke($"[DeltaDescription] failed reading fieldCount");
                    return SessionState.Connected;
                }
                OnDebug?.Invoke($"[DeltaDescription] fieldCount={fieldCount}");

                for (uint f = 0; f < fieldCount; f++)
                {
                    int parseBitIdx = 0;
                    if (!ParseDeltaFieldDescriptions(data, size, ref parseBitIdx))
                    {
                        OnDebug?.Invoke($"[DeltaDescription] failed parsing field {f}");
                        return SessionState.Connected;
                    }
                }

                offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
                OnDebug?.Invoke($"[DeltaDescription] done, new offset={offset}");
            }
            else if (dataType == (byte)ServerMessageType.NewMoveVars)
            {
                int mvSize;
                unsafe { mvSize = sizeof(NewMoveVarsData); }
                if (offset + mvSize > size)
                {
                    OnDebug?.Invoke($"[NewMoveVars] buffer overflow: offset={offset}, need={mvSize}, size={size}");
                    return SessionState.Connected;
                }

                unsafe
                {
                    fixed (byte* p = &data[offset])
                        _ = *(NewMoveVarsData*)p;
                    offset += mvSize;
                }

                MessageReader.ReadString(ref data, ref offset, size);
                OnDebug?.Invoke($"[NewMoveVars] done, structSize={mvSize}");
            }
            else if (dataType == (byte)ServerMessageType.SetView)
            {
                if (offset + 2 > size)
                {
                    OnDebug?.Invoke($"[SetView] buffer overflow: offset={offset}, size={size}");
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
                    OnDebug?.Invoke($"[NewUserMsg] buffer overflow: offset={offset}, need={msgSize}, size={size}");
                    return SessionState.Connected;
                }

                unsafe
                {
                    fixed (byte* p = &data[offset])
                        _ = *(NewUserMsgData*)p;
                    offset += msgSize;
                }
                OnDebug?.Invoke($"[NewUserMsg] done, structSize={msgSize}");
            }
            else if (dataType == (byte)ServerMessageType.StuffText)
            {
                string st = MessageReader.ReadString(ref data, ref offset, size);
                OnDebug?.Invoke($"[StuffText] text=\"{st[..Math.Min(st.Length, 200)]}\"");
            }
            else if (dataType == (byte)ServerMessageType.UpdateUserInfo)
            {
                if (offset + 1 > size) { OnDebug?.Invoke("[UpdateUserInfo] buffer overflow at byte 1"); return SessionState.Connected; }
                offset += 1;
                if (offset + 4 > size) { OnDebug?.Invoke("[UpdateUserInfo] buffer overflow at byte 4"); return SessionState.Connected; }
                offset += 4;
                string uui = MessageReader.ReadString(ref data, ref offset, size);
                OnDebug?.Invoke($"[UpdateUserInfo] userInfo=\"{uui[..Math.Min(uui.Length, 100)]}\"");
                if (offset + 16 > size) { OnDebug?.Invoke("[UpdateUserInfo] buffer overflow at 16"); return SessionState.Connected; }
                offset += 16;
            }
            else if (dataType == (byte)ServerMessageType.ResourceRequest)
            {
                if (offset + 4 > size) { OnDebug?.Invoke("[ResourceRequest] buffer overflow"); return SessionState.Connected; }
                ctx.SpawnCount = BitConverter.ToUInt32(data, offset);
                offset += 4;
                if (offset + 4 > size) { OnDebug?.Invoke("[ResourceRequest] buffer overflow after spawnCount"); return SessionState.Connected; }
                uint resUnknown = BitConverter.ToUInt32(data, offset);
                offset += 4;
                OnDebug?.Invoke($"[ResourceRequest] spawnCount={ctx.SpawnCount}, unknown={resUnknown}");
            }
            else if (dataType == (byte)ServerMessageType.ResourceLocation)
            {
                string loc = MessageReader.ReadString(ref data, ref offset, size);
                OnDebug?.Invoke($"[ResourceLocation] location=\"{loc}\"");
            }
            else if (dataType == (byte)ServerMessageType.ResourceList)
            {
                int listStart = offset;
                ProcessResourceList(ctx, ref data, ref offset, size);
                OnDebug?.Invoke($"[ResourceList] count={ctx.Resources.Length}, dataBytes={offset - listStart}");
                OnResourceList?.Invoke(this, ctx.Resources);
            }
            else if (dataType == (byte)ServerMessageType.TempEntity)
            {
                if (offset + 1 > size) { OnDebug?.Invoke("[TempEntity] buffer overflow"); return SessionState.Connected; }
                offset += 1;
                for (int i = 0; i < 3; i++)
                {
                    if (offset + 2 > size) { OnDebug?.Invoke($"[TempEntity] buffer overflow at coord {i}"); return SessionState.Connected; }
                    offset += 2;
                }
            }
            else if (dataType == (byte)ServerMessageType.SpawnStaticSound)
            {
                offset += 14;
            }
            else if (dataType == (byte)ServerMessageType.SendCvarValue2)
            {
                if (offset + 4 > size) { OnDebug?.Invoke("[SendCvarValue2] buffer overflow"); return SessionState.Connected; }
                uint requestId = BitConverter.ToUInt32(data, offset);
                offset += 4;
                string cvarName = MessageReader.ReadString(ref data, ref offset, size);
                OnDebug?.Invoke($"[SendCvarValue2] requestId={requestId}, cvar=\"{cvarName}\"");
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
                        OnDebug?.Invoke($"[SpawnBaseline] failed reading entityNumber at count={entityCount}");
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
                        OnDebug?.Invoke($"[SpawnBaseline] failed reading entityType at entity={entityNumber}");
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
                OnDebug?.Invoke($"[SpawnBaseline] entities={entityCount}, baselineCount={baselineCount}");
                for (uint ei = 0; ei < baselineCount; ei++)
                    ParseDeltaFields(DeltaDefinitions.EntityState, data, size, ref bitIdx);

                offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
                OnDebug?.Invoke($"[SpawnBaseline] done, totalBits={bitIdx}, newOffset={offset}");
            }
            else if (dataType == (byte)ServerMessageType.Time)
            {
                float time = BitConverter.ToSingle(data, offset);
                offset += 4;
                OnDebug?.Invoke($"[Time] time={time:F2}");
            }
            else if (dataType == (byte)ServerMessageType.LightStyle)
            {
                if (offset + 1 > size) { OnDebug?.Invoke("[LightStyle] buffer overflow"); return SessionState.Connected; }
                offset += 1;
                MessageReader.ReadString(ref data, ref offset, size);
            }
            else if (dataType == (byte)ServerMessageType.SetAngle)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (offset + 2 > size) { OnDebug?.Invoke($"[SetAngle] buffer overflow at angle {i}"); return SessionState.Connected; }
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
                    OnDebug?.Invoke($"[ClientData] deltaSeq={deltaSeq}");
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
                OnDebug?.Invoke($"[ClientData] done, weaponDeltas={weaponCount}");
                offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
            }
            else if (dataType == (byte)ServerMessageType.SignOnNum)
            {
                if (offset + 1 > size) { OnDebug?.Invoke("[SignOnNum] buffer overflow"); return SessionState.Connected; }
                byte signOn = data[offset++];
                OnDebug?.Invoke($"[SignOnNum] value={signOn}");
            }
            else if (dataType == (byte)ServerMessageType.VoiceInit)
            {
                string codec = MessageReader.ReadString(ref data, ref offset, size);
                if (offset + 1 > size) { OnDebug?.Invoke("[VoiceInit] buffer overflow"); return SessionState.Connected; }
                byte quality = data[offset++];
                OnDebug?.Invoke($"[VoiceInit] codec=\"{codec}\", quality={quality}");
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
                OnDebug?.Invoke($"[Sound] channel={channel}, entity={entity}, soundNum={soundNum}, fieldMask=0x{fieldMask:X4}");
                offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
            }
            else if (dataType == (byte)ServerMessageType.Customization)
            {
                if (offset + 1 > size) { OnDebug?.Invoke("[Customization] buffer overflow at 1"); return SessionState.Connected; }
                offset += 1;
                if (offset + 1 > size) { OnDebug?.Invoke("[Customization] buffer overflow at 2"); return SessionState.Connected; }
                offset += 1;
                MessageReader.ReadString(ref data, ref offset, size);
                if (offset + 2 > size) { OnDebug?.Invoke("[Customization] buffer overflow at 3"); return SessionState.Connected; }
                offset += 2;
                if (offset + 4 > size) { OnDebug?.Invoke("[Customization] buffer overflow at 4"); return SessionState.Connected; }
                offset += 4;
                if (offset + 1 > size) { OnDebug?.Invoke("[Customization] buffer overflow at 5"); return SessionState.Connected; }
                offset += 1;
            }
            else if (dataType == (byte)ServerMessageType.Choke) { }
            else if (dataType == 'B' && offset + 2 < size && data[offset] == 'Z' && data[offset + 1] == '2' && data[offset + 2] == 0)
            {
                OnDebug?.Invoke("[BZ2] compressed data detected, skipping");
                OnPrint?.Invoke("BZ2 compressed data received (decompression not yet supported)");
                offset = size;
            }
            else
            {
                offset--;
                OnDebug?.Invoke($"[Connected] Unknown data type: 0x{dataType:X2} at offset={offset}, remaining={size - offset}");
                OnPrint?.Invoke($"Unknown data type at ProcessServerData: 0x{dataType:X2}");
                return SessionState.Connected;
            }
        }

        OnDebug?.Invoke($"[Connected] processed all {size} bytes successfully");
        return SessionState.Connected;
    }

    private async Task SendConnectedAsync(IPEndPoint ep, ClientCommandType cmd, string str, CancellationToken ct)
    {
        var ctx = _contexts[ep];
        uint srcSeq = ctx.SrcSequence++;

        var cmdBytes = new List<byte> { (byte)cmd };
        cmdBytes.AddRange(Encoding.ASCII.GetBytes(str));
        cmdBytes.Add(0);

        cmdBytes = [.. MungeBytes(cmdBytes, (int)(srcSeq & 0xFF))];

        var payload = new byte[cmdBytes.Count + MessageConstants.ConnectedHeadSize];
        BitConverter.GetBytes(srcSeq).CopyTo(payload, 0);
        BitConverter.GetBytes(ctx.DstSequence & MessageConstants.SequenceMask).CopyTo(payload, 4);
        cmdBytes.CopyTo(payload, MessageConstants.ConnectedHeadSize);

        OnDebug?.Invoke($"[SendConnected] cmd={cmd}(0x{(byte)cmd:X2}), str=\"{str}\", srcSeq={srcSeq}, dstSeq={ctx.DstSequence}, mungeKey={srcSeq & 0xFF}, totalLen={payload.Length}");
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
    }
}

public sealed class NoSteamAuthProvider : ISteamAuthProvider
{
    public bool IsAvailable => false;
    public byte GetAuthProtocol() => 3;
    public string GetRawAuthData() => "steam";
}
