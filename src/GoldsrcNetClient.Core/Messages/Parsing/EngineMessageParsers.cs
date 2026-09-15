using GoldsrcNetClient.Core.Delta;
using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Messages.Engine;
using GoldsrcNetClient.Core.Munge;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Core.Messages.Parsing;

/// <summary>
/// The built-in engine (<c>svc_*</c>) message parsers — the client's complete
/// protocol-48 vocabulary. Registration captures the engine-branch dialect
/// (<see cref="IEngineVariant"/>) and the session state the bit-level parsers
/// need (<see cref="SessionData"/> delta tables and client count), so
/// every entry is a pure <c>reader → message</c> function.
/// </summary>
/// <remarks>
/// Side effects (signon replies, userinfo adoption, cvar answers, resource
/// echo-back) deliberately do NOT live here: parsers only decode, and the
/// connection wires behavioral subscribers to the resulting messages.
/// </remarks>
public static class EngineMessageParsers
{
    /// <summary>
    /// Registers every built-in engine parser. Call once per connection while
    /// building the <see cref="ParserRegistry"/>; later registrations replace
    /// earlier ones, so game profiles can override individual slots.
    /// </summary>
    public static void Register(ParserRegistry.Builder builder, IEngineVariant variant, SessionData state)
    {
        // The Sven Co-op engine branch repurposes the legacy svc_foundsecret slot as a
        // string-carrying message; on Valve branches the slot carries no payload.
        if (variant.FoundSecretHasString)
            builder.AddEngine((byte)ServerMessageType.FoundSecret, static (ref BufferReader r) =>
            {
                _ = r.ReadString();
                return new OpaqueEngineMessage((byte)ServerMessageType.FoundSecret);
            });

        builder
            .AddEngine((byte)ServerMessageType.Disconnect, static (ref BufferReader r) =>
            {
                var msg = new DisconnectMessage(r.ReadString());
                r.BytePosition = r.Length; // the engine discards the packet tail
                return msg;
            })
            .AddEngine((byte)ServerMessageType.Print, static (ref BufferReader r) => new PrintMessage(r.ReadString()))
            .AddEngine((byte)ServerMessageType.CenterPrint, static (ref BufferReader r) => new CenterPrintMessage(r.ReadString()))
            .AddEngine((byte)ServerMessageType.StuffText, static (ref BufferReader r) => new StuffTextMessage(r.ReadString()))

            .AddEngine((byte)ServerMessageType.ServerInfo, (ref BufferReader r) => ParseServerInfo(ref r, variant))
            .AddEngine((byte)ServerMessageType.DeltaDescription, (ref BufferReader r) => ParseDeltaDescription(ref r, variant))
            .AddEngine((byte)ServerMessageType.NewMoveVars, static (ref BufferReader r) =>
            {
                var data = r.ReadStruct<NewMoveVarsData>();
                _ = r.ReadString(); // sky name
                return new NewMoveVarsMessage(data);
            })
            .AddEngine((byte)ServerMessageType.NewUserMsg, static (ref BufferReader r) =>
            {
                var data = r.ReadStruct<NewUserMsgData>();
                return new NewUserMsgMessage(data.Index, ReadFixedName(data), data.Size);
            })
            .AddEngine((byte)ServerMessageType.UpdateUserInfo, static (ref BufferReader r) =>
            {
                byte slot = r.ReadUInt8();
                int userId = r.ReadInt32();
                string userInfo = r.ReadString();
                byte[] cdKeyHash = r.ReadBytes(16);
                return new UpdateUserInfoMessage(slot, userId, userInfo, cdKeyHash);
            })
            .AddEngine((byte)ServerMessageType.ResourceRequest, static (ref BufferReader r) =>
                new ResourceRequestMessage(r.ReadUInt32(), r.ReadUInt32()))
            .AddEngine((byte)ServerMessageType.ResourceList, (ref BufferReader r) => ParseResourceList(ref r, variant, state))
            .AddEngine((byte)ServerMessageType.SpawnBaseline, (ref BufferReader r) => ParseSpawnBaseline(ref r, variant, state))
            .AddEngine((byte)ServerMessageType.ClientData, (ref BufferReader r) => ParseClientData(ref r, variant, state))
            .AddEngine((byte)ServerMessageType.SignOnNum, static (ref BufferReader r) => new SignOnNumMessage(r.ReadUInt8()))
            .AddEngine((byte)ServerMessageType.VoiceInit, static (ref BufferReader r) => new VoiceInitMessage(r.ReadString(), r.ReadUInt8()))
            .AddEngine((byte)ServerMessageType.Customization, static (ref BufferReader r) =>
            {
                byte playerSlot = r.ReadUInt8();
                byte resourceType = r.ReadUInt8();
                string name = r.ReadString();
                r.Skip(2 + 4 + 1);
                return new CustomizationMessage(playerSlot, resourceType, name);
            })
            .AddEngine((byte)ServerMessageType.Event, (ref BufferReader r) => ParseEvent(ref r, variant, state, reliable: false))
            .AddEngine((byte)ServerMessageType.EventReliable, (ref BufferReader r) => ParseEvent(ref r, variant, state, reliable: true))
            .AddEngine((byte)ServerMessageType.Sound, (ref BufferReader r) => ParseSound(ref r, variant))
            .AddEngine((byte)ServerMessageType.Pings, static (ref BufferReader r) =>
            {
                for (int i = 0; i < 32; i++)
                {
                    if (r.ReadBits(1) == 0) break;
                    r.ReadBits(5);
                    r.ReadBits(12);
                    r.ReadBits(7);
                }
                r.Align();
                return new PingsMessage();
            })
            .AddEngine((byte)ServerMessageType.SendCvarValue, static (ref BufferReader r) => new SendCvarValueMessage(r.ReadString()))
            .AddEngine((byte)ServerMessageType.SendCvarValue2, static (ref BufferReader r) =>
                new SendCvarValue2Message(r.ReadUInt32(), r.ReadString()))
            .AddEngine((byte)ServerMessageType.TempEntity, (ref BufferReader r) =>
            {
                if (!TempEntityParser.Skip(ref r, variant))
                    throw new InvalidDataException("unknown temp-entity effect type");
                r.Align();
                return new TempEntityMessage();
            })
            .AddEngine((byte)ServerMessageType.Damage, (ref BufferReader r) =>
            {
                r.ReadBits(8);
                r.ReadBits(8);
                for (int i = 0; i < 3; i++)
                    ReadCoord(ref r, variant);
                r.Align();
                return new DamageMessage();
            })
            .AddEngine((byte)ServerMessageType.Version, static (ref BufferReader r) => new VersionMessage(r.ReadUInt32()))
            .AddEngine((byte)ServerMessageType.Time, static (ref BufferReader r) => new TimeMessage(r.ReadSingle()))
            .AddEngine((byte)ServerMessageType.TimeScale, static (ref BufferReader r) => new TimeScaleMessage(r.ReadSingle()))
            .AddEngine((byte)ServerMessageType.LightStyle, static (ref BufferReader r) => new LightStyleMessage(r.ReadUInt8(), r.ReadString()))
            .AddEngine((byte)ServerMessageType.ResourceLocation, static (ref BufferReader r) => new ResourceLocationMessage(r.ReadString()))
            .AddEngine((byte)ServerMessageType.Finale, static (ref BufferReader r) => new FinaleMessage(r.ReadString()))
            .AddEngine((byte)ServerMessageType.Cutscene, static (ref BufferReader r) => new CutsceneMessage(r.ReadString()))
            .AddEngine((byte)ServerMessageType.FileTxferFailed, static (ref BufferReader r) => new FileTxferFailedMessage(r.ReadString()))
            .AddEngine((byte)ServerMessageType.SendExtraInfo, static (ref BufferReader r) =>
                new SendExtraInfoMessage(r.ReadString(), r.ReadUInt8() != 0))
            .AddEngine((byte)ServerMessageType.Exec, static (ref BufferReader r) =>
            {
                byte type = r.ReadUInt8();
                return new ExecMessage(type, type == 1 ? r.ReadUInt8() : (byte)0);
            })
            .AddEngine((byte)ServerMessageType.CdTrack, static (ref BufferReader r) => new CdTrackMessage(r.ReadUInt8(), r.ReadUInt8()))
            .AddEngine((byte)ServerMessageType.WeaponAnim, static (ref BufferReader r) => new WeaponAnimMessage(r.ReadUInt8(), r.ReadUInt8()))
            .AddEngine((byte)ServerMessageType.SetPause, static (ref BufferReader r) =>
            {
                bool paused = r.ReadBits(1) != 0;
                r.Align();
                return new SetPauseMessage(paused);
            });

        // Messages consumed without interpretation: no-payload slots, fixed-size
        // skips, and the opaque flood messages (their length is the packet tail).
        builder
            .AddEngine((byte)ServerMessageType.Nop, static (ref BufferReader r) => new OpaqueEngineMessage((byte)ServerMessageType.Nop))
            .AddEngine((byte)ServerMessageType.Choke, static (ref BufferReader r) => new OpaqueEngineMessage((byte)ServerMessageType.Choke))
            .AddEngine((byte)ServerMessageType.KilledMonster, static (ref BufferReader r) => new OpaqueEngineMessage((byte)ServerMessageType.KilledMonster))
            .AddEngine((byte)ServerMessageType.Bad, static (ref BufferReader r) =>
            {
                r.BytePosition = r.Length;
                return new OpaqueEngineMessage((byte)ServerMessageType.Bad);
            })
            .AddEngine((byte)ServerMessageType.Intermission, static (ref BufferReader r) => new OpaqueEngineMessage((byte)ServerMessageType.Intermission))
            .AddEngine((byte)ServerMessageType.SetView, static (ref BufferReader r) => SkipFixed(ref r, 2, (byte)ServerMessageType.SetView))
            .AddEngine((byte)ServerMessageType.StopSound, static (ref BufferReader r) => SkipFixed(ref r, 2, (byte)ServerMessageType.StopSound))
            .AddEngine((byte)ServerMessageType.SetAngle, static (ref BufferReader r) => SkipFixed(ref r, 6, (byte)ServerMessageType.SetAngle))
            .AddEngine((byte)ServerMessageType.AddAngle, static (ref BufferReader r) => SkipFixed(ref r, 2, (byte)ServerMessageType.AddAngle))
            .AddEngine((byte)ServerMessageType.DecalName, static (ref BufferReader r) => SkipFixed(ref r, 2, (byte)ServerMessageType.DecalName))
            .AddEngine((byte)ServerMessageType.RoomType, static (ref BufferReader r) => SkipFixed(ref r, 2, (byte)ServerMessageType.RoomType))
            .AddEngine((byte)ServerMessageType.CrosshairAngle, static (ref BufferReader r) => SkipFixed(ref r, 2, (byte)ServerMessageType.CrosshairAngle))
            .AddEngine((byte)ServerMessageType.SoundFade, static (ref BufferReader r) => SkipFixed(ref r, 4, (byte)ServerMessageType.SoundFade))
            .AddEngine((byte)ServerMessageType.SpawnStaticSound, static (ref BufferReader r) => SkipFixed(ref r, 14, (byte)ServerMessageType.SpawnStaticSound))
            .AddEngine((byte)ServerMessageType.Particle, SkipRest((byte)ServerMessageType.Particle))
            .AddEngine((byte)ServerMessageType.SpawnStatic, SkipRest((byte)ServerMessageType.SpawnStatic))
            .AddEngine((byte)ServerMessageType.Restore, SkipRest((byte)ServerMessageType.Restore))
            .AddEngine((byte)ServerMessageType.PacketEntities, SkipRest((byte)ServerMessageType.PacketEntities))
            .AddEngine((byte)ServerMessageType.DeltaPacketEntities, SkipRest((byte)ServerMessageType.DeltaPacketEntities))
            .AddEngine((byte)ServerMessageType.Hltv, SkipRest((byte)ServerMessageType.Hltv))
            .AddEngine((byte)ServerMessageType.Director, SkipRest((byte)ServerMessageType.Director))
            .AddEngine((byte)ServerMessageType.VoiceData, SkipRest((byte)ServerMessageType.VoiceData));
    }

