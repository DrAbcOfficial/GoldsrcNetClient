using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Network;

namespace GoldsrcNetClient.Core.Game;

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
public class SvenCoopMessageHandler : HalfLifeMessageHandler
{
    #region Events

    /// <summary>Raised for the map vote list (decoded through CMapVotePanel's vtable handler).</summary>
    public event Action<ScMapListEvent>? ScMapList;
    /// <summary>Raised when a yes/no vote prompt is displayed (decoded through the vote panel's vtable handler).</summary>
    public event Action<ScVoteMenuEvent>? ScVoteMenu;
    /// <summary>Raised for the encrypted per-player authorisation update (framing only —
    /// the payload is CryptoPP AEAD sealed with a client-derived key).</summary>
    public event Action<ScClExtrasInfoEvent>? ScClExtrasInfo;
    /// <summary>Raised when a player's active weapon changes (Sven wire format).</summary>
    public event Action<ScCurWeaponEvent>? ScCurWeapon;
    /// <summary>Raised when health updates (32-bit).</summary>
    public event Action<ScHealthEvent>? ScHealth;
    /// <summary>Raised when armor updates (single byte).</summary>
    public event Action<ScBatteryEvent>? ScBattery;
    /// <summary>Raised when reserve ammo changes (32-bit count).</summary>
    public event Action<ScAmmoXEvent>? ScAmmoX;
    /// <summary>Raised when ammo is picked up (32-bit count).</summary>
    public event Action<ScAmmoPickupEvent>? ScAmmoPickup;
    /// <summary>Raised when a weapon is picked up (id as short).</summary>
    public event Action<ScWeapPickupEvent>? ScWeapPickup;
    /// <summary>Raised when a weapon is registered (32-bit ammo maxima).</summary>
    public event Action<ScWeaponListEvent>? ScWeaponList;
    /// <summary>Raised when a text message is sent (with 4 format parameters).</summary>
    public event Action<ScTextMsgEvent>? ScTextMsg;
    /// <summary>Raised when HUD text is sent.</summary>
    public event Action<ScHudTextEvent>? ScHudText;
    /// <summary>Raised on a concussion effect (direction vector).</summary>
    public event Action<ScConcussEvent>? ScConcuss;
    /// <summary>Raised when fog settings change (extended format).</summary>
    public event Action<ScFogEvent>? ScFog;
    /// <summary>Raised when a text menu is displayed.</summary>
    public event Action<ScShowMenuEvent>? ScShowMenu;
    /// <summary>Raised when HUD elements are hidden (16-bit mask).</summary>
    public event Action<ScHideHudEvent>? ScHideHud;
    /// <summary>Raised when the server sends the voice audibility mask.</summary>
    public event Action<VoiceMaskEvent>? VoiceMask;
    /// <summary>Raised when the camera perspective changes.</summary>
    public event Action<ScViewModeEvent>? ScViewMode;
    /// <summary>Raised when the server requests an MP3 track.</summary>
    public event Action<ScCdAudioEvent>? ScCdAudio;
    /// <summary>Raised when classic gameplay mode toggles.</summary>
    public event Action<ScClassicModeEvent>? ScClassicMode;
    /// <summary>Raised when the view model offset changes.</summary>
    public event Action<ScVModelPosEvent>? ScVModelPos;
    /// <summary>Raised when the round timer end is announced.</summary>
    public event Action<ScTimeEndEvent>? ScTimeEnd;
    /// <summary>Raised when the player enters/leaves a func tank.</summary>
    public event Action<ScOnTankEvent>? ScOnTank;
    /// <summary>Raised when the media playlist changes.</summary>
    public event Action<ScPlaylistEvent>? ScPlaylist;
    /// <summary>Raised when the server requests sentence playback.</summary>
    public event Action<ScSentenceEvent>? ScSentence;
    /// <summary>Raised when class/slot values are validated.</summary>
    public event Action<ScValClassEvent>? ScValClass;
    /// <summary>Raised when the team list with colours is sent.</summary>
    public event Action<ScTeamNamesEvent>? ScTeamNames;
    /// <summary>Raised when a MOTD chunk arrives.</summary>
    public event Action<ScMotdEvent>? ScMotd;
    /// <summary>Raised when the server hostname is announced.</summary>
    public event Action<ScServerNameEvent>? ScServerName;
    /// <summary>Raised when the server version is announced.</summary>
    public event Action<ScServerVersionEvent>? ScServerVersion;
    /// <summary>Raised when the server build string is announced.</summary>
    public event Action<ScServerBuildEvent>? ScServerBuild;
    /// <summary>Raised when the next map is announced.</summary>
    public event Action<ScNextMapEvent>? ScNextMap;
    /// <summary>Raised for scoreboard updates (float score format).</summary>
    public event Action<ScScoreInfoEvent>? ScScoreInfo;
    /// <summary>Raised when a team score updates (two values).</summary>
    public event Action<ScTeamScoreEvent>? ScTeamScore;
    /// <summary>Raised for gib bursts.</summary>
    public event Action<ScGibEvent>? ScGib;
    /// <summary>Raised for custom temporary effects.</summary>
    public event Action<ScTeCustomEvent>? ScTeCustom;
    /// <summary>Raised when an electrified tripwire changes state.</summary>
    public event Action<ScCbElecEvent>? ScCbElec;
    /// <summary>Raised for shock roach flashes.</summary>
    public event Action<ScShkFlashEvent>? ScShkFlash;
    /// <summary>Raised for water tracer decals.</summary>
    public event Action<ScTracerDecalEvent>? ScTracerDecal;
    /// <summary>Raised when spore trails attach/detach.</summary>
    public event Action<ScSporeTrailEvent>? ScSporeTrail;
    /// <summary>Raised for blood effects.</summary>
    public event Action<ScCreateBloodEvent>? ScCreateBlood;
    /// <summary>Raised for gargantua splashes.</summary>
    public event Action<ScGargSplashEvent>? ScGargSplash;
    /// <summary>Raised for flag-driven server-initiated sounds.</summary>
    public event Action<ScStartSoundEvent>? ScStartSound;
    /// <summary>Raised for toxic clouds.</summary>
    public event Action<ScToxicCloudEvent>? ScToxicCloud;
    /// <summary>Raised when a shock roach detonates.</summary>
    public event Action<ScSrDetonateEvent>? ScSrDetonate;
    /// <summary>Raised when a shock roach is primed.</summary>
    public event Action<ScSrPrimedEvent>? ScSrPrimed;
    /// <summary>Raised when a primed shock roach is disarmed.</summary>
    public event Action<ScSrPrimedOffEvent>? ScSrPrimedOff;
    /// <summary>Raised for configurable sprite ramp effects.</summary>
    public event Action<ScRampSpriteEvent>? ScRampSprite;
    /// <summary>Raised for shield ricochets.</summary>
    public event Action<ScShieldRicEvent>? ScShieldRic;
    /// <summary>Raised for weather effect updates.</summary>
    public event Action<ScWeatherFxEvent>? ScWeatherFx;
    /// <summary>Raised when a fixed camera/mouse mode is set.</summary>
    public event Action<ScCameraMouseEvent>? ScCameraMouse;
    /// <summary>Raised for flame thrower flame segments.</summary>
    public event Action<ScFlamethrowerEvent>? ScFlamethrower;
    /// <summary>Raised when the sky box changes.</summary>
    public event Action<ScChangeSkyEvent>? ScChangeSky;
    /// <summary>Raised when a HUD element channel toggles.</summary>
    public event Action<ScToggleElemEvent>? ScToggleElem;
    /// <summary>Raised for custom HUD sprite elements.</summary>
    public event Action<ScCustomSpriteEvent>? ScCustomSprite;
    /// <summary>Raised when a custom numeric HUD element is created/updated.</summary>
    public event Action<ScNumDisplayEvent>? ScNumDisplay;
    /// <summary>Raised when a numeric HUD element value updates.</summary>
    public event Action<ScUpdateNumEvent>? ScUpdateNum;
    /// <summary>Raised when a custom timer HUD element is created/updated.</summary>
    public event Action<ScTimeDisplayEvent>? ScTimeDisplay;
    /// <summary>Raised when a timer HUD element updates.</summary>
    public event Action<ScUpdateTimeEvent>? ScUpdateTime;
    /// <summary>Raised when inventory items are added.</summary>
    public event Action<ScInventoryAddEvent>? ScInventoryAdd;
    /// <summary>Raised when inventory items are removed.</summary>
    public event Action<ScInventoryRemoveEvent>? ScInventoryRemove;
    /// <summary>Raised for portal/monitor entity updates.</summary>
    public event Action<ScPortalUpdateEvent>? ScPortalUpdate;
    /// <summary>Raised for the server info handshake.</summary>
    public event Action<ScClServerInfoEvent>? ScClServerInfo;
    /// <summary>Raised when a weapon sprite is assigned to a HUD slot.</summary>
    public event Action<(short Slot, string Sprite)>? ScWeaponSprite;
    /// <summary>Raised when a custom weapon name is registered.</summary>
    public event Action<(short Slot, string Name)>? ScCustomWeapon;
    /// <summary>Raised when the server requests a key binding name lookup.</summary>
    public event Action<string>? ScKeyBinding;
    /// <summary>Raised when a notification text arrives.</summary>
    public event Action<(byte Type, string Text)>? ScNotifyText;
    /// <summary>Raised when the scoreboard spectator flags update.</summary>
    public event Action<SpectatorEvent>? Spectator;
    /// <summary>Raised when spectator mode is allowed/disallowed.</summary>
    public event Action<AllowSpecEvent>? AllowSpec;
    /// <summary>Raised for SC-specific messages with raw data. Every message registered
    /// by the 5.0.x game DLL has a typed parser here, so built-in dispatch no longer
    /// raises this; it remains (with <see cref="ParseScRaw"/>) as an extension point for
    /// derived handlers covering newer or custom registrations.</summary>
    public event Action<RawUserMessage>? OnScSpecificMessage;

