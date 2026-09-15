using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Network;

namespace GoldsrcNetClient.Core.Game;

/// <summary>
/// Server message handler for Counter-Strike 1.6 / Condition Zero.
/// Extends <see cref="HalfLifeMessageHandler"/> with CS-specific user messages.
/// </summary>
/// <remarks>
/// <para>Adds CS-specific messages: DeathMsg (with IsHeadshot), Money, Radar, ScoreInfo,
/// ScoreAttrib, RoundTime, BombDrop, BombPickup, HostageK, HostagePos, BarTime, BarTime2,
/// BlinkAcct, ArmorType, Crosshair, Fog, NVGToggle, ReceiveW, ReloadSound, SendAudio,
/// ShadowIdx, ShowMenu, ShowTimer, Spectator, TeamScore, VoteMenu, AllowSpec, ForceCam,
/// HLTV, BotVoice, BuyClose, ADStop, ItemStatus, HudTextArgs, HudTextPro.</para>
///
/// <para>For DeathMsg, the IsHeadshot field is populated (0 or 1).</para>
/// </remarks>
public class CounterStrikeMessageHandler : HalfLifeMessageHandler
{
    #region CS-specific Events

    /// <summary>Raised when money amount changes (Counter-Strike).</summary>
    public event Action<MoneyEvent>? Money;
    /// <summary>Raised when a player's radar position updates (Counter-Strike).</summary>
    public event Action<RadarEvent>? Radar;
    /// <summary>Raised when scoreboard player info updates (Counter-Strike).</summary>
    public event Action<ScoreInfoEvent>? ScoreInfo;
    /// <summary>Raised when scoreboard player attributes update (Counter-Strike).</summary>
    public event Action<ScoreAttribEvent>? ScoreAttrib;
    /// <summary>Raised when round time remaining updates (Counter-Strike).</summary>
    public event Action<RoundTimeEvent>? RoundTime;
    /// <summary>Raised when the bomb is dropped or planted (Counter-Strike).</summary>
    public event Action<BombDropEvent>? BombDrop;
    /// <summary>Raised when the bomb is picked up (Counter-Strike).</summary>
    public event Action<BombPickupEvent>? BombPickup;
    /// <summary>Raised when a hostage is killed (Counter-Strike).</summary>
    public event Action<HostageKEvent>? HostageK;
    /// <summary>Raised when a hostage position updates (Counter-Strike).</summary>
    public event Action<HostagePosEvent>? HostagePos;
    /// <summary>Raised when a progress bar is shown (Counter-Strike).</summary>
    public event Action<BarTimeEvent>? BarTime;
    /// <summary>Raised when a progress bar with start percent is shown (Counter-Strike).</summary>
    public event Action<BarTime2Event>? BarTime2;
    /// <summary>Raised when money display flashes (Counter-Strike).</summary>
    public event Action<BlinkAcctEvent>? BlinkAcct;
    /// <summary>Raised when armor type (helmet) changes (Counter-Strike).</summary>
    public event Action<ArmorTypeEvent>? ArmorType;
    /// <summary>Raised when spectator crosshair is toggled (Counter-Strike).</summary>
    public event Action<CrosshairEvent>? Crosshair;
    /// <summary>Raised when night vision is toggled (Counter-Strike).</summary>
    public event Action<NvgToggleEvent>? NvgToggle;
    /// <summary>Raised when a weapon/item is received (Counter-Strike).</summary>
    public event Action<ReceiveWEvent>? ReceiveW;
    /// <summary>Raised when a reload sound plays (Counter-Strike).</summary>
    public event Action<ReloadSoundEvent>? ReloadSound;
    /// <summary>Raised when audio is sent to a client (Counter-Strike).</summary>
    public event Action<SendAudioEvent>? SendAudio;
    /// <summary>Raised when a player's shadow index changes (Counter-Strike).</summary>
    public event Action<ShadowIdxEvent>? ShadowIdx;
    /// <summary>Raised when a menu is shown (Counter-Strike).</summary>
    public event Action<ShowMenuEvent>? ShowMenu;
    /// <summary>Raised when the round timer is shown/hidden (Counter-Strike).</summary>
    public event Action<ShowTimerEvent>? ShowTimer;
    /// <summary>Raised when spectator mode changes (Counter-Strike).</summary>
    public event Action<SpectatorEvent>? Spectator;
    /// <summary>Raised when a team's score updates (Counter-Strike).</summary>
    public event Action<TeamScoreEvent>? TeamScore;
    /// <summary>Raised when a vote menu is shown (Counter-Strike).</summary>
    public event Action<VoteMenuEvent>? VoteMenu;
    /// <summary>Raised when spectator permission changes (Counter-Strike).</summary>
    public event Action<AllowSpecEvent>? AllowSpec;
    /// <summary>Raised when force camera settings change (Counter-Strike).</summary>
    public event Action<ForceCamEvent>? ForceCam;
    /// <summary>Raised when HLTV status updates.</summary>
    public event Action<HltvEvent>? Hltv;
    /// <summary>Raised when a bot voice icon toggles (Condition Zero).</summary>
    public event Action<BotVoiceEvent>? BotVoice;
    /// <summary>Raised when the buy menu is force-closed (Counter-Strike).</summary>
    public event Action<BuyCloseEvent>? BuyClose;
    /// <summary>Raised for ADStop messages (Counter-Strike).</summary>
    public event Action<AdStopEvent>? AdStop;
    /// <summary>Raised when carried item status updates (Counter-Strike).</summary>
    public event Action<ItemStatusEvent>? ItemStatus;
    /// <summary>Raised when HUD text with arguments is received (Counter-Strike).</summary>
    public event Action<HudTextArgsEvent>? HudTextArgs;
    /// <summary>Raised when HUD text pro is received (Counter-Strike).</summary>
    public event Action<HudTextProEvent>? HudTextPro;