    /// <summary>Builds a parser that skips the whole packet tail as one opaque message.</summary>
    private static EngineMessageParser SkipRest(byte type) => (ref BufferReader r) =>
    {
        r.BytePosition = r.Length;
        return new OpaqueEngineMessage(type);
    };

    private static OpaqueEngineMessage SkipFixed(ref BufferReader reader, int byteCount, byte type)
    {
        reader.Skip(byteCount);
        return new OpaqueEngineMessage(type);
    }

    private static unsafe string ReadFixedName(NewUserMsgData data)
    {
        int len = 0;
        while (len < 16 && data.NameData[len] != 0) len++;
        return System.Text.Encoding.UTF8.GetString(data.NameData, len);
    }

    /// <summary>
    /// svc_serverinfo: struct overlay, four strings, one optional flag byte. The
    /// worldmap CRC is decrypted here (Valve branches) or passed through
    /// (plaintext branches) so subscribers never see the munged value.
    /// </summary>
    private static ServerInfoMessage ParseServerInfo(ref BufferReader reader, IEngineVariant variant)
    {
        var si = reader.ReadStruct<ServerInfoData>();
        string gameDir = reader.ReadString();
        string clientDll = reader.ReadString();
        string mapName = reader.ReadString();
        string gameDesc = reader.ReadString();
        if (reader.Remaining > 0)
            reader.Skip(1); // sv_cheats/flag byte
        // Sven Co-op sends the true worldmap CRC and the real client echoes it
        // back verbatim in the spawn command (wire-verified: "spawn 1 1038585952").
        uint worldmapCrc = si.Munge3WorldmapCrc;
        if (!variant.PlaintextWorldmapCrc)
        {
            byte[] crcBytes = BitConverter.GetBytes(si.Munge3WorldmapCrc);
            MungeEngine.UnMunge3(crcBytes, 4, (-1 - si.PlayerNumber) & 0xFF);
            worldmapCrc = BitConverter.ToUInt32(crcBytes);
        }
        return new ServerInfoMessage(si, gameDir, clientDll, mapName, gameDesc, worldmapCrc);
    }