    #endregion

    #region On* methods

    /// <summary>Invokes <see cref="OnScSpecificMessage"/>.</summary>
    protected virtual void ParseScRaw(ref BufferReader r, string name)
    {
        byte[] data = r.RemainingSpan.ToArray();
        r.BytePosition = r.Length;
        OnScSpecificMessage?.Invoke(new RawUserMessage(0, name, data));
    }

    private void OnScCurWeapon(ScCurWeaponEvent ev) => ScCurWeapon?.Invoke(ev);
    private void OnScHealth(ScHealthEvent ev) => ScHealth?.Invoke(ev);
    private void OnScBattery(ScBatteryEvent ev) => ScBattery?.Invoke(ev);
    private void OnScAmmoX(ScAmmoXEvent ev) => ScAmmoX?.Invoke(ev);
    private void OnScAmmoPickup(ScAmmoPickupEvent ev) => ScAmmoPickup?.Invoke(ev);
    private void OnScWeapPickup(ScWeapPickupEvent ev) => ScWeapPickup?.Invoke(ev);
    private void OnScWeaponList(ScWeaponListEvent ev) => ScWeaponList?.Invoke(ev);
    private void OnScTextMsg(ScTextMsgEvent ev) => ScTextMsg?.Invoke(ev);
    private void OnScHudText(ScHudTextEvent ev) => ScHudText?.Invoke(ev);
    private void OnScConcuss(ScConcussEvent ev) => ScConcuss?.Invoke(ev);
    private void OnScFog(ScFogEvent ev) => ScFog?.Invoke(ev);
    private void OnScShowMenu(ScShowMenuEvent ev) => ScShowMenu?.Invoke(ev);
    private void OnScHideHud(ScHideHudEvent ev) => ScHideHud?.Invoke(ev);
    private void OnVoiceMask(VoiceMaskEvent ev) => VoiceMask?.Invoke(ev);
    private void OnScViewMode(ScViewModeEvent ev) => ScViewMode?.Invoke(ev);
    private void OnScCdAudio(ScCdAudioEvent ev) => ScCdAudio?.Invoke(ev);
    private void OnScClassicMode(ScClassicModeEvent ev) => ScClassicMode?.Invoke(ev);
    private void OnScVModelPos(ScVModelPosEvent ev) => ScVModelPos?.Invoke(ev);
    private void OnScTimeEnd(ScTimeEndEvent ev) => ScTimeEnd?.Invoke(ev);
    private void OnScOnTank(ScOnTankEvent ev) => ScOnTank?.Invoke(ev);
    private void OnScPlaylist(ScPlaylistEvent ev) => ScPlaylist?.Invoke(ev);
    private void OnScSentence(ScSentenceEvent ev) => ScSentence?.Invoke(ev);
    private void OnScValClass(ScValClassEvent ev) => ScValClass?.Invoke(ev);
    private void OnScTeamNames(ScTeamNamesEvent ev) => ScTeamNames?.Invoke(ev);
    private void OnScMotd(ScMotdEvent ev) => ScMotd?.Invoke(ev);
    private void OnScServerName(ScServerNameEvent ev) => ScServerName?.Invoke(ev);
    private void OnScServerVersion(ScServerVersionEvent ev) => ScServerVersion?.Invoke(ev);
    private void OnScServerBuild(ScServerBuildEvent ev) => ScServerBuild?.Invoke(ev);
    private void OnScNextMap(ScNextMapEvent ev) => ScNextMap?.Invoke(ev);
    private void OnScScoreInfo(ScScoreInfoEvent ev) => ScScoreInfo?.Invoke(ev);
    private void OnScTeamScore(ScTeamScoreEvent ev) => ScTeamScore?.Invoke(ev);
    private void OnScGib(ScGibEvent ev) => ScGib?.Invoke(ev);
    private void OnScTeCustom(ScTeCustomEvent ev) => ScTeCustom?.Invoke(ev);
    private void OnScCbElec(ScCbElecEvent ev) => ScCbElec?.Invoke(ev);
    private void OnScShkFlash(ScShkFlashEvent ev) => ScShkFlash?.Invoke(ev);
    private void OnScTracerDecal(ScTracerDecalEvent ev) => ScTracerDecal?.Invoke(ev);
    private void OnScSporeTrail(ScSporeTrailEvent ev) => ScSporeTrail?.Invoke(ev);
    private void OnScCreateBlood(ScCreateBloodEvent ev) => ScCreateBlood?.Invoke(ev);
    private void OnScGargSplash(ScGargSplashEvent ev) => ScGargSplash?.Invoke(ev);
    private void OnScStartSound(ScStartSoundEvent ev) => ScStartSound?.Invoke(ev);
    private void OnScToxicCloud(ScToxicCloudEvent ev) => ScToxicCloud?.Invoke(ev);
    private void OnScSrDetonate(ScSrDetonateEvent ev) => ScSrDetonate?.Invoke(ev);
    private void OnScSrPrimed(ScSrPrimedEvent ev) => ScSrPrimed?.Invoke(ev);
    private void OnScSrPrimedOff(ScSrPrimedOffEvent ev) => ScSrPrimedOff?.Invoke(ev);
    private void OnScRampSprite(ScRampSpriteEvent ev) => ScRampSprite?.Invoke(ev);
    private void OnScShieldRic(ScShieldRicEvent ev) => ScShieldRic?.Invoke(ev);
    private void OnScWeatherFx(ScWeatherFxEvent ev) => ScWeatherFx?.Invoke(ev);
    private void OnScCameraMouse(ScCameraMouseEvent ev) => ScCameraMouse?.Invoke(ev);
    private void OnScFlamethrower(ScFlamethrowerEvent ev) => ScFlamethrower?.Invoke(ev);
    private void OnScChangeSky(ScChangeSkyEvent ev) => ScChangeSky?.Invoke(ev);
    private void OnScToggleElem(ScToggleElemEvent ev) => ScToggleElem?.Invoke(ev);
    private void OnScCustomSprite(ScCustomSpriteEvent ev) => ScCustomSprite?.Invoke(ev);
    private void OnScNumDisplay(ScNumDisplayEvent ev) => ScNumDisplay?.Invoke(ev);
    private void OnScUpdateNum(ScUpdateNumEvent ev) => ScUpdateNum?.Invoke(ev);
    private void OnScTimeDisplay(ScTimeDisplayEvent ev) => ScTimeDisplay?.Invoke(ev);
    private void OnScUpdateTime(ScUpdateTimeEvent ev) => ScUpdateTime?.Invoke(ev);
    private void OnScInventoryAdd(ScInventoryAddEvent ev) => ScInventoryAdd?.Invoke(ev);
    private void OnScInventoryRemove(ScInventoryRemoveEvent ev) => ScInventoryRemove?.Invoke(ev);
    private void OnScPortalUpdate(ScPortalUpdateEvent ev) => ScPortalUpdate?.Invoke(ev);
    private void OnScClServerInfo(ScClServerInfoEvent ev) => ScClServerInfo?.Invoke(ev);
    private void OnScMapList(ScMapListEvent ev) => ScMapList?.Invoke(ev);
    private void OnScVoteMenu(ScVoteMenuEvent ev) => ScVoteMenu?.Invoke(ev);
    private void OnScClExtrasInfo(ScClExtrasInfoEvent ev) => ScClExtrasInfo?.Invoke(ev);

