using GoldsrcNetClient.Core.Game;
using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Messages.Parsing;
using GoldsrcNetClient.Core.Messages.Users;
using GoldsrcNetClient.Core.Network;
using GoldsrcNetClient.Core.Protocol;
using System.Text;

namespace GoldsrcNetClient.Test;

/// <summary>
/// Wire-format tests for the Half-Life and Counter-Strike user message parsers,
/// driven through the real pipeline so registration and parsing are covered
/// together. Layouts follow the stock GoldSrc <c>cl_dll</c> handlers.
/// </summary>
public class UserMessageParserTests
{
    /// <summary>A pipeline over a profile's message set plus a helper to parse one message.</summary>
    private sealed class Harness
    {
        private readonly MessageHub _hub = new();
        private readonly MessagePipeline _pipeline;
        private readonly UserMessageRegistry _registry;

        public Harness(Action<ParserRegistry.Builder> register)
        {
            var builder = new ParserRegistry.Builder();
            EngineMessageParsers.Register(builder, EngineVariants.Valve, new SessionData());
            register(builder);
            _registry = new UserMessageRegistry();
            _pipeline = new MessagePipeline(builder.Build(), _registry, _hub);
        }

        public IDisposable On<T>(Action<T> handler) where T : class, IServerMessage => _hub.Subscribe(handler);

        /// <summary>Registers a fixed-size user message and processes one payload.</summary>
        public void Feed(byte index, string name, params byte[] payload)
        {
            _registry.Register(index, name, (byte)payload.Length);
            var stream = new byte[payload.Length + 1];
            stream[0] = index;
            payload.CopyTo(stream, 1);
            var reader = new BufferReader(stream) { BytePosition = 1 };
            _pipeline.ProcessUserMessage(index, ref reader);
        }
    }

    private static byte[] Str(string s) => [.. Encoding.UTF8.GetBytes(s), 0];

    // ─── Half-Life ───

    [Fact]
    public void CurWeapon_ParsesFields()
    {
        var harness = new Harness(HalfLifeMessages.Register);
        CurWeaponMessage? ev = null;
        harness.On<CurWeaponMessage>(m => ev = m);

        harness.Feed(0x50, "CurWeapon", 1, 7, 30);

        Assert.Equal(1, ev!.IsActive);
        Assert.Equal(7, ev.WeaponId);
        Assert.Equal(30, ev.ClipAmmo);
    }

    [Fact]
    public void SayText_ParsesSenderAndText()
    {
        var harness = new Harness(HalfLifeMessages.Register);
        SayTextMessage? ev = null;
        harness.On<SayTextMessage>(m => ev = m);

        harness.Feed(0x51, "SayText", [3, .. Str("hello world")]);

        Assert.Equal(3, ev!.SenderId);
        Assert.Equal("hello world", ev.Message);
    }

    [Fact]
    public void DeathMsg_HalfLife_HasNoHeadshotByte()
    {
        var harness = new Harness(HalfLifeMessages.Register);
        DeathMsgMessage? ev = null;
        harness.On<DeathMsgMessage>(m => ev = m);

        harness.Feed(0x52, "DeathMsg", [1, 2, .. Str("9mmAR")]);

        Assert.Equal(1, ev!.KillerId);
        Assert.Equal(2, ev.VictimId);
        Assert.Equal(0, ev.IsHeadshot);
        Assert.Equal("9mmAR", ev.WeaponName);
    }

    [Fact]
    public void Damage_ReadsByteCoords()
    {
        var harness = new Harness(HalfLifeMessages.Register);
        DamageMessage? ev = null;
        harness.On<DamageMessage>(m => ev = m);

        // armor, health, damageType(4B), then three 16-bit fixed/8 coordinates.
        harness.Feed(0x53, "Damage", [5, 10, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x80, 0x00, 0xC0, 0xFF]);

        Assert.Equal(5, ev!.DamageSave);
        Assert.Equal(10, ev.DamageTake);
        Assert.Equal(8f, ev.OriginX);
        Assert.Equal(16f, ev.OriginY);
        Assert.Equal(-8f, ev.OriginZ);
    }

    [Fact]
    public void WeaponList_ParsesRegistration()
    {
        var harness = new Harness(HalfLifeMessages.Register);
        WeaponListMessage? ev = null;
        harness.On<WeaponListMessage>(m => ev = m);

        var payload = new List<byte>();
        payload.AddRange(Str("weapon_9mmAR"));
        payload.AddRange([2, 150, 3, 60, 1, 2, 0x0E, 0x00, 0x00]);
        harness.Feed(0x54, "WeaponList", payload.ToArray());

        Assert.Equal("weapon_9mmAR", ev!.WeaponName);
        Assert.Equal(2, ev.Ammo1Id);
        Assert.Equal(150, ev.Ammo1Max);
        Assert.Equal(3, ev.Ammo2Id);
        Assert.Equal(60, ev.Ammo2Max);
        Assert.Equal(1, ev.Slot);
        Assert.Equal(2, ev.Position);
        Assert.Equal(14, ev.WeaponId);
    }

    [Fact]
    public void ReqState_PublishesMessage_WithoutReplyingByItself()
    {
        // The VModEnable reply is session behavior owned by the profile, not the parser.
        var harness = new Harness(HalfLifeMessages.Register);
        var seen = 0;
        harness.On<ReqStateMessage>(_ => seen++);

        harness.Feed(0x55, "ReqState", 0xAA, 0xBB);

        Assert.Equal(1, seen);
    }

    // ─── Counter-Strike ───