    /// <summary>
    /// svc_deltadescription: name, 16-bit field count, then each entry as a
    /// delta record against the engine's fixed meta table. Live tables always
    /// win over the compiled fallbacks — this is what makes Sven Co-op's
    /// divergent structures work without protocol changes.
    /// </summary>
    private static DeltaDescriptionMessage ParseDeltaDescription(ref BufferReader reader, IEngineVariant variant)
    {
        string name = reader.ReadString();
        uint fieldCount = reader.ReadBits(16);
        if (fieldCount > 96)
            throw new InvalidDataException($"implausible delta fieldCount={fieldCount} for \"{name}\"");

        var fields = new List<DeltaField>((int)fieldCount);
        for (uint f = 0; f < fieldCount; f++)
        {
            var desc = DeltaReader.ReadFieldDescription(ref reader, variant.DeltaByteCountBits);
            fields.Add(desc.ToDeltaField());
        }
        return new DeltaDescriptionMessage(name, new DeltaType(name, (byte)fieldCount, fields.ToArray()));
    }

    /// <summary>
    /// svc_resourcelist: bit-packed resource descriptors followed by a
    /// consistency list. The raw payload is captured for the echo-back reply
    /// (real-client behavior on svc_resourcerequest).
    /// </summary>
    private static ResourceListMessage ParseResourceList(ref BufferReader reader, IEngineVariant variant, SessionData state)
    {
        // Field widths follow the engine branch (Ghidra: SV_SendResources
        // FUN_01da4200 / consistency FUN_01db83a0 in hw.dll build 10257):
        // Sven widened resource count and the consistency absolute index to
        // 16 bits (Valve/ReHLDS: 12 and 10, RESOURCE_INDEX_BITS).
        int countBits = variant.ResourceIndexBits;
        int absIndexBits = variant.ConsistencyIndexBits;
        int listStart = reader.BytePosition;

        uint resourceCount = reader.ReadBits(countBits);
        var resources = new ResourceInfo[resourceCount];
        for (uint i = 0; i < resourceCount; i++)
        {
            var res = new ResourceInfo();
            resources[i] = res;

            reader.ReadBits(4); // type
            res.Name = reader.ReadBitString();
            if (res.Name.Length > 64)
                throw new InvalidDataException("resource name exceeds 64 bytes");
            reader.ReadBits(12); // resource index
            reader.ReadBits(24); // download size
            uint flag = reader.ReadBits(3);
            res.Flag = (byte)flag;
            if ((res.Flag & (byte)ResourceFlag.Custom) != 0)
                res.Md5 = ReadBitBytes(ref reader, 16);
            if (reader.ReadBits(1) != 0)
                res.Reserved = ReadBitBytes(ref reader, 32);
            res.NeedConsistency = false;
        }

        if (reader.ReadBits(1) != 0)
        {
            int lastIndex = 0;
            while (reader.ReadBits(1) != 0)
            {
                if (reader.ReadBits(1) == 0)
                    lastIndex = (int)reader.ReadBits(absIndexBits);
                else
                    lastIndex += (int)reader.ReadBits(5);

                if (lastIndex >= 0 && lastIndex < resources.Length)
                    resources[lastIndex].NeedConsistency = true;
            }
        }

        reader.Align();
        byte[] raw = reader.Buffer[listStart..reader.BytePosition].ToArray();
        return new ResourceListMessage(resources, raw);
    }

