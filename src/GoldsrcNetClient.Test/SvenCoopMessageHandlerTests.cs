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
/// Wire-format tests for the Sven Co-op user message parsers. All layouts mirror the
/// reverse-engineered client.dll handlers (see sven_coop_usermsgs.md).
/// </summary>
public class SvenCoopMessageHandlerTests
{
    /// <summary>Builds the payload of an svc_newusermsg registration (18 bytes).</summary>
    private static byte[] Registration(byte index, byte size, string name)
    {
        var nameBytes = new byte[16];
        var nameRaw = Encoding.UTF8.GetBytes(name);
        Array.Copy(nameRaw, nameBytes, Math.Min(nameRaw.Length, 16));
        return [index, size, .. nameBytes];
    }

    /// <summary>
    /// Builds a pipeline over the Sven Co-op message set, registers
    /// <paramref name="name"/> at <paramref name="index"/> with a fixed payload
    /// size, and returns a harness plus a reader positioned after the index byte.
    /// </summary>
    private static (SvenHarness Handler, GoldsrcConnection Connection) Setup(
        byte index, string name, out BufferReader reader, params byte[] payload)
    {
        var connection = new GoldsrcConnection();
        var harness = new SvenHarness();
        harness.Register(index, name, (byte)payload.Length);
        var stream = new byte[payload.Length + 1];
        stream[0] = index;
        payload.CopyTo(stream, 1);
        reader = new BufferReader(stream) { BytePosition = 1 };
        return (harness, connection);
    }

    /// <summary>Drives the Sven parsers through the real message pipeline.</summary>
    private sealed class SvenHarness
    {
        private readonly MessageHub _hub = new();
        private readonly MessagePipeline _pipeline;

        public SvenHarness()
        {
            var builder = new ParserRegistry.Builder();
            EngineMessageParsers.Register(builder, EngineVariants.SvenCoop, new SessionData());
            SvenCoopMessages.Register(builder);
            var userMessages = new GoldsrcNetClient.Core.Game.UserMessageRegistry();
            _pipeline = new MessagePipeline(builder.Build(), userMessages, _hub);
            RegisterAction = userMessages.Register;
        }

        private Action<byte, string, byte> RegisterAction { get; }

        public void Register(byte index, string name, byte size) => RegisterAction(index, name, size);

        public void Subscribe<T>(Action<T> handler) where T : class, IServerMessage => _hub.Subscribe(handler);

        public bool HandleMessage(byte index, ref BufferReader reader)
            => _pipeline.ProcessUserMessage(index, ref reader) is not null;
    }

    /// <summary>Wraps a 32-bit Sven coordinate (value × 8).</summary>
    private static IEnumerable<byte> Coord(int raw)
    {
        yield return (byte)(raw & 0xFF);
        yield return (byte)((raw >> 8) & 0xFF);
        yield return (byte)((raw >> 16) & 0xFF);
        yield return (byte)((raw >> 24) & 0xFF);
    }

