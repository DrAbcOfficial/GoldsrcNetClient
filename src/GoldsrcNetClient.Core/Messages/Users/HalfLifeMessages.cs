using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Messages.Parsing;

namespace GoldsrcNetClient.Core.Messages.Users;

/// <summary>
/// The Half-Life (and derived-mod) user message parsers: CurWeapon, Damage,
/// DeathMsg, Health, Battery, AmmoX, AmmoPickup, FlashBat, Flashlight, GameMode,
/// GameTitle, Geiger, HideWeapon, HudText, InitHUD, ItemPickup, ScreenFade,
/// ScreenShake, SetFOV, StatusIcon, TeamInfo, TextMsg, WeaponList, WeapPickup,
/// SayText, Train, VGUIMenu, ResetHUD, Concuss, HudColor plus ReqState.
/// </summary>
/// <remarks>
/// Each message costs one parser method plus one registration line — no event
/// declarations, no dispatch switch. Unregistered names surface as
/// <see cref="RawUserMessage"/>.
/// </remarks>
public static class HalfLifeMessages
{
    /// <summary>
    /// Registers the Half-Life user message parsers — the base vocabulary every
    /// GoldSrc mod inherits. A game profile calls this first, then adds or overrides
    /// its own message names.
    /// </summary>
    public static void Register(ParserRegistry.Builder builder)
    {
        builder
            .AddUser("ReqState", static (ref BufferReader r) => ParseReqState(ref r))
            .AddUser("CurWeapon", static (ref BufferReader r) => ParseCurWeapon(ref r))
            .AddUser("Damage", static (ref BufferReader r) => ParseDamage(ref r))
            .AddUser("DeathMsg", static (ref BufferReader r) => ParseDeathMsg(ref r))
            .AddUser("Health", static (ref BufferReader r) => ParseHealth(ref r))
            .AddUser("Battery", static (ref BufferReader r) => ParseBattery(ref r))
            .AddUser("AmmoX", static (ref BufferReader r) => ParseAmmoX(ref r))
            .AddUser("AmmoPickup", static (ref BufferReader r) => ParseAmmoPickup(ref r))
            .AddUser("FlashBat", static (ref BufferReader r) => ParseFlashBat(ref r))
            .AddUser("Flashlight", static (ref BufferReader r) => ParseFlashlight(ref r))
            .AddUser("GameMode", static (ref BufferReader r) => ParseGameMode(ref r))
            .AddUser("GameTitle", static (ref BufferReader r) => ParseGameTitle(ref r))
            .AddUser("Geiger", static (ref BufferReader r) => ParseGeiger(ref r))
            .AddUser("HideWeapon", static (ref BufferReader r) => ParseHideWeapon(ref r))
            .AddUser("HudText", static (ref BufferReader r) => ParseHudText(ref r))
            .AddUser("InitHUD", static (ref BufferReader r) => ParseInitHUD(ref r))
            .AddUser("ItemPickup", static (ref BufferReader r) => ParseItemPickup(ref r))
            .AddUser("ScreenFade", static (ref BufferReader r) => ParseScreenFade(ref r))
            .AddUser("ScreenShake", static (ref BufferReader r) => ParseScreenShake(ref r))
            .AddUser("SetFOV", static (ref BufferReader r) => ParseSetFOV(ref r))
            .AddUser("StatusIcon", static (ref BufferReader r) => ParseStatusIcon(ref r))
            .AddUser("TeamInfo", static (ref BufferReader r) => ParseTeamInfo(ref r))
            .AddUser("TextMsg", static (ref BufferReader r) => ParseTextMsg(ref r))
            .AddUser("WeaponList", static (ref BufferReader r) => ParseWeaponList(ref r))
            .AddUser("WeapPickup", static (ref BufferReader r) => ParseWeapPickup(ref r))
            .AddUser("SayText", static (ref BufferReader r) => ParseSayText(ref r))
            .AddUser("Train", static (ref BufferReader r) => ParseTrain(ref r))
            .AddUser("VGUIMenu", static (ref BufferReader r) => ParseVguiMenu(ref r))
            .AddUser("ResetHUD", static (ref BufferReader r) => ParseResetHUD(ref r))
            .AddUser("Concuss", static (ref BufferReader r) => ParseConcuss(ref r))
            .AddUser("HudColor", static (ref BufferReader r) => ParseHudColor(ref r))
            .AddUser("Fog", static (ref BufferReader r) => ParseFog(ref r));
    }

    /// <summary>ReqState: the game DLL's voice manager asks for the client's voice
    /// state. The reply (<c>VModEnable 1</c>) is a session behavior, wired by the
    /// Half-Life profile — parsers only decode.</summary>
    private static ReqStateMessage ParseReqState(ref BufferReader r)
    {
        r.BytePosition = r.Length; // the payload is opaque
        return new ReqStateMessage();
    }

    /// <summary>CurWeapon: byte IsActive, byte WeaponId, byte ClipAmmo</summary>
    private static CurWeaponMessage ParseCurWeapon(ref BufferReader r)
    {
        return new CurWeaponMessage(r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8());
    }