    /// <summary>
    /// svc_spawnbaseline: entity-number-indexed delta records until the 16-bit
    /// end marker, then six-bit-count extra baselines. Sven widened the entity
    /// number to 13 bits (wire-verified); Valve keeps 11.
    /// </summary>
    private static SpawnBaselineMessage ParseSpawnBaseline(ref BufferReader reader, IEngineVariant variant, SessionData state)
    {
        if (Environment.GetEnvironmentVariable("GOLDSRC_SKIPBASELINE") == "1")
        {
            reader.BytePosition = reader.Length;
            return new SpawnBaselineMessage(0);
        }

        int entBits = variant.EntityIndexBits;
        int maxEntity = (1 << entBits) - 1;
        int entityCount = 0;
        while (true)
        {
            uint entityNumber = reader.ReadBits(entBits);
            if (entityNumber == maxEntity)
            {
                reader.ReadBits(16 - entBits); // rest of the end marker
                break;
            }

            uint entityType = reader.ReadBits(2);
            DeltaType dt = (entityType & 1) != 0
                ? ResolveDelta(state, entityNumber >= 1 && entityNumber <= state.MaxClients
                    ? DeltaDefinitions.EntityStatePlayer
                    : DeltaDefinitions.EntityState)
                : ResolveDelta(state, DeltaDefinitions.CustomEntityState);

            DeltaReader.ReadFields(dt, ref reader, variant.DeltaByteCountBits);
            entityCount++;
        }

        uint baselineCount = reader.ReadBits(6);
        var baselineDelta = ResolveDelta(state, DeltaDefinitions.EntityState);
        for (uint ei = 0; ei < baselineCount; ei++)
            DeltaReader.ReadFields(baselineDelta, ref reader, variant.DeltaByteCountBits);

        reader.Align();
        return new SpawnBaselineMessage(entityCount);
    }