    #endregion

    /// <inheritdoc />
    protected override bool DispatchUserMessage(GoldsrcConnection connection, byte index, string name, ref BufferReader reader)
    {
        switch (name)
        {
            // ── Sven wire formats that differ from the Half-Life base parsers ──
            case "CurWeapon": ParseCurWeapon(ref reader); return true;
            case "Health": ParseHealth(ref reader); return true;
            case "Battery": ParseBattery(ref reader); return true;
            case "AmmoX": ParseAmmoX(ref reader); return true;
            case "AmmoPickup": ParseAmmoPickup(ref reader); return true;
            case "WeapPickup": ParseWeapPickup(ref reader); return true;
            case "WeaponList": ParseWeaponList(ref reader); return true;
            case "TextMsg": ParseTextMsg(ref reader); return true;
            case "HudText": ParseHudText(ref reader); return true;
            case "GameTitle": ParseGameTitle(ref reader); return true;
            case "Concuss": ParseConcuss(ref reader); return true;
            case "Fog": ParseFog(ref reader); return true;
            case "VGUIMenu": ParseVguiMenu(ref reader); return true;

            // ── Sven-specific (no base handler) ──
            case "ShowMenu": ParseShowMenu(ref reader); return true;
            case "HideHUD": ParseHideHUD(ref reader); return true;
            case "VoiceMask": ParseVoiceMask(ref reader); return true;
            case "Spectator": ParseSpectator(ref reader); return true;
            case "AllowSpec": ParseAllowSpec(ref reader); return true;
            case "TeamScore": ParseTeamScore(ref reader); return true;
            case "ScoreInfo": ParseScoreInfo(ref reader); return true;
            case "TeamNames": ParseTeamNames(ref reader); return true;
            case "MOTD": ParseMotd(ref reader); return true;
            case "ServerName": ParseServerName(ref reader); return true;
            case "ServerVer": ParseServerVersion(ref reader); return true;
            case "ServerBuild": ParseServerBuild(ref reader); return true;
            case "NextMap": ParseNextMap(ref reader); return true;
            case "ViewMode": ParseViewMode(ref reader); return true;
            case "CdAudio": ParseCdAudio(ref reader); return true;
            case "ClassicMode": ParseClassicMode(ref reader); return true;
            case "VModelPos": ParseVModelPos(ref reader); return true;
            case "TimeEnd": ParseTimeEnd(ref reader); return true;
            case "OnTank": ParseOnTank(ref reader); return true;
            case "Playlist": ParsePlaylist(ref reader); return true;
            case "Speaksent": ParseSentence(ref reader); return true;
            case "ValClass": ParseValClass(ref reader); return true;
            case "PrtlUpdt": ParsePortalUpdate(ref reader); return true;
            case "InvAdd": ParseInventoryAdd(ref reader); return true;
            case "InvRemove": ParseInventoryRemove(ref reader); return true;
            case "ToggleElem": ParseToggleElem(ref reader); return true;
            case "CustSpr": ParseCustomSprite(ref reader); return true;
            case "NumDisplay": ParseNumDisplay(ref reader); return true;
            case "UpdateNum": ParseUpdateNum(ref reader); return true;
            case "TimeDisplay": ParseTimeDisplay(ref reader); return true;
            case "UpdateTime": ParseUpdateTime(ref reader); return true;
            case "WeaponSpr": ParseWeaponSprite(ref reader); return true;
            case "CustWeapon": ParseCustomWeapon(ref reader); return true;
            case "PrintKB": ParseKeyBinding(ref reader); return true;
            case "NotifyText": ParseNotifyText(ref reader); return true;
            case "Gib": ParseGib(ref reader); return true;
            case "TE_CUSTOM": ParseTeCustom(ref reader); return true;
            case "CbElec": ParseCbElec(ref reader); return true;
            case "ShkFlash": ParseShkFlash(ref reader); return true;
            case "TracerDecal": ParseTracerDecal(ref reader); return true;
            case "SporeTrail": ParseSporeTrail(ref reader); return true;
            case "CreateBlood": ParseCreateBlood(ref reader); return true;
            case "GargSplash": ParseGargSplash(ref reader); return true;
            case "StartSound": ParseStartSound(ref reader); return true;
            case "ToxicCloud": ParseToxicCloud(ref reader); return true;
            case "SRDetonate": ParseSrDetonate(ref reader); return true;
            case "SRPrimed": ParseSrPrimed(ref reader); return true;
            case "SRPrimedOff": ParseSrPrimedOff(ref reader); return true;
            case "RampSprite": ParseRampSprite(ref reader); return true;
            case "ShieldRic": ParseShieldRic(ref reader); return true;
            case "WeatherFX": ParseWeatherFx(ref reader); return true;
            case "CameraMouse": ParseCameraMouse(ref reader); return true;
            case "Flamethwr": ParseFlamethrower(ref reader); return true;
            case "ChangeSky": ParseChangeSky(ref reader); return true;
            case "ClServerInfo": ParseClServerInfo(ref reader); return true;
            case "ClExtrasInfo": ParseClExtrasInfo(ref reader); return true;
            case "EndVote": ParseEndVote(ref reader); return true;
            case "MapList": ParseMapList(ref reader); return true;
            case "VoteMenu": ParseVoteMenu(ref reader); return true;

            default:
                return base.DispatchUserMessage(connection, index, name, ref reader);
        }
    }

