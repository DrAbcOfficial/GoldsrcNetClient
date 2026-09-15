using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Messages.Parsing;

namespace GoldsrcNetClient.Core.Messages.Users;

/// <summary>
/// Counter-Strike 1.6 / Condition Zero user messages: the shared Half-Life set
/// plus CS-specific names (Money, Radar, ScoreInfo, RoundTime, ShowMenu, ...)
/// and a CS-format DeathMsg carrying the headshot byte.
/// </summary>
public static class CounterStrikeMessages
{
    /// <summary>
    /// Registers the Counter-Strike / Condition Zero user messages on top of the
    /// Half-Life vocabulary. Overrides DeathMsg (headshot byte) and adds the
    /// CS-specific names.
    /// </summary>
    public static void Register(ParserRegistry.Builder builder)
    {
        HalfLifeMessages.Register(builder);
        builder
            .AddUser("DeathMsg", static (ref BufferReader r) => ParseDeathMsg(ref r))
            .AddUser("Money", static (ref BufferReader r) => ParseMoney(ref r))
            .AddUser("Radar", static (ref BufferReader r) => ParseRadar(ref r))
            .AddUser("ScoreInfo", static (ref BufferReader r) => ParseScoreInfo(ref r))
            .AddUser("ScoreAttrib", static (ref BufferReader r) => ParseScoreAttrib(ref r))
            .AddUser("RoundTime", static (ref BufferReader r) => ParseRoundTime(ref r))
            .AddUser("BombDrop", static (ref BufferReader r) => ParseBombDrop(ref r))
            .AddUser("BombPickup", static (ref BufferReader r) => ParseBombPickup(ref r))
            .AddUser("HostageK", static (ref BufferReader r) => ParseHostageK(ref r))
            .AddUser("HostagePos", static (ref BufferReader r) => ParseHostagePos(ref r))
            .AddUser("BarTime", static (ref BufferReader r) => ParseBarTime(ref r))
            .AddUser("BarTime2", static (ref BufferReader r) => ParseBarTime2(ref r))
            .AddUser("BlinkAcct", static (ref BufferReader r) => ParseBlinkAcct(ref r))
            .AddUser("ArmorType", static (ref BufferReader r) => ParseArmorType(ref r))
            .AddUser("Crosshair", static (ref BufferReader r) => ParseCrosshair(ref r))
            .AddUser("NVGToggle", static (ref BufferReader r) => ParseNvgToggle(ref r))
            .AddUser("ReceiveW", static (ref BufferReader r) => ParseReceiveW(ref r))
            .AddUser("ReloadSound", static (ref BufferReader r) => ParseReloadSound(ref r))
            .AddUser("SendAudio", static (ref BufferReader r) => ParseSendAudio(ref r))
            .AddUser("ShadowIdx", static (ref BufferReader r) => ParseShadowIdx(ref r))
            .AddUser("ShowMenu", static (ref BufferReader r) => ParseShowMenu(ref r))
            .AddUser("ShowTimer", static (ref BufferReader r) => ParseShowTimer(ref r))
            .AddUser("Spectator", static (ref BufferReader r) => ParseSpectator(ref r))
            .AddUser("TeamScore", static (ref BufferReader r) => ParseTeamScore(ref r))
            .AddUser("VoteMenu", static (ref BufferReader r) => ParseVoteMenu(ref r))
            .AddUser("AllowSpec", static (ref BufferReader r) => ParseAllowSpec(ref r))
            .AddUser("ForceCam", static (ref BufferReader r) => ParseForceCam(ref r))
            .AddUser("HLTV", static (ref BufferReader r) => ParseHltv(ref r))
            .AddUser("BotVoice", static (ref BufferReader r) => ParseBotVoice(ref r))
            .AddUser("BuyClose", static (ref BufferReader r) => ParseBuyClose(ref r))
            .AddUser("ADStop", static (ref BufferReader r) => ParseAdStop(ref r))
            .AddUser("ItemStatus", static (ref BufferReader r) => ParseItemStatus(ref r))
            .AddUser("HudTextArgs", static (ref BufferReader r) => ParseHudTextArgs(ref r))
            .AddUser("HudTextPro", static (ref BufferReader r) => ParseHudTextPro(ref r));
    }



