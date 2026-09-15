using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Network;
using Microsoft.Extensions.Logging;

namespace GoldsrcNetClient.Core.Game;

/// <summary>
/// Server message handler for Half-Life Deathmatch and derived mods.
/// Parses common Half-Life user messages and raises typed events.
/// </summary>
/// <remarks>
/// <para>Supported messages: CurWeapon, Damage, DeathMsg, Health, Battery,
/// AmmoX, AmmoPickup, FlashBat, Flashlight, GameMode, GameTitle, Geiger,
/// HideWeapon, HudText, InitHUD, ItemPickup, ScreenFade, ScreenShake, SetFOV,
/// StatusIcon, TeamInfo, TextMsg, WeaponList, WeapPickup, SayText, Train,
/// VGUIMenu, ResetHUD, Concuss, HudColor.</para>
///
/// <para>Unrecognized messages raise <see cref="GameMessageHandler.OnRawUserMessage"/>.</para>
/// </remarks>
public class HalfLifeMessageHandler : GameMessageHandler
{
    #region Events

    /// <summary>Raised when a player's active weapon changes.</summary>
    public event Action<CurWeaponEvent>? CurWeapon;
    /// <summary>Raised when a player takes damage.</summary>
    public event Action<DamageEvent>? Damage;
    /// <summary>Raised when a player dies.</summary>
    public event Action<DeathMsgEvent>? DeathMsg;
    /// <summary>Raised when a player's health updates.</summary>
    public event Action<HealthEvent>? Health;
    /// <summary>Raised when a player's armor updates.</summary>
    public event Action<BatteryEvent>? Battery;
    /// <summary>Raised when reserve ammo count changes.</summary>
    public event Action<AmmoXEvent>? AmmoX;
    /// <summary>Raised when ammo is picked up.</summary>
    public event Action<AmmoPickupEvent>? AmmoPickup;
    /// <summary>Raised when flashlight battery changes.</summary>
    public event Action<FlashBatEvent>? FlashBat;
    /// <summary>Raised when flashlight state changes.</summary>
    public event Action<FlashlightEvent>? Flashlight;
    /// <summary>Raised when the game mode changes.</summary>
    public event Action<GameModeEvent>? GameMode;
    /// <summary>Raised to show/hide the game title.</summary>
    public event Action<GameTitleEvent>? GameTitle;
    /// <summary>Raised when near a radiation hazard.</summary>
    public event Action<GeigerEvent>? Geiger;
    /// <summary>Raised when HUD elements are shown/hidden.</summary>
    public event Action<HideWeaponEvent>? HideWeapon;
    /// <summary>Raised when HUD text is sent.</summary>
    public event Action<HudTextEvent>? HudText;
    /// <summary>Raised when the HUD is initialized.</summary>
    public event Action<InitHudEvent>? InitHUD;
    /// <summary>Raised when an item is picked up.</summary>
    public event Action<ItemPickupEvent>? ItemPickup;
    /// <summary>Raised when the screen fades.</summary>
    public event Action<ScreenFadeEvent>? ScreenFade;
    /// <summary>Raised when the screen shakes.</summary>
    public event Action<ScreenShakeEvent>? ScreenShake;
    /// <summary>Raised when field of view changes.</summary>
    public event Action<SetFovEvent>? SetFOV;
    /// <summary>Raised when a status icon is shown/hidden.</summary>
    public event Action<StatusIconEvent>? StatusIcon;
    /// <summary>Raised when a player's team changes.</summary>
    public event Action<TeamInfoEvent>? TeamInfo;
    /// <summary>Raised when a text message is sent.</summary>
    public event Action<TextMsgEvent>? TextMsg;
    /// <summary>Raised when a weapon is registered.</summary>
    public event Action<WeaponListEvent>? WeaponList;
    /// <summary>Raised when a weapon is picked up.</summary>
    public event Action<WeapPickupEvent>? WeapPickup;
    /// <summary>Raised when a chat message is received.</summary>
    public event Action<SayTextEvent>? SayText;
    /// <summary>Raised when train control updates.</summary>
    public event Action<TrainEvent>? Train;
    /// <summary>Raised when a VGUI menu is displayed.</summary>
    public event Action<VguiMenuEvent>? VguiMenu;
    /// <summary>Raised when the HUD is reset.</summary>
    public event Action<ResetHudEvent>? ResetHUD;
    /// <summary>Raised when a concussion effect occurs.</summary>
    public event Action<ConcussEvent>? Concuss;
    /// <summary>Raised when HUD color changes (Opposing Force).</summary>
    public event Action<HudColorEvent>? HudColor;
    /// <summary>Raised when fog settings change (Counter-Strike, Sven Co-op).</summary>
    public event Action<FogEvent>? Fog;