    [Fact]
    public void CurWeapon_ParsesSvenWideFormat()
    {
        // byte active, short wid, long clip, long reserve
        var (handler, conn) = Setup(0x50, "CurWeapon", out var reader,
            0x01, 0x05, 0x00, 0x0F, 0x00, 0x00, 0x00, 0x2C, 0x01, 0x00, 0x00);
        ScCurWeaponMessage? ev = null;
        handler.Subscribe<ScCurWeaponMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x50, ref reader));
        Assert.Equal(12, reader.BytePosition);
        Assert.NotNull(ev);
        Assert.Equal(1, ev!.IsActive);
        Assert.Equal(5, ev.WeaponId);
        Assert.Equal(15, ev.ClipAmmo);
        Assert.Equal(300, ev.ReserveAmmo);
    }

    [Fact]
    public void Health_Reads32BitValue()
    {
        var (handler, conn) = Setup(0x51, "Health", out var reader, 0x64, 0x00, 0x00, 0x00);
        ScHealthMessage? ev = null;
        handler.Subscribe<ScHealthMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x51, ref reader));
        Assert.Equal(5, reader.BytePosition);
        Assert.Equal(100, ev!.Health);
    }

    [Fact]
    public void WeaponList_ParsesLongAmmoMaxima()
    {
        // name, char ammo1id, long ammo1max, char ammo2id, long ammo2max, char slot, char pos, short wid, byte flags
        var payload = new List<byte>();
        payload.AddRange(Encoding.UTF8.GetBytes("weapon_crowbar\0"));
        payload.Add(0x02);                                      // primary ammo id
        payload.AddRange([0xFF, 0x00, 0x00, 0x00]);             // primary max = 255 (sentinel → -1)
        payload.Add(0x03);                                      // secondary ammo id
        payload.AddRange([0x2C, 0x01, 0x00, 0x00]);             // secondary max = 300
        payload.Add(0x01);                                      // slot
        payload.Add(0x02);                                      // position
        payload.AddRange([0x0E, 0x00]);                         // weapon id 14
        payload.Add(0x00);                                      // flags

        var (handler, conn) = Setup(0x52, "WeaponList", out var reader, payload.ToArray());
        ScWeaponListMessage? ev = null;
        handler.Subscribe<ScWeaponListMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x52, ref reader));
        Assert.NotNull(ev);
        Assert.Equal("weapon_crowbar", ev!.WeaponName);
        Assert.Equal(2, ev.PrimaryAmmoId);
        Assert.Equal(-1, ev.PrimaryAmmoMax); // 0x000000FF sentinel
        Assert.Equal(3, ev.SecondaryAmmoId);
        Assert.Equal(300, ev.SecondaryAmmoMax);
        Assert.Equal(1, ev.Slot);
        Assert.Equal(2, ev.Position);
        Assert.Equal(14, ev.WeaponId);
    }

    [Fact]
    public void TextMsg_ParsesMessageAndFourParams()
    {
        var payload = new List<byte> { 0x03 };
        foreach (var s in new[] { "#GAMEOVER", "a", "b", "c", "d" })
            payload.AddRange(Encoding.UTF8.GetBytes(s).Append((byte)0));

        var (handler, conn) = Setup(0x53, "TextMsg", out var reader, payload.ToArray());
        ScTextMsgMessage? ev = null;
        handler.Subscribe<ScTextMsgMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x53, ref reader));
        Assert.NotNull(ev);
        Assert.Equal(3, ev!.MsgDest);
        Assert.Equal("#GAMEOVER", ev.Message);
        Assert.Equal(new[] { "a", "b", "c", "d" }, new[] { ev.Param1, ev.Param2, ev.Param3, ev.Param4 });
    }

    [Fact]
    public void StartSound_ReadsOnlyFlaggedFields()
    {
        // flags 0x0019 = entity(0x10) | volume(0x1) | origin(0x8); then channel + sound index
        var payload = new List<byte>();
        payload.AddRange([0x19, 0x00]);                 // flags
        payload.AddRange([0x07, 0x00]);                 // entity 7
        payload.Add(0xC8);                              // volume 200
        payload.AddRange(Coord(128));                   // origin x = 16.0
        payload.AddRange(Coord(64));                    // origin y = 8.0
        payload.AddRange(Coord(-32));                   // origin z = -4.0
        payload.Add(0x05);                              // channel
        payload.AddRange([0x0B, 0x00]);                 // sound index 11

        var (handler, conn) = Setup(0x54, "StartSound", out var reader, payload.ToArray());
        ScStartSoundMessage? ev = null;
        handler.Subscribe<ScStartSoundMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x54, ref reader));
        Assert.Equal(1 + payload.Count, reader.BytePosition);
        Assert.NotNull(ev);
        Assert.Equal(0x0019, ev!.Flags);
        Assert.Equal((short)7, ev.Entity);
        Assert.Equal((byte)200, ev.Volume);
        Assert.Null(ev.Attenuation);
        Assert.Null(ev.Pitch);
        Assert.Equal(16f, ev.OriginX);
        Assert.Equal(8f, ev.OriginY);
        Assert.Equal(-4f, ev.OriginZ);
        Assert.Null(ev.Duration);
        Assert.Equal(5, ev.Channel);
        Assert.Equal(11, ev.SoundIndex);
    }

    [Fact]
    public void Fog_ParsesSvenExtendedFormat()
    {
        var payload = new List<byte>();
        payload.AddRange([0x00, 0x00]);                 // leading short (discarded by client)
        payload.Add(0x01);                              // enabled
        payload.AddRange(Coord(8)); payload.AddRange(Coord(16)); payload.AddRange(Coord(24));
        payload.AddRange([0x2C, 0x01]);                 // unknown short = 300
        payload.AddRange([0x40, 0x80, 0xC0]);           // r g b
        payload.AddRange([0x10, 0x00]);                 // value1 = 16
        payload.AddRange([0x20, 0x00]);                 // value2 = 32

        var (handler, conn) = Setup(0x55, "Fog", out var reader, payload.ToArray());
        ScFogMessage? ev = null;
        handler.Subscribe<ScFogMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x55, ref reader));
        Assert.Equal(1 + payload.Count, reader.BytePosition);
        Assert.NotNull(ev);
        Assert.True(ev!.Enabled);
        Assert.Equal(1f, ev.OriginX);
        Assert.Equal(2f, ev.OriginY);
        Assert.Equal(3f, ev.OriginZ);
        Assert.Equal(300, ev.Unknown);
        Assert.Equal(0x40, ev.R);
        Assert.Equal(0x80, ev.G);
        Assert.Equal(0xC0, ev.B);
        Assert.Equal(16, ev.Value1);
        Assert.Equal(32, ev.Value2);
    }

    [Fact]
    public void RampSprite_ReadsOnlyFlaggedOptionalFields()
    {
        // flags 0x0010 → only the red byte follows the fixed part
        var payload = new List<byte>();
        payload.AddRange([0x0A, 0x00]);                 // entity 10
        payload.Add(0x05);                              // life ticks
        payload.AddRange(Coord(0)); payload.AddRange(Coord(8)); payload.AddRange(Coord(16));
        payload.AddRange([0x10, 0x00]);                 // flags: only 0x10 (red)
        payload.Add(0xFF);                              // red

        var (handler, conn) = Setup(0x56, "RampSprite", out var reader, payload.ToArray());
        ScRampSpriteMessage? ev = null;
        handler.Subscribe<ScRampSpriteMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x56, ref reader));
        Assert.Equal(1 + payload.Count, reader.BytePosition);
        Assert.NotNull(ev);
        Assert.Equal(10, ev!.Entity);
        Assert.Equal(5, ev.LifeTicks);
        Assert.Equal(1f, ev.Y);
        Assert.Equal(2f, ev.Z);
        Assert.Equal(0x0010, ev.Flags);
        Assert.Equal((byte)0xFF, ev.R);
        Assert.Null(ev.G);
        Assert.Null(ev.B);
        Assert.Null(ev.StartTime);
        Assert.Null(ev.Color2R);
    }

    [Fact]
    public void PrtlUpdt_RemovedPathConsumesEntityAndFlagOnly()
    {
        var (handler, conn) = Setup(0x57, "PrtlUpdt", out var reader,
            0x03, 0x00, 0x00, 0x00,  // entity 3
            0x00);                   // enabled = 0 → remove
        ScPortalUpdateMessage? ev = null;
        handler.Subscribe<ScPortalUpdateMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x57, ref reader));
        Assert.Equal(6, reader.BytePosition);
        Assert.NotNull(ev);
        Assert.True(ev!.Removed);
        Assert.Equal(3, ev.Entity);
    }

    [Fact]
    public void PrtlUpdt_Type1ParsesConditionalSections()
    {
        var payload = new List<byte>();
        payload.AddRange([0x05, 0x00, 0x00, 0x00]);     // entity 5
        payload.Add(0x01);                              // enabled
        payload.AddRange(Coord(8)); payload.AddRange(Coord(0)); payload.AddRange(Coord(0));   // vec1
        payload.AddRange(Coord(0)); payload.AddRange(Coord(16)); payload.AddRange(Coord(0));  // vec2
        payload.Add(0x01);                              // type = 1
        payload.Add(0x00);                              // style
        payload.AddRange([0x00, 0x00, 0x80, 0x3F]);     // life = 1.0f
        payload.Add(0x00);                              // byte1
        payload.AddRange([0x00, 0x00, 0x00, 0x00]);     // long1
        payload.AddRange([0x00, 0x00, 0x00, 0x00]);     // long2
        payload.Add(0x00);                              // flag1 = false
        payload.Add(0x00);                              // model flag = false (no long/coord3/ang3)
        payload.Add(0x01);                              // type-1 clip flag = true
        payload.AddRange([0x0A, 0x00, 0x00, 0x00]);     // long3 = 10
        payload.AddRange([0x14, 0x00, 0x00, 0x00]);     // long4 = 20
        // style != 2 → no string

        var (handler, conn) = Setup(0x58, "PrtlUpdt", out var reader, payload.ToArray());
        ScPortalUpdateMessage? ev = null;
        handler.Subscribe<ScPortalUpdateMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x58, ref reader));
        Assert.Equal(1 + payload.Count, reader.BytePosition);
        Assert.NotNull(ev);
        Assert.False(ev!.Removed);
        Assert.Equal(5, ev.Entity);
        Assert.Equal(1, ev.Type);
        Assert.Equal(1f, ev.Life);
        Assert.Null(ev.ModelIndex);
        Assert.Equal(10, ev.Long3);
        Assert.Equal(20, ev.Long4);
        Assert.Null(ev.FlagA);
        Assert.Null(ev.Name);
    }

    [Fact]
    public void TeamNames_ReadsPerTeamNameAndColour()
    {
        var payload = new List<byte> { 0x02 };          // two teams
        payload.AddRange(Encoding.UTF8.GetBytes("team1\0"));
        payload.AddRange(Coord(80)); payload.AddRange(Coord(160)); payload.AddRange(Coord(240));
        payload.AddRange(Encoding.UTF8.GetBytes("team2\0"));
        payload.AddRange(Coord(8)); payload.AddRange(Coord(16)); payload.AddRange(Coord(24));

        var (handler, conn) = Setup(0x59, "TeamNames", out var reader, payload.ToArray());
        ScTeamNamesMessage? ev = null;
        handler.Subscribe<ScTeamNamesMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x59, ref reader));
        Assert.NotNull(ev);
        Assert.Equal(2, ev!.Teams.Length);
        Assert.Equal("team1", ev.Teams[0].Name);
        Assert.Equal(10f, ev.Teams[0].ColorR);
        Assert.Equal(20f, ev.Teams[0].ColorG);
        Assert.Equal(30f, ev.Teams[0].ColorB);
        Assert.Equal("team2", ev.Teams[1].Name);
        Assert.Equal(1f, ev.Teams[1].ColorR);
    }

    [Fact]
    public void Gib_UnknownTypeCarriesNoCoordinates()
    {
        var (handler, conn) = Setup(0x5A, "Gib", out var reader, 0x03);
        ScGibMessage? ev = null;
        handler.Subscribe<ScGibMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x5A, ref reader));
        Assert.Equal(2, reader.BytePosition); // index + type byte only
        Assert.NotNull(ev);
        Assert.Equal(3, ev!.Type);
        Assert.Equal(0f, ev.OriginX);
    }

    [Fact]
    public void ToggleElem_RaisesChannelAndState()
    {
        var (handler, conn) = Setup(0x5B, "ToggleElem", out var reader, 0x05, 0x01);
        ScToggleElemMessage? ev = null;
        handler.Subscribe<ScToggleElemMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x5B, ref reader));
        Assert.Equal(3, reader.BytePosition);
        Assert.NotNull(ev);
        Assert.Equal(5, ev!.Channel);
        Assert.True(ev.State);
    }

    [Fact]
    public void HideHUD_ReadsShortMask()
    {
        var (handler, conn) = Setup(0x5C, "HideHUD", out var reader, 0x01, 0x01);
        ScHideHudMessage? ev = null;
        handler.Subscribe<ScHideHudMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x5C, ref reader));
        Assert.Equal(3, reader.BytePosition);
        Assert.Equal(0x0101, ev!.Flags);
    }

    [Fact]
    public void ShowMenu_ParsesSvenFormat()
    {
        var text = Encoding.UTF8.GetBytes("Choose a team\0");
        var payload = new List<byte> { 0x07, 0x0A, 0x80 };
        payload.AddRange(text);

        var (handler, conn) = Setup(0x5D, "ShowMenu", out var reader, payload.ToArray());
        ScShowMenuMessage? ev = null;
        handler.Subscribe<ScShowMenuMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x5D, ref reader));
        Assert.NotNull(ev);
        Assert.Equal(0x07, ev!.ValidSlots);
        Assert.Equal(10, ev.DisplayTime);
        Assert.Equal(0x80, ev.Flags);
        Assert.Equal("Choose a team", ev.Text);
    }

    [Fact]
    public void ScoreInfo_ParsesSvenFloatFormat()
    {
        var payload = new List<byte> { 0x02 };                          // player 2
        payload.AddRange([0x00, 0x00, 0xC8, 0x42]);                     // score = 100.0f
        payload.AddRange([0x05, 0x00, 0x00, 0x00]);                     // unknown long = 5
        payload.AddRange([0x00, 0x00, 0x20, 0x41]);                     // float 10.0f
        payload.AddRange([0x00, 0x00, 0x40, 0x40]);                     // float 3.0f
        payload.AddRange([0x10, 0x00, 0x00]);                           // class 0x10 + two bytes

        var (handler, conn) = Setup(0x5E, "ScoreInfo", out var reader, payload.ToArray());
        ScScoreInfoMessage? ev = null;
        handler.Subscribe<ScScoreInfoMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x5E, ref reader));
        Assert.Equal(1 + payload.Count, reader.BytePosition);
        Assert.NotNull(ev);
        Assert.Equal(2, ev!.PlayerIndex);
        Assert.Equal(100f, ev.Score);
        Assert.Equal(5, ev.UnknownLong);
        Assert.Equal(0x10, ev.ClassId);
    }

    [Fact]
    public void MapList_ResetCommandStoresTotalOnly()
    {
        var (handler, conn) = Setup(0x5F, "MapList", out var reader, 0x00, 0x0A, 0x00);
        ScMapListMessage? ev = null;
        handler.Subscribe<ScMapListMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x5F, ref reader));
        Assert.Equal(4, reader.BytePosition);
        Assert.NotNull(ev);
        Assert.True(ev!.IsReset);
        Assert.Equal(10, ev.TotalMaps);
        Assert.Empty(ev.MapNames);
    }

    [Fact]
    public void MapList_CloseCommandConsumesCommandByteOnly()
    {
        var (handler, conn) = Setup(0x5F, "MapList", out var reader, 0x7B);
        ScMapListMessage? ev = null;
        handler.Subscribe<ScMapListMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x5F, ref reader));
        Assert.Equal(2, reader.BytePosition);
        Assert.NotNull(ev);
        Assert.True(ev!.IsClose);
    }

    [Fact]
    public void MapList_UpdateCommandReadsMapNameRange()
    {
        var payload = new List<byte> { 0x05, 0x02, 0x00, 0x04, 0x00 }; // cmd 5, start 2, end 4
        payload.AddRange(Encoding.UTF8.GetBytes("map_a\0"));
        payload.AddRange(Encoding.UTF8.GetBytes("map_b\0"));

        var (handler, conn) = Setup(0x5F, "MapList", out var reader, payload.ToArray());
        ScMapListMessage? ev = null;
        handler.Subscribe<ScMapListMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x5F, ref reader));
        Assert.Equal(1 + payload.Count, reader.BytePosition);
        Assert.NotNull(ev);
        Assert.True(ev!.IsUpdate);
        Assert.Equal(2, ev.StartIndex);
        Assert.Equal(4, ev.EndIndex);
        Assert.Equal(new[] { "map_a", "map_b" }, ev.MapNames);
    }

    [Fact]
    public void VoteMenu_ParsesIdQuestionAndLabels()
    {
        var payload = new List<byte> { 0x03 };
        payload.AddRange(Encoding.UTF8.GetBytes("#Vote_Map\0"));
        payload.AddRange(Encoding.UTF8.GetBytes("\0")); // empty yes → "#Menu_Yes"
        payload.AddRange(Encoding.UTF8.GetBytes("Kick him\0"));

        var (handler, conn) = Setup(0x60, "VoteMenu", out var reader, payload.ToArray());
        ScVoteMenuMessage? ev = null;
        handler.Subscribe<ScVoteMenuMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x60, ref reader));
        Assert.NotNull(ev);
        Assert.Equal(3, ev!.VoteId);
        Assert.Equal("#Vote_Map", ev.Question);
        Assert.Equal(string.Empty, ev.YesLabel);
        Assert.Equal("Kick him", ev.NoLabel);
    }

    [Fact]
    public void ClExtrasInfo_ParsesFramingBlocks()
    {
        var payload = new List<byte>();
        payload.AddRange([0x10, 0x00, 0x00, 0x00]);               // plain length = 16
        payload.AddRange([0x08, 0x00, 0x00, 0x00]);               // iv length = 8
        payload.AddRange([0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88]);
        payload.AddRange([0x0C, 0x00, 0x00, 0x00]);               // encrypted length = 12
        payload.AddRange([0xDE, 0xAD, 0xBE, 0xEF, 1, 2, 3, 4, 5, 6, 7, 8]);
        payload.AddRange([0x10, 0x00, 0x00, 0x00]);               // digest length = 16 (key len)
        payload.AddRange(Enumerable.Repeat((byte)0xAA, 16).ToArray());

        var (handler, conn) = Setup(0x61, "ClExtrasInfo", out var reader, payload.ToArray());
        ScClExtrasInfoMessage? ev = null;
        handler.Subscribe<ScClExtrasInfoMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x61, ref reader));
        Assert.Equal(1 + payload.Count, reader.BytePosition);
        Assert.NotNull(ev);
        Assert.Equal(16, ev!.PlainLength);
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 }, ev.Iv);
        Assert.Equal(12, ev.EncryptedData.Length);
        Assert.Equal(16, ev.EncryptedDigest.Length);
    }

    [Fact]
    public void ChangeSky_ParsesNameAndColour()
    {
        var payload = new List<byte>();
        payload.AddRange(Encoding.UTF8.GetBytes("desert\0"));
        payload.AddRange(Coord(64)); payload.AddRange(Coord(48)); payload.AddRange(Coord(32));

        var (handler, conn) = Setup(0x60, "ChangeSky", out var reader, payload.ToArray());
        ScChangeSkyMessage? ev = null;
        handler.Subscribe<ScChangeSkyMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x60, ref reader));
        Assert.NotNull(ev);
        Assert.Equal("desert", ev!.SkyName);
        Assert.Equal(8f, ev.ColorR);
        Assert.Equal(6f, ev.ColorG);
        Assert.Equal(4f, ev.ColorB);
    }

    [Fact]
    public void UpdateTime_ParsesChannelTimeAndDuration()
    {
        var payload = new List<byte>
        {
            0x03,                                                       // channel
            0x00, 0x00, 0x20, 0x41,                                     // time = 10.0f
            0x00, 0x00, 0xA0, 0x40,                                     // duration = 5.0f
        };

        var (handler, conn) = Setup(0x61, "UpdateTime", out var reader, payload.ToArray());
        ScUpdateTimeMessage? ev = null;
        handler.Subscribe<ScUpdateTimeMessage>(e => ev = e);

        Assert.True(handler.HandleMessage( 0x61, ref reader));
        Assert.NotNull(ev);
        Assert.Equal(3, ev!.Channel);
        Assert.Equal(10f, ev.Time);
        Assert.Equal(5f, ev.Duration);
    }
}