    // ── Shared-name overrides (Sven wire format differs from Half-Life) ──

    /// <summary>CurWeapon (Sven): byte state, short weaponId (-1 hides), long clip, long reserve.</summary>
    protected override void ParseCurWeapon(ref BufferReader r)
    {
        var ev = new ScCurWeaponEvent(r.ReadUInt8(), r.ReadInt16(), r.ReadInt32(), r.ReadInt32());
        OnScCurWeapon(ev);
    }

    /// <summary>Health (Sven): 32-bit value.</summary>
    protected override void ParseHealth(ref BufferReader r)
    {
        OnScHealth(new ScHealthEvent(r.ReadInt32()));
    }

    /// <summary>Battery (Sven): single byte.</summary>
    protected override void ParseBattery(ref BufferReader r)
    {
        OnScBattery(new ScBatteryEvent(r.ReadUInt8()));
    }

    /// <summary>AmmoX (Sven): byte index, 32-bit count.</summary>
    protected override void ParseAmmoX(ref BufferReader r)
    {
        OnScAmmoX(new ScAmmoXEvent(r.ReadUInt8(), r.ReadInt32()));
    }

    /// <summary>AmmoPickup (Sven): byte index, 32-bit count.</summary>
    protected override void ParseAmmoPickup(ref BufferReader r)
    {
        OnScAmmoPickup(new ScAmmoPickupEvent(r.ReadUInt8(), r.ReadInt32()));
    }

    /// <summary>WeapPickup (Sven): weapon id as a short.</summary>
    protected override void ParseWeapPickup(ref BufferReader r)
    {
        OnScWeapPickup(new ScWeapPickupEvent(r.ReadInt16()));
    }

