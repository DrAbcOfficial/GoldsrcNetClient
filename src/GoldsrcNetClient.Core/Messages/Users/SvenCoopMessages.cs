using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Messages.Parsing;

namespace GoldsrcNetClient.Core.Messages.Users;

/// <summary>
/// Server message handler for Sven Co-op.
/// Extends <see cref="HalfLifeMessageHandler"/> with the complete Sven Co-op user
/// message set parsed from wire formats reverse-engineered out of
/// <c>svencoop/cl_dlls/client.dll</c> (see <c>sven_coop_usermsgs.md</c>).
/// </summary>
/// <remarks>
/// <para>Message indices are learned at runtime from <c>svc_newusermsg</c> and dispatched
/// by name, so no compiled-in index table is needed. The client registers its hooks with
/// <c>pfnHookUserMsg</c> under the names used below — those literal strings are the
/// authoritative wire names (the same strings the game DLL announces to us).</para>
///
/// <para>Sven replaces several Half-Life formats with wider types; those are overridden
/// here and raised as <c>Sc*</c> events: CurWeapon (byte,short,long,long), Health (long),
/// Battery (byte), AmmoX/AmmoPickup (byte,long), WeapPickup (short), WeaponList
/// (long ammo maxima), TextMsg (4 format params), HudText (single string), Concuss
/// (3 floats), Fog (extended), GameTitle (no payload), ShowMenu, VGUIMenu, HideHUD (short),
/// Damage (Half-Life layout, 32-bit coords).</para>
///
/// <para>Sven coordinates are 32-bit on the wire (int32 × 1/8), unlike stock GoldSrc's
/// 16-bit coords — use <see cref="ReadCoord32"/> in any parser added here. Verified
/// live against the dedicated server's user-message registration sizes (Damage=18,
/// Fog=24, WeatherFX=68, …).</para>
///
/// <para>Messages handled inside VGUI panel virtuals are decoded through the panel
/// classes' vtables: MapList (CMapVotePanel::vftable+0x21C) and VoteMenu
/// (CVotePopup::vftable+0x214). ClExtrasInfo frames a CryptoPP authenticated-encryption
/// blob whose key is derived client-side by GenerateKey from the ClServerInfo
/// handshake — only its wire framing can be decoded, and the opaque blocks are exposed
/// as-is.</para>
/// </remarks>
public static class SvenCoopMessages
{
    /// <summary>
    /// Registers the complete Sven Co-op user message set on top of the shared
    /// Half-Life vocabulary. Sven re-declares several shared names with its own
    /// wider wire formats — later registrations replace earlier ones, so those
    /// entries simply override the inherited parsers.
    /// </summary>
    public static void Register(ParserRegistry.Builder builder)
    {
        HalfLifeMessages.Register(builder);
        builder
            .AddUser("CurWeapon", static (ref BufferReader r) => ParseCurWeapon(ref r))
            .AddUser("Health", static (ref BufferReader r) => ParseHealth(ref r))
            .AddUser("Battery", static (ref BufferReader r) => ParseBattery(ref r))
            .AddUser("AmmoX", static (ref BufferReader r) => ParseAmmoX(ref r))
            .AddUser("AmmoPickup", static (ref BufferReader r) => ParseAmmoPickup(ref r))
            .AddUser("WeapPickup", static (ref BufferReader r) => ParseWeapPickup(ref r))
            .AddUser("WeaponList", static (ref BufferReader r) => ParseWeaponList(ref r))
            .AddUser("TextMsg", static (ref BufferReader r) => ParseTextMsg(ref r))
            .AddUser("HudText", static (ref BufferReader r) => ParseHudText(ref r))
            .AddUser("GameTitle", static (ref BufferReader r) => ParseGameTitle(ref r))
            .AddUser("Concuss", static (ref BufferReader r) => ParseConcuss(ref r))
            .AddUser("Fog", static (ref BufferReader r) => ParseFog(ref r))
            .AddUser("VGUIMenu", static (ref BufferReader r) => ParseVguiMenu(ref r))
            .AddUser("ShowMenu", static (ref BufferReader r) => ParseShowMenu(ref r))
            .AddUser("HideHUD", static (ref BufferReader r) => ParseHideHUD(ref r))
            .AddUser("VoiceMask", static (ref BufferReader r) => ParseVoiceMask(ref r))
            .AddUser("Spectator", static (ref BufferReader r) => ParseSpectator(ref r))
            .AddUser("AllowSpec", static (ref BufferReader r) => ParseAllowSpec(ref r))
            .AddUser("TeamScore", static (ref BufferReader r) => ParseTeamScore(ref r))
            .AddUser("ScoreInfo", static (ref BufferReader r) => ParseScoreInfo(ref r))
            .AddUser("TeamNames", static (ref BufferReader r) => ParseTeamNames(ref r))
            .AddUser("MOTD", static (ref BufferReader r) => ParseMotd(ref r))
            .AddUser("ServerName", static (ref BufferReader r) => ParseServerName(ref r))
            .AddUser("ServerVer", static (ref BufferReader r) => ParseServerVersion(ref r))
            .AddUser("ServerBuild", static (ref BufferReader r) => ParseServerBuild(ref r))
            .AddUser("NextMap", static (ref BufferReader r) => ParseNextMap(ref r))
            .AddUser("ViewMode", static (ref BufferReader r) => ParseViewMode(ref r))
            .AddUser("CdAudio", static (ref BufferReader r) => ParseCdAudio(ref r))
            .AddUser("ClassicMode", static (ref BufferReader r) => ParseClassicMode(ref r))
            .AddUser("VModelPos", static (ref BufferReader r) => ParseVModelPos(ref r))
            .AddUser("TimeEnd", static (ref BufferReader r) => ParseTimeEnd(ref r))
            .AddUser("OnTank", static (ref BufferReader r) => ParseOnTank(ref r))
            .AddUser("Playlist", static (ref BufferReader r) => ParsePlaylist(ref r))
            .AddUser("Speaksent", static (ref BufferReader r) => ParseSentence(ref r))
            .AddUser("ValClass", static (ref BufferReader r) => ParseValClass(ref r))
            .AddUser("PrtlUpdt", static (ref BufferReader r) => ParsePortalUpdate(ref r))
            .AddUser("InvAdd", static (ref BufferReader r) => ParseInventoryAdd(ref r))
            .AddUser("InvRemove", static (ref BufferReader r) => ParseInventoryRemove(ref r))
            .AddUser("ToggleElem", static (ref BufferReader r) => ParseToggleElem(ref r))
            .AddUser("CustSpr", static (ref BufferReader r) => ParseCustomSprite(ref r))
            .AddUser("NumDisplay", static (ref BufferReader r) => ParseNumDisplay(ref r))
            .AddUser("UpdateNum", static (ref BufferReader r) => ParseUpdateNum(ref r))
            .AddUser("TimeDisplay", static (ref BufferReader r) => ParseTimeDisplay(ref r))
            .AddUser("UpdateTime", static (ref BufferReader r) => ParseUpdateTime(ref r))
            .AddUser("WeaponSpr", static (ref BufferReader r) => ParseWeaponSprite(ref r))
            .AddUser("CustWeapon", static (ref BufferReader r) => ParseCustomWeapon(ref r))
            .AddUser("PrintKB", static (ref BufferReader r) => ParseKeyBinding(ref r))
            .AddUser("NotifyText", static (ref BufferReader r) => ParseNotifyText(ref r))
            .AddUser("Gib", static (ref BufferReader r) => ParseGib(ref r))
            .AddUser("TE_CUSTOM", static (ref BufferReader r) => ParseTeCustom(ref r))
            .AddUser("CbElec", static (ref BufferReader r) => ParseCbElec(ref r))
            .AddUser("ShkFlash", static (ref BufferReader r) => ParseShkFlash(ref r))
            .AddUser("TracerDecal", static (ref BufferReader r) => ParseTracerDecal(ref r))
            .AddUser("SporeTrail", static (ref BufferReader r) => ParseSporeTrail(ref r))
            .AddUser("CreateBlood", static (ref BufferReader r) => ParseCreateBlood(ref r))
            .AddUser("GargSplash", static (ref BufferReader r) => ParseGargSplash(ref r))
            .AddUser("StartSound", static (ref BufferReader r) => ParseStartSound(ref r))
            .AddUser("ToxicCloud", static (ref BufferReader r) => ParseToxicCloud(ref r))
            .AddUser("SRDetonate", static (ref BufferReader r) => ParseSrDetonate(ref r))
            .AddUser("SRPrimed", static (ref BufferReader r) => ParseSrPrimed(ref r))
            .AddUser("SRPrimedOff", static (ref BufferReader r) => ParseSrPrimedOff(ref r))
            .AddUser("RampSprite", static (ref BufferReader r) => ParseRampSprite(ref r))
            .AddUser("ShieldRic", static (ref BufferReader r) => ParseShieldRic(ref r))
            .AddUser("WeatherFX", static (ref BufferReader r) => ParseWeatherFx(ref r))
            .AddUser("CameraMouse", static (ref BufferReader r) => ParseCameraMouse(ref r))
            .AddUser("Flamethwr", static (ref BufferReader r) => ParseFlamethrower(ref r))
            .AddUser("ChangeSky", static (ref BufferReader r) => ParseChangeSky(ref r))
            .AddUser("ClServerInfo", static (ref BufferReader r) => ParseClServerInfo(ref r))
            .AddUser("ClExtrasInfo", static (ref BufferReader r) => ParseClExtrasInfo(ref r))
            .AddUser("EndVote", static (ref BufferReader r) => ParseEndVote(ref r))
            .AddUser("MapList", static (ref BufferReader r) => ParseMapList(ref r))
            .AddUser("VoteMenu", static (ref BufferReader r) => ParseVoteMenu(ref r))
;
    }