    #endregion

    #region Protected On* methods (for derived class event invocation)

    /// <summary>Invokes the <see cref="CurWeapon"/> event.</summary>
    protected virtual void OnCurWeapon(CurWeaponEvent ev) => CurWeapon?.Invoke(ev);
    /// <summary>Invokes the <see cref="Damage"/> event.</summary>
    protected virtual void OnDamage(DamageEvent ev) => Damage?.Invoke(ev);
    /// <summary>Invokes the <see cref="DeathMsg"/> event.</summary>
    protected virtual void OnDeathMsg(DeathMsgEvent ev) => DeathMsg?.Invoke(ev);
    /// <summary>Invokes the <see cref="Health"/> event.</summary>
    protected virtual void OnHealth(HealthEvent ev) => Health?.Invoke(ev);
    /// <summary>Invokes the <see cref="Battery"/> event.</summary>
    protected virtual void OnBattery(BatteryEvent ev) => Battery?.Invoke(ev);
    /// <summary>Invokes the <see cref="AmmoX"/> event.</summary>
    protected virtual void OnAmmoX(AmmoXEvent ev) => AmmoX?.Invoke(ev);
    /// <summary>Invokes the <see cref="AmmoPickup"/> event.</summary>
    protected virtual void OnAmmoPickup(AmmoPickupEvent ev) => AmmoPickup?.Invoke(ev);
    /// <summary>Invokes the <see cref="FlashBat"/> event.</summary>
    protected virtual void OnFlashBat(FlashBatEvent ev) => FlashBat?.Invoke(ev);
    /// <summary>Invokes the <see cref="Flashlight"/> event.</summary>
    protected virtual void OnFlashlight(FlashlightEvent ev) => Flashlight?.Invoke(ev);
    /// <summary>Invokes the <see cref="GameMode"/> event.</summary>
    protected virtual void OnGameMode(GameModeEvent ev) => GameMode?.Invoke(ev);
    /// <summary>Invokes the <see cref="GameTitle"/> event.</summary>
    protected virtual void OnGameTitle(GameTitleEvent ev) => GameTitle?.Invoke(ev);
    /// <summary>Invokes the <see cref="Geiger"/> event.</summary>
    protected virtual void OnGeiger(GeigerEvent ev) => Geiger?.Invoke(ev);
    /// <summary>Invokes the <see cref="HideWeapon"/> event.</summary>
    protected virtual void OnHideWeapon(HideWeaponEvent ev) => HideWeapon?.Invoke(ev);
    /// <summary>Invokes the <see cref="HudText"/> event.</summary>
    protected virtual void OnHudText(HudTextEvent ev) => HudText?.Invoke(ev);
    /// <summary>Invokes the <see cref="InitHUD"/> event.</summary>
    protected virtual void OnInitHUD(InitHudEvent ev) => InitHUD?.Invoke(ev);
    /// <summary>Invokes the <see cref="ItemPickup"/> event.</summary>
    protected virtual void OnItemPickup(ItemPickupEvent ev) => ItemPickup?.Invoke(ev);
    /// <summary>Invokes the <see cref="ScreenFade"/> event.</summary>
    protected virtual void OnScreenFade(ScreenFadeEvent ev) => ScreenFade?.Invoke(ev);
    /// <summary>Invokes the <see cref="ScreenShake"/> event.</summary>
    protected virtual void OnScreenShake(ScreenShakeEvent ev) => ScreenShake?.Invoke(ev);
    /// <summary>Invokes the <see cref="SetFOV"/> event.</summary>
    protected virtual void OnSetFOV(SetFovEvent ev) => SetFOV?.Invoke(ev);
    /// <summary>Invokes the <see cref="StatusIcon"/> event.</summary>
    protected virtual void OnStatusIcon(StatusIconEvent ev) => StatusIcon?.Invoke(ev);
    /// <summary>Invokes the <see cref="TeamInfo"/> event.</summary>
    protected virtual void OnTeamInfo(TeamInfoEvent ev) => TeamInfo?.Invoke(ev);
    /// <summary>Invokes the <see cref="TextMsg"/> event.</summary>
    protected virtual void OnTextMsg(TextMsgEvent ev) => TextMsg?.Invoke(ev);
    /// <summary>Invokes the <see cref="WeaponList"/> event.</summary>
    protected virtual void OnWeaponList(WeaponListEvent ev) => WeaponList?.Invoke(ev);
    /// <summary>Invokes the <see cref="WeapPickup"/> event.</summary>
    protected virtual void OnWeapPickup(WeapPickupEvent ev) => WeapPickup?.Invoke(ev);
    /// <summary>Invokes the <see cref="SayText"/> event.</summary>
    protected virtual void OnSayText(SayTextEvent ev) => SayText?.Invoke(ev);
    /// <summary>Invokes the <see cref="Train"/> event.</summary>
    protected virtual void OnTrain(TrainEvent ev) => Train?.Invoke(ev);
    /// <summary>Invokes the <see cref="VguiMenu"/> event.</summary>
    protected virtual void OnVguiMenu(VguiMenuEvent ev) => VguiMenu?.Invoke(ev);
    /// <summary>Invokes the <see cref="ResetHUD"/> event.</summary>
    protected virtual void OnResetHUD(ResetHudEvent ev) => ResetHUD?.Invoke(ev);
    /// <summary>Invokes the <see cref="Concuss"/> event.</summary>
    protected virtual void OnConcuss(ConcussEvent ev) => Concuss?.Invoke(ev);
    /// <summary>Invokes the <see cref="HudColor"/> event.</summary>
    protected virtual void OnHudColor(HudColorEvent ev) => HudColor?.Invoke(ev);