    private static ClientDataMessage ParseClientData(ref BufferReader reader, IEngineVariant variant, SessionData state)
    {
        if (reader.ReadBits(1) != 0)
            reader.ReadBits(8); // delta sequence
        DeltaReader.ReadFields(ResolveDelta(state, DeltaDefinitions.ClientData), ref reader, variant.DeltaByteCountBits);

        var weaponDelta = ResolveDelta(state, DeltaDefinitions.WeaponData);
        int weaponCount = 0;
        while (reader.ReadBits(1) != 0)
        {
            reader.ReadBits(6); // weapon index
            DeltaReader.ReadFields(weaponDelta, ref reader, variant.DeltaByteCountBits);
            weaponCount++;
        }
        reader.Align();
        return new ClientDataMessage(weaponCount);
    }

    /// <summary>
    /// svc_event: 5-bit count, then per event a 10-bit index, optional 11-bit
    /// packet index, optional event_args delta, optional 16-bit fire time.
    /// svc_event_reliable drops the count and packet index.
    /// </summary>
    private static EventMessage ParseEvent(ref BufferReader reader, IEngineVariant variant, SessionData state, bool reliable)
    {
        var eventDelta = ResolveDelta(state, DeltaDefinitions.Event);
        uint count = reliable ? 1 : reader.ReadBits(5);
        uint last = 0;
        for (uint e = 0; e < count; e++)
        {
            last = reader.ReadBits(10);
            if (!reliable)
            {
                if (reader.ReadBits(1) != 0)
                    reader.ReadBits(11); // packet index
                if (reader.ReadBits(1) != 0)
                    DeltaReader.ReadFields(eventDelta, ref reader, variant.DeltaByteCountBits);
            }
            else
            {
                DeltaReader.ReadFields(eventDelta, ref reader, variant.DeltaByteCountBits);
            }
            if (reader.ReadBits(1) != 0)
                reader.ReadBits(16); // fire time
        }
        reader.Align();
        return new EventMessage(last, reliable);
    }