    /// <summary>WeaponList (Sven): string, char id, long max, char id, long max, char slot, char pos, short wid, byte flags.</summary>
    protected override void ParseWeaponList(ref BufferReader r)
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
        OnScWeaponList(new ScWeaponListEvent(name, (byte)primaryId, primaryMax == 0xFF ? -1 : primaryMax,
            (byte)secondaryId, secondaryMax == 0xFF ? -1 : secondaryMax,
            (byte)slot, (byte)position, weaponId, flags));
    }

    /// <summary>TextMsg (Sven): destination byte plus message and four "#localisable" parameters.</summary>
    protected override void ParseTextMsg(ref BufferReader r)
    {
        byte dest = r.ReadUInt8();
        string message = r.ReadString();
        string p1 = r.ReadString();
        string p2 = r.ReadString();
        string p3 = r.ReadString();
        string p4 = r.ReadString();
        OnScTextMsg(new ScTextMsgEvent(dest, message, p1, p2, p3, p4));
    }

    /// <summary>HudText (Sven): a single text/localisation string.</summary>
    protected override void ParseHudText(ref BufferReader r)
    {
        OnScHudText(new ScHudTextEvent(r.ReadString()));
    }

    /// <summary>GameTitle (Sven): no payload — the message itself means "show".</summary>
    protected override void ParseGameTitle(ref BufferReader r)
    {
        OnGameTitle(new GameTitleEvent(1));
    }

    /// <summary>Concuss (Sven): direction vector as three floats.</summary>
    protected override void ParseConcuss(ref BufferReader r)
    {
        OnScConcuss(new ScConcussEvent(ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r)));
    }

    /// <summary>Fog (Sven): short, enable byte, 3 coords, short, RGB bytes, 2 shorts.
    /// The leading short and the coordinates are read but discarded by the client.</summary>
    protected override void ParseFog(ref BufferReader r)
    {
        r.ReadInt16(); // leading, unused
        bool enabled = r.ReadUInt8() != 0;
        float x = ReadCoord32(ref r), y = ReadCoord32(ref r), z = ReadCoord32(ref r);
        short unknown = r.ReadInt16();
        byte red = r.ReadUInt8(), green = r.ReadUInt8(), blue = r.ReadUInt8();
        OnScFog(new ScFogEvent(enabled, x, y, z, unknown, red, green, blue, r.ReadInt16(), r.ReadInt16()));
    }

    /// <summary>VGUIMenu (Sven): menu type byte; type 4 carries a parameter string.</summary>
    protected override void ParseVguiMenu(ref BufferReader r)
    {
        byte type = r.ReadUInt8();
        string data = type == 4 ? r.ReadString() : string.Empty;
        OnVguiMenu(new VguiMenuEvent(type, data));
    }

    /// <summary>Damage: same field order as Half-Life, but Sven coordinates are 32-bit
    /// (the dedicated server registers Damage with size 18 = 1+1+4+3×4).</summary>
    protected override void ParseDamage(ref BufferReader r)
    {
        byte save = r.ReadUInt8();
        byte take = r.ReadUInt8();
        int damageType = r.ReadInt32();
        OnDamage(new DamageEvent(save, take, damageType, ReadCoord32(ref r), ReadCoord32(ref r), ReadCoord32(ref r)));
    }

    // ── Sven-specific parsers ──

    /// <summary>ShowMenu (Sven): byte slot mask, signed display time, flag byte, text.</summary>
    protected virtual void ParseShowMenu(ref BufferReader r)
    {
        var ev = new ScShowMenuEvent(r.ReadUInt8(), ReadSByte(ref r), r.ReadUInt8(), r.ReadString());
        OnScShowMenu(ev);
    }

    /// <summary>HideHUD (Sven): 16-bit hide mask.</summary>
    protected virtual void ParseHideHUD(ref BufferReader r)
    {
        OnScHideHud(new ScHideHudEvent(r.ReadInt16()));
    }

    /// <summary>VoiceMask: two 32-bit audibility/ban masks plus a flag byte.</summary>
    protected virtual void ParseVoiceMask(ref BufferReader r)
    {
        OnVoiceMask(new VoiceMaskEvent(r.ReadInt32(), r.ReadInt32(), r.ReadUInt8()));
    }

    /// <summary>Spectator: player index and spectator flag, both bytes.</summary>
    protected virtual void ParseSpectator(ref BufferReader r)
    {
        var ev = new SpectatorEvent(r.ReadUInt8(), r.ReadUInt8());
        Spectator?.Invoke(ev);
    }

    /// <summary>AllowSpec: single allow byte.</summary>
    protected virtual void ParseAllowSpec(ref BufferReader r)
    {
        AllowSpec?.Invoke(new AllowSpecEvent(r.ReadUInt8()));
    }

    /// <summary>TeamScore (Sven): team name and two 16-bit scores.</summary>
    protected virtual void ParseTeamScore(ref BufferReader r)
    {
        var team = r.ReadString();
        short score = r.ReadInt16();
        short score2 = r.ReadInt16();
        OnScTeamScore(new ScTeamScoreEvent(team, score, score2));
    }

    /// <summary>ScoreInfo (Sven): byte index, float score, long, float, float, class byte, 2 bytes.</summary>
    protected virtual void ParseScoreInfo(ref BufferReader r)
    {
        var ev = new ScScoreInfoEvent(
            r.ReadUInt8(), ReadFloat(ref r), r.ReadInt32(), ReadFloat(ref r), ReadFloat(ref r),
            r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8());
        OnScScoreInfo(ev);
    }

    /// <summary>TeamNames (Sven): team count, then per team a name and an RGB colour triple.</summary>
    protected virtual void ParseTeamNames(ref BufferReader r)
    {
        byte count = r.ReadUInt8();
        var teams = new ScTeamNamesTeam[count];
        for (int i = 0; i < count; i++)
        {
            var name = r.ReadString();
            float cr = ReadCoord32(ref r), cg = ReadCoord32(ref r), cb = ReadCoord32(ref r);
            teams[i] = new ScTeamNamesTeam(name, cr, cg, cb);
        }
        OnScTeamNames(new ScTeamNamesEvent(teams));
    }

    /// <summary>MOTD (Sven): final-chunk byte plus text; chunks concatenate on the client.</summary>
    protected virtual void ParseMotd(ref BufferReader r)
    {
        OnScMotd(new ScMotdEvent(r.ReadUInt8() != 0, r.ReadString()));
    }

    /// <summary>ServerName (Sven): hostname string.</summary>
    protected virtual void ParseServerName(ref BufferReader r)
    {
        OnScServerName(new ScServerNameEvent(r.ReadString()));
    }

    /// <summary>ServerVer (Sven): version string.</summary>
    protected virtual void ParseServerVersion(ref BufferReader r)
    {
        OnScServerVersion(new ScServerVersionEvent(r.ReadString()));
    }

    /// <summary>ServerBuild (Sven): build string.</summary>
    protected virtual void ParseServerBuild(ref BufferReader r)
    {
        OnScServerBuild(new ScServerBuildEvent(r.ReadString()));
    }

    /// <summary>NextMap (Sven): map name string.</summary>
    protected virtual void ParseNextMap(ref BufferReader r)
    {
        OnScNextMap(new ScNextMapEvent(r.ReadString()));
    }

    /// <summary>ViewMode (Sven): 0 = first person, anything else = third person.</summary>
    protected virtual void ParseViewMode(ref BufferReader r)
    {
        OnScViewMode(new ScViewModeEvent(r.ReadUInt8() != 0));
    }

    /// <summary>CdAudio (Sven): track byte (0 = stop, 1..30 = media/Half-LifeXX).</summary>
    protected virtual void ParseCdAudio(ref BufferReader r)
    {
        OnScCdAudio(new ScCdAudioEvent(r.ReadUInt8()));
    }

    /// <summary>ClassicMode (Sven): mode toggle byte.</summary>
    protected virtual void ParseClassicMode(ref BufferReader r)
    {
        OnScClassicMode(new ScClassicModeEvent(r.ReadUInt8() != 0));
    }

    /// <summary>VModelPos (Sven): enable byte; when set, a coordinate triple.</summary>
    protected virtual void ParseVModelPos(ref BufferReader r)
    {
        bool enabled = r.ReadUInt8() == 1;
        float x = 0, y = 0, z = 0;
        if (enabled)
        {
            x = ReadCoord32(ref r); y = ReadCoord32(ref r); z = ReadCoord32(ref r);
        }
        OnScVModelPos(new ScVModelPosEvent(enabled, x, y, z));
    }

    /// <summary>TimeEnd (Sven): 32-bit round end time (client adds its clock).</summary>
    protected virtual void ParseTimeEnd(ref BufferReader r)
    {
        OnScTimeEnd(new ScTimeEndEvent(r.ReadInt32()));
    }

    /// <summary>OnTank (Sven): tank driving flag byte.</summary>
    protected virtual void ParseOnTank(ref BufferReader r)
    {
        OnScOnTank(new ScOnTankEvent(r.ReadUInt8() != 0));
    }

    /// <summary>Playlist (Sven): playlist name.</summary>
    protected virtual void ParsePlaylist(ref BufferReader r)
    {
        OnScPlaylist(new ScPlaylistEvent(r.ReadString()));
    }

    /// <summary>Speaksent (Sven): sentence name the client feeds to the "speak" command.</summary>
    protected virtual void ParseSentence(ref BufferReader r)
    {
        OnScSentence(new ScSentenceEvent(r.ReadString()));
    }

    /// <summary>ValClass (Sven): five class/slot shorts.</summary>
    protected virtual void ParseValClass(ref BufferReader r)
    {
        var classes = new short[5];
        for (int i = 0; i < 5; i++) classes[i] = r.ReadInt16();
        OnScValClass(new ScValClassEvent(classes));
    }

    /// <summary>PrtlUpdt (Sven): portal/monitor update with conditional sections.
    /// entity(long), enable(byte; 0 removes), vec1, vec2, type(byte), style(byte), life(float),
    /// byte, long, long, flag(byte); type != 0: flag(byte) → long + coord3 + ang3,
    /// type 1: flag → long,long; type 2: two flag bytes; style 2: name string.</summary>
    protected virtual void ParsePortalUpdate(ref BufferReader r)
    {
        int entity = r.ReadInt32();
        if (r.ReadUInt8() == 0)
        {
            OnScPortalUpdate(new ScPortalUpdateEvent(true, entity,
                [], [], 0, 0, 0, 0, 0, 0, false,
                null, null, null, null, null, null, null, null));
            return;
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

        OnScPortalUpdate(new ScPortalUpdateEvent(false, entity, vec1, vec2, type, style, life,
            byte1, long1, long2, flag1, modelIndex, modelOrigin, modelAngles, long3, long4, flagA, flagB, name));
    }

    /// <summary>InvAdd (Sven): id(long), 3 flag bytes, time(float), 5 strings.</summary>
    protected virtual void ParseInventoryAdd(ref BufferReader r)
    {
        int id = r.ReadInt32();
        bool f1 = r.ReadUInt8() != 0, f2 = r.ReadUInt8() != 0, f3 = r.ReadUInt8() != 0;
        float time = ReadFloat(ref r);
        OnScInventoryAdd(new ScInventoryAddEvent(id, f1, f2, f3, time,
            r.ReadString(), r.ReadString(), r.ReadString(), r.ReadString(), r.ReadString()));
    }

    /// <summary>InvRemove (Sven): id (0 removes all) and a flag byte.</summary>
    protected virtual void ParseInventoryRemove(ref BufferReader r)
    {
        OnScInventoryRemove(new ScInventoryRemoveEvent(r.ReadInt32(), r.ReadUInt8()));
    }

    /// <summary>ToggleElem (Sven): HUD channel byte (0-31) and state byte.</summary>
    protected virtual void ParseToggleElem(ref BufferReader r)
    {
        OnScToggleElem(new ScToggleElemEvent(r.ReadUInt8(), r.ReadUInt8() != 0));
    }

    /// <summary>CustSpr (Sven): channel, flags(long), sprite, x, y, w(short), h(short),
    /// two RGBA quads, two bytes, five floats, final byte.</summary>
    protected virtual void ParseCustomSprite(ref BufferReader r)
    {
        byte channel = r.ReadUInt8();
        int flags = r.ReadInt32();
        var sprite = r.ReadString();
        byte x = r.ReadUInt8(), y = r.ReadUInt8();
        short width = r.ReadInt16(), height = r.ReadInt16();
        byte r1 = r.ReadUInt8(), g1 = r.ReadUInt8(), b1 = r.ReadUInt8(), a1 = r.ReadUInt8();
        byte r2 = r.ReadUInt8(), g2 = r.ReadUInt8(), b2 = r.ReadUInt8(), a2 = r.ReadUInt8();
        byte unknown1 = r.ReadUInt8(), unknown2 = r.ReadUInt8();
        var ev = new ScCustomSpriteEvent(channel, flags, sprite, x, y, width, height,
            r1, g1, b1, a1, r2, g2, b2, a2, unknown1, unknown2,
            ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r), r.ReadUInt8());
        OnScCustomSprite(ev);
    }

    /// <summary>NumDisplay (Sven): channel, flags(long), value(float), x, y, width/height floats,
    /// two RGBA quads, font string, region bytes/shorts, four floats, final byte.</summary>
    protected virtual void ParseNumDisplay(ref BufferReader r)
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
        var ev = new ScNumDisplayEvent(channel, flags, value, x, y, width, height,
            r1, g1, b1, a1, r2, g2, b2, a2, font, regionX, regionY, regionWidth, regionHeight,
            ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r), r.ReadUInt8());
        OnScNumDisplay(ev);
    }

    /// <summary>UpdateNum (Sven): channel byte and float value.</summary>
    protected virtual void ParseUpdateNum(ref BufferReader r)
    {
        OnScUpdateNum(new ScUpdateNumEvent(r.ReadUInt8(), ReadFloat(ref r)));
    }

    /// <summary>TimeDisplay (Sven): like NumDisplay but with two leading value floats.</summary>
    protected virtual void ParseTimeDisplay(ref BufferReader r)
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
        var ev = new ScTimeDisplayEvent(channel, flags, value1, value2, width, height,
            r1, g1, b1, a1, r2, g2, b2, a2, font, regionX, regionY, regionWidth, regionHeight,
            ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r), r.ReadUInt8());
        OnScTimeDisplay(ev);
    }

    /// <summary>UpdateTime (Sven): channel byte, time float, duration float.</summary>
    protected virtual void ParseUpdateTime(ref BufferReader r)
    {
        OnScUpdateTime(new ScUpdateTimeEvent(r.ReadUInt8(), ReadFloat(ref r), ReadFloat(ref r)));
    }

    /// <summary>WeaponSpr (Sven): HUD slot short and sprite name.</summary>
    protected virtual void ParseWeaponSprite(ref BufferReader r)
    {
        short slot = r.ReadInt16();
        ScWeaponSprite?.Invoke((slot, r.ReadString()));
    }

    /// <summary>CustWeapon (Sven): HUD slot short and custom weapon name.</summary>
    protected virtual void ParseCustomWeapon(ref BufferReader r)
    {
        short slot = r.ReadInt16();
        ScCustomWeapon?.Invoke((slot, r.ReadString()));
    }

    /// <summary>PrintKB (Sven): key binding name string.</summary>
    protected virtual void ParseKeyBinding(ref BufferReader r)
    {
        ScKeyBinding?.Invoke(r.ReadString());
    }

    /// <summary>NotifyText (Sven): type byte and text.</summary>
    protected virtual void ParseNotifyText(ref BufferReader r)
    {
        byte type = r.ReadUInt8();
        ScNotifyText?.Invoke((type, r.ReadString()));
    }

    /// <summary>Gib (Sven): gib type byte (0, 1, 2, 4 valid) then origin and velocity triples.
    /// Unknown types carry no coordinates.</summary>
    protected virtual void ParseGib(ref BufferReader r)
    {
        byte type = r.ReadUInt8();
        float ox = 0, oy = 0, oz = 0, vx = 0, vy = 0, vz = 0;
        if (type is 0 or 1 or 2 or 4)
        {
            ox = ReadCoord32(ref r); oy = ReadCoord32(ref r); oz = ReadCoord32(ref r);
            vx = ReadCoord32(ref r); vy = ReadCoord32(ref r); vz = ReadCoord32(ref r);
        }
        OnScGib(new ScGibEvent(type, ox, oy, oz, vx, vy, vz));
    }

    /// <summary>TE_CUSTOM (Sven): subtyped effect. Subtype 1: id(short), count(short),
    /// origin triple when count > 0. Subtype 3: one byte. Subtype 2: empty.</summary>
    protected virtual void ParseTeCustom(ref BufferReader r)
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
        OnScTeCustom(new ScTeCustomEvent(subType, id, count, ox, oy, oz, value));
    }

    /// <summary>CbElec (Sven): state byte — bits 0-4 entity index, bit 6 active.</summary>
    protected virtual void ParseCbElec(ref BufferReader r)
    {
        byte data = r.ReadUInt8();
        OnScCbElec(new ScCbElecEvent((data & 0x40) != 0, (byte)(data & 0x1F)));
    }

    /// <summary>ShkFlash (Sven): origin triple plus mode byte (0 = fire, else impact).</summary>
    protected virtual void ParseShkFlash(ref BufferReader r)
    {
        float x = ReadCoord32(ref r), y = ReadCoord32(ref r), z = ReadCoord32(ref r);
        OnScShkFlash(new ScShkFlashEvent(x, y, z, r.ReadUInt8()));
    }

    /// <summary>TracerDecal (Sven): start/end triples, decal type byte, trailing byte.</summary>
    protected virtual void ParseTracerDecal(ref BufferReader r)
    {
        float sx = ReadCoord32(ref r), sy = ReadCoord32(ref r), sz = ReadCoord32(ref r);
        float ex = ReadCoord32(ref r), ey = ReadCoord32(ref r), ez = ReadCoord32(ref r);
        byte type = r.ReadUInt8();
        byte unknown = r.ReadUInt8();
        OnScTracerDecal(new ScTracerDecalEvent(sx, sy, sz, ex, ey, ez, type, unknown));
    }

    /// <summary>SporeTrail (Sven): entity short and attach flag byte.</summary>
    protected virtual void ParseSporeTrail(ref BufferReader r)
    {
        short entity = r.ReadInt16();
        OnScSporeTrail(new ScSporeTrailEvent(entity, r.ReadUInt8() != 0));
    }

    /// <summary>CreateBlood (Sven): origin triple, colour byte, amount byte.</summary>
    protected virtual void ParseCreateBlood(ref BufferReader r)
    {
        float x = ReadCoord32(ref r), y = ReadCoord32(ref r), z = ReadCoord32(ref r);
        OnScCreateBlood(new ScCreateBloodEvent(x, y, z, r.ReadUInt8(), r.ReadUInt8()));
    }

    /// <summary>GargSplash (Sven): origin triple plus colour triple. The client reads the
    /// colour channels as coordinates and takes their absolute value (float bit-mask AND),
    /// then feeds them to the splash temp entity as floats.</summary>
    protected virtual void ParseGargSplash(ref BufferReader r)
    {
        float x = ReadCoord32(ref r), y = ReadCoord32(ref r), z = ReadCoord32(ref r);
        float cr = MathF.Abs(ReadCoord32(ref r));
        float cg = MathF.Abs(ReadCoord32(ref r));
        float cb = MathF.Abs(ReadCoord32(ref r));
        OnScGargSplash(new ScGargSplashEvent(x, y, z, cr, cg, cb));
    }

    /// <summary>StartSound (Sven): flag-driven fields; see <see cref="ScStartSoundEvent"/>.</summary>
    protected virtual void ParseStartSound(ref BufferReader r)
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
        OnScStartSound(new ScStartSoundEvent(flags, entity, volume, attenuation, pitch, ox, oy, oz, duration, channel, soundIndex));
    }

    /// <summary>ToxicCloud (Sven): origin triple.</summary>
    protected virtual void ParseToxicCloud(ref BufferReader r)
    {
        OnScToxicCloud(new ScToxicCloudEvent(ReadCoord32(ref r), ReadCoord32(ref r), ReadCoord32(ref r)));
    }

    /// <summary>SRDetonate (Sven): origin triple and radius byte.</summary>
    protected virtual void ParseSrDetonate(ref BufferReader r)
    {
        float x = ReadCoord32(ref r), y = ReadCoord32(ref r), z = ReadCoord32(ref r);
        OnScSrDetonate(new ScSrDetonateEvent(x, y, z, r.ReadUInt8()));
    }

    /// <summary>SRPrimed (Sven): entity byte and fuse float.</summary>
    protected virtual void ParseSrPrimed(ref BufferReader r)
    {
        byte entity = r.ReadUInt8();
        OnScSrPrimed(new ScSrPrimedEvent(entity, ReadFloat(ref r)));
    }

    /// <summary>SRPrimedOff (Sven): entity byte.</summary>
    protected virtual void ParseSrPrimedOff(ref BufferReader r)
    {
        OnScSrPrimedOff(new ScSrPrimedOffEvent(r.ReadUInt8()));
    }

    /// <summary>RampSprite (Sven): entity short, life byte, origin triple, flags short, then
    /// optional per-flag fields; see <see cref="ScRampSpriteEvent"/>.</summary>
    protected virtual void ParseRampSprite(ref BufferReader r)
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

        OnScRampSprite(new ScRampSpriteEvent(entity, lifeTicks, x, y, z, flags,
            startTime, fadeIn, fadeOut, renderModeFx, red, green, blue, unknownBit15,
            color2R, color2G, color2B,
            (flags & 0x200) != 0 ? r.ReadUInt8() : null,
            (flags & 0x400) != 0 ? r.ReadUInt8() : null,
            (flags & 0x800) != 0 ? r.ReadUInt8() : null,
            (flags & 0x1000) != 0 ? r.ReadUInt8() : null,
            (flags & 0x2000) != 0 ? r.ReadUInt8() : null,
            (flags & 0x4000) != 0 ? r.ReadUInt8() : null,
            (flags & 0x8000) != 0 ? r.ReadUInt8() : null));
    }

    /// <summary>ShieldRic (Sven): origin triple.</summary>
    protected virtual void ParseShieldRic(ref BufferReader r)
    {
        OnScShieldRic(new ScShieldRicEvent(ReadCoord32(ref r), ReadCoord32(ref r), ReadCoord32(ref r)));
    }

    /// <summary>WeatherFX (Sven): type short, min/max triples, angle triple, then a fixed
    /// tail of short/byte/float groups passed through opaquely by the client.</summary>
    protected virtual void ParseWeatherFx(ref BufferReader r)
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
        var ev = new ScWeatherFxEvent(type, minX, minY, minZ, maxX, maxY, maxZ, ax, ay, az,
            unknownShort1, float1, byte1, unknownShort2, float2, byte2, byte3, float3,
            byte4, byte5, byte6, byte7, ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r));
        OnScWeatherFx(ev);
    }

    /// <summary>CameraMouse (Sven): mode byte; mode 2 carries a parameter string.</summary>
    protected virtual void ParseCameraMouse(ref BufferReader r)
    {
        byte mode = r.ReadUInt8();
        OnScCameraMouse(new ScCameraMouseEvent(mode, mode == 2 ? r.ReadString() : string.Empty));
    }

    /// <summary>Flamethwr (Sven): entity byte plus start/end triples.</summary>
    protected virtual void ParseFlamethrower(ref BufferReader r)
    {
        byte entity = r.ReadUInt8();
        float sx = ReadCoord32(ref r), sy = ReadCoord32(ref r), sz = ReadCoord32(ref r);
        float ex = ReadCoord32(ref r), ey = ReadCoord32(ref r), ez = ReadCoord32(ref r);
        OnScFlamethrower(new ScFlamethrowerEvent(entity, sx, sy, sz, ex, ey, ez));
    }

    /// <summary>ChangeSky (Sven): sky name string and colour triple (-1s keep the default).</summary>
    protected virtual void ParseChangeSky(ref BufferReader r)
    {
        var sky = r.ReadString();
        float cr = ReadCoord32(ref r), cg = ReadCoord32(ref r), cb = ReadCoord32(ref r);
        OnScChangeSky(new ScChangeSkyEvent(sky, cr, cg, cb));
    }

    /// <summary>ClServerInfo (Sven): flag byte, 32-bit value and key string.</summary>
    protected virtual void ParseClServerInfo(ref BufferReader r)
    {
        OnScClServerInfo(new ScClServerInfoEvent(r.ReadUInt8(), r.ReadInt32(), r.ReadString()));
    }

    /// <summary>EndVote (Sven): empty payload.</summary>
    protected virtual void ParseEndVote(ref BufferReader r)
    {
        // no payload — arrival itself clears the vote UI
    }

    /// <summary>MapList (Sven): decoded from CMapVotePanel's vtable+0x21C virtual. A command
    /// byte selects reset (0: clears the list and stores the total count), close (0x7B), or
    /// an incremental update (start/end shorts plus one map-name string per entry).</summary>
    protected virtual void ParseMapList(ref BufferReader r)
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
        OnScMapList(new ScMapListEvent(command, totalMaps, startIndex, endIndex, mapNames));
    }

    /// <summary>VoteMenu (Sven): decoded from the vote panel's vtable+0x214 virtual:
    /// vote id byte, question string, yes-label and no-label strings (empty labels
    /// fall back to "#Menu_Yes"/"#Menu_No" on the client).</summary>
    protected virtual void ParseVoteMenu(ref BufferReader r)
    {
        byte voteId = r.ReadUInt8();
        OnScVoteMenu(new ScVoteMenuEvent(voteId, r.ReadString(), r.ReadString(), r.ReadString()));
    }

    /// <summary>ClExtrasInfo (Sven): four length-prefixed blocks framing a CryptoPP
    /// authenticated-encryption payload — plain length, IV, encrypted data, and a
    /// digest sized to the session key. The client derives the key via GenerateKey
    /// from the ClServerInfo handshake and decrypts to
    /// "playerIndex\nauthId\n\"name\"\nlevel" which updates the scoreboard's per-player
    /// admin level; the key material never leaves the client, so only the framing is
    /// decoded here.</summary>
    protected virtual void ParseClExtrasInfo(ref BufferReader r)
    {
        int plainLength = r.ReadInt32();
        byte[] iv = ReadBlock(ref r);
        byte[] encryptedData = ReadBlock(ref r);
        byte[] encryptedDigest = ReadBlock(ref r);
        OnScClExtrasInfo(new ScClExtrasInfoEvent(plainLength, iv, encryptedData, encryptedDigest));
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
    protected static float ReadCoord32(ref BufferReader r)
    {
        return r.ReadInt32() / 8.0f;
    }

    /// <summary>Reads a little-endian 32-bit float.</summary>
    protected static float ReadFloat(ref BufferReader r)
    {
        return r.ReadSingle();
    }

    /// <summary>Reads a signed byte.</summary>
    protected static sbyte ReadSByte(ref BufferReader r) => (sbyte)r.ReadUInt8();

    /// <summary>Reads a Sven angle: signed byte scaled by 360/256 (1.40625).</summary>
    protected static float ReadAngle(ref BufferReader r) => ReadSByte(ref r) * (360f / 256f);

    /// <summary>Reads a Sven 16-bit angle: signed short scaled by 360/65536.</summary>
    protected static float ReadAngle16(ref BufferReader r) => r.ReadInt16() * (360f / 65536f);

    /// <summary>Reads three coordinates into an array (READ_COORD_vec3 helper order).</summary>
    protected static float[] ReadCoordVec3(ref BufferReader r) =>
        [ReadCoord32(ref r), ReadCoord32(ref r), ReadCoord32(ref r)];

    /// <summary>Reads three angles into an array (READ_ANGLE_vec3 helper order).</summary>
    protected static float[] ReadAngleVec3(ref BufferReader r) =>
        [ReadAngle(ref r), ReadAngle(ref r), ReadAngle(ref r)];
}
