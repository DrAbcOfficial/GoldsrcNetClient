using GoldsrcNetClient.Core.Io;
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
/// Reads past the end of the buffer throw <see cref="EndOfBufferException"/>.
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
    /// Skips one temp-entity effect starting at the reader's bit position (which
    /// must point at the type byte). Returns false when the effect type is
    /// unknown — its length is indeterminate, so the enclosing packet cannot be
    /// reliably parsed further. Underflow throws instead.
    /// </summary>
    /// <param name="reader">Reader positioned at the effect type byte; advanced past the effect.</param>
    /// <param name="variant">Engine-branch dialect (selects the coordinate encoding).</param>
    public static bool Skip(ref BufferReader reader, IEngineVariant variant)
    {
        bool wide = variant.WideCoordinates;
        uint type = reader.ReadBits(8);

        switch (type)
        {
            // Beams: start/end as points or entity refs, then model + 9 parameter bytes.
            case TeBeamPoints:
                SkipCoords(ref reader, wide, 6);
                reader.ReadBits(16); // modelIndex
                SkipBytes(ref reader, 9); // startFrame, frameRate, life, width, noise, r, g, b, speed
                return true;
            case TeBeamEntPoint:
                reader.ReadBits(16); // startEnt
                SkipCoords(ref reader, wide, 3); // end
                reader.ReadBits(16);
                SkipBytes(ref reader, 9);
                return true;
            case TeBeamEnts:
                SkipShorts(ref reader, 2); // startEnt, endEnt
                reader.ReadBits(16);
                SkipBytes(ref reader, 9);
                return true;
            case TeLightning:
                SkipCoords(ref reader, wide, 6);
                reader.ReadBits(16);
                SkipBytes(ref reader, 3); // life, width, noise
                return true;
            case TeBeamTorus:
            case TeBeamDisk:
            case TeBeamCylinder:
                SkipCoords(ref reader, wide, 6);
                reader.ReadBits(16);
                SkipBytes(ref reader, 8); // startFrame, frameRate, life, width, noise, r, g, b
                return true;
            case TeBeamFollow:
                SkipShorts(ref reader, 2); // startEnt, modelIndex
                SkipBytes(ref reader, 6); // life, width, r, g, b, a
                return true;
            case TeBeamRing:
                SkipShorts(ref reader, 2); // startEnt, endEnt
                reader.ReadBits(16);
                SkipBytes(ref reader, 9);
                return true;
            case TeBeamSprite:
                SkipCoords(ref reader, wide, 6);
                SkipShorts(ref reader, 2); // beam model, sprite model
                return true;
            case TeBeam:
            case TeBeamHose:
                return true; // obsolete, no payload
            case TeKillBeam:
                reader.ReadBits(16);
                return true;

            // Simple point effects.
            case TeGunshot:
            case TeTarExplosion:
            case TeSparks:
            case TeLavaSplash:
            case TeTeleport:
            case TeShowLine:
            case TeArmorRicochet:
                SkipCoords(ref reader, wide, 3);
                return true;
            case TeExplosion:
                SkipCoords(ref reader, wide, 3);
                reader.ReadBits(16); // modelIndex
                SkipBytes(ref reader, 3); // scale, frameRate, flags
                return true;
            case TeSmoke:
                SkipCoords(ref reader, wide, 3);
                reader.ReadBits(16); // modelIndex
                SkipBytes(ref reader, 2); // scale, frameRate
                return true;
            case TeTracer:
                SkipCoords(ref reader, wide, 6);
                return true;
            case TeExplosion2:
                SkipCoords(ref reader, wide, 3);
                SkipBytes(ref reader, 2); // color, count
                return true;
            case TeImplosion:
                SkipCoords(ref reader, wide, 3);
                SkipBytes(ref reader, 3); // scale, count, life
                return true;
            case TeSpriteTrail:
                SkipCoords(ref reader, wide, 6);
                reader.ReadBits(16); // modelIndex
                SkipBytes(ref reader, 5); // count, life, scale, velocity, random
                return true;
            case TeSprite:
            case TeGlowSprite:
                SkipCoords(ref reader, wide, 3);
                reader.ReadBits(16); // modelIndex
                SkipBytes(ref reader, 2); // scale, brightness/life
                return true;
            case TeStreakSplash:
                SkipCoords(ref reader, wide, 6);
                SkipBytes(ref reader, 1); // color
                reader.ReadBits(16); // count
                SkipShorts(ref reader, 2); // velocity, random
                return true;
            case TeDLight:
                SkipCoords(ref reader, wide, 3);
                SkipBytes(ref reader, 6); // radius, r, g, b, life, decay
                return true;
            case TeELight:
                reader.ReadBits(16); // entity
                SkipCoords(ref reader, wide, 3); // origin
                ReadCoord(ref reader, wide); // radius
                SkipBytes(ref reader, 4); // r, g, b, life
                ReadCoord(ref reader, wide); // decay
                return true;
            case TeTextMessage:
                SkipTextMessage(ref reader);
                return true;
            case TeLine:
            case TeBox:
                SkipCoords(ref reader, wide, 6);
                reader.ReadBits(16); // life
                SkipBytes(ref reader, 3); // r, g, b
                return true;

            // Decals.
            case TeBspDecal:
            {
                SkipCoords(ref reader, wide, 3);
                reader.ReadBits(16); // decalIndex
                uint entityIndex = reader.ReadBits(16);
                // modelIndex only follows when the decal is shot onto a brush
                // entity (wire-verified: world decals end the message here).
                if (entityIndex != 0)
                    reader.ReadBits(16);
                return true;
            }
            case TeDecal:
            case TeDecalHigh:
                SkipCoords(ref reader, wide, 3);
                SkipBytes(ref reader, 1); // decalIndex
                reader.ReadBits(16); // entityIndex
                return true;
            case TeWorldDecal:
            case TeWorldDecalHigh:
                SkipCoords(ref reader, wide, 3);
                SkipBytes(ref reader, 1); // decalIndex
                return true;

            // Entity/model effects.
            case TeLargeFunnel:
                SkipCoords(ref reader, wide, 3);
                SkipShorts(ref reader, 2); // modelIndex, flags
                return true;
            case TeBloodStream:
            case TeBlood:
                SkipCoords(ref reader, wide, 6);
                SkipBytes(ref reader, 2); // color, count
                return true;
            case TeFizz:
                SkipShorts(ref reader, 2); // entity, model
                SkipBytes(ref reader, 1); // density
                return true;
            case TeModel:
                SkipCoords(ref reader, wide, 6);
                reader.ReadBitAngle(8); // yaw
                reader.ReadBits(16); // modelIndex
                SkipBytes(ref reader, 2); // sound flags, life
                return true;
            case TeExplodeModel:
                SkipCoords(ref reader, wide, 3);
                ReadCoord(ref reader, wide); // velocity
                SkipShorts(ref reader, 2); // modelIndex, count
                SkipBytes(ref reader, 1); // life
                return true;
            case TeBreakModel:
                SkipCoords(ref reader, wide, 9); // mins, maxs, angles
                SkipBytes(ref reader, 1); // random
                reader.ReadBits(16); // modelIndex
                SkipBytes(ref reader, 3); // count, life, flags
                return true;
            case TeGunshotDecal:
                SkipCoords(ref reader, wide, 3);
                reader.ReadBits(16); // entityIndex
                SkipBytes(ref reader, 1); // decalIndex
                return true;
            case TeSpriteSpray:
                SkipCoords(ref reader, wide, 6);
                reader.ReadBits(16); // modelIndex
                SkipBytes(ref reader, 3); // count, velocity, random
                return true;
            case TePlayerDecal:
                SkipBytes(ref reader, 1); // playernum
                SkipCoords(ref reader, wide, 3);
                reader.ReadBits(16); // entityIndex
                SkipBytes(ref reader, 1); // decalIndex
                return true;
            case TeBubbles:
            case TeBubbleTrail:
                SkipCoords(ref reader, wide, 6);
                ReadCoord(ref reader, wide); // water height
                reader.ReadBits(16); // modelIndex
                SkipBytes(ref reader, 1); // count
                ReadCoord(ref reader, wide); // velocity
                return true;
            case TeBloodSprite:
                SkipCoords(ref reader, wide, 3);
                SkipShorts(ref reader, 2); // sprite1, sprite2
                SkipBytes(ref reader, 2); // color, scale
                return true;
            case TeProjectile:
                SkipCoords(ref reader, wide, 6);
                reader.ReadBits(16); // modelIndex
                SkipBytes(ref reader, 2); // life, playernum
                return true;
            case TeSpray:
                SkipCoords(ref reader, wide, 6);
                reader.ReadBits(16); // modelIndex
                SkipBytes(ref reader, 4); // count, velocity, random, rendermode
                return true;
            case TePlayerSprites:
                SkipShorts(ref reader, 2); // entity, modelIndex
                SkipBytes(ref reader, 2); // count, random
                return true;
            case TeParticleBurst:
                SkipCoords(ref reader, wide, 3);
                reader.ReadBits(16); // radius
                SkipBytes(ref reader, 2); // color, life
                return true;
            case TeFireField:
                SkipCoords(ref reader, wide, 3);
                SkipShorts(ref reader, 2); // radius, modelIndex
                SkipBytes(ref reader, 3); // count, flags, life
                return true;
            case TePlayerAttachment:
                SkipBytes(ref reader, 1); // playernum
                ReadCoord(ref reader, wide); // height
                reader.ReadBits(16); // modelIndex
                reader.ReadBits(16); // life
                return true;
            case TeKillPlayerAttachments:
                SkipBytes(ref reader, 1); // playernum
                return true;
            case TeMultiGunshot:
                SkipCoords(ref reader, wide, 8); // origin, direction(×0.1), angles(×0.01)
                SkipBytes(ref reader, 2); // count, decalIndex
                return true;
            case TeUserTracer:
                SkipCoords(ref reader, wide, 6);
                SkipBytes(ref reader, 3); // life, color, scale
                return true;
            default:
                // Unknown types have an indeterminate length; the packet cannot stay in sync.
                return false;
        }
    }

    private static void ReadCoord(ref BufferReader reader, bool wide)
    {
        if (wide)
            reader.ReadBitCoordWide();
        else
            reader.ReadBitCoord();
    }

    private static void SkipCoords(ref BufferReader reader, bool wide, int count)
    {
        for (int i = 0; i < count; i++)
            ReadCoord(ref reader, wide);
    }

    private static void SkipShorts(ref BufferReader reader, int count)
    {
        for (int i = 0; i < count; i++)
            reader.ReadBits(16);
    }

    private static void SkipBytes(ref BufferReader reader, int count)
    {
        for (int i = 0; i < count; i++)
            reader.ReadBits(8);
    }

    /// <summary>
    /// Skips a TE_TEXTMESSAGE payload: channel byte, x/y screen positions
    /// (16-bit fixed /8192), effect byte, two RGBA color quads, fadein/fadeout/
    /// holdtime (16-bit fixed /256), fxtime (only when effect == 2), and the
    /// null-terminated message string.
    /// </summary>
    private static void SkipTextMessage(ref BufferReader reader)
    {
        SkipBytes(ref reader, 1);  // channel
        SkipShorts(ref reader, 2); // x, y

        uint effect = reader.ReadBits(8);

        SkipBytes(ref reader, 8);  // r1,g1,b1,a1, r2,g2,b2,a2
        SkipShorts(ref reader, 3); // fadein, fadeout, holdtime
        if (effect == 2)
            SkipShorts(ref reader, 1); // fxtime
        reader.ReadBitString(); // message
    }
}