    // ── Shared-name overrides (Sven wire format differs from Half-Life) ──

    /// <summary>CurWeapon (Sven): byte state, short weaponId (-1 hides), long clip, long reserve.</summary>
    private static ScCurWeaponMessage ParseCurWeapon(ref BufferReader r)
    {
        return new ScCurWeaponMessage(r.ReadUInt8(), r.ReadInt16(), r.ReadInt32(), r.ReadInt32());
    }

    /// <summary>Health (Sven): 32-bit value.</summary>
    private static ScHealthMessage ParseHealth(ref BufferReader r)
    {
        return new ScHealthMessage(r.ReadInt32());
    }

    /// <summary>Battery (Sven): single byte.</summary>
    private static ScBatteryMessage ParseBattery(ref BufferReader r)
    {
        return new ScBatteryMessage(r.ReadUInt8());
    }

    /// <summary>AmmoX (Sven): byte index, 32-bit count.</summary>
    private static ScAmmoXMessage ParseAmmoX(ref BufferReader r)
    {
        return new ScAmmoXMessage(r.ReadUInt8(), r.ReadInt32());
    }

    /// <summary>AmmoPickup (Sven): byte index, 32-bit count.</summary>
    private static ScAmmoPickupMessage ParseAmmoPickup(ref BufferReader r)
    {
        return new ScAmmoPickupMessage(r.ReadUInt8(), r.ReadInt32());
    }

    /// <summary>WeapPickup (Sven): weapon id as a short.</summary>
    private static ScWeapPickupMessage ParseWeapPickup(ref BufferReader r)
    {
        return new ScWeapPickupMessage(r.ReadInt16());
    }

