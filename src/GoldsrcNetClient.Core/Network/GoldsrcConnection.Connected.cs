using GoldsrcNetClient.Core.Delta;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Munge;
using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.Core.Util;
using Microsoft.Extensions.Logging;
using System.Net;

namespace GoldsrcNetClient.Core.Network;

public partial class GoldsrcConnection
{
    private SessionState ProcessConnected(IPEndPoint ep, ref uint srcSequence, ref uint dstSequence,
        byte[] data, int size)
    {
        var ctx = _contexts[ep];

        var reader = new MessageReader(data, size);
        Logger.LogDebug($"[Connected] processing {size} bytes, srcSeq={srcSequence}, dstSeq={dstSequence}");

        while (reader.Remaining > 0)
        {
            byte dataType = reader.Data[reader.Offset++];
            int dataLen = reader.Remaining;
            string typeName = Enum.IsDefined(typeof(ServerMessageType), dataType) ? ((ServerMessageType)dataType).ToString() : $"0x{dataType:X2}";
            Logger.LogDebug($"[Connected] type={typeName} (0x{dataType:X2}), remaining={dataLen}");

            if (_messageHandler.HandleMessage(this, dataType, reader))
                continue;

            if (dataType == (byte)ServerMessageType.Nop) { }
            else if (dataType == (byte)ServerMessageType.Bad)
            {
                Logger.LogWarning("[Bad] server sent bad message, consuming remaining data");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.Disconnect)
            {
                string reason = reader.ReadString();
                Logger.LogWarning($"[Disconnect] server disconnected: reason=\"{reason}\"");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.Print)
            {
                string msg = reader.ReadString();
                Logger.LogDebug($"[Print] msg=\"{msg[..Math.Min(msg.Length, 200)]}\"");
                Logger.LogInformation(msg);
            }
            else if (dataType == (byte)ServerMessageType.CenterPrint)
            {
                string centerMsg = reader.ReadString();
                Logger.LogDebug($"[CenterPrint] msg=\"{centerMsg}\"");
            }
            else if (dataType == (byte)ServerMessageType.ServerInfo)
            {
                if (!HandleServerInfo(ctx, reader)) return SessionState.Connected;
            }
            else if (dataType == (byte)ServerMessageType.DeltaDescription)
            {
                if (!HandleDeltaDescription(reader)) return SessionState.Connected;
            }
            else if (dataType == (byte)ServerMessageType.NewMoveVars)
            {
                if (!HandleNewMoveVars(reader)) return SessionState.Connected;
            }
            else if (dataType == (byte)ServerMessageType.SetView)
            {
                if (reader.Offset + 2 > reader.Size) { Logger.LogWarning("[SetView] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 2;
            }
            else if (dataType == (byte)ServerMessageType.NewUserMsg)
            {
                if (!HandleNewUserMsg(reader)) return SessionState.Connected;
            }
            else if (dataType == (byte)ServerMessageType.StuffText)
            {
                string st = reader.ReadString();
                Logger.LogDebug($"[StuffText] text=\"{st[..Math.Min(st.Length, 200)]}\"");
            }
            else if (dataType == (byte)ServerMessageType.UpdateUserInfo)
            {
                if (!HandleUpdateUserInfo(reader)) return SessionState.Connected;
            }
            else if (dataType == (byte)ServerMessageType.ResourceRequest)
            {
                if (!HandleResourceRequest(ctx, reader)) return SessionState.Connected;
            }
            else if (dataType == (byte)ServerMessageType.ResourceLocation)
            {
                string loc = reader.ReadString();
                Logger.LogDebug($"[ResourceLocation] location=\"{loc}\"");
            }
            else if (dataType == (byte)ServerMessageType.ResourceList)
            {
                int listStart = reader.Offset;
                ProcessResourceList(ctx, reader);
                int dataBytes = reader.Offset - listStart;
                ctx.ResourceListRawBytes = reader.Data[listStart..reader.Offset];
                Logger.LogDebug($"[ResourceList] count={ctx.Resources.Length}, dataBytes={dataBytes}");
                OnResourceList?.Invoke(this, ctx.Resources);
            }
            else if (dataType == (byte)ServerMessageType.TempEntity)
            {
                if (reader.Offset + 1 > reader.Size) { Logger.LogWarning("[TempEntity] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 1;
                for (int i = 0; i < 3; i++)
                {
                    if (reader.Offset + 2 > reader.Size) { Logger.LogWarning($"[TempEntity] buffer overflow at coord {i}"); return SessionState.Connected; }
                    reader.Offset += 2;
                }
            }
            else if (dataType == (byte)ServerMessageType.SpawnStaticSound)
            {
                reader.Offset += 14;
            }
            else if (dataType == (byte)ServerMessageType.SendCvarValue2)
            {
                if (!HandleSendCvarValue2(reader)) return SessionState.Connected;
            }
            else if (dataType == (byte)ServerMessageType.SendCvarValue)
            {
                if (!HandleSendCvarValue(reader)) return SessionState.Connected;
            }
            else if (dataType == (byte)ServerMessageType.SpawnBaseline)
            {
                if (!HandleSpawnBaseline(ctx, reader)) return SessionState.Connected;
            }
            else if (dataType == (byte)ServerMessageType.Time)
            {
                reader.ReadSingle(out float time);
                Logger.LogDebug($"[Time] time={time:F2}");
            }
            else if (dataType == (byte)ServerMessageType.LightStyle)
            {
                if (reader.Offset + 1 > reader.Size) { Logger.LogWarning("[LightStyle] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 1;
                reader.ReadString();
            }
            else if (dataType == (byte)ServerMessageType.SetAngle)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (reader.Offset + 2 > reader.Size) { Logger.LogWarning($"[SetAngle] buffer overflow at angle {i}"); return SessionState.Connected; }
                    reader.Offset += 2;
                }
            }
            else if (dataType == (byte)ServerMessageType.ClientData)
            {
                if (!HandleClientData(reader)) return SessionState.Connected;
            }
            else if (dataType == (byte)ServerMessageType.SignOnNum)
            {
                HandleSignOnNum(reader);
            }
            else if (dataType == (byte)ServerMessageType.VoiceInit)
            {
                HandleVoiceInit(reader);
            }
            else if (dataType == (byte)ServerMessageType.Sound)
            {
                if (!HandleSound(reader)) return SessionState.Connected;
            }
            else if (dataType == (byte)ServerMessageType.Customization)
            {
                if (!HandleCustomization(reader)) return SessionState.Connected;
            }
            else if (dataType == (byte)ServerMessageType.Choke) { }
            else if (dataType == (byte)ServerMessageType.Event)
            {
                Logger.LogDebug("[Event] game event received, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.Version)
            {
                if (reader.Offset + 4 > reader.Size) { Logger.LogWarning("[Version] buffer overflow"); return SessionState.Connected; }
                uint version = reader.ReadUInt32();
                Logger.LogDebug($"[Version] protocol={version}");
            }
            else if (dataType == (byte)ServerMessageType.StopSound)
            {
                if (reader.Offset + 2 > reader.Size) { Logger.LogWarning("[StopSound] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 2;
            }
            else if (dataType == (byte)ServerMessageType.Pings)
            {
                HandlePings(reader);
            }
            else if (dataType == (byte)ServerMessageType.Particle)
            {
                Logger.LogDebug("[Particle] particle effect, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.Damage)
            {
                if (reader.Offset + 8 > reader.Size) { Logger.LogWarning("[Damage] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 8;
                if (reader.Offset + 3 > reader.Size) { Logger.LogWarning("[Damage] buffer overflow at coords"); return SessionState.Connected; }
                reader.Offset += 3;
            }
            else if (dataType == (byte)ServerMessageType.SpawnStatic)
            {
                Logger.LogDebug("[SpawnStatic] static entity, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.EventReliable)
            {
                Logger.LogDebug("[EventReliable] reliable event, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.SetPause)
            {
                uint paused = 0;
                int bitIdx = 0;
                BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref paused, 1);
                reader.Offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
                Logger.LogDebug($"[SetPause] paused={paused}");
            }
            else if (dataType == (byte)ServerMessageType.KilledMonster) { }
            else if (dataType == (byte)ServerMessageType.FoundSecret) { }
            else if (dataType == (byte)ServerMessageType.Intermission)
            {
                Logger.LogDebug("[Intermission] intermission started");
            }
            else if (dataType == (byte)ServerMessageType.Finale)
            {
                string finaleStr = reader.ReadString();
                Logger.LogDebug($"[Finale] text=\"{finaleStr}\"");
            }
            else if (dataType == (byte)ServerMessageType.CdTrack)
            {
                if (reader.Offset + 2 > reader.Size) { Logger.LogWarning("[CdTrack] buffer overflow"); return SessionState.Connected; }
                byte track = reader.ReadByte();
                byte loopTrack = reader.ReadByte();
                Logger.LogDebug($"[CdTrack] track={track}, loop={loopTrack}");
            }
            else if (dataType == (byte)ServerMessageType.Restore)
            {
                Logger.LogDebug("[Restore] restore game state, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.Cutscene)
            {
                string cutscene = reader.ReadString();
                Logger.LogDebug($"[Cutscene] name=\"{cutscene}\"");
            }
            else if (dataType == (byte)ServerMessageType.WeaponAnim)
            {
                if (reader.Offset + 2 > reader.Size) { Logger.LogWarning("[WeaponAnim] buffer overflow"); return SessionState.Connected; }
                byte anim = reader.ReadByte();
                byte body = reader.ReadByte();
                Logger.LogDebug($"[WeaponAnim] anim={anim}, body={body}");
            }
            else if (dataType == (byte)ServerMessageType.DecalName)
            {
                if (reader.Offset + 2 > reader.Size) { Logger.LogWarning("[DecalName] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 2;
            }
            else if (dataType == (byte)ServerMessageType.RoomType)
            {
                if (reader.Offset + 2 > reader.Size) { Logger.LogWarning("[RoomType] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 2;
            }
            else if (dataType == (byte)ServerMessageType.AddAngle)
            {
                if (reader.Offset + 2 > reader.Size) { Logger.LogWarning("[AddAngle] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 2;
            }
            else if (dataType == (byte)ServerMessageType.PacketEntities)
            {
                Logger.LogDebug("[PacketEntities] full entity packet, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.DeltaPacketEntities)
            {
                Logger.LogDebug("[DeltaPacketEntities] delta entity packet, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.CrosshairAngle)
            {
                if (reader.Offset + 2 > reader.Size) { Logger.LogWarning("[CrosshairAngle] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 2;
            }
            else if (dataType == (byte)ServerMessageType.SoundFade)
            {
                if (reader.Offset + 4 > reader.Size) { Logger.LogWarning("[SoundFade] buffer overflow"); return SessionState.Connected; }
                reader.Offset += 4;
            }
            else if (dataType == (byte)ServerMessageType.FileTxferFailed)
            {
                string failName = reader.ReadString();
                Logger.LogDebug($"[FileTxferFailed] file=\"{failName}\"");
            }
            else if (dataType == (byte)ServerMessageType.Hltv)
            {
                Logger.LogDebug("[Hltv] HLTV data, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.Director)
            {
                Logger.LogDebug("[Director] director command, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.VoiceData)
            {
                Logger.LogDebug("[VoiceData] voice data, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.SendExtraInfo)
            {
                Logger.LogDebug("[SendExtraInfo] extra info, skipping");
                reader.Offset = reader.Size;
            }
            else if (dataType == (byte)ServerMessageType.TimeScale)
            {
                reader.ReadSingle(out float timeScale);
                Logger.LogDebug($"[TimeScale] scale={timeScale:F2}");
            }
            else if (dataType == (byte)ServerMessageType.Exec)
            {
                string execCmd = reader.ReadString();
                Logger.LogDebug($"[Exec] cmd=\"{execCmd}\"");
            }
            else if (dataType == 'B' && reader.Offset + 2 < reader.Size && reader.Data[reader.Offset] == 'Z' && reader.Data[reader.Offset + 1] == '2' && reader.Data[reader.Offset + 2] == 0)
            {
                Logger.LogWarning("[BZ2] compressed data detected, skipping");
                Logger.LogWarning("BZ2 compressed data received (decompression not yet supported)");
                reader.Offset = reader.Size;
            }
            else if (ScanForBz2(reader.Data, reader.Offset, reader.Size))
            {
                Logger.LogWarning("[BZ2] compressed data detected at offset, skipping");
                reader.Offset = reader.Size;
            }
            else
            {
                reader.Offset--;
                Logger.LogWarning($"[Connected] Unknown data type: 0x{dataType:X2} at offset={reader.Offset}, remaining={reader.Remaining}");
                return SessionState.Connected;
            }
        }

        Logger.LogDebug($"[Connected] processed all {reader.Size} bytes successfully");
        return SessionState.Connected;
    }

    // --- Individual message handlers extracted for clarity ---

    private bool HandleServerInfo(ConnectionContext ctx, MessageReader reader)
    {
        int structSize = 33;
        if (reader.Offset + structSize > reader.Size)
        {
            Logger.LogWarning($"[ServerInfo] buffer overflow: offset={reader.Offset}, need={structSize}, size={reader.Size}");
            return false;
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
        Logger.LogDebug($"[ServerInfo] proto={si.ProtocolVersion}, spawnCount={si.SpawnCount}, maxClients={si.MaxClients}, playerNum={si.PlayerNumber}, worldmapCrcRaw=0x{si.Munge3WorldmapCrc:X8}, unmungeSeq={unmungeSeq}");
        MungeEngine.UnMunge3(crcBytes, 4, (int)((-1 - ctx.PlayerNumber) & 0xFF));
        ctx.WorldmapCrc = BitConverter.ToUInt32(crcBytes);
        Logger.LogDebug($"[ServerInfo] worldmapCrcUnMunaged=0x{ctx.WorldmapCrc:X8}");

        for (int i = 0; i < 4; i++)
            reader.ReadString();

        if (reader.Offset + 1 <= reader.Size)
            reader.Offset++;

        OnServerInfo?.Invoke(this, si);

        if (!_sentContinueLoading)
        {
            _sentContinueLoading = true;
            Logger.LogDebug("[SignOn] sending sendres");
            _ = SendStringCmdAsync(ClientCommandType.StringCmd, "sendres", CancellationToken.None);
        }

        return true;
    }

    private bool HandleDeltaDescription(MessageReader reader)
    {
        string name = reader.ReadString();
        Logger.LogDebug($"[DeltaDescription] deltaName=\"{name}\"");
        var dt = DeltaDefinitions.Find(name);
        if (dt == null)
        {
            Logger.LogWarning($"[DeltaDescription] unknown delta \"{name}\", skipping");
            return false;
        }

        int bitIdx = 0;
        uint fieldCount = 0;
        if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref fieldCount, 16))
        {
            Logger.LogWarning($"[DeltaDescription] failed reading fieldCount");
            return false;
        }
        Logger.LogDebug($"[DeltaDescription] fieldCount={fieldCount}");

        for (uint f = 0; f < fieldCount; f++)
        {
            int parseBitIdx = 0;
            if (!ParseDeltaFieldDescriptions(reader.Data, reader.Size, ref parseBitIdx))
            {
                Logger.LogWarning($"[DeltaDescription] failed parsing field {f}");
                return false;
            }
        }

        reader.Offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
        Logger.LogDebug($"[DeltaDescription] done, new offset={reader.Offset}");
        return true;
    }

    private bool HandleNewMoveVars(MessageReader reader)
    {
        int mvSize;
        unsafe { mvSize = sizeof(NewMoveVarsData); }
        if (reader.Offset + mvSize > reader.Size)
        {
            Logger.LogWarning($"[NewMoveVars] buffer overflow: offset={reader.Offset}, need={mvSize}, size={reader.Size}");
            return false;
        }

        unsafe
        {
            fixed (byte* p = &reader.Data[reader.Offset])
                _ = *(NewMoveVarsData*)p;
            reader.Offset += mvSize;
        }

        reader.ReadString();
        Logger.LogDebug($"[NewMoveVars] done, structSize={mvSize}");
        return true;
    }

    private bool HandleNewUserMsg(MessageReader reader)
    {
        int msgSize;
        unsafe { msgSize = sizeof(NewUserMsgData); }
        if (reader.Offset + msgSize > reader.Size)
        {
            Logger.LogWarning($"[NewUserMsg] buffer overflow: offset={reader.Offset}, need={msgSize}, size={reader.Size}");
            return false;
        }

        unsafe
        {
            fixed (byte* p = &reader.Data[reader.Offset])
                _ = *(NewUserMsgData*)p;
            reader.Offset += msgSize;
        }
        Logger.LogDebug($"[NewUserMsg] done, structSize={msgSize}");
        return true;
    }

    private bool HandleUpdateUserInfo(MessageReader reader)
    {
        if (reader.Offset + 1 > reader.Size) { Logger.LogWarning("[UpdateUserInfo] buffer overflow at byte 1"); return false; }
        reader.Offset += 1;
        if (reader.Offset + 4 > reader.Size) { Logger.LogWarning("[UpdateUserInfo] buffer overflow at byte 4"); return false; }
        reader.Offset += 4;
        string uui = reader.ReadString();
        UserInfo = uui;
        Logger.LogDebug($"[UpdateUserInfo] userInfo=\"{uui[..Math.Min(uui.Length, 100)]}\"");
        if (reader.Offset + 16 > reader.Size) { Logger.LogWarning("[UpdateUserInfo] buffer overflow at 16"); return false; }
        reader.Offset += 16;
        return true;
    }

    private bool HandleResourceRequest(ConnectionContext ctx, MessageReader reader)
    {
        if (reader.Offset + 4 > reader.Size) { Logger.LogWarning("[ResourceRequest] buffer overflow"); return false; }
        ctx.SpawnCount = reader.ReadUInt32();
        if (reader.Offset + 4 > reader.Size) { Logger.LogWarning("[ResourceRequest] buffer overflow after spawnCount"); return false; }
        uint resUnknown = reader.ReadUInt32();
        Logger.LogDebug($"[ResourceRequest] spawnCount={ctx.SpawnCount}, unknown={resUnknown}");
        return true;
    }

    private bool HandleSendCvarValue2(MessageReader reader)
    {
        if (reader.Offset + 4 > reader.Size) { Logger.LogWarning("[SendCvarValue2] buffer overflow"); return false; }
        uint requestId = reader.ReadUInt32();
        string cvarName = reader.ReadString();
        Logger.LogDebug($"[SendCvarValue2] requestId={requestId}, cvar=\"{cvarName}\"");
        var reply = new List<byte>();
        MessageWriter.WriteUInt32(reply, requestId);
        MessageWriter.WriteString(reply, cvarName);
        MessageWriter.WriteString(reply, Settings.GetDefaultCvarValue(cvarName));
        _ = SendCommandAsync(ClientCommandType.CvarValue2, reply.ToArray(), CancellationToken.None);
        return true;
    }

    private bool HandleSendCvarValue(MessageReader reader)
    {
        string cvarName = reader.ReadString();
        Logger.LogDebug($"[SendCvarValue] cvar=\"{cvarName}\"");
        var reply = new List<byte>();
        MessageWriter.WriteString(reply, Settings.GetDefaultCvarValue(cvarName));
        _ = SendCommandAsync(ClientCommandType.CvarValue, reply.ToArray(), CancellationToken.None);
        return true;
    }

    private bool HandleSpawnBaseline(ConnectionContext ctx, MessageReader reader)
    {
        int bitIdx = 0;
        int entityCount = 0;
        while (true)
        {
            uint entityNumber = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref entityNumber, 11))
            {
                Logger.LogWarning($"[SpawnBaseline] failed reading entityNumber at count={entityCount}");
                return false;
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
                Logger.LogWarning($"[SpawnBaseline] failed reading entityType at entity={entityNumber}");
                return false;
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
        Logger.LogDebug($"[SpawnBaseline] entities={entityCount}, baselineCount={baselineCount}");
        for (uint ei = 0; ei < baselineCount; ei++)
            ParseDeltaFields(DeltaDefinitions.EntityState, reader.Data, reader.Size, ref bitIdx);

        reader.Offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
        Logger.LogDebug($"[SpawnBaseline] done, totalBits={bitIdx}, newOffset={reader.Offset}");

        if (!_sentSpawn && _sentContinueLoading)
        {
            _sentSpawn = true;
            uint spawnCount = ctx.SpawnCount;
            int rawCrc = (int)ctx.WorldmapCrc;
            byte[] crcBytes = BitConverter.GetBytes(rawCrc);
            int mungeKey = ~(int)spawnCount;
            Logger.LogDebug($"[SignOn] spawn: rawCrc=0x{rawCrc:X8}, mungeKey=0x{mungeKey:X8} (spawnCount={spawnCount})");
            MungeEngine.Munge2(crcBytes, 4, mungeKey);
            int mungedCrc = BitConverter.ToInt32(crcBytes, 0);
            var spawnCmd = $"spawn {spawnCount} {mungedCrc}";
            Logger.LogDebug($"[SignOn] sending spawn (spawnCount={spawnCount}, mungedCrc=0x{mungedCrc:X8})");
            _ = SendStringCmdAsync(ClientCommandType.StringCmd, spawnCmd, CancellationToken.None);
        }

        return true;
    }

    private bool HandleClientData(MessageReader reader)
    {
        int bitIdx = 0;
        uint haveDeltaSeq = 0;
        if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref haveDeltaSeq, 1)) return false;
        if (haveDeltaSeq != 0)
        {
            uint deltaSeq = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref deltaSeq, 8)) return false;
            Logger.LogDebug($"[ClientData] deltaSeq={deltaSeq}");
        }
        ParseDeltaFields(DeltaDefinitions.ClientData, reader.Data, reader.Size, ref bitIdx);

        int weaponCount = 0;
        while (true)
        {
            uint haveDelta = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref haveDelta, 1)) return false;
            if (haveDelta == 0) break;
            uint index = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref index, 6)) return false;
            ParseDeltaFields(DeltaDefinitions.WeaponData, reader.Data, reader.Size, ref bitIdx);
            weaponCount++;
        }
        Logger.LogDebug($"[ClientData] done, weaponDeltas={weaponCount}");
        reader.Offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
        return true;
    }

    private void HandleSignOnNum(MessageReader reader)
    {
        if (reader.Offset + 1 > reader.Size) { Logger.LogWarning("[SignOnNum] buffer overflow"); return; }
        byte signOn = reader.Data[reader.Offset++];
        Logger.LogDebug($"[SignOnNum] value={signOn}");
        if (signOn == 1)
        {
            Logger.LogInformation("[SignOn] signon=1 received, signon sequence complete. Sending sendents.");
            Logger.LogDebug("[SignOn] sending sendents (final handshake step)");
            _ = SendStringCmdAsync(ClientCommandType.StringCmd, "sendents", CancellationToken.None);
        }
        else
        {
            Logger.LogDebug($"[SignOn] signon={signOn} received (not 1, no action)");
        }
    }

    private void HandleVoiceInit(MessageReader reader)
    {
        string codec = reader.ReadString();
        if (reader.Offset + 1 > reader.Size) { Logger.LogWarning("[VoiceInit] buffer overflow"); return; }
        byte quality = reader.Data[reader.Offset++];
        Logger.LogDebug($"[VoiceInit] codec=\"{codec}\", quality={quality}");
    }

    private bool HandleSound(MessageReader reader)
    {
        int bitIdx = 0;
        uint fieldMask = 0;
        if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref fieldMask, 9)) return false;

        if ((fieldMask & SoundFlags.Volume) != 0)
        {
            uint vol = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref vol, 8)) return false;
        }
        if ((fieldMask & SoundFlags.Attenuation) != 0)
        {
            uint attn = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref attn, 8)) return false;
        }

        uint channel = 0;
        if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref channel, 3)) return false;
        uint entity = 0;
        if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref entity, 11)) return false;
        uint soundNum = 0;
        int snBits = (fieldMask & SoundFlags.LargeIndex) != 0 ? 16 : 8;
        if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref soundNum, snBits)) return false;

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
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref pitch, 8)) return false;
        }
        Logger.LogDebug($"[Sound] channel={channel}, entity={entity}, soundNum={soundNum}, fieldMask=0x{fieldMask:X4}");
        reader.Offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
        return true;
    }

    private bool HandleCustomization(MessageReader reader)
    {
        if (reader.Offset + 1 > reader.Size) { Logger.LogWarning("[Customization] buffer overflow at 1"); return false; }
        byte playerSlot = reader.Data[reader.Offset++];
        if (reader.Offset + 1 > reader.Size) { Logger.LogWarning("[Customization] buffer overflow at 2"); return false; }
        byte resourceType = reader.Data[reader.Offset++];
        string resourceName = reader.ReadString();
        if (reader.Offset + 2 > reader.Size) { Logger.LogWarning("[Customization] buffer overflow at 3"); return false; }
        reader.Offset += 2;
        if (reader.Offset + 4 > reader.Size) { Logger.LogWarning("[Customization] buffer overflow at 4"); return false; }
        reader.Offset += 4;
        if (reader.Offset + 1 > reader.Size) { Logger.LogWarning("[Customization] buffer overflow at 5"); return false; }
        reader.Offset += 1;
        Logger.LogDebug($"[Customization] player={playerSlot}, type={resourceType}, name=\"{resourceName}\"");
        return true;
    }

    private void HandlePings(MessageReader reader)
    {
        int bitIdx = 0;
        for (int i = 0; i < 32; i++)
        {
            uint hasEntry = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref hasEntry, 1))
                break;
            if (hasEntry == 0) break;
            uint slot = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref slot, 5)) return;
            uint ping = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref ping, 12)) return;
            uint loss = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref loss, 7)) return;
        }
        reader.Offset += bitIdx / 8 + (bitIdx % 8 != 0 ? 1 : 0);
    }
}