    #endregion

    /// <inheritdoc />
    protected override bool DispatchUserMessage(GoldsrcConnection connection, byte index, string name, ref BufferReader reader)
    {
        switch (name)
        {
            case "ReqState":
                // The game DLL asks the client to re-send its state; the vanilla
                // client replies with the "fullupdate" console command.
                ParseReqState(connection, ref reader);
                return true;
            case "CurWeapon": ParseCurWeapon(ref reader); return true;
            case "Damage": ParseDamage(ref reader); return true;
            case "DeathMsg": ParseDeathMsg(ref reader); return true;
            case "Health": ParseHealth(ref reader); return true;
            case "Battery": ParseBattery(ref reader); return true;
            case "AmmoX": ParseAmmoX(ref reader); return true;
            case "AmmoPickup": ParseAmmoPickup(ref reader); return true;
            case "FlashBat": ParseFlashBat(ref reader); return true;
            case "Flashlight": ParseFlashlight(ref reader); return true;
            case "GameMode": ParseGameMode(ref reader); return true;
            case "GameTitle": ParseGameTitle(ref reader); return true;
            case "Geiger": ParseGeiger(ref reader); return true;
            case "HideWeapon": ParseHideWeapon(ref reader); return true;
            case "HudText": ParseHudText(ref reader); return true;
            case "InitHUD": ParseInitHUD(ref reader); return true;
            case "ItemPickup": ParseItemPickup(ref reader); return true;
            case "ScreenFade": ParseScreenFade(ref reader); return true;
            case "ScreenShake": ParseScreenShake(ref reader); return true;
            case "SetFOV": ParseSetFOV(ref reader); return true;
            case "StatusIcon": ParseStatusIcon(ref reader); return true;
            case "TeamInfo": ParseTeamInfo(ref reader); return true;
            case "TextMsg": ParseTextMsg(ref reader); return true;
            case "WeaponList": ParseWeaponList(ref reader); return true;
            case "WeapPickup": ParseWeapPickup(ref reader); return true;
            case "SayText": ParseSayText(ref reader); return true;
            case "Train": ParseTrain(ref reader); return true;
            case "VGUIMenu": ParseVguiMenu(ref reader); return true;
            case "ResetHUD": ParseResetHUD(ref reader); return true;
            case "Concuss": ParseConcuss(ref reader); return true;
            case "HudColor": ParseHudColor(ref reader); return true;
            case "Fog": ParseFog(ref reader); return true;
            default: return false;
        }
    }