    #endregion

    /// <inheritdoc />
    protected override bool DispatchUserMessage(GoldsrcConnection connection, byte index, string name, ref BufferReader reader)
    {
        switch (name)
        {
            case "DeathMsg": ParseDeathMsg(ref reader); return true;
            case "Money": ParseMoney(ref reader); return true;
            case "Radar": ParseRadar(ref reader); return true;
            case "ScoreInfo": ParseScoreInfo(ref reader); return true;
            case "ScoreAttrib": ParseScoreAttrib(ref reader); return true;
            case "RoundTime": ParseRoundTime(ref reader); return true;
            case "BombDrop": ParseBombDrop(ref reader); return true;
            case "BombPickup": ParseBombPickup(ref reader); return true;
            case "HostageK": ParseHostageK(ref reader); return true;
            case "HostagePos": ParseHostagePos(ref reader); return true;
            case "BarTime": ParseBarTime(ref reader); return true;
            case "BarTime2": ParseBarTime2(ref reader); return true;
            case "BlinkAcct": ParseBlinkAcct(ref reader); return true;
            case "ArmorType": ParseArmorType(ref reader); return true;
            case "Crosshair": ParseCrosshair(ref reader); return true;
            case "NVGToggle": ParseNvgToggle(ref reader); return true;
            case "ReceiveW": ParseReceiveW(ref reader); return true;
            case "ReloadSound": ParseReloadSound(ref reader); return true;
            case "SendAudio": ParseSendAudio(ref reader); return true;
            case "ShadowIdx": ParseShadowIdx(ref reader); return true;
            case "ShowMenu": ParseShowMenu(ref reader); return true;
            case "ShowTimer": ParseShowTimer(ref reader); return true;
            case "Spectator": ParseSpectator(ref reader); return true;
            case "TeamScore": ParseTeamScore(ref reader); return true;
            case "VoteMenu": ParseVoteMenu(ref reader); return true;
            case "AllowSpec": ParseAllowSpec(ref reader); return true;
            case "ForceCam": ParseForceCam(ref reader); return true;
            case "HLTV": ParseHltv(ref reader); return true;
            case "BotVoice": ParseBotVoice(ref reader); return true;
            case "BuyClose": ParseBuyClose(ref reader); return true;
            case "ADStop": ParseAdStop(ref reader); return true;
            case "ItemStatus": ParseItemStatus(ref reader); return true;
            case "HudTextArgs": ParseHudTextArgs(ref reader); return true;
            case "HudTextPro": ParseHudTextPro(ref reader); return true;
            default: return base.DispatchUserMessage(connection, index, name, ref reader);
        }
    }

    /// <summary>DeathMsg (CS format): byte KillerId, byte VictimId, byte IsHeadshot, string WeaponName</summary>
    protected override void ParseDeathMsg(ref BufferReader r)
    {
        var ev = new DeathMsgEvent(r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8(), r.ReadString());
        OnDeathMsg(ev);
    }

    /// <summary>Money: short Amount, byte FlashAmount</summary>
    protected virtual void ParseMoney(ref BufferReader r)
    {
        var ev = new MoneyEvent(r.ReadInt16(), r.ReadUInt8());
        Money?.Invoke(ev);
    }

    /// <summary>Radar: byte PlayerIndex, coord x, y, z</summary>
    protected virtual void ParseRadar(ref BufferReader r)
    {
        var ev = new RadarEvent(r.ReadUInt8(), r.ReadCoord16(), r.ReadCoord16(), r.ReadCoord16());
        Radar?.Invoke(ev);
    }

