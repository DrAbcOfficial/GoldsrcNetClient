using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Protocol;
using Microsoft.Extensions.Logging;
using System.Net;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// Connected-packet message loop and the table-driven server-message dispatch.
/// Each entry parses exactly one message; a <c>false</c> return (or a
/// <see cref="EndOfBufferException"/>/<see cref="InvalidDataException"/> throw)
/// discards the remainder of the packet. Signon-flow and entity/bitstream
/// parsers live in the sibling partial files.
/// </summary>
public partial class GoldsrcConnection
{
    /// <summary>Parses one server message; returning false aborts the rest of the packet (buffer overflow).</summary>
    internal delegate bool MessageParser(ConnectionContext ctx, ref BufferReader reader);

    /// <summary>Cached type names for diagnostics — avoids per-message reflection.</summary>
    private static readonly string[] MessageTypeNames = BuildMessageTypeNames();

    private static string[] BuildMessageTypeNames()
    {
        var names = new string[256];
        foreach (ServerMessageType value in Enum.GetValues<ServerMessageType>())
            names[(int)value] = value.ToString();
        return names;
    }

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

        var reader = new BufferReader(data);
        Logger.LogDebug($"[Connected] processing {data.Length} bytes");
        if (data.Length > 16)
            Logger.LogDebug($"[Connected] head: {data.AsSpan().ToHexPreview(512)}");

        while (reader.Remaining > 0)
        {
            byte dataType = reader.ReadUInt8();
            string typeName = MessageTypeNames[dataType] is { } name ? name : $"0x{dataType:X2}";
            Logger.LogDebug($"[Connected] type={typeName} (0x{dataType:X2}), remaining={reader.Remaining}");

            try
            {
                if (_messageHandler.HandleMessage(this, dataType, ref reader))
                    continue;

                if (!_messageParsers.TryGetValue(dataType, out var parser))
                {
                    reader.BytePosition--; // rewind the type byte for the log offset
                    Logger.LogWarning($"[Connected] Unknown data type: 0x{dataType:X2} at offset={reader.BytePosition}, remaining={reader.Remaining}");
                    return;
                }

                if (!parser(ctx, ref reader))
                    return;
            }
            catch (Exception ex) when (ex is EndOfBufferException or InvalidDataException)
            {
                // A truncated or malformed message desynchronises the stream —
                // discard the rest of the packet, like the engine does.
                Logger.LogWarning($"[Connected] {typeName} aborted packet: {ex.Message}");
                return;
            }
        }

        Logger.LogDebug($"[Connected] processed all {reader.Length} bytes successfully");
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
            ? (ctx, ref reader) => { reader.ReadString(); return true; }
            : (ctx, ref reader) => true;

        // Messages whose payload this client cannot interpret structurally: log and
        // consume the rest of the packet so the dispatch loop stays in sync.
        MessageParser SkipRest(string name) => (ctx, ref reader) =>
        {
            Logger.LogDebug($"[{name}] skipping to end of packet ({reader.Remaining} bytes)");
            reader.BytePosition = reader.Length;
            return true;
        };

