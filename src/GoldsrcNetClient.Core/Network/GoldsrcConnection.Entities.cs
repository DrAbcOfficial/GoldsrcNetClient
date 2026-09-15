using GoldsrcNetClient.Core.Delta;
using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// Bitstream parsers for entity/state server messages: delta descriptions,
/// spawn baselines, clientdata, events, sounds, pings, damage and temp entities.
/// The shared <see cref="BufferReader"/> drives both byte and bit reads from
/// one position — no manual bit↔byte bridging.
/// </summary>
public partial class GoldsrcConnection
{
    /// <summary>
    /// Parses one <c>svc_deltadescription</c> and registers the decoded field
    /// table in <see cref="ConnectionContext.DeltaTables"/>. The engine writes
    /// every table inside SV_SendServerinfo, before any delta-compressed
    /// payload, so consumers can rely on the live definitions being present.
    /// Each field entry is itself a delta record against the engine's fixed
    /// meta description (see <see cref="DeltaReader.ReadFieldDescription"/>).
    /// </summary>
    private bool HandleDeltaDescription(ConnectionContext ctx, ref BufferReader reader)
    {
        string name = reader.ReadString();
        Logger.LogDebug($"[DeltaDescription] deltaName=\"{name}\"");

        uint fieldCount = reader.ReadBits(16);
        if (fieldCount > 96)
            throw new InvalidDataException($"Implausible delta fieldCount={fieldCount} for \"{name}\".");

        var fields = new List<DeltaField>((int)fieldCount);
        for (uint f = 0; f < fieldCount; f++)
        {
            var desc = DeltaReader.ReadFieldDescription(ref reader, _variant.DeltaByteCountBits);
            fields.Add(desc.ToDeltaField());
            Logger.LogDebug($"[DeltaDescription] \"{name}\"[{f}] {desc.FieldName} type={desc.FieldType} bits={desc.SignificantBits} pre={desc.Premultiply} post={desc.PostMultiply}");
        }

        ctx.DeltaTables[name] = new DeltaType(name, (byte)fieldCount, fields.ToArray());
        Logger.LogDebug($"[DeltaDescription] registered \"{name}\" with {fieldCount} fields (offset={reader.BytePosition})");
        return true;
    }

    /// <summary>
    /// Resolves the live delta table received via svc_deltadescription, falling
    /// back to the compiled Valve layout when the server has not sent one
    /// (e.g. a message arriving before the descriptions, or hand-crafted tests).
    /// </summary>
    private static DeltaType ResolveDelta(ConnectionContext ctx, DeltaType fallback)
        => ctx.DeltaTables.TryGetValue(fallback.DeltaName, out var dt) ? dt : fallback;

    private bool HandleSpawnBaseline(ConnectionContext ctx, ref BufferReader reader)
    {
        // Sven widened the entity-number field: hw.dll's SV_CreateBaseline reads
        // the width from a variable (FUN_01da9fa0) with the 0xFFFF/16-bit end
        // marker unchanged. Wire-verified (message dump): 13 bits (8192 edicts)
        // cleanly enumerates every baseline and hits the end marker; Valve
        // keeps 11 bits (2048 edicts).
        int entBits = _variant.EntityIndexBits;
        int maxEntity = (1 << entBits) - 1;

        if (Environment.GetEnvironmentVariable("GOLDSRC_SKIPBASELINE") == "1")
        {
            Logger.LogWarning("[SpawnBaseline] skipped (diagnostics)");
            return true;
        }

        int entityCount = 0;
        while (true)
        {
            uint entityNumber = reader.ReadBits(entBits);

            if (entityNumber == maxEntity)
            {
                reader.ReadBits(16 - entBits); // rest of the 16-bit end marker
                break;
            }

            uint entityType = reader.ReadBits(2);

            DeltaType dt;
            if ((entityType & 1) != 0)
            {
                bool isPlayer = entityNumber >= 1 && entityNumber <= ctx.MaxClients;
                dt = isPlayer
                    ? ResolveDelta(ctx, DeltaDefinitions.EntityStatePlayer)
                    : ResolveDelta(ctx, DeltaDefinitions.EntityState);
            }
            else
            {
                dt = ResolveDelta(ctx, DeltaDefinitions.CustomEntityState);
            }

            DeltaReader.ReadFields(dt, ref reader, _variant.DeltaByteCountBits);
            entityCount++;
        }

        uint baselineCount = reader.ReadBits(6);
        Logger.LogDebug($"[SpawnBaseline] entities={entityCount}, baselineCount={baselineCount}");
        var baselineDelta = ResolveDelta(ctx, DeltaDefinitions.EntityState);
        for (uint ei = 0; ei < baselineCount; ei++)
            DeltaReader.ReadFields(baselineDelta, ref reader, _variant.DeltaByteCountBits);

        reader.Align();
        Logger.LogDebug($"[SpawnBaseline] done, bitPosition={reader.BitPosition}, newOffset={reader.BytePosition}");

        TrySendSpawn(ctx);

        return true;
    }