    /// <summary>ScoreInfo: byte PlayerId, short Score, short Deaths, byte IsAlive, byte TeamId</summary>
    protected virtual void ParseScoreInfo(ref BufferReader r)
    {
        var ev = new ScoreInfoEvent(r.ReadUInt8(), r.ReadInt16(), r.ReadInt16(), r.ReadUInt8(), r.ReadUInt8());
        ScoreInfo?.Invoke(ev);
    }

    /// <summary>ScoreAttrib: byte PlayerId, byte Flags</summary>
    protected virtual void ParseScoreAttrib(ref BufferReader r)
    {
        var ev = new ScoreAttribEvent(r.ReadUInt8(), r.ReadUInt8());
        ScoreAttrib?.Invoke(ev);
    }

    /// <summary>RoundTime: short Seconds</summary>
    protected virtual void ParseRoundTime(ref BufferReader r)
    {
        var ev = new RoundTimeEvent(r.ReadInt16());
        RoundTime?.Invoke(ev);
    }

    /// <summary>BombDrop: coord x, y, z, byte Planted</summary>
    protected virtual void ParseBombDrop(ref BufferReader r)
    {
        var ev = new BombDropEvent(r.ReadCoord16(), r.ReadCoord16(), r.ReadCoord16(), r.ReadUInt8());
        BombDrop?.Invoke(ev);
    }

    /// <summary>BombPickup: no args</summary>
    protected virtual void ParseBombPickup(ref BufferReader r)
    {
        var ev = new BombPickupEvent();
        BombPickup?.Invoke(ev);
    }

    /// <summary>HostageK: byte HostageId</summary>
    protected virtual void ParseHostageK(ref BufferReader r)
    {
        var ev = new HostageKEvent(r.ReadUInt8());
        HostageK?.Invoke(ev);
    }

    /// <summary>HostagePos: byte Flag, byte HostageId, coord x, y, z</summary>
    protected virtual void ParseHostagePos(ref BufferReader r)
    {
        var ev = new HostagePosEvent(r.ReadUInt8(), r.ReadUInt8(), r.ReadCoord16(), r.ReadCoord16(), r.ReadCoord16());
        HostagePos?.Invoke(ev);
    }

    /// <summary>BarTime: short Duration</summary>
    protected virtual void ParseBarTime(ref BufferReader r)
    {
        var ev = new BarTimeEvent(r.ReadInt16());
        BarTime?.Invoke(ev);
    }

    /// <summary>BarTime2: short Duration, short StartPercent</summary>
    protected virtual void ParseBarTime2(ref BufferReader r)
    {
        var ev = new BarTime2Event(r.ReadInt16(), r.ReadInt16());
        BarTime2?.Invoke(ev);
    }

    /// <summary>BlinkAcct: byte BlinkAmount</summary>
    protected virtual void ParseBlinkAcct(ref BufferReader r)
    {
        var ev = new BlinkAcctEvent(r.ReadUInt8());
        BlinkAcct?.Invoke(ev);
    }

    /// <summary>ArmorType: byte HasHelmet</summary>
    protected virtual void ParseArmorType(ref BufferReader r)
    {
        var ev = new ArmorTypeEvent(r.ReadUInt8());
        ArmorType?.Invoke(ev);
    }

    /// <summary>Crosshair: byte Show</summary>
    protected virtual void ParseCrosshair(ref BufferReader r)
    {
        var ev = new CrosshairEvent(r.ReadUInt8());
        Crosshair?.Invoke(ev);
    }

    /// <summary>NVGToggle: byte Mode</summary>
    protected virtual void ParseNvgToggle(ref BufferReader r)
    {
        var ev = new NvgToggleEvent(r.ReadUInt8());
        NvgToggle?.Invoke(ev);
    }

    /// <summary>ReceiveW: byte ItemId</summary>
    protected virtual void ParseReceiveW(ref BufferReader r)
    {
        var ev = new ReceiveWEvent(r.ReadUInt8());
        ReceiveW?.Invoke(ev);
    }

    /// <summary>ReloadSound: byte PlayerIndex, byte WeaponId</summary>
    protected virtual void ParseReloadSound(ref BufferReader r)
    {
        var ev = new ReloadSoundEvent(r.ReadUInt8(), r.ReadUInt8());
        ReloadSound?.Invoke(ev);
    }

    /// <summary>SendAudio: byte Channel, string SoundName</summary>
    protected virtual void ParseSendAudio(ref BufferReader r)
    {
        var ev = new SendAudioEvent(r.ReadUInt8(), r.ReadString());
        SendAudio?.Invoke(ev);
    }

