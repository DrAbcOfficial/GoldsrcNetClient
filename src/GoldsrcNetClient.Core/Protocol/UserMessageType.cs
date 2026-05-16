namespace GoldsrcNetClient.Core.Protocol;

/// <summary>
/// Well-known user message type names registered by game servers during
/// <see cref="ServerMessageType.NewUserMsg"/> registration.
/// User messages are assigned dynamic byte indices at or above
/// <see cref="ServerMessageType.UserMessageStart"/> (0x40).
/// Use these constants with <see cref="Game.UserMessageRegistry"/> to resolve
/// message names to their runtime byte indices.
/// </summary>
public static class UserMessageType
{
    // --- Half-Life Deathmatch ---
    public const string CurWeapon = "CurWeapon";
    public const string Damage = "Damage";
    public const string DeathMsg = "DeathMsg";
    public const string Health = "Health";
    public const string Battery = "Battery";
    public const string AmmoX = "AmmoX";
    public const string AmmoPickup = "AmmoPickup";
    public const string FlashBat = "FlashBat";
    public const string Flashlight = "Flashlight";
    public const string GameMode = "GameMode";
    public const string GameTitle = "GameTitle";
    public const string Geiger = "Geiger";
    public const string HideWeapon = "HideWeapon";
    public const string HudText = "HudText";
    public const string InitHUD = "InitHUD";
    public const string ItemPickup = "ItemPickup";
    public const string ScreenFade = "ScreenFade";
    public const string ScreenShake = "ScreenShake";
    public const string SetFOV = "SetFOV";
    public const string StatusIcon = "StatusIcon";
    public const string TeamInfo = "TeamInfo";
    public const string TextMsg = "TextMsg";
    public const string WeaponList = "WeaponList";
    public const string WeapPickup = "WeapPickup";
    public const string SayText = "SayText";
    public const string Train = "Train";
    public const string VGUIMenu = "VGUIMenu";
    public const string ResetHUD = "ResetHUD";
    public const string Concuss = "Concuss";
    public const string HudColor = "HudColor";

    // --- Counter-Strike ---
    public const string Money = "Money";
    public const string Radar = "Radar";
    public const string ScoreInfo = "ScoreInfo";
    public const string ScoreAttrib = "ScoreAttrib";
    public const string RoundTime = "RoundTime";
    public const string BombDrop = "BombDrop";
    public const string BombPickup = "BombPickup";
    public const string HostageK = "HostageK";
    public const string HostagePos = "HostagePos";
    public const string BarTime = "BarTime";
    public const string BarTime2 = "BarTime2";
    public const string BlinkAcct = "BlinkAcct";
    public const string ArmorType = "ArmorType";
    public const string Crosshair = "Crosshair";
    public const string Fog = "Fog";
    public const string NVGToggle = "NVGToggle";
    public const string ReceiveW = "ReceiveW";
    public const string ReloadSound = "ReloadSound";
    public const string SendAudio = "SendAudio";
    public const string ShadowIdx = "ShadowIdx";
    public const string ShowMenu = "ShowMenu";
    public const string ShowTimer = "ShowTimer";
    public const string Spectator = "Spectator";
    public const string TeamScore = "TeamScore";
    public const string VoteMenu = "VoteMenu";
    public const string AllowSpec = "AllowSpec";
    public const string ForceCam = "ForceCam";
    public const string Hltv = "HLTV";
    public const string BotVoice = "BotVoice";
    public const string BuyClose = "BuyClose";
    public const string ADStop = "ADStop";
    public const string ItemStatus = "ItemStatus";
    public const string HudTextArgs = "HudTextArgs";
    public const string HudTextPro = "HudTextPro";

    // --- Sven Co-op ---
    public const string Camera = "Camera";
    public const string CameraMouse = "CameraMouse";
    public const string CbElec = "CbElec";
    public const string CreateBlood = "CreateBlood";
    public const string GargSplash = "GargSplash";
    public const string Gib = "Gib";
    public const string SporeTrail = "SporeTrail";
    public const string ToxicCloud = "ToxicCloud";
}