        return new()
        {
        [(byte)ServerMessageType.Nop] = (ctx, ref reader) => true,
        [(byte)ServerMessageType.Choke] = (ctx, ref reader) => true,
        [(byte)ServerMessageType.KilledMonster] = (ctx, ref reader) => true,
        [(byte)ServerMessageType.FoundSecret] = foundSecret,

        [(byte)ServerMessageType.Bad] = (ctx, ref reader) =>
        {
            Logger.LogWarning("[Bad] server sent bad message, consuming remaining data");
            reader.BytePosition = reader.Length;
            return true;
        },

        [(byte)ServerMessageType.Disconnect] = (ctx, ref reader) =>
        {
            string reason = reader.ReadString();
            Logger.LogWarning($"[Disconnect] server disconnected: reason=\"{reason}\"");
            reader.BytePosition = reader.Length;
            OnServerDisconnect?.Invoke(reason);
            return true;
        },

        [(byte)ServerMessageType.Print] = (ctx, ref reader) =>
        {
            string msg = reader.ReadString();
            Logger.LogDebug($"[Print] msg=\"{msg[..Math.Min(msg.Length, 200)]}\"");
            OnConsolePrint?.Invoke(msg);
            return true;
        },

        [(byte)ServerMessageType.CenterPrint] = (ctx, ref reader) =>
        {
            string centerMsg = reader.ReadString();
            Logger.LogDebug($"[CenterPrint] msg=\"{centerMsg}\"");
            OnCenterPrint?.Invoke(centerMsg);
            return true;
        },

        [(byte)ServerMessageType.StuffText] = (ctx, ref reader) =>
        {
            string st = reader.ReadString();
            Logger.LogDebug($"[StuffText] text=\"{st[..Math.Min(st.Length, 200)]}\"");
            return true;
        },

        [(byte)ServerMessageType.ServerInfo] = (ctx, ref reader) => HandleServerInfo(ctx, ref reader),
        [(byte)ServerMessageType.DeltaDescription] = (ctx, ref reader) => HandleDeltaDescription(ctx, ref reader),
        [(byte)ServerMessageType.NewMoveVars] = (ctx, ref reader) => HandleNewMoveVars(ref reader),
        [(byte)ServerMessageType.NewUserMsg] = (ctx, ref reader) => HandleNewUserMsg(ref reader),
        [(byte)ServerMessageType.UpdateUserInfo] = (ctx, ref reader) => HandleUpdateUserInfo(ctx, ref reader),
        [(byte)ServerMessageType.ResourceRequest] = (ctx, ref reader) => HandleResourceRequest(ctx, ref reader),
        [(byte)ServerMessageType.SpawnBaseline] = (ctx, ref reader) => HandleSpawnBaseline(ctx, ref reader),
        [(byte)ServerMessageType.ClientData] = (ctx, ref reader) => HandleClientData(ctx, ref reader),
        [(byte)ServerMessageType.SignOnNum] = (ctx, ref reader) => HandleSignOnNum(ref reader),
        [(byte)ServerMessageType.VoiceInit] = (ctx, ref reader) => HandleVoiceInit(ref reader),
        [(byte)ServerMessageType.Sound] = (ctx, ref reader) => HandleSound(ref reader),
        [(byte)ServerMessageType.Customization] = (ctx, ref reader) => HandleCustomization(ref reader),
        [(byte)ServerMessageType.Event] = (ctx, ref reader) => HandleEvent(ctx, ref reader, reliable: false),
        [(byte)ServerMessageType.EventReliable] = (ctx, ref reader) => HandleEvent(ctx, ref reader, reliable: true),
        [(byte)ServerMessageType.Pings] = (ctx, ref reader) => HandlePings(ref reader),
        [(byte)ServerMessageType.SendCvarValue] = (ctx, ref reader) => HandleSendCvarValue(ref reader),
        [(byte)ServerMessageType.SendCvarValue2] = (ctx, ref reader) => HandleSendCvarValue2(ref reader),

        [(byte)ServerMessageType.SetView] = (ctx, ref reader) => CheckedSkip(ref reader, "SetView", 2),
        [(byte)ServerMessageType.StopSound] = (ctx, ref reader) => CheckedSkip(ref reader, "StopSound", 2),
        [(byte)ServerMessageType.SetAngle] = (ctx, ref reader) => CheckedSkip(ref reader, "SetAngle", 2, 2, 2),
        [(byte)ServerMessageType.AddAngle] = (ctx, ref reader) => CheckedSkip(ref reader, "AddAngle", 2),
        [(byte)ServerMessageType.DecalName] = (ctx, ref reader) => CheckedSkip(ref reader, "DecalName", 2),
        [(byte)ServerMessageType.RoomType] = (ctx, ref reader) => CheckedSkip(ref reader, "RoomType", 2),
        [(byte)ServerMessageType.CrosshairAngle] = (ctx, ref reader) => CheckedSkip(ref reader, "CrosshairAngle", 2),
        [(byte)ServerMessageType.SoundFade] = (ctx, ref reader) => CheckedSkip(ref reader, "SoundFade", 4),
        [(byte)ServerMessageType.TempEntity] = (ctx, ref reader) => HandleTempEntity(ref reader),
        [(byte)ServerMessageType.Damage] = (ctx, ref reader) => HandleDamage(ref reader),
        [(byte)ServerMessageType.SpawnStaticSound] = (ctx, ref reader) => CheckedSkip(ref reader, "SpawnStaticSound", 14),

        [(byte)ServerMessageType.Version] = (ctx, ref reader) =>
        {
            uint version = reader.ReadUInt32();
            Logger.LogDebug($"[Version] protocol={version}");
            return true;
        },

        [(byte)ServerMessageType.Time] = (ctx, ref reader) =>
        {
            float time = reader.ReadSingle();
            Logger.LogDebug($"[Time] time={time:F2}");
            return true;
        },

        [(byte)ServerMessageType.TimeScale] = (ctx, ref reader) =>
        {
            float timeScale = reader.ReadSingle();
            Logger.LogDebug($"[TimeScale] scale={timeScale:F2}");
            return true;
        },

        [(byte)ServerMessageType.LightStyle] = (ctx, ref reader) =>
        {
            reader.ReadUInt8();
            reader.ReadString();
            return true;
        },

        [(byte)ServerMessageType.ResourceLocation] = (ctx, ref reader) =>
        {
            string loc = reader.ReadString();
            Logger.LogDebug($"[ResourceLocation] location=\"{loc}\"");
            return true;
        },

        [(byte)ServerMessageType.ResourceList] = (ctx, ref reader) =>
        {
            int listStart = reader.BytePosition;
            ProcessResourceList(ctx, ref reader);
            ctx.ResourceListRawBytes = reader.Buffer[listStart..reader.BytePosition].ToArray();
            Logger.LogDebug($"[ResourceList] count={ctx.Resources.Length}, dataBytes={reader.BytePosition - listStart}");
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

        [(byte)ServerMessageType.Intermission] = (ctx, ref reader) =>
        {
            Logger.LogDebug("[Intermission] intermission started");
            return true;
        },

        [(byte)ServerMessageType.Finale] = (ctx, ref reader) =>
        {
            string finaleStr = reader.ReadString();
            Logger.LogDebug($"[Finale] text=\"{finaleStr}\"");
            return true;
        },

        [(byte)ServerMessageType.Cutscene] = (ctx, ref reader) =>
        {
            string cutscene = reader.ReadString();
            Logger.LogDebug($"[Cutscene] name=\"{cutscene}\"");
            return true;
        },

        [(byte)ServerMessageType.FileTxferFailed] = (ctx, ref reader) =>
        {
            string failName = reader.ReadString();
            Logger.LogDebug($"[FileTxferFailed] file=\"{failName}\"");
            return true;
        },

        [(byte)ServerMessageType.SendExtraInfo] = (ctx, ref reader) =>
        {
            // Payload: gamedir string + one byte (sv_cheats flag).
            string extraDir = reader.ReadString();
            byte extraFlag = reader.ReadUInt8();
            Logger.LogDebug($"[SendExtraInfo] dir=\"{extraDir}\", svCheats={extraFlag}");
            return true;
        },

        [(byte)ServerMessageType.Exec] = (ctx, ref reader) =>
        {
            // Payload: one exec-type byte; type 1 is followed by a class number byte (TFC).
            byte execType = reader.ReadUInt8();
            byte execClass = 0;
            if (execType == 1)
                execClass = reader.ReadUInt8();
            Logger.LogDebug($"[Exec] type={execType}, class={execClass}");
            return true;
        },

        [(byte)ServerMessageType.CdTrack] = (ctx, ref reader) =>
        {
            byte track = reader.ReadUInt8();
            byte loopTrack = reader.ReadUInt8();
            Logger.LogDebug($"[CdTrack] track={track}, loop={loopTrack}");
            return true;
        },

        [(byte)ServerMessageType.WeaponAnim] = (ctx, ref reader) =>
        {
            byte anim = reader.ReadUInt8();
            byte body = reader.ReadUInt8();
            Logger.LogDebug($"[WeaponAnim] anim={anim}, body={body}");
            return true;
        },

        [(byte)ServerMessageType.SetPause] = (ctx, ref reader) =>
        {
            uint paused = reader.ReadBits(1);
            Logger.LogDebug($"[SetPause] paused={paused}");
            return true;
        },
        };
    }