    /// <summary>ShadowIdx: byte PlayerId, byte ShadowId</summary>
    protected virtual void ParseShadowIdx(ref BufferReader r)
    {
        var ev = new ShadowIdxEvent(r.ReadUInt8(), r.ReadUInt8());
        ShadowIdx?.Invoke(ev);
    }

    /// <summary>ShowMenu: short ValidSlots, byte DisplayTime, byte NeedMore, string Text</summary>
    protected virtual void ParseShowMenu(ref BufferReader r)
    {
        var ev = new ShowMenuEvent(r.ReadInt16(), r.ReadUInt8(), r.ReadUInt8(), r.ReadString());
        ShowMenu?.Invoke(ev);
    }

    /// <summary>ShowTimer: byte Show</summary>
    protected virtual void ParseShowTimer(ref BufferReader r)
    {
        var ev = new ShowTimerEvent(r.ReadUInt8());
        ShowTimer?.Invoke(ev);
    }

    /// <summary>Spectator: byte PlayerId, byte Mode</summary>
    protected virtual void ParseSpectator(ref BufferReader r)
    {
        var ev = new SpectatorEvent(r.ReadUInt8(), r.ReadUInt8());
        Spectator?.Invoke(ev);
    }

    /// <summary>TeamScore: string TeamName, short Score</summary>
    protected virtual void ParseTeamScore(ref BufferReader r)
    {
        var ev = new TeamScoreEvent(r.ReadString(), r.ReadInt16());
        TeamScore?.Invoke(ev);
    }

    /// <summary>VoteMenu: short ValidSlots, byte DisplayTime, string Text</summary>
    protected virtual void ParseVoteMenu(ref BufferReader r)
    {
        var ev = new VoteMenuEvent(r.ReadInt16(), r.ReadUInt8(), r.ReadString());
        VoteMenu?.Invoke(ev);
    }

    /// <summary>AllowSpec: byte Allowed</summary>
    protected virtual void ParseAllowSpec(ref BufferReader r)
    {
        var ev = new AllowSpecEvent(r.ReadUInt8());
        AllowSpec?.Invoke(ev);
    }

    /// <summary>ForceCam: byte ForcecamValue, byte ForcechasecamValue, byte Unknown</summary>
    protected virtual void ParseForceCam(ref BufferReader r)
    {
        var ev = new ForceCamEvent(r.ReadUInt8(), r.ReadUInt8(), r.ReadUInt8());
        ForceCam?.Invoke(ev);
    }

    /// <summary>HLTV: byte ClientId, byte Flags</summary>
    protected virtual void ParseHltv(ref BufferReader r)
    {
        var ev = new HltvEvent(r.ReadUInt8(), r.ReadUInt8());
        Hltv?.Invoke(ev);
    }

    /// <summary>BotVoice: byte Status, byte PlayerIndex</summary>
    protected virtual void ParseBotVoice(ref BufferReader r)
    {
        var ev = new BotVoiceEvent(r.ReadUInt8(), r.ReadUInt8());
        BotVoice?.Invoke(ev);
    }

    /// <summary>BuyClose: no args</summary>
    protected virtual void ParseBuyClose(ref BufferReader r)
    {
        var ev = new BuyCloseEvent();
        BuyClose?.Invoke(ev);
    }

    /// <summary>ADStop: no args</summary>
    protected virtual void ParseAdStop(ref BufferReader r)
    {
        var ev = new AdStopEvent();
        AdStop?.Invoke(ev);
    }

    /// <summary>ItemStatus: int ItemBits</summary>
    protected virtual void ParseItemStatus(ref BufferReader r)
    {
        var ev = new ItemStatusEvent(r.ReadInt32());
        ItemStatus?.Invoke(ev);
    }

    /// <summary>HudTextArgs: string TextCode, byte Style, then repeated NumberOfSubMessages and sub-message strings</summary>
    protected virtual void ParseHudTextArgs(ref BufferReader r)
    {
        string textCode = r.ReadString();
        byte style = r.ReadUInt8();
        byte subCount = r.ReadUInt8();
        var args = new string[subCount];
        for (int i = 0; i < subCount; i++)
            args[i] = r.ReadString();
        var ev = new HudTextArgsEvent(textCode, style, args);
        HudTextArgs?.Invoke(ev);
    }

    /// <summary>HudTextPro: string TextCode, byte Style (CS big-style HUD text)</summary>
    protected virtual void ParseHudTextPro(ref BufferReader r)
    {
        var ev = new HudTextProEvent(r.ReadString(), r.ReadUInt8());
        HudTextPro?.Invoke(ev);
    }
}