    private bool HandleClientData(ConnectionContext ctx, ref BufferReader reader)
    {
        uint haveDeltaSeq = reader.ReadBits(1);
        if (haveDeltaSeq != 0)
        {
            uint deltaSeq = reader.ReadBits(8);
            Logger.LogDebug($"[ClientData] deltaSeq={deltaSeq}");
        }
        var clientDelta = ResolveDelta(ctx, DeltaDefinitions.ClientData);
        var weaponDelta = ResolveDelta(ctx, DeltaDefinitions.WeaponData);
        DeltaReader.ReadFields(clientDelta, ref reader, _variant.DeltaByteCountBits);

        int weaponCount = 0;
        while (true)
        {
            uint haveDelta = reader.ReadBits(1);
            if (haveDelta == 0) break;
            reader.ReadBits(6); // weapon index
            DeltaReader.ReadFields(weaponDelta, ref reader, _variant.DeltaByteCountBits);
            weaponCount++;
        }
        Logger.LogDebug($"[ClientData] done, weaponDeltas={weaponCount}");
        reader.Align();
        return true;
    }

    private bool HandleEvent(ConnectionContext ctx, ref BufferReader reader, bool reliable)
    {
        // svc_event:      5 bits count, then per event: 10 bits index,
        //                 1 bit ent-in-pack -> 11 bits packet index,
        //                 1 bit has-args -> delta event_args_t, 1 bit has-fire -> 16 bits.
        // svc_event_reliable: same per-event layout without the count and packet index.
        var eventDelta = ResolveDelta(ctx, DeltaDefinitions.Event);
        uint count = 1;
        if (!reliable)
            count = reader.ReadBits(5);

        for (uint e = 0; e < count; e++)
        {
            uint eventIndex = reader.ReadBits(10);

            if (!reliable)
            {
                uint hasEnts = reader.ReadBits(1);
                if (hasEnts != 0)
                    reader.ReadBits(11); // packet index
            }

            if (!reliable)
            {
                uint hasArgs = reader.ReadBits(1);
                if (hasArgs != 0)
                    DeltaReader.ReadFields(eventDelta, ref reader, _variant.DeltaByteCountBits);
            }
            else
            {
                DeltaReader.ReadFields(eventDelta, ref reader, _variant.DeltaByteCountBits);
            }

            uint hasFire = reader.ReadBits(1);
            if (hasFire != 0)
                reader.ReadBits(16); // fire time

            Logger.LogDebug($"[Event] index={eventIndex}{(reliable ? " (reliable)" : "")}");
        }

        reader.Align();
        return true;
    }

    private bool HandleSound(ref BufferReader reader)
    {
        uint fieldMask = reader.ReadBits(9);

        if ((fieldMask & SoundFlags.Volume) != 0)
            reader.ReadBits(8);
        if ((fieldMask & SoundFlags.Attenuation) != 0)
            reader.ReadBits(8);

        uint channel = reader.ReadBits(3);
        // Entity index uses the same entity-bits width as the baselines.
        uint entity = reader.ReadBits(_variant.EntityIndexBits);
        int snBits = (fieldMask & SoundFlags.LargeIndex) != 0 ? 16 : 8;
        uint soundNum = reader.ReadBits(snBits);

        uint xf = reader.ReadBits(1);
        uint yf = reader.ReadBits(1);
        uint zf = reader.ReadBits(1);
        if (xf != 0) ReadCoord(ref reader);
        if (yf != 0) ReadCoord(ref reader);
        if (zf != 0) ReadCoord(ref reader);

        if ((fieldMask & SoundFlags.Pitch) != 0)
            reader.ReadBits(8);
        Logger.LogDebug($"[Sound] channel={channel}, entity={entity}, soundNum={soundNum}, fieldMask=0x{fieldMask:X4}");
        reader.Align();
        return true;
    }

    private bool HandlePings(ref BufferReader reader)
    {
        for (int i = 0; i < 32; i++)
        {
            uint hasEntry = reader.ReadBits(1);
            if (hasEntry == 0) break;
            reader.ReadBits(5); // slot
            reader.ReadBits(12); // ping
            reader.ReadBits(7); // loss
        }
        reader.Align();
        return true;
    }

    /// <summary>
    /// svc_tempentity: one effect type byte followed by a bit-packed payload
    /// whose layout depends on the type. See <see cref="TempEntityParser.Skip"/>.
    /// </summary>
    private bool HandleTempEntity(ref BufferReader reader)
    {
        if (!TempEntityParser.Skip(ref reader, _variant))
        {
            Logger.LogWarning("[TempEntity] unparseable effect, aborting packet");
            return false;
        }
        reader.Align();
        return true;
    }

    /// <summary>
    /// svc_damage (Quake-inherited layout): armor byte, blood byte, then three
    /// bit coordinates for the damage origin — always present.
    /// </summary>
    private bool HandleDamage(ref BufferReader reader)
    {
        reader.ReadBits(8); // armor
        reader.ReadBits(8); // blood
        for (int i = 0; i < 3; i++)
            ReadCoord(ref reader);
        reader.Align();
        return true;
    }

    /// <summary>
    /// Reads one coordinate with the width matching the target engine branch:
    /// Sven's 32-bit 16.16 fixed point, or Valve's 18-bit bit coordinate.
    /// </summary>
    private void ReadCoord(ref BufferReader reader)
    {
        if (_variant.WideCoordinates)
            reader.ReadBitCoordWide();
        else
            reader.ReadBitCoord();
    }
}
