using GoldsrcNetClient.Core.Messages;
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
    /// <summary>Raised when fog settings change (Counter-Strike / Sven Co-op).</summary>
    public event Action<FogEvent>? Fog;
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
    protected override bool DispatchUserMessage(GoldsrcConnection connection, byte index, string name, MessageReader reader)
    {
        switch (name)
        {
            case "DeathMsg":    ParseDeathMsg(reader); return true;
            case "Money":       ParseMoney(reader); return true;
            case "Radar":       ParseRadar(reader); return true;
            case "ScoreInfo":   ParseScoreInfo(reader); return true;
            case "ScoreAttrib": ParseScoreAttrib(reader); return true;
            case "RoundTime":   ParseRoundTime(reader); return true;
            case "BombDrop":    ParseBombDrop(reader); return true;
            case "BombPickup":  ParseBombPickup(reader); return true;
            case "HostageK":    ParseHostageK(reader); return true;
            case "HostagePos":  ParseHostagePos(reader); return true;
            case "BarTime":     ParseBarTime(reader); return true;
            case "BarTime2":    ParseBarTime2(reader); return true;
            case "BlinkAcct":   ParseBlinkAcct(reader); return true;
            case "ArmorType":   ParseArmorType(reader); return true;
            case "Crosshair":   ParseCrosshair(reader); return true;
            case "Fog":         ParseFog(reader); return true;
            case "NVGToggle":   ParseNvgToggle(reader); return true;
            case "ReceiveW":    ParseReceiveW(reader); return true;
            case "ReloadSound": ParseReloadSound(reader); return true;
            case "SendAudio":   ParseSendAudio(reader); return true;
            case "ShadowIdx":   ParseShadowIdx(reader); return true;
            case "ShowMenu":    ParseShowMenu(reader); return true;
            case "ShowTimer":   ParseShowTimer(reader); return true;
            case "Spectator":   ParseSpectator(reader); return true;
            case "TeamScore":   ParseTeamScore(reader); return true;
            case "VoteMenu":    ParseVoteMenu(reader); return true;
            case "AllowSpec":   ParseAllowSpec(reader); return true;
            case "ForceCam":    ParseForceCam(reader); return true;
            case "HLTV":        ParseHltv(reader); return true;
            case "BotVoice":    ParseBotVoice(reader); return true;
            case "BuyClose":    ParseBuyClose(reader); return true;
            case "ADStop":      ParseAdStop(reader); return true;
            case "ItemStatus":  ParseItemStatus(reader); return true;
            case "HudTextArgs": ParseHudTextArgs(reader); return true;
            case "HudTextPro":  ParseHudTextPro(reader); return true;
            default: return base.DispatchUserMessage(connection, index, name, reader);
        }
    }

    /// <summary>DeathMsg (CS format): byte KillerId, byte VictimId, byte IsHeadshot, string WeaponName</summary>
    protected override void ParseDeathMsg(MessageReader r)
    {
        var ev = new DeathMsgEvent(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadString());
        OnDeathMsg(ev);
    }

    /// <summary>Money: short Amount, byte FlashAmount</summary>
    protected virtual void ParseMoney(MessageReader r)
    {
        var ev = new MoneyEvent(ReadShort(r), r.ReadByte());
        Money?.Invoke(ev);
    }

    /// <summary>Radar: byte PlayerIndex, coord x, y, z</summary>
    protected virtual void ParseRadar(MessageReader r)
    {
        var ev = new RadarEvent(r.ReadByte(), ReadCoord(r), ReadCoord(r), ReadCoord(r));
        Radar?.Invoke(ev);
    }

    /// <summary>ScoreInfo: byte PlayerId, short Score, short Deaths, byte IsAlive, byte TeamId</summary>
    protected virtual void ParseScoreInfo(MessageReader r)
    {
        var ev = new ScoreInfoEvent(r.ReadByte(), ReadShort(r), ReadShort(r), r.ReadByte(), r.ReadByte());
        ScoreInfo?.Invoke(ev);
    }

    /// <summary>ScoreAttrib: byte PlayerId, byte Flags</summary>
    protected virtual void ParseScoreAttrib(MessageReader r)
    {
        var ev = new ScoreAttribEvent(r.ReadByte(), r.ReadByte());
        ScoreAttrib?.Invoke(ev);
    }

    /// <summary>RoundTime: short Seconds</summary>
    protected virtual void ParseRoundTime(MessageReader r)
    {
        var ev = new RoundTimeEvent(ReadShort(r));
        RoundTime?.Invoke(ev);
    }

    /// <summary>BombDrop: coord x, y, z, byte Planted</summary>
    protected virtual void ParseBombDrop(MessageReader r)
    {
        var ev = new BombDropEvent(ReadCoord(r), ReadCoord(r), ReadCoord(r), r.ReadByte());
        BombDrop?.Invoke(ev);
    }

    /// <summary>BombPickup: no args</summary>
    protected virtual void ParseBombPickup(MessageReader r)
    {
        var ev = new BombPickupEvent();
        BombPickup?.Invoke(ev);
    }

    /// <summary>HostageK: byte HostageId</summary>
    protected virtual void ParseHostageK(MessageReader r)
    {
        var ev = new HostageKEvent(r.ReadByte());
        HostageK?.Invoke(ev);
    }

    /// <summary>HostagePos: byte Flag, byte HostageId, coord x, y, z</summary>
    protected virtual void ParseHostagePos(MessageReader r)
    {
        var ev = new HostagePosEvent(r.ReadByte(), r.ReadByte(), ReadCoord(r), ReadCoord(r), ReadCoord(r));
        HostagePos?.Invoke(ev);
    }

    /// <summary>BarTime: short Duration</summary>
    protected virtual void ParseBarTime(MessageReader r)
    {
        var ev = new BarTimeEvent(ReadShort(r));
        BarTime?.Invoke(ev);
    }

    /// <summary>BarTime2: short Duration, short StartPercent</summary>
    protected virtual void ParseBarTime2(MessageReader r)
    {
        var ev = new BarTime2Event(ReadShort(r), ReadShort(r));
        BarTime2?.Invoke(ev);
    }

    /// <summary>BlinkAcct: byte BlinkAmount</summary>
    protected virtual void ParseBlinkAcct(MessageReader r)
    {
        var ev = new BlinkAcctEvent(r.ReadByte());
        BlinkAcct?.Invoke(ev);
    }

    /// <summary>ArmorType: byte HasHelmet</summary>
    protected virtual void ParseArmorType(MessageReader r)
    {
        var ev = new ArmorTypeEvent(r.ReadByte());
        ArmorType?.Invoke(ev);
    }

    /// <summary>Crosshair: byte Show</summary>
    protected virtual void ParseCrosshair(MessageReader r)
    {
        var ev = new CrosshairEvent(r.ReadByte());
        Crosshair?.Invoke(ev);
    }

    /// <summary>Fog: byte R, G, B, Density</summary>
    protected virtual void ParseFog(MessageReader r)
    {
        var ev = new FogEvent(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
        Fog?.Invoke(ev);
    }

    /// <summary>NVGToggle: byte Mode</summary>
    protected virtual void ParseNvgToggle(MessageReader r)
    {
        var ev = new NvgToggleEvent(r.ReadByte());
        NvgToggle?.Invoke(ev);
    }

    /// <summary>ReceiveW: byte ItemId</summary>
    protected virtual void ParseReceiveW(MessageReader r)
    {
        var ev = new ReceiveWEvent(r.ReadByte());
        ReceiveW?.Invoke(ev);
    }

    /// <summary>ReloadSound: byte PlayerIndex, byte WeaponId</summary>
    protected virtual void ParseReloadSound(MessageReader r)
    {
        var ev = new ReloadSoundEvent(r.ReadByte(), r.ReadByte());
        ReloadSound?.Invoke(ev);
    }

    /// <summary>SendAudio: byte Channel, string SoundName</summary>
    protected virtual void ParseSendAudio(MessageReader r)
    {
        var ev = new SendAudioEvent(r.ReadByte(), r.ReadString());
        SendAudio?.Invoke(ev);
    }

    /// <summary>ShadowIdx: byte PlayerId, byte ShadowId</summary>
    protected virtual void ParseShadowIdx(MessageReader r)
    {
        var ev = new ShadowIdxEvent(r.ReadByte(), r.ReadByte());
        ShadowIdx?.Invoke(ev);
    }

    /// <summary>ShowMenu: short ValidSlots, byte DisplayTime, byte NeedMore, string Text</summary>
    protected virtual void ParseShowMenu(MessageReader r)
    {
        var ev = new ShowMenuEvent(ReadShort(r), r.ReadByte(), r.ReadByte(), r.ReadString());
        ShowMenu?.Invoke(ev);
    }

    /// <summary>ShowTimer: byte Show</summary>
    protected virtual void ParseShowTimer(MessageReader r)
    {
        var ev = new ShowTimerEvent(r.ReadByte());
        ShowTimer?.Invoke(ev);
    }

    /// <summary>Spectator: byte PlayerId, byte Mode</summary>
    protected virtual void ParseSpectator(MessageReader r)
    {
        var ev = new SpectatorEvent(r.ReadByte(), r.ReadByte());
        Spectator?.Invoke(ev);
    }

    /// <summary>TeamScore: string TeamName, short Score</summary>
    protected virtual void ParseTeamScore(MessageReader r)
    {
        var ev = new TeamScoreEvent(r.ReadString(), ReadShort(r));
        TeamScore?.Invoke(ev);
    }

    /// <summary>VoteMenu: short ValidSlots, byte DisplayTime, string Text</summary>
    protected virtual void ParseVoteMenu(MessageReader r)
    {
        var ev = new VoteMenuEvent(ReadShort(r), r.ReadByte(), r.ReadString());
        VoteMenu?.Invoke(ev);
    }

    /// <summary>AllowSpec: byte Allowed</summary>
    protected virtual void ParseAllowSpec(MessageReader r)
    {
        var ev = new AllowSpecEvent(r.ReadByte());
        AllowSpec?.Invoke(ev);
    }

    /// <summary>ForceCam: byte ForcecamValue, byte ForcechasecamValue, byte Unknown</summary>
    protected virtual void ParseForceCam(MessageReader r)
    {
        var ev = new ForceCamEvent(r.ReadByte(), r.ReadByte(), r.ReadByte());
        ForceCam?.Invoke(ev);
    }

    /// <summary>HLTV: byte ClientId, byte Flags</summary>
    protected virtual void ParseHltv(MessageReader r)
    {
        var ev = new HltvEvent(r.ReadByte(), r.ReadByte());
        Hltv?.Invoke(ev);
    }

    /// <summary>BotVoice: byte Status, byte PlayerIndex</summary>
    protected virtual void ParseBotVoice(MessageReader r)
    {
        var ev = new BotVoiceEvent(r.ReadByte(), r.ReadByte());
        BotVoice?.Invoke(ev);
    }

    /// <summary>BuyClose: no args</summary>
    protected virtual void ParseBuyClose(MessageReader r)
    {
        var ev = new BuyCloseEvent();
        BuyClose?.Invoke(ev);
    }

    /// <summary>ADStop: no args</summary>
    protected virtual void ParseAdStop(MessageReader r)
    {
        var ev = new AdStopEvent();
        AdStop?.Invoke(ev);
    }

    /// <summary>ItemStatus: int ItemBits</summary>
    protected virtual void ParseItemStatus(MessageReader r)
    {
        var ev = new ItemStatusEvent(ReadInt32(r));
        ItemStatus?.Invoke(ev);
    }

    /// <summary>HudTextArgs: string TextCode, byte Style, then repeated NumberOfSubMessages and sub-message strings</summary>
    protected virtual void ParseHudTextArgs(MessageReader r)
    {
        string textCode = r.ReadString();
        byte style = r.ReadByte();
        byte subCount = r.ReadByte();
        var args = new string[subCount];
        for (int i = 0; i < subCount; i++)
            args[i] = r.ReadString();
        var ev = new HudTextArgsEvent(textCode, style, args);
        HudTextArgs?.Invoke(ev);
    }

    /// <summary>HudTextPro: string TextCode, byte Style (CS big-style HUD text)</summary>
    protected virtual void ParseHudTextPro(MessageReader r)
    {
        var ev = new HudTextProEvent(r.ReadString(), r.ReadByte());
        HudTextPro?.Invoke(ev);
    }
}