    /// <summary>WeaponList (Sven): string, char id, long max, char id, long max, char slot, char pos, short wid, byte flags.</summary>
    private static ScWeaponListMessage ParseWeaponList(ref BufferReader r)
    {
        var name = r.ReadString();
        sbyte primaryId = ReadSByte(ref r);
        int primaryMax = r.ReadInt32();
        sbyte secondaryId = ReadSByte(ref r);
        int secondaryMax = r.ReadInt32();
        sbyte slot = ReadSByte(ref r);
        sbyte position = ReadSByte(ref r);
        short weaponId = r.ReadInt16();
        byte flags = r.ReadUInt8();
        return new ScWeaponListMessage(name, (byte)primaryId, primaryMax == 0xFF ? -1 : primaryMax,
            (byte)secondaryId, secondaryMax == 0xFF ? -1 : secondaryMax,
            (byte)slot, (byte)position, weaponId, flags);
    }

    /// <summary>TextMsg (Sven): destination byte plus message and four "#localisable" parameters.</summary>
    private static ScTextMsgMessage ParseTextMsg(ref BufferReader r)
    {
        byte dest = r.ReadUInt8();
        string message = r.ReadString();
        string p1 = r.ReadString();
        string p2 = r.ReadString();
        string p3 = r.ReadString();
        string p4 = r.ReadString();
        return new ScTextMsgMessage(dest, message, p1, p2, p3, p4);
    }

    /// <summary>HudText (Sven): a single text/localisation string.</summary>
    private static ScHudTextMessage ParseHudText(ref BufferReader r)
    {
        return new ScHudTextMessage(r.ReadString());
    }

    /// <summary>GameTitle (Sven): no payload — the message itself means "show".</summary>
    private static GameTitleMessage ParseGameTitle(ref BufferReader r)
    {
        return new GameTitleMessage(1);
    }

