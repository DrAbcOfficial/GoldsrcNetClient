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
    /// <summary>Parses one server message; returning false aborts the rest of the packet (buffer overflow).</summary>
    internal delegate bool MessageParser(ConnectionContext ctx, MessageReader reader);

    private void ProcessConnected(IPEndPoint ep, byte[] data)
    {
        var ctx = _sessions[ep].Context;

        var reader = new MessageReader(data, data.Length);
        Logger.LogDebug($"[Connected] processing {data.Length} bytes");
        if (data.Length > 16)
            Logger.LogDebug($"[Connected] head: {Convert.ToHexString(data.AsSpan(0, Math.Min(data.Length, 48)).ToArray())}");

        while (reader.Remaining > 0)
        {
            byte dataType = reader.Data[reader.Offset++];
            int dataLen = reader.Remaining;
            string typeName = Enum.IsDefined(typeof(ServerMessageType), dataType) ? ((ServerMessageType)dataType).ToString() : $"0x{dataType:X2}";
            Logger.LogDebug($"[Connected] type={typeName} (0x{dataType:X2}), remaining={dataLen}");

            if (_messageHandler.HandleMessage(this, dataType, reader))
                continue;

            if (!_messageParsers.TryGetValue(dataType, out var parser))
            {
                reader.Offset--;
                Logger.LogWarning($"[Connected] Unknown data type: 0x{dataType:X2} at offset={reader.Offset}, remaining={reader.Remaining}");
                return;
            }

            if (!parser(ctx, reader))
                return;
        }

        Logger.LogDebug($"[Connected] processed all {reader.Size} bytes successfully");
    }

    /// <summary>
    /// Builds the server-message dispatch table. Each entry parses exactly one
    /// message; a <c>false</c> return discards the remainder of the packet.
    /// </summary>
    private Dictionary<byte, MessageParser> BuildMessageParsers()
    {
        // The Sven Co-op engine branch repurposes the legacy svc_foundsecret slot as a
        // string-carrying message (SV_New_f writes it before the serverinfo banner);
        // on Valve branches the slot is unused and carries no payload.
        var foundSecret = _useLongFragmentFields
            ? (MessageParser)((_, reader) => { reader.ReadString(); return true; })
            : (_, _) => true;

        return new()
        {
        [(byte)ServerMessageType.Nop] = (_, _) => true,
        [(byte)ServerMessageType.Choke] = (_, _) => true,
        [(byte)ServerMessageType.KilledMonster] = (_, _) => true,
        [(byte)ServerMessageType.FoundSecret] = foundSecret,

        [(byte)ServerMessageType.Bad] = (_, reader) =>
        {
            Logger.LogWarning("[Bad] server sent bad message, consuming remaining data");
            reader.Offset = reader.Size;
            return true;
        },

        [(byte)ServerMessageType.Disconnect] = (_, reader) =>
        {
            string reason = reader.ReadString();
            Logger.LogWarning($"[Disconnect] server disconnected: reason=\"{reason}\"");
            reader.Offset = reader.Size;
            OnServerDisconnect?.Invoke(reason);
            return true;
        },

        [(byte)ServerMessageType.Print] = (_, reader) =>
        {
            string msg = reader.ReadString();
            Logger.LogDebug($"[Print] msg=\"{msg[..Math.Min(msg.Length, 200)]}\"");
            OnConsolePrint?.Invoke(msg);
            return true;
        },

        [(byte)ServerMessageType.CenterPrint] = (_, reader) =>
        {
            string centerMsg = reader.ReadString();
            Logger.LogDebug($"[CenterPrint] msg=\"{centerMsg}\"");
            OnCenterPrint?.Invoke(centerMsg);
            return true;
        },

        [(byte)ServerMessageType.StuffText] = (_, reader) =>
        {
            string st = reader.ReadString();
            Logger.LogDebug($"[StuffText] text=\"{st[..Math.Min(st.Length, 200)]}\"");
            return true;
        },

        [(byte)ServerMessageType.ServerInfo] = (ctx, reader) => HandleServerInfo(ctx, reader),
        [(byte)ServerMessageType.DeltaDescription] = (_, reader) => HandleDeltaDescription(reader),
        [(byte)ServerMessageType.NewMoveVars] = (_, reader) => HandleNewMoveVars(reader),
        [(byte)ServerMessageType.NewUserMsg] = (_, reader) => HandleNewUserMsg(reader),
        [(byte)ServerMessageType.UpdateUserInfo] = (ctx, reader) => HandleUpdateUserInfo(ctx, reader),
        [(byte)ServerMessageType.ResourceRequest] = (ctx, reader) => HandleResourceRequest(ctx, reader),
        [(byte)ServerMessageType.SpawnBaseline] = (ctx, reader) => HandleSpawnBaseline(ctx, reader),
        [(byte)ServerMessageType.ClientData] = (_, reader) => HandleClientData(reader),
        [(byte)ServerMessageType.SignOnNum] = (_, reader) => HandleSignOnNum(reader),
        [(byte)ServerMessageType.VoiceInit] = (_, reader) => HandleVoiceInit(reader),
        [(byte)ServerMessageType.Sound] = (_, reader) => HandleSound(reader),
        [(byte)ServerMessageType.Customization] = (_, reader) => HandleCustomization(reader),
        [(byte)ServerMessageType.Event] = (_, reader) => HandleEvent(reader, reliable: false),
        [(byte)ServerMessageType.EventReliable] = (_, reader) => HandleEvent(reader, reliable: true),
        [(byte)ServerMessageType.Pings] = (_, reader) => HandlePings(reader),
        [(byte)ServerMessageType.SendCvarValue] = (_, reader) => HandleSendCvarValue(reader),
        [(byte)ServerMessageType.SendCvarValue2] = (_, reader) => HandleSendCvarValue2(reader),

        [(byte)ServerMessageType.SetView] = (_, reader) => CheckedSkip(reader, "SetView", 2),
        [(byte)ServerMessageType.StopSound] = (_, reader) => CheckedSkip(reader, "StopSound", 2),
        [(byte)ServerMessageType.SetAngle] = (_, reader) => CheckedSkip(reader, "SetAngle", 2, 2, 2),
        [(byte)ServerMessageType.AddAngle] = (_, reader) => CheckedSkip(reader, "AddAngle", 2),
        [(byte)ServerMessageType.DecalName] = (_, reader) => CheckedSkip(reader, "DecalName", 2),
        [(byte)ServerMessageType.RoomType] = (_, reader) => CheckedSkip(reader, "RoomType", 2),
        [(byte)ServerMessageType.CrosshairAngle] = (_, reader) => CheckedSkip(reader, "CrosshairAngle", 2),
        [(byte)ServerMessageType.SoundFade] = (_, reader) => CheckedSkip(reader, "SoundFade", 4),
        [(byte)ServerMessageType.TempEntity] = (_, reader) => CheckedSkip(reader, "TempEntity", 1, 2, 2, 2),
        [(byte)ServerMessageType.Damage] = (_, reader) => CheckedSkip(reader, "Damage", 8, 3),
        [(byte)ServerMessageType.SpawnStaticSound] = (_, reader) => CheckedSkip(reader, "SpawnStaticSound", 14),

        [(byte)ServerMessageType.Version] = (_, reader) =>
        {
            if (!CheckedSkip(reader, "Version", 4)) return false;
            uint version = BitConverter.ToUInt32(reader.Data, reader.Offset - 4);
            Logger.LogDebug($"[Version] protocol={version}");
            return true;
        },

        [(byte)ServerMessageType.Time] = (_, reader) =>
        {
            reader.ReadSingle(out float time);
            Logger.LogDebug($"[Time] time={time:F2}");
            return true;
        },

        [(byte)ServerMessageType.TimeScale] = (_, reader) =>
        {
            reader.ReadSingle(out float timeScale);
            Logger.LogDebug($"[TimeScale] scale={timeScale:F2}");
            return true;
        },

        [(byte)ServerMessageType.LightStyle] = (_, reader) =>
        {
            if (!CheckedSkip(reader, "LightStyle", 1)) return false;
            reader.ReadString();
            return true;
        },

        [(byte)ServerMessageType.ResourceLocation] = (_, reader) =>
        {
            string loc = reader.ReadString();
            Logger.LogDebug($"[ResourceLocation] location=\"{loc}\"");
            return true;
        },

        [(byte)ServerMessageType.ResourceList] = (ctx, reader) =>
        {
            int listStart = reader.Offset;
            ProcessResourceList(ctx, reader);
            ctx.ResourceListRawBytes = reader.Data[listStart..reader.Offset];
            Logger.LogDebug($"[ResourceList] count={ctx.Resources.Length}, dataBytes={reader.Offset - listStart}");
            OnResourceList?.Invoke(this, ctx.Resources);

            // Resource parsing completes the client's loading phase — request
            // the spawn now (the engine sends it at the end of resource processing).
            TrySendSpawn(ctx);
            return true;
        },

        [(byte)ServerMessageType.Particle] = (_, reader) =>
        {
            Logger.LogDebug("[Particle] particle effect, skipping");
            reader.Offset = reader.Size;
            return true;
        },

        [(byte)ServerMessageType.SpawnStatic] = (_, reader) =>
        {
            Logger.LogDebug("[SpawnStatic] static entity, skipping");
            reader.Offset = reader.Size;
            return true;
        },

        [(byte)ServerMessageType.Restore] = (_, reader) =>
        {
            Logger.LogDebug("[Restore] restore game state, skipping");
            reader.Offset = reader.Size;
            return true;
        },

        [(byte)ServerMessageType.PacketEntities] = (_, reader) =>
        {
            Logger.LogDebug("[PacketEntities] full entity packet, skipping");
            reader.Offset = reader.Size;
            return true;
        },

        [(byte)ServerMessageType.DeltaPacketEntities] = (_, reader) =>
        {
            Logger.LogDebug("[DeltaPacketEntities] delta entity packet, skipping");
            reader.Offset = reader.Size;
            return true;
        },

        [(byte)ServerMessageType.Hltv] = (_, reader) =>
        {
            Logger.LogDebug("[Hltv] HLTV data, skipping");
            reader.Offset = reader.Size;
            return true;
        },

        [(byte)ServerMessageType.Director] = (_, reader) =>
        {
            Logger.LogDebug("[Director] director command, skipping");
            reader.Offset = reader.Size;
            return true;
        },

        [(byte)ServerMessageType.VoiceData] = (_, reader) =>
        {
            Logger.LogDebug("[VoiceData] voice data, skipping");
            reader.Offset = reader.Size;
            return true;
        },

        [(byte)ServerMessageType.Intermission] = (_, _) =>
        {
            Logger.LogDebug("[Intermission] intermission started");
            return true;
        },

        [(byte)ServerMessageType.Finale] = (_, reader) =>
        {
            string finaleStr = reader.ReadString();
            Logger.LogDebug($"[Finale] text=\"{finaleStr}\"");
            return true;
        },

        [(byte)ServerMessageType.Cutscene] = (_, reader) =>
        {
            string cutscene = reader.ReadString();
            Logger.LogDebug($"[Cutscene] name=\"{cutscene}\"");
            return true;
        },

        [(byte)ServerMessageType.FileTxferFailed] = (_, reader) =>
        {
            string failName = reader.ReadString();
            Logger.LogDebug($"[FileTxferFailed] file=\"{failName}\"");
            return true;
        },

        [(byte)ServerMessageType.SendExtraInfo] = (_, reader) =>
        {
            // Payload: gamedir string + one byte (sv_cheats flag).
            string extraDir = reader.ReadString();
            byte extraFlag = reader.ReadByte();
            Logger.LogDebug($"[SendExtraInfo] dir=\"{extraDir}\", svCheats={extraFlag}");
            return true;
        },

        [(byte)ServerMessageType.Exec] = (_, reader) =>
        {
            // Payload: one exec-type byte; type 1 is followed by a class number byte (TFC).
            byte execType = reader.ReadByte();
            byte execClass = 0;
            if (execType == 1)
                execClass = reader.ReadByte();
            Logger.LogDebug($"[Exec] type={execType}, class={execClass}");
            return true;
        },

        [(byte)ServerMessageType.CdTrack] = (_, reader) =>
        {
            if (!CheckedSkip(reader, "CdTrack", 2)) return false;
            byte track = reader.Data[reader.Offset - 2];
            byte loopTrack = reader.Data[reader.Offset - 1];
            Logger.LogDebug($"[CdTrack] track={track}, loop={loopTrack}");
            return true;
        },

        [(byte)ServerMessageType.WeaponAnim] = (_, reader) =>
        {
            if (!CheckedSkip(reader, "WeaponAnim", 2)) return false;
            byte anim = reader.Data[reader.Offset - 2];
            byte body = reader.Data[reader.Offset - 1];
            Logger.LogDebug($"[WeaponAnim] anim={anim}, body={body}");
            return true;
        },

        [(byte)ServerMessageType.SetPause] = (_, reader) =>
        {
            int bitIdx = reader.Offset * 8;
            uint paused = 0;
            BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref paused, 1);
            reader.Offset = (bitIdx + 7) / 8;
            Logger.LogDebug($"[SetPause] paused={paused}");
            return true;
        },
        };
    }

    /// <summary>Skips fixed-size payloads, aborting the packet when data is missing.</summary>
    private bool CheckedSkip(MessageReader reader, string name, params ReadOnlySpan<int> sizes)
    {
        for (int i = 0; i < sizes.Length; i++)
        {
            if (reader.Offset + sizes[i] > reader.Size)
            {
                Logger.LogWarning(sizes.Length == 1
                    ? $"[{name}] buffer overflow"
                    : $"[{name}] buffer overflow (part {i})");
                return false;
            }
            reader.Offset += sizes[i];
        }
        return true;
    }

    // --- Individual message handlers ---

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
        // The worldmap CRC is munged with an 8-bit key: the server uses
        // COM_Munge3(..., (-1 - playernum) & 0xFF) (ReHLDS SV_SendServerinfo,
        // Xash3D same). A wider key does not invert it and corrupts the CRC we
        // echo back in the spawn command — the server's periodic
        // SV_CheckMapDifferences then flags us as overflowed ("Reliable channel
        // overflowed") within ~5 seconds.
        int unmungeKey = (-1 - ctx.PlayerNumber) & 0xFF;
        Logger.LogDebug($"[ServerInfo] proto={si.ProtocolVersion}, spawnCount={si.SpawnCount}, maxClients={si.MaxClients}, playerNum={si.PlayerNumber}, worldmapCrcRaw=0x{si.Munge3WorldmapCrc:X8}, unmungeKey=0x{unmungeKey:X2}");
        MungeEngine.UnMunge3(crcBytes, 4, unmungeKey);
        ctx.WorldmapCrc = BitConverter.ToUInt32(crcBytes);
        Logger.LogDebug($"[ServerInfo] worldmapCrcUnMunged=0x{ctx.WorldmapCrc:X8}");

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

    private bool HandleEvent(MessageReader reader, bool reliable)
    {
        // svc_event:      5 bits count, then per event: 10 bits index,
        //                 1 bit ent-in-pack -> 11 bits packet index,
        //                 1 bit has-args -> delta event_args_t, 1 bit has-fire -> 16 bits.
        // svc_event_reliable: same per-event layout without the count and packet index.
        int bitIdx = reader.Offset * 8;
        uint count = 1;
        if (!reliable && !BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref count, 5))
            return false;

        for (uint e = 0; e < count; e++)
        {
            uint eventIndex = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref eventIndex, 10)) return false;

            if (!reliable)
            {
                uint hasEnts = 0;
                if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref hasEnts, 1)) return false;
                if (hasEnts != 0)
                {
                    uint packetIndex = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref packetIndex, 11)) return false;
                }
            }

            if (!reliable)
            {
                uint hasArgs = 0;
                if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref hasArgs, 1)) return false;
                if (hasArgs != 0 && !DeltaReader.ReadFields(DeltaDefinitions.Event, reader.Data, reader.Size, ref bitIdx))
                    return false;
            }
            else if (!DeltaReader.ReadFields(DeltaDefinitions.Event, reader.Data, reader.Size, ref bitIdx))
            {
                return false;
            }

            uint hasFire = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref hasFire, 1)) return false;
            if (hasFire != 0)
            {
                uint fireTime = 0;
                if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref fireTime, 16)) return false;
            }

            Logger.LogDebug($"[Event] index={eventIndex}{(reliable ? " (reliable)" : "")}");
        }

        reader.Offset = (bitIdx + 7) / 8;
        return true;
    }

    private bool HandleDeltaDescription(MessageReader reader)
    {
        string name = reader.ReadString();
        Logger.LogDebug($"[DeltaDescription] deltaName=\"{name}\"");

        // Field entries are themselves delta-encoded against the meta
        // delta_description_t definition. Unknown delta names are still parsed
        // field-by-field so the message stream stays in sync.
        int bitIdx = reader.Offset * 8;
        uint fieldCount = 0;
        if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref fieldCount, 16))
        {
            Logger.LogWarning("[DeltaDescription] failed reading fieldCount");
            return false;
        }
        Logger.LogDebug($"[DeltaDescription] fieldCount={fieldCount}");

        for (uint f = 0; f < fieldCount; f++)
        {
            int fieldStartBit = bitIdx;
            if (!DeltaReader.ReadFieldDescription(reader.Data, reader.Size, ref bitIdx))
            {
                Logger.LogWarning($"[DeltaDescription] failed parsing field {f}");
                return false;
            }
            Logger.LogDebug($"[DeltaDescription] field {f}: {bitIdx - fieldStartBit} bits");
        }

        reader.Offset = (bitIdx + 7) / 8;
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

    private bool HandleUpdateUserInfo(ConnectionContext ctx, MessageReader reader)
    {
        if (reader.Offset + 1 > reader.Size) { Logger.LogWarning("[UpdateUserInfo] buffer overflow at byte 1"); return false; }
        byte slot = reader.Data[reader.Offset];
        reader.Offset += 1;
        if (reader.Offset + 4 > reader.Size) { Logger.LogWarning("[UpdateUserInfo] buffer overflow at byte 4"); return false; }
        reader.Offset += 4;
        string uui = reader.ReadString();
        if (reader.Offset + 16 > reader.Size) { Logger.LogWarning("[UpdateUserInfo] buffer overflow at 16"); return false; }
        reader.Offset += 16;
        Logger.LogDebug($"[UpdateUserInfo] slot={slot}, userInfo=\"{uui[..Math.Min(uui.Length, 100)]}\"");

        // The server broadcasts this message for every player. Only adopt the
        // server-normalized copy of OUR userinfo — other slots belong to other
        // clients and must never overwrite the local settings.
        if (slot == ctx.PlayerNumber)
            UserInfo = uui;
        return true;
    }

    private bool HandleResourceRequest(ConnectionContext ctx, MessageReader reader)
    {
        if (reader.Offset + 4 > reader.Size) { Logger.LogWarning("[ResourceRequest] buffer overflow"); return false; }
        ctx.SpawnCount = reader.ReadUInt32();
        if (reader.Offset + 4 > reader.Size) { Logger.LogWarning("[ResourceRequest] buffer overflow after spawnCount"); return false; }
        uint startIndex = reader.ReadUInt32();
        Logger.LogDebug($"[ResourceRequest] spawnCount={ctx.SpawnCount}, startIndex={startIndex}");

        // Reply by echoing the server's svc_resourcelist payload back. This client
        // holds no custom content, so with no list received yet the reply is an
        // empty count (16-bit) — byte-aligned, unlike the server's bit-packed list.
        byte[] reply = ctx.ResourceListRawBytes.Length > 0 ? (byte[])ctx.ResourceListRawBytes.Clone() : [0x00, 0x00];
        Logger.LogDebug($"[ResourceRequest] replying with clc_resourcelist ({reply.Length} bytes)");
        _ = SendCommandAsync(ClientCommandType.ResourceList, reply, CancellationToken.None);
        return true;
    }

    private bool HandleSendCvarValue2(MessageReader reader)
    {
        if (reader.Offset + 4 > reader.Size) { Logger.LogWarning("[SendCvarValue2] buffer overflow"); return false; }
        uint requestId = reader.ReadUInt32();
        string cvarName = reader.ReadString();
        Logger.LogDebug($"[SendCvarValue2] requestId={requestId}, cvar=\"{cvarName}\"");
        _ = SendCvarValue2Async((int)requestId, cvarName, Settings.GetDefaultCvarValue(cvarName));
        return true;
    }

    private bool HandleSendCvarValue(MessageReader reader)
    {
        string cvarName = reader.ReadString();
        Logger.LogDebug($"[SendCvarValue] cvar=\"{cvarName}\"");
        _ = SendCvarValueAsync(cvarName, Settings.GetDefaultCvarValue(cvarName));
        return true;
    }

    private bool HandleSpawnBaseline(ConnectionContext ctx, MessageReader reader)
    {
        int bitIdx = reader.Offset * 8;
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

            DeltaReader.ReadFields(dt, reader.Data, reader.Size, ref bitIdx);
            entityCount++;
        }

        uint baselineCount = 0;
        BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref baselineCount, 6);
        Logger.LogDebug($"[SpawnBaseline] entities={entityCount}, baselineCount={baselineCount}");
        for (uint ei = 0; ei < baselineCount; ei++)
            DeltaReader.ReadFields(DeltaDefinitions.EntityState, reader.Data, reader.Size, ref bitIdx);

        reader.Offset = (bitIdx + 7) / 8;
        Logger.LogDebug($"[SpawnBaseline] done, totalBits={bitIdx}, newOffset={reader.Offset}");

        TrySendSpawn(ctx);

        return true;
    }

    /// <summary>
    /// Sends the final <c>spawn &lt;count&gt; &lt;mungedCrc&gt;</c> stringcmd of the
    /// sign-on sequence. The CRC is the decrypted worldmap CRC re-munged with an
    /// 8-bit key — the server unmunges with COM_UnMunge2(..., (-1 - spawncount) &amp; 0xFF)
    /// and SV_CheckMapDifferences drops the connection (as a fake overflow) if the
    /// recovered value does not match the real worldmap CRC. Fires once per connection.
    /// </summary>
    /// <remarks>
    /// The Sven Co-op engine branch sends the worldmap CRC as-is (wire-verified against
    /// a real 5.26 client: <c>spawn 1 1038585952</c> equals the raw map CRC), so the
    /// Munge2 pass is skipped when the Sven branch flags are active.
    /// </remarks>
    private void TrySendSpawn(ConnectionContext ctx)
    {
        if (_sentSpawn || !_sentContinueLoading)
            return;
        _sentSpawn = true;

        uint spawnCount = ctx.SpawnCount;
        int rawCrc = (int)ctx.WorldmapCrc;
        byte[] crcBytes = BitConverter.GetBytes(rawCrc);
        int mungeKey = (-1 - (int)spawnCount) & 0xFF;
        Logger.LogDebug($"[SignOn] spawn: rawCrc=0x{rawCrc:X8}, mungeKey=0x{mungeKey:X2} (spawnCount={spawnCount})");
        if (!_useLongFragmentFields)
            MungeEngine.Munge2(crcBytes, 4, mungeKey);
        int mungedCrc = BitConverter.ToInt32(crcBytes, 0);
        var spawnCmd = $"spawn {spawnCount} {mungedCrc}";
        Logger.LogDebug($"[SignOn] sending spawn (spawnCount={spawnCount}, mungedCrc=0x{mungedCrc:X8})");
        _ = SendStringCmdAsync(ClientCommandType.StringCmd, spawnCmd, CancellationToken.None);

        if (_useLongFragmentFields)
        {
            // Sven's signon does not end with a parseable svc_signonnum for us (its
            // delta descriptions diverge from the Valve layout, so the flood tail is
            // not decodable); the real client sends sendents right after spawn.
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                Logger.LogDebug("[SignOn] sending sendents (Sven flow)");
                await SendStringCmdAsync(ClientCommandType.StringCmd, "sendents", CancellationToken.None);
            });
        }
    }

    private bool HandleClientData(MessageReader reader)
    {
        int bitIdx = reader.Offset * 8;
        uint haveDeltaSeq = 0;
        if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref haveDeltaSeq, 1)) return false;
        if (haveDeltaSeq != 0)
        {
            uint deltaSeq = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref deltaSeq, 8)) return false;
            Logger.LogDebug($"[ClientData] deltaSeq={deltaSeq}");
        }
        DeltaReader.ReadFields(DeltaDefinitions.ClientData, reader.Data, reader.Size, ref bitIdx);

        int weaponCount = 0;
        while (true)
        {
            uint haveDelta = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref haveDelta, 1)) return false;
            if (haveDelta == 0) break;
            uint index = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref index, 6)) return false;
            DeltaReader.ReadFields(DeltaDefinitions.WeaponData, reader.Data, reader.Size, ref bitIdx);
            weaponCount++;
        }
        Logger.LogDebug($"[ClientData] done, weaponDeltas={weaponCount}");
        reader.Offset = (bitIdx + 7) / 8;
        return true;
    }

    private bool HandleSignOnNum(MessageReader reader)
    {
        if (reader.Offset + 1 > reader.Size) { Logger.LogWarning("[SignOnNum] buffer overflow"); return true; }
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
        return true;
    }

    private bool HandleVoiceInit(MessageReader reader)
    {
        string codec = reader.ReadString();
        if (reader.Offset + 1 > reader.Size) { Logger.LogWarning("[VoiceInit] buffer overflow"); return true; }
        byte quality = reader.Data[reader.Offset++];
        Logger.LogDebug($"[VoiceInit] codec=\"{codec}\", quality={quality}");
        return true;
    }

    private bool HandleSound(MessageReader reader)
    {
        int bitIdx = reader.Offset * 8;
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
        reader.Offset = (bitIdx + 7) / 8;
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

    private bool HandlePings(MessageReader reader)
    {
        int bitIdx = reader.Offset * 8;
        for (int i = 0; i < 32; i++)
        {
            uint hasEntry = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref hasEntry, 1))
                break;
            if (hasEntry == 0) break;
            uint slot = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref slot, 5)) return true;
            uint ping = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref ping, 12)) return true;
            uint loss = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref loss, 7)) return true;
        }
        reader.Offset = (bitIdx + 7) / 8;
        return true;
    }
}