    /// <summary>ReqState: the game DLL's voice manager requests the client's voice state;
    /// the vanilla client replies with the <c>VModEnable 1</c> console command. Unanswered
    /// polls keep the server re-queueing state until its reliable channel overflows.</summary>
    protected virtual void ParseReqState(GoldsrcConnection connection, ref BufferReader reader)
    {
        reader.BytePosition = reader.Length; // consume the opaque payload
        connection.Logger.LogDebug("[ReqState] replying VModEnable 1");
        _ = connection.SendStringCmdAsync(Protocol.ClientCommandType.StringCmd, "VModEnable 1");
    }

    /// <summary>CurWeapon: byte IsActive, byte WeaponId, byte ClipAmmo</summary>
    protected virtual void ParseCurWeapon(ref BufferReader r)
    {
        var ev = new CurWeaponEvent(r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8());
        OnCurWeapon(ev);
    }

    protected virtual void ParseDamage(ref BufferReader r)
    {
        var ev = new DamageEvent(r.ReadUInt8(), r.ReadUInt8(), r.ReadInt32(), r.ReadCoord16(), r.ReadCoord16(), r.ReadCoord16());
        OnDamage(ev);
    }

    protected virtual void ParseDeathMsg(ref BufferReader r)
    {
        var ev = new DeathMsgEvent(r.ReadUInt8(), r.ReadUInt8(), 0, r.ReadString());
        OnDeathMsg(ev);
    }

    protected virtual void ParseHealth(ref BufferReader r)
    {
        var ev = new HealthEvent(r.ReadUInt8());
        OnHealth(ev);
    }

    protected virtual void ParseBattery(ref BufferReader r)
    {
        var ev = new BatteryEvent(r.ReadInt16());
        OnBattery(ev);
    }

    protected virtual void ParseAmmoX(ref BufferReader r)
    {
        var ev = new AmmoXEvent(r.ReadUInt8(), r.ReadUInt8());
        OnAmmoX(ev);
    }

    protected virtual void ParseAmmoPickup(ref BufferReader r)
    {
        var ev = new AmmoPickupEvent(r.ReadUInt8(), r.ReadUInt8());
        OnAmmoPickup(ev);
    }

    protected virtual void ParseFlashBat(ref BufferReader r)
    {
        var ev = new FlashBatEvent(r.ReadUInt8());
        OnFlashBat(ev);
    }

    protected virtual void ParseFlashlight(ref BufferReader r)
    {
        var ev = new FlashlightEvent(r.ReadUInt8(), r.ReadUInt8());
        OnFlashlight(ev);
    }

    protected virtual void ParseGameMode(ref BufferReader r)
    {
        var ev = new GameModeEvent(r.ReadUInt8());
        OnGameMode(ev);
    }

    protected virtual void ParseGameTitle(ref BufferReader r)
    {
        var ev = new GameTitleEvent(r.ReadUInt8());
        OnGameTitle(ev);
    }