    /// <summary>Concuss (Sven): direction vector as three floats.</summary>
    private static ScConcussMessage ParseConcuss(ref BufferReader r)
    {
        return new ScConcussMessage(ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r));
    }

    /// <summary>Fog (Sven): short, enable byte, 3 coords, short, RGB bytes, 2 shorts.
    /// The leading short and the coordinates are read but discarded by the client.</summary>
    private static ScFogMessage ParseFog(ref BufferReader r)
    {
        r.ReadInt16(); // leading, unused
        bool enabled = r.ReadUInt8() != 0;
        float x = ReadCoord32(ref r), y = ReadCoord32(ref r), z = ReadCoord32(ref r);
        short unknown = r.ReadInt16();
        byte red = r.ReadUInt8(), green = r.ReadUInt8(), blue = r.ReadUInt8();
        return new ScFogMessage(enabled, x, y, z, unknown, red, green, blue, r.ReadInt16(), r.ReadInt16());
    }

    /// <summary>VGUIMenu (Sven): menu type byte; type 4 carries a parameter string.</summary>
    private static VguiMenuMessage ParseVguiMenu(ref BufferReader r)
    {
        byte type = r.ReadUInt8();
        string data = type == 4 ? r.ReadString() : string.Empty;
        return new VguiMenuMessage(type, data);
    }

    /// <summary>Damage: same field order as Half-Life, but Sven coordinates are 32-bit
    /// (the dedicated server registers Damage with size 18 = 1+1+4+3×4).</summary>
    private static DamageMessage ParseDamage(ref BufferReader r)
    {
        byte save = r.ReadUInt8();
        byte take = r.ReadUInt8();
        int damageType = r.ReadInt32();
        return new DamageMessage(save, take, damageType, ReadCoord32(ref r), ReadCoord32(ref r), ReadCoord32(ref r));
    }

    // ── Sven-specific parsers ──

    /// <summary>ShowMenu (Sven): byte slot mask, signed display time, flag byte, text.</summary>
    private static ScShowMenuMessage ParseShowMenu(ref BufferReader r)
    {
        return new ScShowMenuMessage(r.ReadUInt8(), ReadSByte(ref r), r.ReadUInt8(), r.ReadString());
    }

    /// <summary>HideHUD (Sven): 16-bit hide mask.</summary>
    private static ScHideHudMessage ParseHideHUD(ref BufferReader r)
    {
        return new ScHideHudMessage(r.ReadInt16());
    }

    /// <summary>VoiceMask: two 32-bit audibility/ban masks plus a flag byte.</summary>
    private static VoiceMaskMessage ParseVoiceMask(ref BufferReader r)
    {
        return new VoiceMaskMessage(r.ReadInt32(), r.ReadInt32(), r.ReadUInt8());
    }

    /// <summary>Spectator: player index and spectator flag, both bytes.</summary>
    private static SpectatorMessage ParseSpectator(ref BufferReader r)
    {
        return new SpectatorMessage(r.ReadUInt8(), r.ReadUInt8());
    }

    /// <summary>AllowSpec: single allow byte.</summary>
    private static AllowSpecMessage ParseAllowSpec(ref BufferReader r)
    {
        return new AllowSpecMessage(r.ReadUInt8());
    }

    /// <summary>TeamScore (Sven): team name and two 16-bit scores.</summary>
    private static ScTeamScoreMessage ParseTeamScore(ref BufferReader r)
    {
        var team = r.ReadString();
        short score = r.ReadInt16();
        short score2 = r.ReadInt16();
        return new ScTeamScoreMessage(team, score, score2);
    }

    /// <summary>ScoreInfo (Sven): byte index, float score, long, float, float, class byte, 2 bytes.</summary>
    private static ScScoreInfoMessage ParseScoreInfo(ref BufferReader r)
    {
        return new ScScoreInfoMessage(
            r.ReadUInt8(), ReadFloat(ref r), r.ReadInt32(), ReadFloat(ref r), ReadFloat(ref r),
            r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8());
    }

    /// <summary>TeamNames (Sven): team count, then per team a name and an RGB colour triple.</summary>
    private static ScTeamNamesMessage ParseTeamNames(ref BufferReader r)
    {
        byte count = r.ReadUInt8();
        var teams = new ScTeamNamesTeam[count];
        for (int i = 0; i < count; i++)
        {
            var name = r.ReadString();
            float cr = ReadCoord32(ref r), cg = ReadCoord32(ref r), cb = ReadCoord32(ref r);
            teams[i] = new ScTeamNamesTeam(name, cr, cg, cb);
        }
        return new ScTeamNamesMessage(teams);
    }

    /// <summary>MOTD (Sven): final-chunk byte plus text; chunks concatenate on the client.</summary>
    private static ScMotdMessage ParseMotd(ref BufferReader r)
    {
        return new ScMotdMessage(r.ReadUInt8() != 0, r.ReadString());
    }

    /// <summary>ServerName (Sven): hostname string.</summary>
    private static ScServerNameMessage ParseServerName(ref BufferReader r)
    {
        return new ScServerNameMessage(r.ReadString());
    }

    /// <summary>ServerVer (Sven): version string.</summary>
    private static ScServerVersionMessage ParseServerVersion(ref BufferReader r)
    {
        return new ScServerVersionMessage(r.ReadString());
    }

    /// <summary>ServerBuild (Sven): build string.</summary>
    private static ScServerBuildMessage ParseServerBuild(ref BufferReader r)
    {
        return new ScServerBuildMessage(r.ReadString());
    }

    /// <summary>NextMap (Sven): map name string.</summary>
    private static ScNextMapMessage ParseNextMap(ref BufferReader r)
    {
        return new ScNextMapMessage(r.ReadString());
    }

    /// <summary>ViewMode (Sven): 0 = first person, anything else = third person.</summary>
    private static ScViewModeMessage ParseViewMode(ref BufferReader r)
    {
        return new ScViewModeMessage(r.ReadUInt8() != 0);
    }

    /// <summary>CdAudio (Sven): track byte (0 = stop, 1..30 = media/Half-LifeXX).</summary>
    private static ScCdAudioMessage ParseCdAudio(ref BufferReader r)
    {
        return new ScCdAudioMessage(r.ReadUInt8());
    }

    /// <summary>ClassicMode (Sven): mode toggle byte.</summary>
    private static ScClassicModeMessage ParseClassicMode(ref BufferReader r)
    {
        return new ScClassicModeMessage(r.ReadUInt8() != 0);
    }

    /// <summary>VModelPos (Sven): enable byte; when set, a coordinate triple.</summary>
    private static ScVModelPosMessage ParseVModelPos(ref BufferReader r)
    {
        bool enabled = r.ReadUInt8() == 1;
        float x = 0, y = 0, z = 0;
        if (enabled)
        {
            x = ReadCoord32(ref r); y = ReadCoord32(ref r); z = ReadCoord32(ref r);
        }
        return new ScVModelPosMessage(enabled, x, y, z);
    }

    /// <summary>TimeEnd (Sven): 32-bit round end time (client adds its clock).</summary>
    private static ScTimeEndMessage ParseTimeEnd(ref BufferReader r)
    {
        return new ScTimeEndMessage(r.ReadInt32());
    }

    /// <summary>OnTank (Sven): tank driving flag byte.</summary>
    private static ScOnTankMessage ParseOnTank(ref BufferReader r)
    {
        return new ScOnTankMessage(r.ReadUInt8() != 0);
    }

    /// <summary>Playlist (Sven): playlist name.</summary>
    private static ScPlaylistMessage ParsePlaylist(ref BufferReader r)
    {
        return new ScPlaylistMessage(r.ReadString());
    }

    /// <summary>Speaksent (Sven): sentence name the client feeds to the "speak" command.</summary>
    private static ScSentenceMessage ParseSentence(ref BufferReader r)
    {
        return new ScSentenceMessage(r.ReadString());
    }

    /// <summary>ValClass (Sven): five class/slot shorts.</summary>
    private static ScValClassMessage ParseValClass(ref BufferReader r)
    {
        var classes = new short[5];
        for (int i = 0; i < 5; i++) classes[i] = r.ReadInt16();
        return new ScValClassMessage(classes);
    }

    /// <summary>PrtlUpdt (Sven): portal/monitor update with conditional sections.
    /// entity(long), enable(byte; 0 removes), vec1, vec2, type(byte), style(byte), life(float),
    /// byte, long, long, flag(byte); type != 0: flag(byte) → long + coord3 + ang3,
    /// type 1: flag → long,long; type 2: two flag bytes; style 2: name string.</summary>
    private static ScPortalUpdateMessage ParsePortalUpdate(ref BufferReader r)
    {        int entity = r.ReadInt32();
        if (r.ReadUInt8() == 0)
        {
            return new ScPortalUpdateMessage(true, entity,
                [], [], 0, 0, 0, 0, 0, 0, false,
                null, null, null, null, null, null, null, null);
        }

        var vec1 = ReadCoordVec3(ref r);
        var vec2 = ReadCoordVec3(ref r);
        byte type = r.ReadUInt8();
        byte style = r.ReadUInt8();
        float life = ReadFloat(ref r);
        byte byte1 = r.ReadUInt8();
        int long1 = r.ReadInt32();
        int long2 = r.ReadInt32();
        bool flag1 = r.ReadUInt8() != 0;

        int? modelIndex = null;
        float[]? modelOrigin = null, modelAngles = null;
        int? long3 = null, long4 = null;
        bool? flagA = null, flagB = null;
        if (type != 0)
        {
            if (r.ReadUInt8() != 0)
            {
                modelIndex = r.ReadInt32();
                modelOrigin = ReadCoordVec3(ref r);
                modelAngles = ReadAngleVec3(ref r);
            }
            if (type == 1)
            {
                if (r.ReadUInt8() != 0)
                {
                    long3 = r.ReadInt32();
                    long4 = r.ReadInt32();
                }
            }
            else if (type == 2)
            {
                flagA = r.ReadUInt8() != 0;
                flagB = r.ReadUInt8() != 0;
            }
        }
        string? name = style == 2 ? r.ReadString() : null;

        return new ScPortalUpdateMessage(false, entity, vec1, vec2, type, style, life,
            byte1, long1, long2, flag1, modelIndex, modelOrigin, modelAngles, long3, long4, flagA, flagB, name);
}

    /// <summary>InvAdd (Sven): id(long), 3 flag bytes, time(float), 5 strings.</summary>
    private static ScInventoryAddMessage ParseInventoryAdd(ref BufferReader r)
    {
        int id = r.ReadInt32();
        bool f1 = r.ReadUInt8() != 0, f2 = r.ReadUInt8() != 0, f3 = r.ReadUInt8() != 0;
        float time = ReadFloat(ref r);
        return new ScInventoryAddMessage(id, f1, f2, f3, time,
            r.ReadString(), r.ReadString(), r.ReadString(), r.ReadString(), r.ReadString());
    }

    /// <summary>InvRemove (Sven): id (0 removes all) and a flag byte.</summary>
    private static ScInventoryRemoveMessage ParseInventoryRemove(ref BufferReader r)
    {
        return new ScInventoryRemoveMessage(r.ReadInt32(), r.ReadUInt8());
    }

    /// <summary>ToggleElem (Sven): HUD channel byte (0-31) and state byte.</summary>
    private static ScToggleElemMessage ParseToggleElem(ref BufferReader r)
    {
        return new ScToggleElemMessage(r.ReadUInt8(), r.ReadUInt8() != 0);
    }

    /// <summary>CustSpr (Sven): channel, flags(long), sprite, x, y, w(short), h(short),
    /// two RGBA quads, two bytes, five floats, final byte.</summary>
    private static ScCustomSpriteMessage ParseCustomSprite(ref BufferReader r)
    {
        byte channel = r.ReadUInt8();
        int flags = r.ReadInt32();
        var sprite = r.ReadString();
        byte x = r.ReadUInt8(), y = r.ReadUInt8();
        short width = r.ReadInt16(), height = r.ReadInt16();
        byte r1 = r.ReadUInt8(), g1 = r.ReadUInt8(), b1 = r.ReadUInt8(), a1 = r.ReadUInt8();
        byte r2 = r.ReadUInt8(), g2 = r.ReadUInt8(), b2 = r.ReadUInt8(), a2 = r.ReadUInt8();
        byte unknown1 = r.ReadUInt8(), unknown2 = r.ReadUInt8();
        return new ScCustomSpriteMessage(channel, flags, sprite, x, y, width, height,
            r1, g1, b1, a1, r2, g2, b2, a2, unknown1, unknown2,
            ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r), r.ReadUInt8());
    }

    /// <summary>NumDisplay (Sven): channel, flags(long), value(float), x, y, width/height floats,
    /// two RGBA quads, font string, region bytes/shorts, four floats, final byte.</summary>
    private static ScNumDisplayMessage ParseNumDisplay(ref BufferReader r)
    {
        byte channel = r.ReadUInt8();
        int flags = r.ReadInt32();
        float value = ReadFloat(ref r);
        byte x = r.ReadUInt8(), y = r.ReadUInt8();
        float width = ReadFloat(ref r), height = ReadFloat(ref r);
        byte r1 = r.ReadUInt8(), g1 = r.ReadUInt8(), b1 = r.ReadUInt8(), a1 = r.ReadUInt8();
        byte r2 = r.ReadUInt8(), g2 = r.ReadUInt8(), b2 = r.ReadUInt8(), a2 = r.ReadUInt8();
        var font = r.ReadString();
        byte regionX = r.ReadUInt8(), regionY = r.ReadUInt8();
        short regionWidth = r.ReadInt16(), regionHeight = r.ReadInt16();
        return new ScNumDisplayMessage(channel, flags, value, x, y, width, height,
            r1, g1, b1, a1, r2, g2, b2, a2, font, regionX, regionY, regionWidth, regionHeight,
            ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r), r.ReadUInt8());
    }

    /// <summary>UpdateNum (Sven): channel byte and float value.</summary>
    private static ScUpdateNumMessage ParseUpdateNum(ref BufferReader r)
    {
        return new ScUpdateNumMessage(r.ReadUInt8(), ReadFloat(ref r));
    }

    /// <summary>TimeDisplay (Sven): like NumDisplay but with two leading value floats.</summary>
    private static ScTimeDisplayMessage ParseTimeDisplay(ref BufferReader r)
    {
        byte channel = r.ReadUInt8();
        int flags = r.ReadInt32();
        float value1 = ReadFloat(ref r), value2 = ReadFloat(ref r);
        float width = ReadFloat(ref r), height = ReadFloat(ref r);
        byte r1 = r.ReadUInt8(), g1 = r.ReadUInt8(), b1 = r.ReadUInt8(), a1 = r.ReadUInt8();
        byte r2 = r.ReadUInt8(), g2 = r.ReadUInt8(), b2 = r.ReadUInt8(), a2 = r.ReadUInt8();
        var font = r.ReadString();
        byte regionX = r.ReadUInt8(), regionY = r.ReadUInt8();
        short regionWidth = r.ReadInt16(), regionHeight = r.ReadInt16();
        return new ScTimeDisplayMessage(channel, flags, value1, value2, width, height,
            r1, g1, b1, a1, r2, g2, b2, a2, font, regionX, regionY, regionWidth, regionHeight,
            ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r), r.ReadUInt8());
    }

    /// <summary>UpdateTime (Sven): channel byte, time float, duration float.</summary>
    private static ScUpdateTimeMessage ParseUpdateTime(ref BufferReader r)
    {
        return new ScUpdateTimeMessage(r.ReadUInt8(), ReadFloat(ref r), ReadFloat(ref r));
    }

    /// <summary>WeaponSpr (Sven): HUD slot short and sprite name.</summary>
    private static WeaponSpriteMessage ParseWeaponSprite(ref BufferReader r)
    {        return new WeaponSpriteMessage(r.ReadInt16(), r.ReadString());
}

    /// <summary>CustWeapon (Sven): HUD slot short and custom weapon name.</summary>
    private static CustomWeaponMessage ParseCustomWeapon(ref BufferReader r)
    {        return new CustomWeaponMessage(r.ReadInt16(), r.ReadString());
}

    /// <summary>PrintKB (Sven): key binding name string.</summary>
    private static KeyBindingMessage ParseKeyBinding(ref BufferReader r)
    {        return new KeyBindingMessage(r.ReadString());
}

    /// <summary>NotifyText (Sven): type byte and text.</summary>
    private static NotifyTextMessage ParseNotifyText(ref BufferReader r)
    {        return new NotifyTextMessage(r.ReadUInt8(), r.ReadString());
}

    /// <summary>Gib (Sven): gib type byte (0, 1, 2, 4 valid) then origin and velocity triples.
    /// Unknown types carry no coordinates.</summary>
    private static ScGibMessage ParseGib(ref BufferReader r)
    {
        byte type = r.ReadUInt8();
        float ox = 0, oy = 0, oz = 0, vx = 0, vy = 0, vz = 0;
        if (type is 0 or 1 or 2 or 4)
        {
            ox = ReadCoord32(ref r); oy = ReadCoord32(ref r); oz = ReadCoord32(ref r);
            vx = ReadCoord32(ref r); vy = ReadCoord32(ref r); vz = ReadCoord32(ref r);
        }
        return new ScGibMessage(type, ox, oy, oz, vx, vy, vz);
    }

    /// <summary>TE_CUSTOM (Sven): subtyped effect. Subtype 1: id(short), count(short),
    /// origin triple when count > 0. Subtype 3: one byte. Subtype 2: empty.</summary>
    private static ScTeCustomMessage ParseTeCustom(ref BufferReader r)
    {
        byte subType = r.ReadUInt8();
        short id = 0, count = 0;
        float ox = 0, oy = 0, oz = 0;
        byte value = 0;
        if (subType == 1)
        {
            id = r.ReadInt16();
            count = r.ReadInt16();
            if (count > 0)
            {
                ox = ReadCoord32(ref r); oy = ReadCoord32(ref r); oz = ReadCoord32(ref r);
            }
        }
        else if (subType == 3)
        {
            value = r.ReadUInt8();
        }
        return new ScTeCustomMessage(subType, id, count, ox, oy, oz, value);
    }

    /// <summary>CbElec (Sven): state byte — bits 0-4 entity index, bit 6 active.</summary>
    private static ScCbElecMessage ParseCbElec(ref BufferReader r)
    {
        byte data = r.ReadUInt8();
        return new ScCbElecMessage((data & 0x40) != 0, (byte)(data & 0x1F));
    }

    /// <summary>ShkFlash (Sven): origin triple plus mode byte (0 = fire, else impact).</summary>
    private static ScShkFlashMessage ParseShkFlash(ref BufferReader r)
    {
        float x = ReadCoord32(ref r), y = ReadCoord32(ref r), z = ReadCoord32(ref r);
        return new ScShkFlashMessage(x, y, z, r.ReadUInt8());
    }

    /// <summary>TracerDecal (Sven): start/end triples, decal type byte, trailing byte.</summary>
    private static ScTracerDecalMessage ParseTracerDecal(ref BufferReader r)
    {
        float sx = ReadCoord32(ref r), sy = ReadCoord32(ref r), sz = ReadCoord32(ref r);
        float ex = ReadCoord32(ref r), ey = ReadCoord32(ref r), ez = ReadCoord32(ref r);
        byte type = r.ReadUInt8();
        byte unknown = r.ReadUInt8();
        return new ScTracerDecalMessage(sx, sy, sz, ex, ey, ez, type, unknown);
    }

    /// <summary>SporeTrail (Sven): entity short and attach flag byte.</summary>
    private static ScSporeTrailMessage ParseSporeTrail(ref BufferReader r)
    {
        short entity = r.ReadInt16();
        return new ScSporeTrailMessage(entity, r.ReadUInt8() != 0);
    }

    /// <summary>CreateBlood (Sven): origin triple, colour byte, amount byte.</summary>
    private static ScCreateBloodMessage ParseCreateBlood(ref BufferReader r)
    {
        float x = ReadCoord32(ref r), y = ReadCoord32(ref r), z = ReadCoord32(ref r);
        return new ScCreateBloodMessage(x, y, z, r.ReadUInt8(), r.ReadUInt8());
    }

    /// <summary>GargSplash (Sven): origin triple plus colour triple. The client reads the
    /// colour channels as coordinates and takes their absolute value (float bit-mask AND),
    /// then feeds them to the splash temp entity as floats.</summary>
    private static ScGargSplashMessage ParseGargSplash(ref BufferReader r)
    {
        float x = ReadCoord32(ref r), y = ReadCoord32(ref r), z = ReadCoord32(ref r);
        float cr = MathF.Abs(ReadCoord32(ref r));
        float cg = MathF.Abs(ReadCoord32(ref r));
        float cb = MathF.Abs(ReadCoord32(ref r));
        return new ScGargSplashMessage(x, y, z, cr, cg, cb);
    }

    /// <summary>StartSound (Sven): flag-driven fields; see <see cref="ScStartSoundEvent"/>.</summary>
    private static ScStartSoundMessage ParseStartSound(ref BufferReader r)
    {
        short flags = r.ReadInt16();
        short? entity = (flags & 0x10) != 0 ? r.ReadInt16() : null;
        byte? volume = (flags & 0x01) != 0 ? r.ReadUInt8() : null;
        byte? attenuation = (flags & 0x02) != 0 ? r.ReadUInt8() : null;
        byte? pitch = (flags & 0x04) != 0 ? r.ReadUInt8() : null;
        float? ox = null, oy = null, oz = null;
        if ((flags & 0x08) != 0)
        {
            ox = ReadCoord32(ref r); oy = ReadCoord32(ref r); oz = ReadCoord32(ref r);
        }
        float? duration = (flags & 0x8000) != 0 ? ReadFloat(ref r) : null;
        byte channel = r.ReadUInt8();
        short soundIndex = r.ReadInt16();
        return new ScStartSoundMessage(flags, entity, volume, attenuation, pitch, ox, oy, oz, duration, channel, soundIndex);
    }

    /// <summary>ToxicCloud (Sven): origin triple.</summary>
    private static ScToxicCloudMessage ParseToxicCloud(ref BufferReader r)
    {
        return new ScToxicCloudMessage(ReadCoord32(ref r), ReadCoord32(ref r), ReadCoord32(ref r));
    }

    /// <summary>SRDetonate (Sven): origin triple and radius byte.</summary>
    private static ScSrDetonateMessage ParseSrDetonate(ref BufferReader r)
    {
        float x = ReadCoord32(ref r), y = ReadCoord32(ref r), z = ReadCoord32(ref r);
        return new ScSrDetonateMessage(x, y, z, r.ReadUInt8());
    }

    /// <summary>SRPrimed (Sven): entity byte and fuse float.</summary>
    private static ScSrPrimedMessage ParseSrPrimed(ref BufferReader r)
    {
        byte entity = r.ReadUInt8();
        return new ScSrPrimedMessage(entity, ReadFloat(ref r));
    }

    /// <summary>SRPrimedOff (Sven): entity byte.</summary>
    private static ScSrPrimedOffMessage ParseSrPrimedOff(ref BufferReader r)
    {
        return new ScSrPrimedOffMessage(r.ReadUInt8());
    }

    /// <summary>RampSprite (Sven): entity short, life byte, origin triple, flags short, then
    /// optional per-flag fields; see <see cref="ScRampSpriteEvent"/>.</summary>
    private static ScRampSpriteMessage ParseRampSprite(ref BufferReader r)
    {
        short entity = r.ReadInt16();
        byte lifeTicks = r.ReadUInt8();
        float x = ReadCoord32(ref r), y = ReadCoord32(ref r), z = ReadCoord32(ref r);
        short flags = r.ReadInt16();

        byte? startTime = null, fadeIn = null, fadeOut = null, renderModeFx = null;
        byte? red = null, green = null, blue = null, unknownBit15 = null;
        if ((flags & 0x1) != 0) startTime = r.ReadUInt8();
        if ((flags & 0x2) != 0) fadeIn = r.ReadUInt8();
        if ((flags & 0x4) != 0) fadeOut = r.ReadUInt8();
        if ((flags & 0x8) != 0) renderModeFx = r.ReadUInt8();
        if ((flags & 0x10) != 0) red = r.ReadUInt8();
        if ((flags & 0x20) != 0) green = r.ReadUInt8();
        if ((flags & 0x40) != 0) blue = r.ReadUInt8();
        if ((flags & 0x8000) != 0) unknownBit15 = r.ReadUInt8();

        float? color2R = null, color2G = null, color2B = null;
        if ((flags & 0x100) != 0)
        {
            color2R = ReadCoord32(ref r); color2G = ReadCoord32(ref r); color2B = ReadCoord32(ref r);
        }
        return new ScRampSpriteMessage(entity, lifeTicks, x, y, z, flags,
            startTime, fadeIn, fadeOut, renderModeFx, red, green, blue, unknownBit15,
            color2R, color2G, color2B,
            (flags & 0x200) != 0 ? r.ReadUInt8() : null,
            (flags & 0x400) != 0 ? r.ReadUInt8() : null,
            (flags & 0x800) != 0 ? r.ReadUInt8() : null,
            (flags & 0x1000) != 0 ? r.ReadUInt8() : null,
            (flags & 0x2000) != 0 ? r.ReadUInt8() : null,
            (flags & 0x4000) != 0 ? r.ReadUInt8() : null,
            (flags & 0x8000) != 0 ? r.ReadUInt8() : null);
    }

    /// <summary>ShieldRic (Sven): origin triple.</summary>
    private static ScShieldRicMessage ParseShieldRic(ref BufferReader r)
    {
        return new ScShieldRicMessage(ReadCoord32(ref r), ReadCoord32(ref r), ReadCoord32(ref r));
    }

    /// <summary>WeatherFX (Sven): type short, min/max triples, angle triple, then a fixed
    /// tail of short/byte/float groups passed through opaquely by the client.</summary>
    private static ScWeatherFxMessage ParseWeatherFx(ref BufferReader r)
    {
        short type = r.ReadInt16();
        float minX = ReadCoord32(ref r), minY = ReadCoord32(ref r), minZ = ReadCoord32(ref r);
        float maxX = ReadCoord32(ref r), maxY = ReadCoord32(ref r), maxZ = ReadCoord32(ref r);
        float ax = ReadAngle(ref r), ay = ReadAngle(ref r), az = ReadAngle(ref r);
        short unknownShort1 = r.ReadInt16();
        float float1 = ReadFloat(ref r);
        byte byte1 = r.ReadUInt8();
        short unknownShort2 = r.ReadInt16();
        float float2 = ReadFloat(ref r);
        byte byte2 = r.ReadUInt8(), byte3 = r.ReadUInt8();
        float float3 = ReadFloat(ref r);
        byte byte4 = r.ReadUInt8(), byte5 = r.ReadUInt8(), byte6 = r.ReadUInt8(), byte7 = r.ReadUInt8();
        return new ScWeatherFxMessage(type, minX, minY, minZ, maxX, maxY, maxZ, ax, ay, az,
            unknownShort1, float1, byte1, unknownShort2, float2, byte2, byte3, float3,
            byte4, byte5, byte6, byte7, ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r));
    }

    /// <summary>CameraMouse (Sven): mode byte; mode 2 carries a parameter string.</summary>
    private static ScCameraMouseMessage ParseCameraMouse(ref BufferReader r)
    {
        byte mode = r.ReadUInt8();
        return new ScCameraMouseMessage(mode, mode == 2 ? r.ReadString() : string.Empty);
    }

    /// <summary>Flamethwr (Sven): entity byte plus start/end triples.</summary>
    private static ScFlamethrowerMessage ParseFlamethrower(ref BufferReader r)
    {
        byte entity = r.ReadUInt8();
        float sx = ReadCoord32(ref r), sy = ReadCoord32(ref r), sz = ReadCoord32(ref r);
        float ex = ReadCoord32(ref r), ey = ReadCoord32(ref r), ez = ReadCoord32(ref r);
        return new ScFlamethrowerMessage(entity, sx, sy, sz, ex, ey, ez);
    }

    /// <summary>ChangeSky (Sven): sky name string and colour triple (-1s keep the default).</summary>
    private static ScChangeSkyMessage ParseChangeSky(ref BufferReader r)
    {
        var sky = r.ReadString();
        float cr = ReadCoord32(ref r), cg = ReadCoord32(ref r), cb = ReadCoord32(ref r);
        return new ScChangeSkyMessage(sky, cr, cg, cb);
    }

    /// <summary>ClServerInfo (Sven): flag byte, 32-bit value and key string.</summary>
    private static ScClServerInfoMessage ParseClServerInfo(ref BufferReader r)
    {
        return new ScClServerInfoMessage(r.ReadUInt8(), r.ReadInt32(), r.ReadString());
    }

    /// <summary>EndVote (Sven): empty payload.</summary>
    private static EndVoteMessage ParseEndVote(ref BufferReader r)
    {        return new EndVoteMessage();
}

    /// <summary>MapList (Sven): decoded from CMapVotePanel's vtable+0x21C virtual. A command
    /// byte selects reset (0: clears the list and stores the total count), close (0x7B), or
    /// an incremental update (start/end shorts plus one map-name string per entry).</summary>
    private static ScMapListMessage ParseMapList(ref BufferReader r)
    {
        byte command = r.ReadUInt8();
        short totalMaps = 0, startIndex = 0, endIndex = 0;
        string[] mapNames = [];
        if (command == 0)
        {
            totalMaps = r.ReadInt16();
        }
        else if (command != 0x7B)
        {
            startIndex = r.ReadInt16();
            endIndex = r.ReadInt16();
            int count = endIndex - startIndex;
            if (count > 0)
            {
                mapNames = new string[count];
                for (int i = 0; i < count; i++)
                    mapNames[i] = r.ReadString();
            }
        }
        return new ScMapListMessage(command, totalMaps, startIndex, endIndex, mapNames);
    }

    /// <summary>VoteMenu (Sven): decoded from the vote panel's vtable+0x214 virtual:
    /// vote id byte, question string, yes-label and no-label strings (empty labels
    /// fall back to "#Menu_Yes"/"#Menu_No" on the client).</summary>
    private static ScVoteMenuMessage ParseVoteMenu(ref BufferReader r)
    {
        byte voteId = r.ReadUInt8();
        return new ScVoteMenuMessage(voteId, r.ReadString(), r.ReadString(), r.ReadString());
    }

    /// <summary>ClExtrasInfo (Sven): four length-prefixed blocks framing a CryptoPP
    /// authenticated-encryption payload — plain length, IV, encrypted data, and a
    /// digest sized to the session key. The client derives the key via GenerateKey
    /// from the ClServerInfo handshake and decrypts to
    /// "playerIndex\nauthId\n\"name\"\nlevel" which updates the scoreboard's per-player
    /// admin level; the key material never leaves the client, so only the framing is
    /// decoded here.</summary>
    private static ScClExtrasInfoMessage ParseClExtrasInfo(ref BufferReader r)
    {
        int plainLength = r.ReadInt32();
        byte[] iv = ReadBlock(ref r);
        byte[] encryptedData = ReadBlock(ref r);
        byte[] encryptedDigest = ReadBlock(ref r);
        return new ScClExtrasInfoMessage(plainLength, iv, encryptedData, encryptedDigest);
    }

    /// <summary>Reads a 32-bit length followed by that many bytes (empty on invalid length).</summary>
    private static byte[] ReadBlock(ref BufferReader r)
    {
        int length = r.ReadInt32();
        return length <= 0 ? [] : r.ReadBytes(length);
    }

    // ── read helpers ──

    /// <summary>Reads a Sven Co-op coordinate: 32-bit on the wire, scaled by 1/8 —
    /// unlike stock GoldSrc's 16-bit coordinate. Verified against the dedicated server's
    /// user-message registration sizes (Damage=18, Fog=24, WeatherFX=68, ShkFlash=13,
    /// CreateBlood=14, SRDetonate=13, ShieldRic=12), which only add up with 4-byte coords.</summary>
    private static float ReadCoord32(ref BufferReader r)
    {
        return r.ReadInt32() / 8.0f;
    }

    /// <summary>Reads a little-endian 32-bit float.</summary>
    private static float ReadFloat(ref BufferReader r)
    {
        return r.ReadSingle();
    }

    /// <summary>Reads a signed byte.</summary>
    private static sbyte ReadSByte(ref BufferReader r) => (sbyte)r.ReadUInt8();

    /// <summary>Reads a Sven angle: signed byte scaled by 360/256 (1.40625).</summary>
    private static float ReadAngle(ref BufferReader r) => ReadSByte(ref r) * (360f / 256f);

    /// <summary>Reads a Sven 16-bit angle: signed short scaled by 360/65536.</summary>
    private static float ReadAngle16(ref BufferReader r) => r.ReadInt16() * (360f / 65536f);

    /// <summary>Reads three coordinates into an array (READ_COORD_vec3 helper order).</summary>
    private static float[] ReadCoordVec3(ref BufferReader r) =>
        [ReadCoord32(ref r), ReadCoord32(ref r), ReadCoord32(ref r)];

    /// <summary>Reads three angles into an array (READ_ANGLE_vec3 helper order).</summary>
    private static float[] ReadAngleVec3(ref BufferReader r) =>
        [ReadAngle(ref r), ReadAngle(ref r), ReadAngle(ref r)];
}