    private static DamageMessage ParseDamage(ref BufferReader r)
    {
        return new DamageMessage(r.ReadUInt8(), r.ReadUInt8(), r.ReadInt32(), r.ReadCoord16(), r.ReadCoord16(), r.ReadCoord16());
    }

    private static DeathMsgMessage ParseDeathMsg(ref BufferReader r)
    {
        return new DeathMsgMessage(r.ReadUInt8(), r.ReadUInt8(), 0, r.ReadString());
    }

    private static HealthMessage ParseHealth(ref BufferReader r)
    {
        return new HealthMessage(r.ReadUInt8());
    }

    private static BatteryMessage ParseBattery(ref BufferReader r)
    {
        return new BatteryMessage(r.ReadInt16());
    }

    private static AmmoXMessage ParseAmmoX(ref BufferReader r)
    {
        return new AmmoXMessage(r.ReadUInt8(), r.ReadUInt8());
    }

    private static AmmoPickupMessage ParseAmmoPickup(ref BufferReader r)
    {
        return new AmmoPickupMessage(r.ReadUInt8(), r.ReadUInt8());
    }

    private static FlashBatMessage ParseFlashBat(ref BufferReader r)
    {
        return new FlashBatMessage(r.ReadUInt8());
    }

    private static FlashlightMessage ParseFlashlight(ref BufferReader r)
    {
        return new FlashlightMessage(r.ReadUInt8(), r.ReadUInt8());
    }

    private static GameModeMessage ParseGameMode(ref BufferReader r)
    {
        return new GameModeMessage(r.ReadUInt8());
    }

    private static GameTitleMessage ParseGameTitle(ref BufferReader r)
    {
        return new GameTitleMessage(r.ReadUInt8());
    }

    private static GeigerMessage ParseGeiger(ref BufferReader r)
    {
        return new GeigerMessage(r.ReadUInt8());
    }

    private static HideWeaponMessage ParseHideWeapon(ref BufferReader r)
    {
        return new HideWeaponMessage(r.ReadUInt8());
    }

    private static HudTextMessage ParseHudText(ref BufferReader r)
    {
        return new HudTextMessage(r.ReadString(), r.ReadUInt8());
    }

    private static InitHudMessage ParseInitHUD(ref BufferReader r)
    {
        return new InitHudMessage();
    }

    private static ItemPickupMessage ParseItemPickup(ref BufferReader r)
    {
        return new ItemPickupMessage(r.ReadString());
    }

    private static ScreenFadeMessage ParseScreenFade(ref BufferReader r)
    {
        return new ScreenFadeMessage(r.ReadInt16(), r.ReadInt16(), r.ReadInt16(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8());
    }

    private static ScreenShakeMessage ParseScreenShake(ref BufferReader r)
    {
        return new ScreenShakeMessage(r.ReadInt16(), r.ReadInt16(), r.ReadInt16());
    }

    private static SetFovMessage ParseSetFOV(ref BufferReader r)
    {
        return new SetFovMessage(r.ReadUInt8());
    }

    private static StatusIconMessage ParseStatusIcon(ref BufferReader r)
    {
        return new StatusIconMessage(r.ReadUInt8(), r.ReadString(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8());
    }

    private static TeamInfoMessage ParseTeamInfo(ref BufferReader r)
    {
        return new TeamInfoMessage(r.ReadUInt8(), r.ReadString());
    }

    private static TextMsgMessage ParseTextMsg(ref BufferReader r)
    {
        return new TextMsgMessage(r.ReadUInt8(), r.ReadString());
    }

    private static WeaponListMessage ParseWeaponList(ref BufferReader r)
    {
        return new WeaponListMessage(r.ReadString(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8());
    }

    private static WeapPickupMessage ParseWeapPickup(ref BufferReader r)
    {
        return new WeapPickupMessage(r.ReadString());
    }

    private static SayTextMessage ParseSayText(ref BufferReader r)
    {
        return new SayTextMessage(r.ReadUInt8(), r.ReadString());
    }

    private static TrainMessage ParseTrain(ref BufferReader r)
    {
        return new TrainMessage(r.ReadUInt8());
    }

    private static VguiMenuMessage ParseVguiMenu(ref BufferReader r)
    {
        return new VguiMenuMessage(r.ReadUInt8(), r.ReadStringLine());
    }

    private static ResetHudMessage ParseResetHUD(ref BufferReader r)
    {
        return new ResetHudMessage();
    }

    private static ConcussMessage ParseConcuss(ref BufferReader r)
    {
        return new ConcussMessage(r.ReadUInt8());
    }

    private static HudColorMessage ParseHudColor(ref BufferReader r)
    {
        return new HudColorMessage(r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8());
    }

    /// <summary>Fog: byte R, G, B, Density (Counter-Strike and Sven Co-op send this;
    /// stock Half-Life servers never do, so the case is inert there).</summary>
    private static FogMessage ParseFog(ref BufferReader r)
    {
        return new FogMessage(r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8());
    }
}
