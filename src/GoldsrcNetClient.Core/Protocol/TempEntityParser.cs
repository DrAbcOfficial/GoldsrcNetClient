using GoldsrcNetClient.Core.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoldsrcNetClient.Core.Protocol;

/// <summary>
/// Parses (skips) <c>svc_tempentity</c> payloads. The payload is a bit stream:
/// one byte selects the effect type, then each type carries its own bit-packed
/// field layout (bit coordinates, not byte coordinates — skipping fixed byte
/// counts desynchronises the stream). Field layouts mirror the engine's
/// CL_ParseTempEntity (Xash3D cl_tent.c / cl_efx.c CL_ParseViewBeam, protocol-48
/// GoldSrc branch: no length prefix, the type byte is read directly from the
/// stream, MSG_ReadCoord is a bit coordinate and MSG_ReadAngle an 8-bit angle).
/// </summary>
public static class TempEntityParser
{
    // TE_* constants (common/const.h).
    private const uint TeBeamPoints = 0;
    private const uint TeBeamEntPoint = 1;
    private const uint TeGunshot = 2;
    private const uint TeExplosion = 3;
    private const uint TeTarExplosion = 4;
    private const uint TeSmoke = 5;
    private const uint TeTracer = 6;
    private const uint TeLightning = 7;
    private const uint TeBeamEnts = 8;
    private const uint TeSparks = 9;
    private const uint TeLavaSplash = 10;
    private const uint TeTeleport = 11;
    private const uint TeExplosion2 = 12;
    private const uint TeBspDecal = 13;
    private const uint TeImplosion = 14;
    private const uint TeSpriteTrail = 15;
    private const uint TeBeam = 16;
    private const uint TeSprite = 17;
    private const uint TeBeamSprite = 18;
    private const uint TeBeamTorus = 19;
    private const uint TeBeamDisk = 20;
    private const uint TeBeamCylinder = 21;
    private const uint TeBeamFollow = 22;
    private const uint TeGlowSprite = 23;
    private const uint TeBeamRing = 24;
    private const uint TeStreakSplash = 25;
    private const uint TeBeamHose = 26;
    private const uint TeDLight = 27;
    private const uint TeELight = 28;
    private const uint TeTextMessage = 29;
    private const uint TeLine = 30;
    private const uint TeBox = 31;
    private const uint TeKillBeam = 99;
    private const uint TeLargeFunnel = 100;
    private const uint TeBloodStream = 101;
    private const uint TeShowLine = 102;
    private const uint TeBlood = 103;
    private const uint TeDecal = 104;
    private const uint TeFizz = 105;
    private const uint TeModel = 106;
    private const uint TeExplodeModel = 107;
    private const uint TeBreakModel = 108;
    private const uint TeGunshotDecal = 109;
    private const uint TeSpriteSpray = 110;
    private const uint TeArmorRicochet = 111;
    private const uint TePlayerDecal = 112;
    private const uint TeBubbles = 113;
    private const uint TeBubbleTrail = 114;
    private const uint TeBloodSprite = 115;
    private const uint TeWorldDecal = 116;
    private const uint TeWorldDecalHigh = 117;
    private const uint TeDecalHigh = 118;
    private const uint TeProjectile = 119;
    private const uint TeSpray = 120;
    private const uint TePlayerSprites = 121;
    private const uint TeParticleBurst = 122;
    private const uint TeFireField = 123;
    private const uint TePlayerAttachment = 124;
    private const uint TeKillPlayerAttachments = 125;
    private const uint TeMultiGunshot = 126;
    private const uint TeUserTracer = 127;