    [Fact]
    public void DeathMsg_CounterStrike_HasHeadshotByte()
    {
        var harness = new Harness(CounterStrikeMessages.Register);
        DeathMsgMessage? ev = null;
        harness.On<DeathMsgMessage>(m => ev = m);

        harness.Feed(0x60, "DeathMsg", [1, 2, 1, .. Str("ak47")]);

        Assert.Equal(1, ev!.KillerId);
        Assert.Equal(2, ev.VictimId);
        Assert.Equal(1, ev.IsHeadshot);
        Assert.Equal("ak47", ev.WeaponName);
    }

    [Fact]
    public void CounterStrike_AlsoHasHalfLifeMessages()
    {
        var harness = new Harness(CounterStrikeMessages.Register);
        SayTextMessage? chat = null;
        harness.On<SayTextMessage>(m => chat = m);

        harness.Feed(0x61, "SayText", [0, .. Str("ct win")]);

        Assert.Equal("ct win", chat!.Message);
    }

    [Fact]
    public void Money_ParsesAmountAndFlash()
    {
        var harness = new Harness(CounterStrikeMessages.Register);
        MoneyMessage? ev = null;
        harness.On<MoneyMessage>(m => ev = m);

        harness.Feed(0x62, "Money", 0x2C, 0x01, 0x02); // 300, flash 2

        Assert.Equal(300, ev!.Amount);
        Assert.Equal(2, ev.FlashAmount);
    }

    [Fact]
    public void Radar_ParsesIndexAndCoordinates()
    {
        var harness = new Harness(CounterStrikeMessages.Register);
        RadarMessage? ev = null;
        harness.On<RadarMessage>(m => ev = m);

        harness.Feed(0x63, "Radar", [5, 0x40, 0x00, 0x80, 0x00, 0xC0, 0xFF]);

        Assert.Equal(5, ev!.PlayerIndex);
        Assert.Equal(8f, ev.X);
        Assert.Equal(16f, ev.Y);
        Assert.Equal(-8f, ev.Z);
    }

    [Fact]
    public void ScoreInfo_ParsesScoreboardEntry()
    {
        var harness = new Harness(CounterStrikeMessages.Register);
        ScoreInfoMessage? ev = null;
        harness.On<ScoreInfoMessage>(m => ev = m);

        harness.Feed(0x64, "ScoreInfo", [3, 0x0A, 0x00, 0x02, 0x00, 1, 2]);

        Assert.Equal(3, ev!.PlayerId);
        Assert.Equal(10, ev.Score);
        Assert.Equal(2, ev.Deaths);
        Assert.Equal(1, ev.IsAlive);
        Assert.Equal(2, ev.TeamId);
    }

    [Fact]
    public void TeamScore_ParsesNameAndScore()
    {
        var harness = new Harness(CounterStrikeMessages.Register);
        TeamScoreMessage? ev = null;
        harness.On<TeamScoreMessage>(m => ev = m);

        harness.Feed(0x65, "TeamScore", [.. Str("TERRORIST"), 0x05, 0x00]);

        Assert.Equal("TERRORIST", ev!.TeamName);
        Assert.Equal(5, ev.Score);
    }

    [Fact]
    public void ShowMenu_ParsesSlotsAndText()
    {
        var harness = new Harness(CounterStrikeMessages.Register);
        ShowMenuMessage? ev = null;
        harness.On<ShowMenuMessage>(m => ev = m);

        harness.Feed(0x66, "ShowMenu", [0xFF, 0x00, 30, 0, .. Str("#Buy_Kit")]);

        Assert.Equal(0x00FF, ev!.ValidSlots);
        Assert.Equal(30, ev.DisplayTime);
        Assert.Equal("#Buy_Kit", ev.Text);
    }

    [Fact]
    public void HudTextArgs_ParsesSubMessages()
    {
        var harness = new Harness(CounterStrikeMessages.Register);
        HudTextArgsMessage? ev = null;
        harness.On<HudTextArgsMessage>(m => ev = m);

        harness.Feed(0x67, "HudTextArgs", [.. Str("#Hint"), 1, 2, .. Str("a"), .. Str("b")]);

        Assert.Equal("#Hint", ev!.TextCode);
        Assert.Equal(1, ev.Style);
        Assert.Equal(["a", "b"], ev.Args);
    }

    [Theory]
    [InlineData(0x70, "ArmorType", new byte[] { 1 }, 1)]
    [InlineData(0x71, "NVGToggle", new byte[] { 0 }, 0)]
    [InlineData(0x72, "BuyClose", new byte[] { }, 0)]
    public void SingleByteMessages_Parse(byte index, string name, byte[] payload, int expected)
    {
        var harness = new Harness(CounterStrikeMessages.Register);
        int observed = -1;
        int count = 0;

        switch (name)
        {
            case "ArmorType":
                harness.On<ArmorTypeMessage>(m => { observed = m.HasHelmet; count++; });
                break;
            case "NVGToggle":
                harness.On<NvgToggleMessage>(m => { observed = m.Mode; count++; });
                break;
            case "BuyClose":
                harness.On<BuyCloseMessage>(_ => { observed = 0; count++; });
                break;
        }

        harness.Feed(index, name, payload);

        Assert.Equal(1, count);
        Assert.Equal(expected, observed);
    }

    [Fact]
    public void UnknownName_PublishesRawUserMessage()
    {
        var harness = new Harness(HalfLifeMessages.Register);
        RawUserMessage? raw = null;
        harness.On<RawUserMessage>(m => raw = m);

        harness.Feed(0x7F, "SomeModThing", 0xAA, 0xBB);

        Assert.Equal("SomeModThing", raw!.Name);
        Assert.Equal((byte[])[0xAA, 0xBB], raw.Data);
    }
}