    protected virtual void ParseGeiger(ref BufferReader r)
    {
        var ev = new GeigerEvent(r.ReadUInt8());
        OnGeiger(ev);
    }

    protected virtual void ParseHideWeapon(ref BufferReader r)
    {
        var ev = new HideWeaponEvent(r.ReadUInt8());
        OnHideWeapon(ev);
    }

    protected virtual void ParseHudText(ref BufferReader r)
    {
        var ev = new HudTextEvent(r.ReadString(), r.ReadUInt8());
        OnHudText(ev);
    }

    protected virtual void ParseInitHUD(ref BufferReader r)
    {
        var ev = new InitHudEvent();
        OnInitHUD(ev);
    }

    protected virtual void ParseItemPickup(ref BufferReader r)
    {
        var ev = new ItemPickupEvent(r.ReadString());
        OnItemPickup(ev);
    }

    protected virtual void ParseScreenFade(ref BufferReader r)
    {
        var ev = new ScreenFadeEvent(r.ReadInt16(), r.ReadInt16(), r.ReadInt16(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8());
        OnScreenFade(ev);
    }

    protected virtual void ParseScreenShake(ref BufferReader r)
    {
        var ev = new ScreenShakeEvent(r.ReadInt16(), r.ReadInt16(), r.ReadInt16());
        OnScreenShake(ev);
    }

    protected virtual void ParseSetFOV(ref BufferReader r)
    {
        var ev = new SetFovEvent(r.ReadUInt8());
        OnSetFOV(ev);
    }

    protected virtual void ParseStatusIcon(ref BufferReader r)
    {
        var ev = new StatusIconEvent(r.ReadUInt8(), r.ReadString(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8());
        OnStatusIcon(ev);
    }

    protected virtual void ParseTeamInfo(ref BufferReader r)
    {
        var ev = new TeamInfoEvent(r.ReadUInt8(), r.ReadString());
        OnTeamInfo(ev);
    }

    protected virtual void ParseTextMsg(ref BufferReader r)
    {
        var ev = new TextMsgEvent(r.ReadUInt8(), r.ReadString());
        OnTextMsg(ev);
    }

    protected virtual void ParseWeaponList(ref BufferReader r)
    {
        var ev = new WeaponListEvent(r.ReadString(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8());
        OnWeaponList(ev);
    }

    protected virtual void ParseWeapPickup(ref BufferReader r)
    {
        var ev = new WeapPickupEvent(r.ReadString());
        OnWeapPickup(ev);
    }

    protected virtual void ParseSayText(ref BufferReader r)
    {
        var ev = new SayTextEvent(r.ReadUInt8(), r.ReadString());
        OnSayText(ev);
    }

    protected virtual void ParseTrain(ref BufferReader r)
    {
        var ev = new TrainEvent(r.ReadUInt8());
        OnTrain(ev);
    }

    protected virtual void ParseVguiMenu(ref BufferReader r)
    {
        var ev = new VguiMenuEvent(r.ReadUInt8(), r.ReadStringLine());
        OnVguiMenu(ev);
    }

    protected virtual void ParseResetHUD(ref BufferReader r)
    {
        var ev = new ResetHudEvent();
        OnResetHUD(ev);
    }

    protected virtual void ParseConcuss(ref BufferReader r)
    {
        var ev = new ConcussEvent(r.ReadUInt8());
        OnConcuss(ev);
    }

    protected virtual void ParseHudColor(ref BufferReader r)
    {
        var ev = new HudColorEvent(r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8());
        OnHudColor(ev);
    }

    /// <summary>Fog: byte R, G, B, Density (Counter-Strike and Sven Co-op send this;
    /// stock Half-Life servers never do, so the case is inert there).</summary>
    protected virtual void ParseFog(ref BufferReader r)
    {
        var ev = new FogEvent(r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8());
        Fog?.Invoke(ev);
    }
}