    /// <summary>Skips fixed-size payloads, aborting the packet when data is missing.</summary>
    private bool CheckedSkip(ref BufferReader reader, string name, params ReadOnlySpan<int> sizes)
    {
        int total = 0;
        foreach (int size in sizes)
            total += size;
        if (reader.Remaining < total)
        {
            Logger.LogWarning(sizes.Length == 1
                ? $"[{name}] buffer overflow"
                : $"[{name}] buffer overflow ({total} bytes needed, {reader.Remaining} left)");
            return false;
        }
        reader.Skip(total);
        return true;
    }

    // --- Simple struct/string parsers ---

    private bool HandleNewMoveVars(ref BufferReader reader)
    {
        reader.ReadStruct<NewMoveVarsData>();
        reader.ReadString();
        Logger.LogDebug("[NewMoveVars] done");
        return true;
    }

    private bool HandleNewUserMsg(ref BufferReader reader)
    {
        reader.ReadStruct<NewUserMsgData>();
        Logger.LogDebug("[NewUserMsg] done");
        return true;
    }

    private bool HandleUpdateUserInfo(ConnectionContext ctx, ref BufferReader reader)
    {
        byte slot = reader.ReadUInt8();
        reader.Skip(4); // user ID
        string uui = reader.ReadString();
        reader.Skip(16); // HashedCDKey
        Logger.LogDebug($"[UpdateUserInfo] slot={slot}, userInfo=\"{uui[..Math.Min(uui.Length, 100)]}\"");

        // The server broadcasts this message for every player. Only adopt the
        // server-normalized copy of OUR userinfo — other slots belong to other
        // clients and must never overwrite the local settings.
        if (slot == ctx.PlayerNumber)
            UserInfo = uui;
        return true;
    }