    /// <summary>DeathMsg (CS format): byte KillerId, byte VictimId, byte IsHeadshot, string WeaponName</summary>
    private static DeathMsgMessage ParseDeathMsg(ref BufferReader r)
    {
        return new DeathMsgMessage(r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8(), r.ReadString());
    }

    /// <summary>Money: short Amount, byte FlashAmount</summary>
    private static MoneyMessage ParseMoney(ref BufferReader r)
    {
        return new MoneyMessage(r.ReadInt16(), r.ReadUInt8());
    }

    /// <summary>Radar: byte PlayerIndex, coord x, y, z</summary>
    private static RadarMessage ParseRadar(ref BufferReader r)
    {
        return new RadarMessage(r.ReadUInt8(), r.ReadCoord16(), r.ReadCoord16(), r.ReadCoord16());
    }

    /// <summary>ScoreInfo: byte PlayerId, short Score, short Deaths, byte IsAlive, byte TeamId</summary>
    private static ScoreInfoMessage ParseScoreInfo(ref BufferReader r)
    {
        return new ScoreInfoMessage(r.ReadUInt8(), r.ReadInt16(), r.ReadInt16(), r.ReadUInt8(), r.ReadUInt8());
    }

    /// <summary>ScoreAttrib: byte PlayerId, byte Flags</summary>
    private static ScoreAttribMessage ParseScoreAttrib(ref BufferReader r)
    {
        return new ScoreAttribMessage(r.ReadUInt8(), r.ReadUInt8());
    }

    /// <summary>RoundTime: short Seconds</summary>
    private static RoundTimeMessage ParseRoundTime(ref BufferReader r)
    {
        return new RoundTimeMessage(r.ReadInt16());
    }

    /// <summary>BombDrop: coord x, y, z, byte Planted</summary>
    private static BombDropMessage ParseBombDrop(ref BufferReader r)
    {
        return new BombDropMessage(r.ReadCoord16(), r.ReadCoord16(), r.ReadCoord16(), r.ReadUInt8());
    }

    /// <summary>BombPickup: no args</summary>
    private static BombPickupMessage ParseBombPickup(ref BufferReader r)
    {
        return new BombPickupMessage();
    }

    /// <summary>HostageK: byte HostageId</summary>
    private static HostageKMessage ParseHostageK(ref BufferReader r)
    {
        return new HostageKMessage(r.ReadUInt8());
    }

    /// <summary>HostagePos: byte Flag, byte HostageId, coord x, y, z</summary>
    private static HostagePosMessage ParseHostagePos(ref BufferReader r)
    {
        return new HostagePosMessage(r.ReadUInt8(), r.ReadUInt8(), r.ReadCoord16(), r.ReadCoord16(), r.ReadCoord16());
    }

    /// <summary>BarTime: short Duration</summary>
    private static BarTimeMessage ParseBarTime(ref BufferReader r)
    {
        return new BarTimeMessage(r.ReadInt16());
    }

    /// <summary>BarTime2: short Duration, short StartPercent</summary>
    private static BarTime2Message ParseBarTime2(ref BufferReader r)
    {
        return new BarTime2Message(r.ReadInt16(), r.ReadInt16());
    }

    /// <summary>BlinkAcct: byte BlinkAmount</summary>
    private static BlinkAcctMessage ParseBlinkAcct(ref BufferReader r)
    {
        return new BlinkAcctMessage(r.ReadUInt8());
    }

    /// <summary>ArmorType: byte HasHelmet</summary>
    private static ArmorTypeMessage ParseArmorType(ref BufferReader r)
    {
        return new ArmorTypeMessage(r.ReadUInt8());
    }

    /// <summary>Crosshair: byte Show</summary>
    private static CrosshairMessage ParseCrosshair(ref BufferReader r)
    {
        return new CrosshairMessage(r.ReadUInt8());
    }

    /// <summary>NVGToggle: byte Mode</summary>
    private static NvgToggleMessage ParseNvgToggle(ref BufferReader r)
    {
        return new NvgToggleMessage(r.ReadUInt8());
    }

    /// <summary>ReceiveW: byte ItemId</summary>
    private static ReceiveWMessage ParseReceiveW(ref BufferReader r)
    {
        return new ReceiveWMessage(r.ReadUInt8());
    }

