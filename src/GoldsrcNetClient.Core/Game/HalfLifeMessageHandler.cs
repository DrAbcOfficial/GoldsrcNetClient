using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Network;

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
    protected override bool DispatchUserMessage(GoldsrcConnection connection, byte index, string name, MessageReader reader)
    {
        switch (name)
        {
            case "CurWeapon": ParseCurWeapon(reader); return true;
            case "Damage": ParseDamage(reader); return true;
            case "DeathMsg": ParseDeathMsg(reader); return true;
            case "Health": ParseHealth(reader); return true;
            case "Battery": ParseBattery(reader); return true;
            case "AmmoX": ParseAmmoX(reader); return true;
            case "AmmoPickup": ParseAmmoPickup(reader); return true;
            case "FlashBat": ParseFlashBat(reader); return true;
            case "Flashlight": ParseFlashlight(reader); return true;
            case "GameMode": ParseGameMode(reader); return true;
            case "GameTitle": ParseGameTitle(reader); return true;
            case "Geiger": ParseGeiger(reader); return true;
            case "HideWeapon": ParseHideWeapon(reader); return true;
            case "HudText": ParseHudText(reader); return true;
            case "InitHUD": ParseInitHUD(reader); return true;
            case "ItemPickup": ParseItemPickup(reader); return true;
            case "ScreenFade": ParseScreenFade(reader); return true;
            case "ScreenShake": ParseScreenShake(reader); return true;
            case "SetFOV": ParseSetFOV(reader); return true;
            case "StatusIcon": ParseStatusIcon(reader); return true;
            case "TeamInfo": ParseTeamInfo(reader); return true;
            case "TextMsg": ParseTextMsg(reader); return true;
            case "WeaponList": ParseWeaponList(reader); return true;
            case "WeapPickup": ParseWeapPickup(reader); return true;
            case "SayText": ParseSayText(reader); return true;
            case "Train": ParseTrain(reader); return true;
            case "VGUIMenu": ParseVguiMenu(reader); return true;
            case "ResetHUD": ParseResetHUD(reader); return true;
            case "Concuss": ParseConcuss(reader); return true;
            case "HudColor": ParseHudColor(reader); return true;
            default: return false;
        }
    }

    /// <summary>CurWeapon: byte IsActive, byte WeaponId, byte ClipAmmo</summary>
    protected virtual void ParseCurWeapon(MessageReader r)
    {
        var ev = new CurWeaponEvent(r.ReadByte(), r.ReadByte(), r.ReadByte());
        OnCurWeapon(ev);
    }

    protected virtual void ParseDamage(MessageReader r)
    {
        var ev = new DamageEvent(r.ReadByte(), r.ReadByte(), ReadInt32(r), ReadCoord(r), ReadCoord(r), ReadCoord(r));
        OnDamage(ev);
    }

    protected virtual void ParseDeathMsg(MessageReader r)
    {
        var ev = new DeathMsgEvent(r.ReadByte(), r.ReadByte(), 0, r.ReadString());
        OnDeathMsg(ev);
    }

    protected virtual void ParseHealth(MessageReader r)
    {
        var ev = new HealthEvent(r.ReadByte());
        OnHealth(ev);
    }

    protected virtual void ParseBattery(MessageReader r)
    {
        var ev = new BatteryEvent(ReadShort(r));
        OnBattery(ev);
    }

    protected virtual void ParseAmmoX(MessageReader r)
    {
        var ev = new AmmoXEvent(r.ReadByte(), r.ReadByte());
        OnAmmoX(ev);
    }

    protected virtual void ParseAmmoPickup(MessageReader r)
    {
        var ev = new AmmoPickupEvent(r.ReadByte(), r.ReadByte());
        OnAmmoPickup(ev);
    }

    protected virtual void ParseFlashBat(MessageReader r)
    {
        var ev = new FlashBatEvent(r.ReadByte());
        OnFlashBat(ev);
    }

    protected virtual void ParseFlashlight(MessageReader r)
    {
        var ev = new FlashlightEvent(r.ReadByte(), r.ReadByte());
        OnFlashlight(ev);
    }

    protected virtual void ParseGameMode(MessageReader r)
    {
        var ev = new GameModeEvent(r.ReadByte());
        OnGameMode(ev);
    }

    protected virtual void ParseGameTitle(MessageReader r)
    {
        var ev = new GameTitleEvent(r.ReadByte());
        OnGameTitle(ev);
    }

    protected virtual void ParseGeiger(MessageReader r)
    {
        var ev = new GeigerEvent(r.ReadByte());
        OnGeiger(ev);
    }

    protected virtual void ParseHideWeapon(MessageReader r)
    {
        var ev = new HideWeaponEvent(r.ReadByte());
        OnHideWeapon(ev);
    }

    protected virtual void ParseHudText(MessageReader r)
    {
        var ev = new HudTextEvent(r.ReadString(), r.ReadByte());
        OnHudText(ev);
    }

    protected virtual void ParseInitHUD(MessageReader r)
    {
        var ev = new InitHudEvent();
        OnInitHUD(ev);
    }

    protected virtual void ParseItemPickup(MessageReader r)
    {
        var ev = new ItemPickupEvent(r.ReadString());
        OnItemPickup(ev);
    }

    protected virtual void ParseScreenFade(MessageReader r)
    {
        var ev = new ScreenFadeEvent(ReadShort(r), ReadShort(r), ReadShort(r), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
        OnScreenFade(ev);
    }

    protected virtual void ParseScreenShake(MessageReader r)
    {
        var ev = new ScreenShakeEvent(ReadShort(r), ReadShort(r), ReadShort(r));
        OnScreenShake(ev);
    }

    protected virtual void ParseSetFOV(MessageReader r)
    {
        var ev = new SetFovEvent(r.ReadByte());
        OnSetFOV(ev);
    }

    protected virtual void ParseStatusIcon(MessageReader r)
    {
        var ev = new StatusIconEvent(r.ReadByte(), r.ReadString(), r.ReadByte(), r.ReadByte(), r.ReadByte());
        OnStatusIcon(ev);
    }

    protected virtual void ParseTeamInfo(MessageReader r)
    {
        var ev = new TeamInfoEvent(r.ReadByte(), r.ReadString());
        OnTeamInfo(ev);
    }

    protected virtual void ParseTextMsg(MessageReader r)
    {
        var ev = new TextMsgEvent(r.ReadByte(), r.ReadString());
        OnTextMsg(ev);
    }

    protected virtual void ParseWeaponList(MessageReader r)
    {
        var ev = new WeaponListEvent(r.ReadString(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
        OnWeaponList(ev);
    }

    protected virtual void ParseWeapPickup(MessageReader r)
    {
        var ev = new WeapPickupEvent(r.ReadString());
        OnWeapPickup(ev);
    }

    protected virtual void ParseSayText(MessageReader r)
    {
        var ev = new SayTextEvent(r.ReadByte(), r.ReadString());
        OnSayText(ev);
    }

    protected virtual void ParseTrain(MessageReader r)
    {
        var ev = new TrainEvent(r.ReadByte());
        OnTrain(ev);
    }

    protected virtual void ParseVguiMenu(MessageReader r)
    {
        var ev = new VguiMenuEvent(r.ReadByte(), r.ReadStringLine());
        OnVguiMenu(ev);
    }

    protected virtual void ParseResetHUD(MessageReader r)
    {
        var ev = new ResetHudEvent();
        OnResetHUD(ev);
    }

    protected virtual void ParseConcuss(MessageReader r)
    {
        var ev = new ConcussEvent(r.ReadByte());
        OnConcuss(ev);
    }

    protected virtual void ParseHudColor(MessageReader r)
    {
        var ev = new HudColorEvent(r.ReadByte(), r.ReadByte(), r.ReadByte());
        OnHudColor(ev);
    }
}
