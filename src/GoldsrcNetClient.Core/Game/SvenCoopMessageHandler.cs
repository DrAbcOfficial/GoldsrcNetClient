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
/// (3 floats), Fog (extended), GameTitle (no payload), ShowMenu, VGUIMenu, HideHUD (short).</para>
///
/// <para>Messages handled inside VGUI panel virtuals are decoded through the panel
/// classes' vtables: MapList (CMapVotePanel::vftable+0x21C) and VoteMenu
/// (vtable+0x214). ClExtrasInfo frames a CryptoPP authenticated-encryption blob whose
/// key is derived client-side by GenerateKey from the ClServerInfo handshake — only
/// its wire framing can be decoded, and the opaque blocks are exposed as-is.</para>
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
    /// <summary>Raised for SC-specific messages with raw data when the structure cannot be decoded.</summary>
    public event Action<RawUserMessage>? OnScSpecificMessage;

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

    #endregion

    #region On* methods

    /// <summary>Invokes <see cref="OnScSpecificMessage"/>.</summary>
    protected virtual void ParseScRaw(MessageReader r, string name)
    {
        byte[] data = new byte[r.Remaining];
        Array.Copy(r.Data, r.Offset, data, 0, data.Length);
        r.Offset = r.Size;
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
    protected override bool DispatchUserMessage(GoldsrcConnection connection, byte index, string name, MessageReader reader)
    {
        switch (name)
        {
            // ── Sven wire formats that differ from the Half-Life base parsers ──
            case "CurWeapon": ParseCurWeapon(reader); return true;
            case "Health": ParseHealth(reader); return true;
            case "Battery": ParseBattery(reader); return true;
            case "AmmoX": ParseAmmoX(reader); return true;
            case "AmmoPickup": ParseAmmoPickup(reader); return true;
            case "WeapPickup": ParseWeapPickup(reader); return true;
            case "WeaponList": ParseWeaponList(reader); return true;
            case "TextMsg": ParseTextMsg(reader); return true;
            case "HudText": ParseHudText(reader); return true;
            case "GameTitle": ParseGameTitle(reader); return true;
            case "Concuss": ParseConcuss(reader); return true;
            case "Fog": ParseFog(reader); return true;
            case "VGUIMenu": ParseVguiMenu(reader); return true;

            // ── Sven-specific (no base handler) ──
            case "ShowMenu": ParseShowMenu(reader); return true;
            case "HideHUD": ParseHideHUD(reader); return true;
            case "VoiceMask": ParseVoiceMask(reader); return true;
            case "Spectator": ParseSpectator(reader); return true;
            case "AllowSpec": ParseAllowSpec(reader); return true;
            case "TeamScore": ParseTeamScore(reader); return true;
            case "ScoreInfo": ParseScoreInfo(reader); return true;
            case "TeamNames": ParseTeamNames(reader); return true;
            case "MOTD": ParseMotd(reader); return true;
            case "ServerName": ParseServerName(reader); return true;
            case "ServerVer": ParseServerVersion(reader); return true;
            case "ServerBuild": ParseServerBuild(reader); return true;
            case "NextMap": ParseNextMap(reader); return true;
            case "ViewMode": ParseViewMode(reader); return true;
            case "CdAudio": ParseCdAudio(reader); return true;
            case "ClassicMode": ParseClassicMode(reader); return true;
            case "VModelPos": ParseVModelPos(reader); return true;
            case "TimeEnd": ParseTimeEnd(reader); return true;
            case "OnTank": ParseOnTank(reader); return true;
            case "Playlist": ParsePlaylist(reader); return true;
            case "Speaksent": ParseSentence(reader); return true;
            case "ValClass": ParseValClass(reader); return true;
            case "PrtlUpdt": ParsePortalUpdate(reader); return true;
            case "InvAdd": ParseInventoryAdd(reader); return true;
            case "InvRemove": ParseInventoryRemove(reader); return true;
            case "ToggleElem": ParseToggleElem(reader); return true;
            case "CustSpr": ParseCustomSprite(reader); return true;
            case "NumDisplay": ParseNumDisplay(reader); return true;
            case "UpdateNum": ParseUpdateNum(reader); return true;
            case "TimeDisplay": ParseTimeDisplay(reader); return true;
            case "UpdateTime": ParseUpdateTime(reader); return true;
            case "WeaponSpr": ParseWeaponSprite(reader); return true;
            case "CustWeapon": ParseCustomWeapon(reader); return true;
            case "PrintKB": ParseKeyBinding(reader); return true;
            case "NotifyText": ParseNotifyText(reader); return true;
            case "Gib": ParseGib(reader); return true;
            case "TE_CUSTOM": ParseTeCustom(reader); return true;
            case "CbElec": ParseCbElec(reader); return true;
            case "ShkFlash": ParseShkFlash(reader); return true;
            case "TracerDecal": ParseTracerDecal(reader); return true;
            case "SporeTrail": ParseSporeTrail(reader); return true;
            case "CreateBlood": ParseCreateBlood(reader); return true;
            case "GargSplash": ParseGargSplash(reader); return true;
            case "StartSound": ParseStartSound(reader); return true;
            case "ToxicCloud": ParseToxicCloud(reader); return true;
            case "SRDetonate": ParseSrDetonate(reader); return true;
            case "SRPrimed": ParseSrPrimed(reader); return true;
            case "SRPrimedOff": ParseSrPrimedOff(reader); return true;
            case "RampSprite": ParseRampSprite(reader); return true;
            case "ShieldRic": ParseShieldRic(reader); return true;
            case "WeatherFX": ParseWeatherFx(reader); return true;
            case "CameraMouse": ParseCameraMouse(reader); return true;
            case "Flamethwr": ParseFlamethrower(reader); return true;
            case "ChangeSky": ParseChangeSky(reader); return true;
            case "ClServerInfo": ParseClServerInfo(reader); return true;
            case "ClExtrasInfo": ParseClExtrasInfo(reader); return true;
            case "EndVote": ParseEndVote(reader); return true;
            case "MapList": ParseMapList(reader); return true;
            case "VoteMenu": ParseVoteMenu(reader); return true;

            default:
                return base.DispatchUserMessage(connection, index, name, reader);
        }
    }

    // ── Shared-name overrides (Sven wire format differs from Half-Life) ──

    /// <summary>CurWeapon (Sven): byte state, short weaponId (-1 hides), long clip, long reserve.</summary>
    protected override void ParseCurWeapon(MessageReader r)
    {
        var ev = new ScCurWeaponEvent(r.ReadByte(), ReadShort(r), ReadInt32(r), ReadInt32(r));
        OnScCurWeapon(ev);
    }

    /// <summary>Health (Sven): 32-bit value.</summary>
    protected override void ParseHealth(MessageReader r)
    {
        OnScHealth(new ScHealthEvent(ReadInt32(r)));
    }

    /// <summary>Battery (Sven): single byte.</summary>
    protected override void ParseBattery(MessageReader r)
    {
        OnScBattery(new ScBatteryEvent(r.ReadByte()));
    }

    /// <summary>AmmoX (Sven): byte index, 32-bit count.</summary>
    protected override void ParseAmmoX(MessageReader r)
    {
        OnScAmmoX(new ScAmmoXEvent(r.ReadByte(), ReadInt32(r)));
    }

    /// <summary>AmmoPickup (Sven): byte index, 32-bit count.</summary>
    protected override void ParseAmmoPickup(MessageReader r)
    {
        OnScAmmoPickup(new ScAmmoPickupEvent(r.ReadByte(), ReadInt32(r)));
    }

    /// <summary>WeapPickup (Sven): weapon id as a short.</summary>
    protected override void ParseWeapPickup(MessageReader r)
    {
        OnScWeapPickup(new ScWeapPickupEvent(ReadShort(r)));
    }

    /// <summary>WeaponList (Sven): string, char id, long max, char id, long max, char slot, char pos, short wid, byte flags.</summary>
    protected override void ParseWeaponList(MessageReader r)
    {
        var name = r.ReadString();
        sbyte primaryId = ReadSByte(r);
        int primaryMax = ReadInt32(r);
        sbyte secondaryId = ReadSByte(r);
        int secondaryMax = ReadInt32(r);
        sbyte slot = ReadSByte(r);
        sbyte position = ReadSByte(r);
        short weaponId = ReadShort(r);
        byte flags = r.ReadByte();
        OnScWeaponList(new ScWeaponListEvent(name, (byte)primaryId, primaryMax == 0xFF ? -1 : primaryMax,
            (byte)secondaryId, secondaryMax == 0xFF ? -1 : secondaryMax,
            (byte)slot, (byte)position, weaponId, flags));
    }

    /// <summary>TextMsg (Sven): destination byte plus message and four "#localisable" parameters.</summary>
    protected override void ParseTextMsg(MessageReader r)
    {
        byte dest = r.ReadByte();
        string message = r.ReadString();
        string p1 = r.ReadString();
        string p2 = r.ReadString();
        string p3 = r.ReadString();
        string p4 = r.ReadString();
        OnScTextMsg(new ScTextMsgEvent(dest, message, p1, p2, p3, p4));
    }

    /// <summary>HudText (Sven): a single text/localisation string.</summary>
    protected override void ParseHudText(MessageReader r)
    {
        OnScHudText(new ScHudTextEvent(r.ReadString()));
    }

    /// <summary>GameTitle (Sven): no payload — the message itself means "show".</summary>
    protected override void ParseGameTitle(MessageReader r)
    {
        OnGameTitle(new GameTitleEvent(1));
    }

    /// <summary>Concuss (Sven): direction vector as three floats.</summary>
    protected override void ParseConcuss(MessageReader r)
    {
        OnScConcuss(new ScConcussEvent(ReadFloat(r), ReadFloat(r), ReadFloat(r)));
    }

    /// <summary>Fog (Sven): short, enable byte, 3 coords, short, RGB bytes, 2 shorts.
    /// The leading short and the coordinates are read but discarded by the client.</summary>
    protected override void ParseFog(MessageReader r)
    {
        ReadShort(r); // leading, unused
        bool enabled = r.ReadByte() != 0;
        float x = ReadCoord(r), y = ReadCoord(r), z = ReadCoord(r);
        short unknown = ReadShort(r);
        byte red = r.ReadByte(), green = r.ReadByte(), blue = r.ReadByte();
        OnScFog(new ScFogEvent(enabled, x, y, z, unknown, red, green, blue, ReadShort(r), ReadShort(r)));
    }

    /// <summary>VGUIMenu (Sven): menu type byte; type 4 carries a parameter string.</summary>
    protected override void ParseVguiMenu(MessageReader r)
    {
        byte type = r.ReadByte();
        string data = type == 4 ? r.ReadString() : string.Empty;
        OnVguiMenu(new VguiMenuEvent(type, data));
    }

    // ── Sven-specific parsers ──

    /// <summary>ShowMenu (Sven): byte slot mask, signed display time, flag byte, text.</summary>
    protected virtual void ParseShowMenu(MessageReader r)
    {
        var ev = new ScShowMenuEvent(r.ReadByte(), ReadSByte(r), r.ReadByte(), r.ReadString());
        OnScShowMenu(ev);
    }

    /// <summary>HideHUD (Sven): 16-bit hide mask.</summary>
    protected virtual void ParseHideHUD(MessageReader r)
    {
        OnScHideHud(new ScHideHudEvent(ReadShort(r)));
    }

    /// <summary>VoiceMask: two 32-bit audibility/ban masks plus a flag byte.</summary>
    protected virtual void ParseVoiceMask(MessageReader r)
    {
        OnVoiceMask(new VoiceMaskEvent(ReadInt32(r), ReadInt32(r), r.ReadByte()));
    }

    /// <summary>Spectator: player index and spectator flag, both bytes.</summary>
    protected virtual void ParseSpectator(MessageReader r)
    {
        var ev = new SpectatorEvent(r.ReadByte(), r.ReadByte());
        Spectator?.Invoke(ev);
    }

    /// <summary>AllowSpec: single allow byte.</summary>
    protected virtual void ParseAllowSpec(MessageReader r)
    {
        AllowSpec?.Invoke(new AllowSpecEvent(r.ReadByte()));
    }

    /// <summary>TeamScore (Sven): team name and two 16-bit scores.</summary>
    protected virtual void ParseTeamScore(MessageReader r)
    {
        var team = r.ReadString();
        short score = ReadShort(r);
        short score2 = ReadShort(r);
        OnScTeamScore(new ScTeamScoreEvent(team, score, score2));
    }

    /// <summary>ScoreInfo (Sven): byte index, float score, long, float, float, class byte, 2 bytes.</summary>
    protected virtual void ParseScoreInfo(MessageReader r)
    {
        var ev = new ScScoreInfoEvent(
            r.ReadByte(), ReadFloat(r), ReadInt32(r), ReadFloat(r), ReadFloat(r),
            r.ReadByte(), r.ReadByte(), r.ReadByte());
        OnScScoreInfo(ev);
    }

    /// <summary>TeamNames (Sven): team count, then per team a name and an RGB colour triple.</summary>
    protected virtual void ParseTeamNames(MessageReader r)
    {
        byte count = r.ReadByte();
        var teams = new ScTeamNamesTeam[count];
        for (int i = 0; i < count; i++)
        {
            var name = r.ReadString();
            float cr = ReadCoord(r), cg = ReadCoord(r), cb = ReadCoord(r);
            teams[i] = new ScTeamNamesTeam(name, cr, cg, cb);
        }
        OnScTeamNames(new ScTeamNamesEvent(teams));
    }

    /// <summary>MOTD (Sven): final-chunk byte plus text; chunks concatenate on the client.</summary>
    protected virtual void ParseMotd(MessageReader r)
    {
        OnScMotd(new ScMotdEvent(r.ReadByte() != 0, r.ReadString()));
    }

    /// <summary>ServerName (Sven): hostname string.</summary>
    protected virtual void ParseServerName(MessageReader r)
    {
        OnScServerName(new ScServerNameEvent(r.ReadString()));
    }

    /// <summary>ServerVer (Sven): version string.</summary>
    protected virtual void ParseServerVersion(MessageReader r)
    {
        OnScServerVersion(new ScServerVersionEvent(r.ReadString()));
    }

    /// <summary>ServerBuild (Sven): build string.</summary>
    protected virtual void ParseServerBuild(MessageReader r)
    {
        OnScServerBuild(new ScServerBuildEvent(r.ReadString()));
    }

    /// <summary>NextMap (Sven): map name string.</summary>
    protected virtual void ParseNextMap(MessageReader r)
    {
        OnScNextMap(new ScNextMapEvent(r.ReadString()));
    }

    /// <summary>ViewMode (Sven): 0 = first person, anything else = third person.</summary>
    protected virtual void ParseViewMode(MessageReader r)
    {
        OnScViewMode(new ScViewModeEvent(r.ReadByte() != 0));
    }

    /// <summary>CdAudio (Sven): track byte (0 = stop, 1..30 = media/Half-LifeXX).</summary>
    protected virtual void ParseCdAudio(MessageReader r)
    {
        OnScCdAudio(new ScCdAudioEvent(r.ReadByte()));
    }

    /// <summary>ClassicMode (Sven): mode toggle byte.</summary>
    protected virtual void ParseClassicMode(MessageReader r)
    {
        OnScClassicMode(new ScClassicModeEvent(r.ReadByte() != 0));
    }

    /// <summary>VModelPos (Sven): enable byte; when set, a coordinate triple.</summary>
    protected virtual void ParseVModelPos(MessageReader r)
    {
        bool enabled = r.ReadByte() == 1;
        float x = 0, y = 0, z = 0;
        if (enabled)
        {
            x = ReadCoord(r); y = ReadCoord(r); z = ReadCoord(r);
        }
        OnScVModelPos(new ScVModelPosEvent(enabled, x, y, z));
    }

    /// <summary>TimeEnd (Sven): 32-bit round end time (client adds its clock).</summary>
    protected virtual void ParseTimeEnd(MessageReader r)
    {
        OnScTimeEnd(new ScTimeEndEvent(ReadInt32(r)));
    }

    /// <summary>OnTank (Sven): tank driving flag byte.</summary>
    protected virtual void ParseOnTank(MessageReader r)
    {
        OnScOnTank(new ScOnTankEvent(r.ReadByte() != 0));
    }

    /// <summary>Playlist (Sven): playlist name.</summary>
    protected virtual void ParsePlaylist(MessageReader r)
    {
        OnScPlaylist(new ScPlaylistEvent(r.ReadString()));
    }

    /// <summary>Speaksent (Sven): sentence name the client feeds to the "speak" command.</summary>
    protected virtual void ParseSentence(MessageReader r)
    {
        OnScSentence(new ScSentenceEvent(r.ReadString()));
    }

    /// <summary>ValClass (Sven): five class/slot shorts.</summary>
    protected virtual void ParseValClass(MessageReader r)
    {
        var classes = new short[5];
        for (int i = 0; i < 5; i++) classes[i] = ReadShort(r);
        OnScValClass(new ScValClassEvent(classes));
    }

    /// <summary>PrtlUpdt (Sven): portal/monitor update with conditional sections.
    /// entity(long), enable(byte; 0 removes), vec1, vec2, type(byte), style(byte), life(float),
    /// byte, long, long, flag(byte); type != 0: flag(byte) → long + coord3 + ang3,
    /// type 1: flag → long,long; type 2: two flag bytes; style 2: name string.</summary>
    protected virtual void ParsePortalUpdate(MessageReader r)
    {
        int entity = ReadInt32(r);
        if (r.ReadByte() == 0)
        {
            OnScPortalUpdate(new ScPortalUpdateEvent(true, entity,
                [], [], 0, 0, 0, 0, 0, 0, false,
                null, null, null, null, null, null, null, null));
            return;
        }

        var vec1 = ReadCoordVec3(r);
        var vec2 = ReadCoordVec3(r);
        byte type = r.ReadByte();
        byte style = r.ReadByte();
        float life = ReadFloat(r);
        byte byte1 = r.ReadByte();
        int long1 = ReadInt32(r);
        int long2 = ReadInt32(r);
        bool flag1 = r.ReadByte() != 0;

        int? modelIndex = null;
        float[]? modelOrigin = null, modelAngles = null;
        int? long3 = null, long4 = null;
        bool? flagA = null, flagB = null;
        if (type != 0)
        {
            if (r.ReadByte() != 0)
            {
                modelIndex = ReadInt32(r);
                modelOrigin = ReadCoordVec3(r);
                modelAngles = ReadAngleVec3(r);
            }
            if (type == 1)
            {
                if (r.ReadByte() != 0)
                {
                    long3 = ReadInt32(r);
                    long4 = ReadInt32(r);
                }
            }
            else if (type == 2)
            {
                flagA = r.ReadByte() != 0;
                flagB = r.ReadByte() != 0;
            }
        }
        string? name = style == 2 ? r.ReadString() : null;

        OnScPortalUpdate(new ScPortalUpdateEvent(false, entity, vec1, vec2, type, style, life,
            byte1, long1, long2, flag1, modelIndex, modelOrigin, modelAngles, long3, long4, flagA, flagB, name));
    }

    /// <summary>InvAdd (Sven): id(long), 3 flag bytes, time(float), 5 strings.</summary>
    protected virtual void ParseInventoryAdd(MessageReader r)
    {
        int id = ReadInt32(r);
        bool f1 = r.ReadByte() != 0, f2 = r.ReadByte() != 0, f3 = r.ReadByte() != 0;
        float time = ReadFloat(r);
        OnScInventoryAdd(new ScInventoryAddEvent(id, f1, f2, f3, time,
            r.ReadString(), r.ReadString(), r.ReadString(), r.ReadString(), r.ReadString()));
    }

    /// <summary>InvRemove (Sven): id (0 removes all) and a flag byte.</summary>
    protected virtual void ParseInventoryRemove(MessageReader r)
    {
        OnScInventoryRemove(new ScInventoryRemoveEvent(ReadInt32(r), r.ReadByte()));
    }

    /// <summary>ToggleElem (Sven): HUD channel byte (0-31) and state byte.</summary>
    protected virtual void ParseToggleElem(MessageReader r)
    {
        OnScToggleElem(new ScToggleElemEvent(r.ReadByte(), r.ReadByte() != 0));
    }

    /// <summary>CustSpr (Sven): channel, flags(long), sprite, x, y, w(short), h(short),
    /// two RGBA quads, two bytes, five floats, final byte.</summary>
    protected virtual void ParseCustomSprite(MessageReader r)
    {
        byte channel = r.ReadByte();
        int flags = ReadInt32(r);
        var sprite = r.ReadString();
        byte x = r.ReadByte(), y = r.ReadByte();
        short width = ReadShort(r), height = ReadShort(r);
        byte r1 = r.ReadByte(), g1 = r.ReadByte(), b1 = r.ReadByte(), a1 = r.ReadByte();
        byte r2 = r.ReadByte(), g2 = r.ReadByte(), b2 = r.ReadByte(), a2 = r.ReadByte();
        byte unknown1 = r.ReadByte(), unknown2 = r.ReadByte();
        var ev = new ScCustomSpriteEvent(channel, flags, sprite, x, y, width, height,
            r1, g1, b1, a1, r2, g2, b2, a2, unknown1, unknown2,
            ReadFloat(r), ReadFloat(r), ReadFloat(r), ReadFloat(r), ReadFloat(r), r.ReadByte());
        OnScCustomSprite(ev);
    }

    /// <summary>NumDisplay (Sven): channel, flags(long), value(float), x, y, width/height floats,
    /// two RGBA quads, font string, region bytes/shorts, four floats, final byte.</summary>
    protected virtual void ParseNumDisplay(MessageReader r)
    {
        byte channel = r.ReadByte();
        int flags = ReadInt32(r);
        float value = ReadFloat(r);
        byte x = r.ReadByte(), y = r.ReadByte();
        float width = ReadFloat(r), height = ReadFloat(r);
        byte r1 = r.ReadByte(), g1 = r.ReadByte(), b1 = r.ReadByte(), a1 = r.ReadByte();
        byte r2 = r.ReadByte(), g2 = r.ReadByte(), b2 = r.ReadByte(), a2 = r.ReadByte();
        var font = r.ReadString();
        byte regionX = r.ReadByte(), regionY = r.ReadByte();
        short regionWidth = ReadShort(r), regionHeight = ReadShort(r);
        var ev = new ScNumDisplayEvent(channel, flags, value, x, y, width, height,
            r1, g1, b1, a1, r2, g2, b2, a2, font, regionX, regionY, regionWidth, regionHeight,
            ReadFloat(r), ReadFloat(r), ReadFloat(r), ReadFloat(r), r.ReadByte());
        OnScNumDisplay(ev);
    }

    /// <summary>UpdateNum (Sven): channel byte and float value.</summary>
    protected virtual void ParseUpdateNum(MessageReader r)
    {
        OnScUpdateNum(new ScUpdateNumEvent(r.ReadByte(), ReadFloat(r)));
    }

    /// <summary>TimeDisplay (Sven): like NumDisplay but with two leading value floats.</summary>
    protected virtual void ParseTimeDisplay(MessageReader r)
    {
        byte channel = r.ReadByte();
        int flags = ReadInt32(r);
        float value1 = ReadFloat(r), value2 = ReadFloat(r);
        float width = ReadFloat(r), height = ReadFloat(r);
        byte r1 = r.ReadByte(), g1 = r.ReadByte(), b1 = r.ReadByte(), a1 = r.ReadByte();
        byte r2 = r.ReadByte(), g2 = r.ReadByte(), b2 = r.ReadByte(), a2 = r.ReadByte();
        var font = r.ReadString();
        byte regionX = r.ReadByte(), regionY = r.ReadByte();
        short regionWidth = ReadShort(r), regionHeight = ReadShort(r);
        var ev = new ScTimeDisplayEvent(channel, flags, value1, value2, width, height,
            r1, g1, b1, a1, r2, g2, b2, a2, font, regionX, regionY, regionWidth, regionHeight,
            ReadFloat(r), ReadFloat(r), ReadFloat(r), ReadFloat(r), r.ReadByte());
        OnScTimeDisplay(ev);
    }

    /// <summary>UpdateTime (Sven): channel byte, time float, duration float.</summary>
    protected virtual void ParseUpdateTime(MessageReader r)
    {
        OnScUpdateTime(new ScUpdateTimeEvent(r.ReadByte(), ReadFloat(r), ReadFloat(r)));
    }

    /// <summary>WeaponSpr (Sven): HUD slot short and sprite name.</summary>
    protected virtual void ParseWeaponSprite(MessageReader r)
    {
        short slot = ReadShort(r);
        ScWeaponSprite?.Invoke((slot, r.ReadString()));
    }

    /// <summary>CustWeapon (Sven): HUD slot short and custom weapon name.</summary>
    protected virtual void ParseCustomWeapon(MessageReader r)
    {
        short slot = ReadShort(r);
        ScCustomWeapon?.Invoke((slot, r.ReadString()));
    }

    /// <summary>PrintKB (Sven): key binding name string.</summary>
    protected virtual void ParseKeyBinding(MessageReader r)
    {
        ScKeyBinding?.Invoke(r.ReadString());
    }

    /// <summary>NotifyText (Sven): type byte and text.</summary>
    protected virtual void ParseNotifyText(MessageReader r)
    {
        byte type = r.ReadByte();
        ScNotifyText?.Invoke((type, r.ReadString()));
    }

    /// <summary>Gib (Sven): gib type byte (0, 1, 2, 4 valid) then origin and velocity triples.
    /// Unknown types carry no coordinates.</summary>
    protected virtual void ParseGib(MessageReader r)
    {
        byte type = r.ReadByte();
        float ox = 0, oy = 0, oz = 0, vx = 0, vy = 0, vz = 0;
        if (type is 0 or 1 or 2 or 4)
        {
            ox = ReadCoord(r); oy = ReadCoord(r); oz = ReadCoord(r);
            vx = ReadCoord(r); vy = ReadCoord(r); vz = ReadCoord(r);
        }
        OnScGib(new ScGibEvent(type, ox, oy, oz, vx, vy, vz));
    }

    /// <summary>TE_CUSTOM (Sven): subtyped effect. Subtype 1: id(short), count(short),
    /// origin triple when count > 0. Subtype 3: one byte. Subtype 2: empty.</summary>
    protected virtual void ParseTeCustom(MessageReader r)
    {
        byte subType = r.ReadByte();
        short id = 0, count = 0;
        float ox = 0, oy = 0, oz = 0;
        byte value = 0;
        if (subType == 1)
        {
            id = ReadShort(r);
            count = ReadShort(r);
            if (count > 0)
            {
                ox = ReadCoord(r); oy = ReadCoord(r); oz = ReadCoord(r);
            }
        }
        else if (subType == 3)
        {
            value = r.ReadByte();
        }
        OnScTeCustom(new ScTeCustomEvent(subType, id, count, ox, oy, oz, value));
    }

    /// <summary>CbElec (Sven): state byte — bits 0-4 entity index, bit 6 active.</summary>
    protected virtual void ParseCbElec(MessageReader r)
    {
        byte data = r.ReadByte();
        OnScCbElec(new ScCbElecEvent((data & 0x40) != 0, (byte)(data & 0x1F)));
    }

    /// <summary>ShkFlash (Sven): origin triple plus mode byte (0 = fire, else impact).</summary>
    protected virtual void ParseShkFlash(MessageReader r)
    {
        float x = ReadCoord(r), y = ReadCoord(r), z = ReadCoord(r);
        OnScShkFlash(new ScShkFlashEvent(x, y, z, r.ReadByte()));
    }

    /// <summary>TracerDecal (Sven): start/end triples, decal type byte, trailing byte.</summary>
    protected virtual void ParseTracerDecal(MessageReader r)
    {
        float sx = ReadCoord(r), sy = ReadCoord(r), sz = ReadCoord(r);
        float ex = ReadCoord(r), ey = ReadCoord(r), ez = ReadCoord(r);
        byte type = r.ReadByte();
        byte unknown = r.ReadByte();
        OnScTracerDecal(new ScTracerDecalEvent(sx, sy, sz, ex, ey, ez, type, unknown));
    }

    /// <summary>SporeTrail (Sven): entity short and attach flag byte.</summary>
    protected virtual void ParseSporeTrail(MessageReader r)
    {
        short entity = ReadShort(r);
        OnScSporeTrail(new ScSporeTrailEvent(entity, r.ReadByte() != 0));
    }

    /// <summary>CreateBlood (Sven): origin triple, colour byte, amount byte.</summary>
    protected virtual void ParseCreateBlood(MessageReader r)
    {
        float x = ReadCoord(r), y = ReadCoord(r), z = ReadCoord(r);
        OnScCreateBlood(new ScCreateBloodEvent(x, y, z, r.ReadByte(), r.ReadByte()));
    }

    /// <summary>GargSplash (Sven): origin triple plus colour triple. The client reads the
    /// colour channels as coordinates and takes their absolute value (float bit-mask AND),
    /// then feeds them to the splash temp entity as floats.</summary>
    protected virtual void ParseGargSplash(MessageReader r)
    {
        float x = ReadCoord(r), y = ReadCoord(r), z = ReadCoord(r);
        float cr = MathF.Abs(ReadCoord(r));
        float cg = MathF.Abs(ReadCoord(r));
        float cb = MathF.Abs(ReadCoord(r));
        OnScGargSplash(new ScGargSplashEvent(x, y, z, cr, cg, cb));
    }

    /// <summary>StartSound (Sven): flag-driven fields; see <see cref="ScStartSoundEvent"/>.</summary>
    protected virtual void ParseStartSound(MessageReader r)
    {
        short flags = ReadShort(r);
        short? entity = (flags & 0x10) != 0 ? ReadShort(r) : null;
        byte? volume = (flags & 0x01) != 0 ? r.ReadByte() : null;
        byte? attenuation = (flags & 0x02) != 0 ? r.ReadByte() : null;
        byte? pitch = (flags & 0x04) != 0 ? r.ReadByte() : null;
        float? ox = null, oy = null, oz = null;
        if ((flags & 0x08) != 0)
        {
            ox = ReadCoord(r); oy = ReadCoord(r); oz = ReadCoord(r);
        }
        float? duration = (flags & 0x8000) != 0 ? ReadFloat(r) : null;
        byte channel = r.ReadByte();
        short soundIndex = ReadShort(r);
        OnScStartSound(new ScStartSoundEvent(flags, entity, volume, attenuation, pitch, ox, oy, oz, duration, channel, soundIndex));
    }

    /// <summary>ToxicCloud (Sven): origin triple.</summary>
    protected virtual void ParseToxicCloud(MessageReader r)
    {
        OnScToxicCloud(new ScToxicCloudEvent(ReadCoord(r), ReadCoord(r), ReadCoord(r)));
    }

    /// <summary>SRDetonate (Sven): origin triple and radius byte.</summary>
    protected virtual void ParseSrDetonate(MessageReader r)
    {
        float x = ReadCoord(r), y = ReadCoord(r), z = ReadCoord(r);
        OnScSrDetonate(new ScSrDetonateEvent(x, y, z, r.ReadByte()));
    }

    /// <summary>SRPrimed (Sven): entity byte and fuse float.</summary>
    protected virtual void ParseSrPrimed(MessageReader r)
    {
        byte entity = r.ReadByte();
        OnScSrPrimed(new ScSrPrimedEvent(entity, ReadFloat(r)));
    }

    /// <summary>SRPrimedOff (Sven): entity byte.</summary>
    protected virtual void ParseSrPrimedOff(MessageReader r)
    {
        OnScSrPrimedOff(new ScSrPrimedOffEvent(r.ReadByte()));
    }

    /// <summary>RampSprite (Sven): entity short, life byte, origin triple, flags short, then
    /// optional per-flag fields; see <see cref="ScRampSpriteEvent"/>.</summary>
    protected virtual void ParseRampSprite(MessageReader r)
    {
        short entity = ReadShort(r);
        byte lifeTicks = r.ReadByte();
        float x = ReadCoord(r), y = ReadCoord(r), z = ReadCoord(r);
        short flags = ReadShort(r);

        byte? startTime = null, fadeIn = null, fadeOut = null, renderModeFx = null;
        byte? red = null, green = null, blue = null, unknownBit15 = null;
        if ((flags & 0x1) != 0) startTime = r.ReadByte();
        if ((flags & 0x2) != 0) fadeIn = r.ReadByte();
        if ((flags & 0x4) != 0) fadeOut = r.ReadByte();
        if ((flags & 0x8) != 0) renderModeFx = r.ReadByte();
        if ((flags & 0x10) != 0) red = r.ReadByte();
        if ((flags & 0x20) != 0) green = r.ReadByte();
        if ((flags & 0x40) != 0) blue = r.ReadByte();
        if ((flags & 0x8000) != 0) unknownBit15 = r.ReadByte();

        float? color2R = null, color2G = null, color2B = null;
        if ((flags & 0x100) != 0)
        {
            color2R = ReadCoord(r); color2G = ReadCoord(r); color2B = ReadCoord(r);
        }

        OnScRampSprite(new ScRampSpriteEvent(entity, lifeTicks, x, y, z, flags,
            startTime, fadeIn, fadeOut, renderModeFx, red, green, blue, unknownBit15,
            color2R, color2G, color2B,
            (flags & 0x200) != 0 ? r.ReadByte() : null,
            (flags & 0x400) != 0 ? r.ReadByte() : null,
            (flags & 0x800) != 0 ? r.ReadByte() : null,
            (flags & 0x1000) != 0 ? r.ReadByte() : null,
            (flags & 0x2000) != 0 ? r.ReadByte() : null,
            (flags & 0x4000) != 0 ? r.ReadByte() : null,
            (flags & 0x8000) != 0 ? r.ReadByte() : null));
    }

    /// <summary>ShieldRic (Sven): origin triple.</summary>
    protected virtual void ParseShieldRic(MessageReader r)
    {
        OnScShieldRic(new ScShieldRicEvent(ReadCoord(r), ReadCoord(r), ReadCoord(r)));
    }

    /// <summary>WeatherFX (Sven): type short, min/max triples, angle triple, then a fixed
    /// tail of short/byte/float groups passed through opaquely by the client.</summary>
    protected virtual void ParseWeatherFx(MessageReader r)
    {
        short type = ReadShort(r);
        float minX = ReadCoord(r), minY = ReadCoord(r), minZ = ReadCoord(r);
        float maxX = ReadCoord(r), maxY = ReadCoord(r), maxZ = ReadCoord(r);
        float ax = ReadAngle(r), ay = ReadAngle(r), az = ReadAngle(r);
        short unknownShort1 = ReadShort(r);
        float float1 = ReadFloat(r);
        byte byte1 = r.ReadByte();
        short unknownShort2 = ReadShort(r);
        float float2 = ReadFloat(r);
        byte byte2 = r.ReadByte(), byte3 = r.ReadByte();
        float float3 = ReadFloat(r);
        byte byte4 = r.ReadByte(), byte5 = r.ReadByte(), byte6 = r.ReadByte(), byte7 = r.ReadByte();
        var ev = new ScWeatherFxEvent(type, minX, minY, minZ, maxX, maxY, maxZ, ax, ay, az,
            unknownShort1, float1, byte1, unknownShort2, float2, byte2, byte3, float3,
            byte4, byte5, byte6, byte7, ReadFloat(r), ReadFloat(r), ReadFloat(r), ReadFloat(r));
        OnScWeatherFx(ev);
    }

    /// <summary>CameraMouse (Sven): mode byte; mode 2 carries a parameter string.</summary>
    protected virtual void ParseCameraMouse(MessageReader r)
    {
        byte mode = r.ReadByte();
        OnScCameraMouse(new ScCameraMouseEvent(mode, mode == 2 ? r.ReadString() : string.Empty));
    }

    /// <summary>Flamethwr (Sven): entity byte plus start/end triples.</summary>
    protected virtual void ParseFlamethrower(MessageReader r)
    {
        byte entity = r.ReadByte();
        float sx = ReadCoord(r), sy = ReadCoord(r), sz = ReadCoord(r);
        float ex = ReadCoord(r), ey = ReadCoord(r), ez = ReadCoord(r);
        OnScFlamethrower(new ScFlamethrowerEvent(entity, sx, sy, sz, ex, ey, ez));
    }

    /// <summary>ChangeSky (Sven): sky name string and colour triple (-1s keep the default).</summary>
    protected virtual void ParseChangeSky(MessageReader r)
    {
        var sky = r.ReadString();
        float cr = ReadCoord(r), cg = ReadCoord(r), cb = ReadCoord(r);
        OnScChangeSky(new ScChangeSkyEvent(sky, cr, cg, cb));
    }

    /// <summary>ClServerInfo (Sven): flag byte, 32-bit value and key string.</summary>
    protected virtual void ParseClServerInfo(MessageReader r)
    {
        OnScClServerInfo(new ScClServerInfoEvent(r.ReadByte(), ReadInt32(r), r.ReadString()));
    }

    /// <summary>EndVote (Sven): empty payload.</summary>
    protected virtual void ParseEndVote(MessageReader r)
    {
        // no payload — arrival itself clears the vote UI
    }

    /// <summary>MapList (Sven): decoded from CMapVotePanel's vtable+0x21C virtual. A command
    /// byte selects reset (0: clears the list and stores the total count), close (0x7B), or
    /// an incremental update (start/end shorts plus one map-name string per entry).</summary>
    protected virtual void ParseMapList(MessageReader r)
    {
        byte command = r.ReadByte();
        short totalMaps = 0, startIndex = 0, endIndex = 0;
        string[] mapNames = [];
        if (command == 0)
        {
            totalMaps = ReadShort(r);
        }
        else if (command != 0x7B)
        {
            startIndex = ReadShort(r);
            endIndex = ReadShort(r);
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
    protected virtual void ParseVoteMenu(MessageReader r)
    {
        byte voteId = r.ReadByte();
        OnScVoteMenu(new ScVoteMenuEvent(voteId, r.ReadString(), r.ReadString(), r.ReadString()));
    }

    /// <summary>ClExtrasInfo (Sven): four length-prefixed blocks framing a CryptoPP
    /// authenticated-encryption payload — plain length, IV, encrypted data, and a
    /// digest sized to the session key. The client derives the key via GenerateKey
    /// from the ClServerInfo handshake and decrypts to
    /// "playerIndex\nauthId\n\"name\"\nlevel" which updates the scoreboard's per-player
    /// admin level; the key material never leaves the client, so only the framing is
    /// decoded here.</summary>
    protected virtual void ParseClExtrasInfo(MessageReader r)
    {
        int plainLength = ReadInt32(r);
        byte[] iv = ReadBlock(r);
        byte[] encryptedData = ReadBlock(r);
        byte[] encryptedDigest = ReadBlock(r);
        OnScClExtrasInfo(new ScClExtrasInfoEvent(plainLength, iv, encryptedData, encryptedDigest));
    }

    /// <summary>Reads a 32-bit length followed by that many bytes (empty on invalid length).</summary>
    private static byte[] ReadBlock(MessageReader r)
    {
        int length = ReadInt32(r);
        if (length <= 0)
            return [];
        var data = new byte[length];
        r.ReadBytes(data);
        return data;
    }

    // ── read helpers ──

    /// <summary>Reads a little-endian 32-bit float (0 on overflow).</summary>
    protected static float ReadFloat(MessageReader r)
    {
        r.ReadSingle(out float value);
        return value;
    }

    /// <summary>Reads a signed byte.</summary>
    protected static sbyte ReadSByte(MessageReader r) => (sbyte)r.ReadByte();

    /// <summary>Reads a Sven angle: signed byte scaled by 360/256 (1.40625).</summary>
    protected static float ReadAngle(MessageReader r) => ReadSByte(r) * (360f / 256f);

    /// <summary>Reads a Sven 16-bit angle: signed short scaled by 360/65536.</summary>
    protected static float ReadAngle16(MessageReader r) => ReadShort(r) * (360f / 65536f);

    /// <summary>Reads three coordinates into an array (READ_COORD_vec3 helper order).</summary>
    protected static float[] ReadCoordVec3(MessageReader r) =>
        [ReadCoord(r), ReadCoord(r), ReadCoord(r)];

    /// <summary>Reads three angles into an array (READ_ANGLE_vec3 helper order).</summary>
    protected static float[] ReadAngleVec3(MessageReader r) =>
        [ReadAngle(r), ReadAngle(r), ReadAngle(r)];
}