    /// <summary>ReloadSound: byte PlayerIndex, byte WeaponId</summary>
    private static ReloadSoundMessage ParseReloadSound(ref BufferReader r)
    {
        return new ReloadSoundMessage(r.ReadUInt8(), r.ReadUInt8());
    }

    /// <summary>SendAudio: byte Channel, string SoundName</summary>
    private static SendAudioMessage ParseSendAudio(ref BufferReader r)
    {
        return new SendAudioMessage(r.ReadUInt8(), r.ReadString());
    }

    /// <summary>ShadowIdx: byte PlayerId, byte ShadowId</summary>
    private static ShadowIdxMessage ParseShadowIdx(ref BufferReader r)
    {
        return new ShadowIdxMessage(r.ReadUInt8(), r.ReadUInt8());
    }

    /// <summary>ShowMenu: short ValidSlots, byte DisplayTime, byte NeedMore, string Text</summary>
    private static ShowMenuMessage ParseShowMenu(ref BufferReader r)
    {
        return new ShowMenuMessage(r.ReadInt16(), r.ReadUInt8(), r.ReadUInt8(), r.ReadString());
    }

    /// <summary>ShowTimer: byte Show</summary>
    private static ShowTimerMessage ParseShowTimer(ref BufferReader r)
    {
        return new ShowTimerMessage(r.ReadUInt8());
    }

    /// <summary>Spectator: byte PlayerId, byte Mode</summary>
    private static SpectatorMessage ParseSpectator(ref BufferReader r)
    {
        return new SpectatorMessage(r.ReadUInt8(), r.ReadUInt8());
    }

    /// <summary>TeamScore: string TeamName, short Score</summary>
    private static TeamScoreMessage ParseTeamScore(ref BufferReader r)
    {
        return new TeamScoreMessage(r.ReadString(), r.ReadInt16());
    }

    /// <summary>VoteMenu: short ValidSlots, byte DisplayTime, string Text</summary>
    private static VoteMenuMessage ParseVoteMenu(ref BufferReader r)
    {
        return new VoteMenuMessage(r.ReadInt16(), r.ReadUInt8(), r.ReadString());
    }

    /// <summary>AllowSpec: byte Allowed</summary>
    private static AllowSpecMessage ParseAllowSpec(ref BufferReader r)
    {
        return new AllowSpecMessage(r.ReadUInt8());
    }

    /// <summary>ForceCam: byte ForcecamValue, byte ForcechasecamValue, byte Unknown</summary>
    private static ForceCamMessage ParseForceCam(ref BufferReader r)
    {
        return new ForceCamMessage(r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8());
    }

    /// <summary>HLTV: byte ClientId, byte Flags</summary>
    private static HltvMessage ParseHltv(ref BufferReader r)
    {
        return new HltvMessage(r.ReadUInt8(), r.ReadUInt8());
    }

    /// <summary>BotVoice: byte Status, byte PlayerIndex</summary>
    private static BotVoiceMessage ParseBotVoice(ref BufferReader r)
    {
        return new BotVoiceMessage(r.ReadUInt8(), r.ReadUInt8());
    }

    /// <summary>BuyClose: no args</summary>
    private static BuyCloseMessage ParseBuyClose(ref BufferReader r)
    {
        return new BuyCloseMessage();
    }

    /// <summary>ADStop: no args</summary>
    private static AdStopMessage ParseAdStop(ref BufferReader r)
    {
        return new AdStopMessage();
    }

    /// <summary>ItemStatus: int ItemBits</summary>
    private static ItemStatusMessage ParseItemStatus(ref BufferReader r)
    {
        return new ItemStatusMessage(r.ReadInt32());
    }

    /// <summary>HudTextArgs: string TextCode, byte Style, then repeated NumberOfSubMessages and sub-message strings</summary>
    private static HudTextArgsMessage ParseHudTextArgs(ref BufferReader r)
    {
        string textCode = r.ReadString();
        byte style = r.ReadUInt8();
        byte subCount = r.ReadUInt8();
        var args = new string[subCount];
        for (int i = 0; i < subCount; i++)
            args[i] = r.ReadString();
        return new HudTextArgsMessage(textCode, style, args);
    }

    /// <summary>HudTextPro: string TextCode, byte Style (CS big-style HUD text)</summary>
    private static HudTextProMessage ParseHudTextPro(ref BufferReader r)
    {
        return new HudTextProMessage(r.ReadString(), r.ReadUInt8());
    }
}