    private static SoundMessage ParseSound(ref BufferReader reader, IEngineVariant variant)
    {
        uint fieldMask = reader.ReadBits(9);
        if ((fieldMask & SoundFlags.Volume) != 0)
            reader.ReadBits(8);
        if ((fieldMask & SoundFlags.Attenuation) != 0)
            reader.ReadBits(8);
        uint channel = reader.ReadBits(3);
        uint entity = reader.ReadBits(variant.EntityIndexBits);
        uint soundNum = reader.ReadBits((fieldMask & SoundFlags.LargeIndex) != 0 ? 16 : 8);
        for (int i = 0; i < 3; i++)
        {
            if (reader.ReadBits(1) != 0)
                ReadCoord(ref reader, variant);
        }
        if ((fieldMask & SoundFlags.Pitch) != 0)
            reader.ReadBits(8);
        reader.Align();
        return new SoundMessage(channel, entity, soundNum, fieldMask);
    }

    private static void ReadCoord(ref BufferReader reader, IEngineVariant variant)
    {
        if (variant.WideCoordinates)
            reader.ReadBitCoordWide();
        else
            reader.ReadBitCoord();
    }

    private static byte[] ReadBitBytes(ref BufferReader reader, int count)
    {
        var bytes = new byte[count];
        for (int b = 0; b < count; b++)
            bytes[b] = (byte)reader.ReadBits(8);
        return bytes;
    }

    /// <summary>
    /// Resolves the live delta table received via svc_deltadescription, falling
    /// back to the compiled Valve layout when the server has not sent one.
    /// </summary>
    private static DeltaType ResolveDelta(SessionData state, DeltaType fallback)
        => state.DeltaTables.TryGetValue(fallback.DeltaName, out var dt) ? dt : fallback;
}