    private bool HandleSendCvarValue(ref BufferReader reader)
    {
        string cvarName = reader.ReadString();
        Logger.LogDebug($"[SendCvarValue] cvar=\"{cvarName}\"");
        _ = SendCvarValueAsync(cvarName, Settings.GetDefaultCvarValue(cvarName));
        return true;
    }

    private bool HandleSendCvarValue2(ref BufferReader reader)
    {
        uint requestId = reader.ReadUInt32();
        string cvarName = reader.ReadString();
        Logger.LogDebug($"[SendCvarValue2] requestId={requestId}, cvar=\"{cvarName}\"");
        _ = SendCvarValue2Async((int)requestId, cvarName, Settings.GetDefaultCvarValue(cvarName));
        return true;
    }

    private bool HandleVoiceInit(ref BufferReader reader)
    {
        string codec = reader.ReadString();
        byte quality = reader.ReadUInt8();
        Logger.LogDebug($"[VoiceInit] codec=\"{codec}\", quality={quality}");
        return true;
    }

    private bool HandleCustomization(ref BufferReader reader)
    {
        byte playerSlot = reader.ReadUInt8();
        byte resourceType = reader.ReadUInt8();
        string resourceName = reader.ReadString();
        reader.Skip(2); // next download index
        reader.Skip(4); // download size
        reader.Skip(1); // flags
        Logger.LogDebug($"[Customization] player={playerSlot}, type={resourceType}, name=\"{resourceName}\"");
        return true;
    }
}