    /// <summary>
    /// Skips one temp-entity effect starting at <paramref name="bitIdx"/> (which
    /// must point at the type byte). Returns false when the stream is exhausted
    /// or the type is unknown — in both cases the enclosing packet cannot be
    /// reliably parsed further.
    /// </summary>
    /// <param name="variant">Engine-branch dialect (selects the coordinate encoding).</param>
    public static bool Skip(byte[] data, ref int bitIdx, int size, IEngineVariant variant, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        CoordReader read = variant.WideCoordinates ? ReadCoordWideInner : ReadCoordPacked;
        uint type = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref type, 8))
            return false;

        switch (type)
        {
            // Beams: start/end as points or entity refs, then model + 9 parameter bytes.
            case TeBeamPoints:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 6)) return false;
                if (!ReadShort(data, ref bitIdx, size)) return false;
                return SkipBytes(data, ref bitIdx, size, 9); // startFrame, frameRate, life, width, noise, r, g, b, speed
            }
            case TeBeamEntPoint:
            {
                if (!ReadShort(data, ref bitIdx, size)) return false; // startEnt
                if (!SkipCoords(read, data, ref bitIdx, size, 3)) return false; // end
                if (!ReadShort(data, ref bitIdx, size)) return false;
                return SkipBytes(data, ref bitIdx, size, 9);
            }
            case TeBeamEnts:
            {
                if (!SkipShorts(data, ref bitIdx, size, 2)) return false; // startEnt, endEnt
                if (!ReadShort(data, ref bitIdx, size)) return false;
                return SkipBytes(data, ref bitIdx, size, 9);
            }
            case TeLightning:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 6)) return false;
                if (!ReadShort(data, ref bitIdx, size)) return false;
                return SkipBytes(data, ref bitIdx, size, 3); // life, width, noise
            }
            case TeBeamTorus:
            case TeBeamDisk:
            case TeBeamCylinder:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 6)) return false;
                if (!ReadShort(data, ref bitIdx, size)) return false;
                return SkipBytes(data, ref bitIdx, size, 8); // startFrame, frameRate, life, width, noise, r, g, b
            }
            case TeBeamFollow:
            {
                if (!SkipShorts(data, ref bitIdx, size, 2)) return false; // startEnt, modelIndex
                return SkipBytes(data, ref bitIdx, size, 6); // life, width, r, g, b, a
            }
            case TeBeamRing:
            {
                if (!SkipShorts(data, ref bitIdx, size, 2)) return false; // startEnt, endEnt
                if (!ReadShort(data, ref bitIdx, size)) return false;
                return SkipBytes(data, ref bitIdx, size, 9);
            }
            case TeBeamSprite:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 6)) return false;
                return SkipShorts(data, ref bitIdx, size, 2); // beam model, sprite model
            }
            case TeBeam:
            case TeBeamHose:
                return true; // obsolete, no payload
            case TeKillBeam:
                return ReadShort(data, ref bitIdx, size);

            // Simple point effects.
            case TeGunshot:
            case TeTarExplosion:
            case TeSparks:
            case TeLavaSplash:
            case TeTeleport:
            case TeShowLine:
            case TeArmorRicochet:
                return SkipCoords(read, data, ref bitIdx, size, 3);
            case TeExplosion:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 3)) return false;
                if (!ReadShort(data, ref bitIdx, size)) return false; // modelIndex
                return SkipBytes(data, ref bitIdx, size, 3); // scale, frameRate, flags
            }
            case TeSmoke:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 3)) return false;
                if (!ReadShort(data, ref bitIdx, size)) return false; // modelIndex
                return SkipBytes(data, ref bitIdx, size, 2); // scale, frameRate
            }
            case TeTracer:
                return SkipCoords(read, data, ref bitIdx, size, 6);
            case TeExplosion2:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 3)) return false;
                return SkipBytes(data, ref bitIdx, size, 2); // color, count
            }
            case TeImplosion:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 3)) return false;
                return SkipBytes(data, ref bitIdx, size, 3); // scale, count, life
            }
            case TeSpriteTrail:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 6)) return false;
                if (!ReadShort(data, ref bitIdx, size)) return false; // modelIndex
                return SkipBytes(data, ref bitIdx, size, 5); // count, life, scale, velocity, random
            }
            case TeSprite:
            case TeGlowSprite:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 3)) return false;
                if (!ReadShort(data, ref bitIdx, size)) return false; // modelIndex
                return SkipBytes(data, ref bitIdx, size, 2); // scale, brightness/life
            }
            case TeStreakSplash:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 6)) return false;
                if (!SkipBytes(data, ref bitIdx, size, 1)) return false; // color
                if (!ReadShort(data, ref bitIdx, size)) return false; // count
                return SkipShorts(data, ref bitIdx, size, 2); // velocity, random
            }
            case TeDLight:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 3)) return false;
                return SkipBytes(data, ref bitIdx, size, 6); // radius, r, g, b, life, decay
            }
            case TeELight:
            {
                if (!ReadShort(data, ref bitIdx, size)) return false; // entity
                if (!SkipCoords(read, data, ref bitIdx, size, 3)) return false; // origin
                if (!read(data, ref bitIdx, size)) return false; // radius
                if (!SkipBytes(data, ref bitIdx, size, 4)) return false; // r, g, b, life
                return read(data, ref bitIdx, size); // decay
            }
            case TeTextMessage:
                return SkipTextMessage(data, ref bitIdx, size);
            case TeLine:
            case TeBox:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 6)) return false;
                if (!ReadShort(data, ref bitIdx, size)) return false; // life
                return SkipBytes(data, ref bitIdx, size, 3); // r, g, b
            }

            // Decals.
            case TeBspDecal:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 3)) return false;
                if (!ReadShort(data, ref bitIdx, size)) return false; // decalIndex
                uint entityIndex = 0;
                if (!BitReader.ReadBits(data, ref bitIdx, size, ref entityIndex, 16)) return false;
                // modelIndex only follows when the decal is shot onto a brush
                // entity (wire-verified: world decals end the message here).
                return entityIndex == 0 || ReadShort(data, ref bitIdx, size);
            }
            case TeDecal:
            case TeDecalHigh:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 3)) return false;
                if (!SkipBytes(data, ref bitIdx, size, 1)) return false; // decalIndex
                return ReadShort(data, ref bitIdx, size); // entityIndex
            }
            case TeWorldDecal:
            case TeWorldDecalHigh:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 3)) return false;
                return SkipBytes(data, ref bitIdx, size, 1); // decalIndex
            }

            // Entity/model effects.
            case TeLargeFunnel:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 3)) return false;
                return SkipShorts(data, ref bitIdx, size, 2); // modelIndex, flags
            }
            case TeBloodStream:
            case TeBlood:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 6)) return false;
                return SkipBytes(data, ref bitIdx, size, 2); // color, count
            }
            case TeFizz:
            {
                if (!SkipShorts(data, ref bitIdx, size, 2)) return false; // entity, model
                return SkipBytes(data, ref bitIdx, size, 1); // density
            }
            case TeModel:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 6)) return false;
                if (!ReadAngle8(data, ref bitIdx, size)) return false; // yaw
                if (!ReadShort(data, ref bitIdx, size)) return false; // modelIndex
                return SkipBytes(data, ref bitIdx, size, 2); // sound flags, life
            }
            case TeExplodeModel:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 3)) return false;
                if (!read(data, ref bitIdx, size)) return false; // velocity
                if (!SkipShorts(data, ref bitIdx, size, 2)) return false; // modelIndex, count
                return SkipBytes(data, ref bitIdx, size, 1); // life
            }
            case TeBreakModel:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 9)) return false; // mins, maxs, angles
                if (!SkipBytes(data, ref bitIdx, size, 1)) return false; // random
                if (!ReadShort(data, ref bitIdx, size)) return false; // modelIndex
                return SkipBytes(data, ref bitIdx, size, 3); // count, life, flags
            }
            case TeGunshotDecal:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 3)) return false;
                if (!ReadShort(data, ref bitIdx, size)) return false; // entityIndex
                return SkipBytes(data, ref bitIdx, size, 1); // decalIndex
            }
            case TeSpriteSpray:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 6)) return false;
                if (!ReadShort(data, ref bitIdx, size)) return false; // modelIndex
                return SkipBytes(data, ref bitIdx, size, 3); // count, velocity, random
            }
            case TePlayerDecal:
            {
                if (!SkipBytes(data, ref bitIdx, size, 1)) return false; // playernum
                if (!SkipCoords(read, data, ref bitIdx, size, 3)) return false;
                if (!ReadShort(data, ref bitIdx, size)) return false; // entityIndex
                return SkipBytes(data, ref bitIdx, size, 1); // decalIndex
            }
            case TeBubbles:
            case TeBubbleTrail:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 6)) return false;
                if (!read(data, ref bitIdx, size)) return false; // water height
                if (!ReadShort(data, ref bitIdx, size)) return false; // modelIndex
                if (!SkipBytes(data, ref bitIdx, size, 1)) return false; // count
                return read(data, ref bitIdx, size); // velocity
            }
            case TeBloodSprite:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 3)) return false;
                if (!SkipShorts(data, ref bitIdx, size, 2)) return false; // sprite1, sprite2
                return SkipBytes(data, ref bitIdx, size, 2); // color, scale
            }
            case TeProjectile:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 6)) return false;
                if (!ReadShort(data, ref bitIdx, size)) return false; // modelIndex
                return SkipBytes(data, ref bitIdx, size, 2); // life, playernum
            }
            case TeSpray:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 6)) return false;
                if (!ReadShort(data, ref bitIdx, size)) return false; // modelIndex
                return SkipBytes(data, ref bitIdx, size, 4); // count, velocity, random, rendermode
            }
            case TePlayerSprites:
            {
                if (!SkipShorts(data, ref bitIdx, size, 2)) return false; // entity, modelIndex
                return SkipBytes(data, ref bitIdx, size, 2); // count, random
            }
            case TeParticleBurst:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 3)) return false;
                if (!ReadShort(data, ref bitIdx, size)) return false; // radius
                return SkipBytes(data, ref bitIdx, size, 2); // color, life
            }
            case TeFireField:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 3)) return false;
                if (!SkipShorts(data, ref bitIdx, size, 2)) return false; // radius, modelIndex
                return SkipBytes(data, ref bitIdx, size, 3); // count, flags, life
            }
            case TePlayerAttachment:
            {
                if (!SkipBytes(data, ref bitIdx, size, 1)) return false; // playernum
                if (!read(data, ref bitIdx, size)) return false; // height
                if (!ReadShort(data, ref bitIdx, size)) return false; // modelIndex
                return ReadShort(data, ref bitIdx, size); // life
            }
            case TeKillPlayerAttachments:
                return SkipBytes(data, ref bitIdx, size, 1); // playernum
            case TeMultiGunshot:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 8)) return false; // origin, direction(×0.1), angles(×0.01)
                return SkipBytes(data, ref bitIdx, size, 2); // count, decalIndex
            }
            case TeUserTracer:
            {
                if (!SkipCoords(read, data, ref bitIdx, size, 6)) return false;
                return SkipBytes(data, ref bitIdx, size, 3); // life, color, scale
            }
            default:
                // Unknown types have an indeterminate length; the packet cannot stay in sync.
                return false;
        }
    }

    private delegate bool CoordReader(byte[] data, ref int bitIdx, int size);

    private static bool ReadCoordPacked(byte[] data, ref int bitIdx, int size)
    {
        float v = 0;
        return BitReader.ReadBitCoord(data, ref bitIdx, size, ref v);
    }

    private static bool ReadCoordWideInner(byte[] data, ref int bitIdx, int size)
    {
        float v = 0;
        return BitReader.ReadCoordWide(data, ref bitIdx, size, ref v);
    }

    private static bool ReadAngle8(byte[] data, ref int bitIdx, int size)
    {
        float v = 0;
        return BitReader.ReadBitAngle(data, ref bitIdx, size, ref v, 8);
    }

    private static bool SkipCoords(CoordReader read, byte[] data, ref int bitIdx, int size, int count)
    {
        for (int i = 0; i < count; i++)
            if (!read(data, ref bitIdx, size))
                return false;
        return true;
    }

    private static bool SkipShorts(byte[] data, ref int bitIdx, int size, int count)
    {
        for (int i = 0; i < count; i++)
            if (!ReadShort(data, ref bitIdx, size))
                return false;
        return true;
    }

    private static bool ReadShort(byte[] data, ref int bitIdx, int size)
    {
        uint v = 0;
        return BitReader.ReadBits(data, ref bitIdx, size, ref v, 16);
    }

    private static bool SkipBytes(byte[] data, ref int bitIdx, int size, int count)
    {
        for (int i = 0; i < count; i++)
        {
            uint v = 0;
            if (!BitReader.ReadBits(data, ref bitIdx, size, ref v, 8))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Skips a TE_TEXTMESSAGE payload: channel byte, x/y screen positions
    /// (16-bit fixed /8192), effect byte, two RGBA color quads, fadein/fadeout/
    /// holdtime (16-bit fixed /256), fxtime (only when effect == 2), and the
    /// null-terminated message string.
    /// </summary>
    private static bool SkipTextMessage(byte[] data, ref int bitIdx, int size)
    {
        if (!SkipBytes(data, ref bitIdx, size, 1)) return false;  // channel
        if (!SkipShorts(data, ref bitIdx, size, 2)) return false; // x, y

        uint effect = 0;
        if (!BitReader.ReadBits(data, ref bitIdx, size, ref effect, 8)) return false;

        if (!SkipBytes(data, ref bitIdx, size, 8)) return false;  // r1,g1,b1,a1, r2,g2,b2,a2
        if (!SkipShorts(data, ref bitIdx, size, 3)) return false; // fadein, fadeout, holdtime
        if (effect == 2 && !SkipShorts(data, ref bitIdx, size, 1)) return false; // fxtime
        return BitReader.ReadBitString(data, ref bitIdx, size, out _); // message
    }
}
