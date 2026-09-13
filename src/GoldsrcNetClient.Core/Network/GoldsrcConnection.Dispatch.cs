using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.Core.Util;
using Microsoft.Extensions.Logging;
using System.Net;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// Connected-packet message loop and the table-driven server-message dispatch.
/// Each entry parses exactly one message; a <c>false</c> return discards the
/// remainder of the packet (buffer overflow). Signon-flow and entity/bitstream
/// parsers live in the sibling partial files.
/// </summary>
public partial class GoldsrcConnection
{
    /// <summary>Parses one server message; returning false aborts the rest of the packet (buffer overflow).</summary>
    internal delegate bool MessageParser(ConnectionContext ctx, MessageReader reader);

    private void ProcessConnected(IPEndPoint ep, byte[] data)
    {
        var ctx = _sessions[ep].Context;

        // Diagnostics: set GOLDSRC_MSGDUMP=<path> to append every dispatched
        // message stream to a binary file (length-prefixed records). Far cheaper
        // than console logging, so it does not destabilise the receive loop.
        var dumpStream = _messageDumpStream;
        if (dumpStream != null)
        {
            try
            {
                dumpStream.Write(BitConverter.GetBytes(data.Length), 0, 4);
                dumpStream.Write(data, 0, data.Length);
                dumpStream.Flush();
            }
            catch { /* diagnostics only */ }
        }

        var reader = new MessageReader(data, data.Length);
        Logger.LogDebug($"[Connected] processing {data.Length} bytes");
        if (data.Length > 16)
            Logger.LogDebug($"[Connected] head: {data.AsSpan().ToHexPreview(512)}");

        while (reader.Remaining > 0)
        {
            byte dataType = reader.Data[reader.Offset++];
            string typeName = Enum.IsDefined(typeof(ServerMessageType), dataType) ? ((ServerMessageType)dataType).ToString() : $"0x{dataType:X2}";
            Logger.LogDebug($"[Connected] type={typeName} (0x{dataType:X2}), remaining={reader.Remaining}");

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
    /// Builds the server-message dispatch table.
    /// </summary>
    private Dictionary<byte, MessageParser> BuildMessageParsers()
    {
        // The Sven Co-op engine branch repurposes the legacy svc_foundsecret slot as a
        // string-carrying message (SV_New_f writes it before the serverinfo banner);
        // on Valve branches the slot is unused and carries no payload.
        MessageParser foundSecret = _variant.FoundSecretHasString
            ? (_, reader) => { reader.ReadString(); return true; }
            : (_, _) => true;

        // Messages whose payload this client cannot interpret structurally: log and
        // consume the rest of the packet so the dispatch loop stays in sync.
        MessageParser SkipRest(string name) => (_, reader) =>
        {
            Logger.LogDebug($"[{name}] skipping to end of packet ({reader.Remaining} bytes)");
            reader.Offset = reader.Size;
            return true;
        };

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
        [(byte)ServerMessageType.DeltaDescription] = (ctx, reader) => HandleDeltaDescription(ctx, reader),
        [(byte)ServerMessageType.NewMoveVars] = (_, reader) => HandleNewMoveVars(reader),
        [(byte)ServerMessageType.NewUserMsg] = (_, reader) => HandleNewUserMsg(reader),
        [(byte)ServerMessageType.UpdateUserInfo] = (ctx, reader) => HandleUpdateUserInfo(ctx, reader),
        [(byte)ServerMessageType.ResourceRequest] = (ctx, reader) => HandleResourceRequest(ctx, reader),
        [(byte)ServerMessageType.SpawnBaseline] = (ctx, reader) => HandleSpawnBaseline(ctx, reader),
        [(byte)ServerMessageType.ClientData] = (ctx, reader) => HandleClientData(ctx, reader),
        [(byte)ServerMessageType.SignOnNum] = (_, reader) => HandleSignOnNum(reader),
        [(byte)ServerMessageType.VoiceInit] = (_, reader) => HandleVoiceInit(reader),
        [(byte)ServerMessageType.Sound] = (_, reader) => HandleSound(reader),
        [(byte)ServerMessageType.Customization] = (_, reader) => HandleCustomization(reader),
        [(byte)ServerMessageType.Event] = (ctx, reader) => HandleEvent(ctx, reader, reliable: false),
        [(byte)ServerMessageType.EventReliable] = (ctx, reader) => HandleEvent(ctx, reader, reliable: true),
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
        [(byte)ServerMessageType.TempEntity] = (_, reader) => HandleTempEntity(reader),
        [(byte)ServerMessageType.Damage] = (_, reader) => HandleDamage(reader),
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

        [(byte)ServerMessageType.Particle] = SkipRest("Particle"),
        [(byte)ServerMessageType.SpawnStatic] = SkipRest("SpawnStatic"),
        [(byte)ServerMessageType.Restore] = SkipRest("Restore"),
        [(byte)ServerMessageType.PacketEntities] = SkipRest("PacketEntities"),
        [(byte)ServerMessageType.DeltaPacketEntities] = SkipRest("DeltaPacketEntities"),
        [(byte)ServerMessageType.Hltv] = SkipRest("Hltv"),
        [(byte)ServerMessageType.Director] = SkipRest("Director"),
        [(byte)ServerMessageType.VoiceData] = SkipRest("VoiceData"),

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

    // --- Simple struct/string parsers ---

    private unsafe bool HandleNewMoveVars(MessageReader reader)
    {
        if (!reader.ReadStruct<NewMoveVarsData>(out _))
        {
            Logger.LogWarning($"[NewMoveVars] buffer overflow: offset={reader.Offset}, size={reader.Size}");
            return false;
        }
        reader.ReadString();
        Logger.LogDebug($"[NewMoveVars] done, structSize={sizeof(NewMoveVarsData)}");
        return true;
    }

    private unsafe bool HandleNewUserMsg(MessageReader reader)
    {
        if (!reader.ReadStruct<NewUserMsgData>(out _))
        {
            Logger.LogWarning($"[NewUserMsg] buffer overflow: offset={reader.Offset}, size={reader.Size}");
            return false;
        }
        Logger.LogDebug($"[NewUserMsg] done, structSize={sizeof(NewUserMsgData)}");
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

    private bool HandleSendCvarValue(MessageReader reader)
    {
        string cvarName = reader.ReadString();
        Logger.LogDebug($"[SendCvarValue] cvar=\"{cvarName}\"");
        _ = SendCvarValueAsync(cvarName, Settings.GetDefaultCvarValue(cvarName));
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

    private bool HandleVoiceInit(MessageReader reader)
    {
        string codec = reader.ReadString();
        if (reader.Offset + 1 > reader.Size) { Logger.LogWarning("[VoiceInit] buffer overflow"); return true; }
        byte quality = reader.Data[reader.Offset++];
        Logger.LogDebug($"[VoiceInit] codec=\"{codec}\", quality={quality}");
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
}
