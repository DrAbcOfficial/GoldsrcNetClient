using GoldsrcNetClient.Core.Game;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Network;
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

    /// <summary>Registers <paramref name="name"/> at <paramref name="index"/> with a fixed
    /// payload size and returns a reader positioned at the message index byte.</summary>
    private static (SvenCoopMessageHandler Handler, MessageReader Reader, GoldsrcConnection Connection) Setup(
        byte index, string name, params byte[] payload)
    {
        var connection = new GoldsrcConnection();
        var handler = new SvenCoopMessageHandler();
        handler.Registry.Register(new MessageReader(Registration(index, (byte)payload.Length, name)));
        var reader = new MessageReader([index, .. payload]) { Offset = 1 };
        return (handler, reader, connection);
    }

    /// <summary>Wraps a 16-bit GoldSrc coordinate (value × 8).</summary>
    private static IEnumerable<byte> Coord(short raw)
    {
        yield return (byte)(raw & 0xFF);
        yield return (byte)(raw >> 8);
    }

    [Fact]
    public void CurWeapon_ParsesSvenWideFormat()
    {
        // byte active, short wid, long clip, long reserve
        var (handler, reader, conn) = Setup(0x50, "CurWeapon",
            0x01, 0x05, 0x00, 0x0F, 0x00, 0x00, 0x00, 0x2C, 0x01, 0x00, 0x00);
        ScCurWeaponEvent? ev = null;
        handler.ScCurWeapon += e => ev = e;

        Assert.True(handler.HandleMessage(conn, 0x50, reader));
        Assert.Equal(12, reader.Offset);
        Assert.NotNull(ev);
        Assert.Equal(1, ev!.Value.IsActive);
        Assert.Equal(5, ev.Value.WeaponId);
        Assert.Equal(15, ev.Value.ClipAmmo);
        Assert.Equal(300, ev.Value.ReserveAmmo);
    }

    [Fact]
    public void Health_Reads32BitValue()
    {
        var (handler, reader, conn) = Setup(0x51, "Health", 0x64, 0x00, 0x00, 0x00);
        ScHealthEvent? ev = null;
        handler.ScHealth += e => ev = e;

        Assert.True(handler.HandleMessage(conn, 0x51, reader));
        Assert.Equal(5, reader.Offset);
        Assert.Equal(100, ev!.Value.Health);
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

        var (handler, reader, conn) = Setup(0x52, "WeaponList", payload.ToArray());
        ScWeaponListEvent? ev = null;
        handler.ScWeaponList += e => ev = e;

        Assert.True(handler.HandleMessage(conn, 0x52, reader));
        Assert.NotNull(ev);
        Assert.Equal("weapon_crowbar", ev!.Value.WeaponName);
        Assert.Equal(2, ev.Value.PrimaryAmmoId);
        Assert.Equal(-1, ev.Value.PrimaryAmmoMax); // 0x000000FF sentinel
        Assert.Equal(3, ev.Value.SecondaryAmmoId);
        Assert.Equal(300, ev.Value.SecondaryAmmoMax);
        Assert.Equal(1, ev.Value.Slot);
        Assert.Equal(2, ev.Value.Position);
        Assert.Equal(14, ev.Value.WeaponId);
    }

    [Fact]
    public void TextMsg_ParsesMessageAndFourParams()
    {
        var payload = new List<byte> { 0x03 };
        foreach (var s in new[] { "#GAMEOVER", "a", "b", "c", "d" })
            payload.AddRange(Encoding.UTF8.GetBytes(s).Append((byte)0));

        var (handler, reader, conn) = Setup(0x53, "TextMsg", payload.ToArray());
        ScTextMsgEvent? ev = null;
        handler.ScTextMsg += e => ev = e;

        Assert.True(handler.HandleMessage(conn, 0x53, reader));
        Assert.NotNull(ev);
        Assert.Equal(3, ev!.Value.MsgDest);
        Assert.Equal("#GAMEOVER", ev.Value.Message);
        Assert.Equal(new[] { "a", "b", "c", "d" }, new[] { ev.Value.Param1, ev.Value.Param2, ev.Value.Param3, ev.Value.Param4 });
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

        var (handler, reader, conn) = Setup(0x54, "StartSound", payload.ToArray());
        ScStartSoundEvent? ev = null;
        handler.ScStartSound += e => ev = e;

        Assert.True(handler.HandleMessage(conn, 0x54, reader));
        Assert.Equal(1 + payload.Count, reader.Offset);
        Assert.NotNull(ev);
        Assert.Equal(0x0019, ev!.Value.Flags);
        Assert.Equal((short)7, ev.Value.Entity);
        Assert.Equal((byte)200, ev.Value.Volume);
        Assert.Null(ev.Value.Attenuation);
        Assert.Null(ev.Value.Pitch);
        Assert.Equal(16f, ev.Value.OriginX);
        Assert.Equal(8f, ev.Value.OriginY);
        Assert.Equal(-4f, ev.Value.OriginZ);
        Assert.Null(ev.Value.Duration);
        Assert.Equal(5, ev.Value.Channel);
        Assert.Equal(11, ev.Value.SoundIndex);
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

        var (handler, reader, conn) = Setup(0x55, "Fog", payload.ToArray());
        ScFogEvent? ev = null;
        handler.ScFog += e => ev = e;

        Assert.True(handler.HandleMessage(conn, 0x55, reader));
        Assert.Equal(1 + payload.Count, reader.Offset);
        Assert.NotNull(ev);
        Assert.True(ev!.Value.Enabled);
        Assert.Equal(1f, ev.Value.OriginX);
        Assert.Equal(2f, ev.Value.OriginY);
        Assert.Equal(3f, ev.Value.OriginZ);
        Assert.Equal(300, ev.Value.Unknown);
        Assert.Equal(0x40, ev.Value.R);
        Assert.Equal(0x80, ev.Value.G);
        Assert.Equal(0xC0, ev.Value.B);
        Assert.Equal(16, ev.Value.Value1);
        Assert.Equal(32, ev.Value.Value2);
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

        var (handler, reader, conn) = Setup(0x56, "RampSprite", payload.ToArray());
        ScRampSpriteEvent? ev = null;
        handler.ScRampSprite += e => ev = e;

        Assert.True(handler.HandleMessage(conn, 0x56, reader));
        Assert.Equal(1 + payload.Count, reader.Offset);
        Assert.NotNull(ev);
        Assert.Equal(10, ev!.Value.Entity);
        Assert.Equal(5, ev.Value.LifeTicks);
        Assert.Equal(1f, ev.Value.Y);
        Assert.Equal(2f, ev.Value.Z);
        Assert.Equal(0x0010, ev.Value.Flags);
        Assert.Equal((byte)0xFF, ev.Value.R);
        Assert.Null(ev.Value.G);
        Assert.Null(ev.Value.B);
        Assert.Null(ev.Value.StartTime);
        Assert.Null(ev.Value.Color2R);
    }

    [Fact]
    public void PrtlUpdt_RemovedPathConsumesEntityAndFlagOnly()
    {
        var (handler, reader, conn) = Setup(0x57, "PrtlUpdt",
            0x03, 0x00, 0x00, 0x00,  // entity 3
            0x00);                   // enabled = 0 → remove
        ScPortalUpdateEvent? ev = null;
        handler.ScPortalUpdate += e => ev = e;

        Assert.True(handler.HandleMessage(conn, 0x57, reader));
        Assert.Equal(6, reader.Offset);
        Assert.NotNull(ev);
        Assert.True(ev!.Value.Removed);
        Assert.Equal(3, ev.Value.Entity);
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

        var (handler, reader, conn) = Setup(0x58, "PrtlUpdt", payload.ToArray());
        ScPortalUpdateEvent? ev = null;
        handler.ScPortalUpdate += e => ev = e;

        Assert.True(handler.HandleMessage(conn, 0x58, reader));
        Assert.Equal(1 + payload.Count, reader.Offset);
        Assert.NotNull(ev);
        Assert.False(ev!.Value.Removed);
        Assert.Equal(5, ev.Value.Entity);
        Assert.Equal(1, ev.Value.Type);
        Assert.Equal(1f, ev.Value.Life);
        Assert.Null(ev.Value.ModelIndex);
        Assert.Equal(10, ev.Value.Long3);
        Assert.Equal(20, ev.Value.Long4);
        Assert.Null(ev.Value.FlagA);
        Assert.Null(ev.Value.Name);
    }

    [Fact]
    public void TeamNames_ReadsPerTeamNameAndColour()
    {
        var payload = new List<byte> { 0x02 };          // two teams
        payload.AddRange(Encoding.UTF8.GetBytes("team1\0"));
        payload.AddRange(Coord(80)); payload.AddRange(Coord(160)); payload.AddRange(Coord(240));
        payload.AddRange(Encoding.UTF8.GetBytes("team2\0"));
        payload.AddRange(Coord(8)); payload.AddRange(Coord(16)); payload.AddRange(Coord(24));

        var (handler, reader, conn) = Setup(0x59, "TeamNames", payload.ToArray());
        ScTeamNamesEvent? ev = null;
        handler.ScTeamNames += e => ev = e;

        Assert.True(handler.HandleMessage(conn, 0x59, reader));
        Assert.NotNull(ev);
        Assert.Equal(2, ev!.Value.Teams.Length);
        Assert.Equal("team1", ev.Value.Teams[0].Name);
        Assert.Equal(10f, ev.Value.Teams[0].ColorR);
        Assert.Equal(20f, ev.Value.Teams[0].ColorG);
        Assert.Equal(30f, ev.Value.Teams[0].ColorB);
        Assert.Equal("team2", ev.Value.Teams[1].Name);
        Assert.Equal(1f, ev.Value.Teams[1].ColorR);
    }

    [Fact]
    public void Gib_UnknownTypeCarriesNoCoordinates()
    {
        var (handler, reader, conn) = Setup(0x5A, "Gib", 0x03);
        ScGibEvent? ev = null;
        handler.ScGib += e => ev = e;

        Assert.True(handler.HandleMessage(conn, 0x5A, reader));
        Assert.Equal(2, reader.Offset); // index + type byte only
        Assert.NotNull(ev);
        Assert.Equal(3, ev!.Value.Type);
        Assert.Equal(0f, ev.Value.OriginX);
    }

    [Fact]
    public void ToggleElem_RaisesChannelAndState()
    {
        var (handler, reader, conn) = Setup(0x5B, "ToggleElem", 0x05, 0x01);
        ScToggleElemEvent? ev = null;
        handler.ScToggleElem += e => ev = e;

        Assert.True(handler.HandleMessage(conn, 0x5B, reader));
        Assert.Equal(3, reader.Offset);
        Assert.NotNull(ev);
        Assert.Equal(5, ev!.Value.Channel);
        Assert.True(ev.Value.State);
    }

    [Fact]
    public void HideHUD_ReadsShortMask()
    {
        var (handler, reader, conn) = Setup(0x5C, "HideHUD", 0x01, 0x01);
        ScHideHudEvent? ev = null;
        handler.ScHideHud += e => ev = e;

        Assert.True(handler.HandleMessage(conn, 0x5C, reader));
        Assert.Equal(3, reader.Offset);
        Assert.Equal(0x0101, ev!.Value.Flags);
    }

    [Fact]
    public void ShowMenu_ParsesSvenFormat()
    {
        var text = Encoding.UTF8.GetBytes("Choose a team\0");
        var payload = new List<byte> { 0x07, 0x0A, 0x80 };
        payload.AddRange(text);

        var (handler, reader, conn) = Setup(0x5D, "ShowMenu", payload.ToArray());
        ScShowMenuEvent? ev = null;
        handler.ScShowMenu += e => ev = e;

        Assert.True(handler.HandleMessage(conn, 0x5D, reader));
        Assert.NotNull(ev);
        Assert.Equal(0x07, ev!.Value.ValidSlots);
        Assert.Equal(10, ev.Value.DisplayTime);
        Assert.Equal(0x80, ev.Value.Flags);
        Assert.Equal("Choose a team", ev.Value.Text);
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

        var (handler, reader, conn) = Setup(0x5E, "ScoreInfo", payload.ToArray());
        ScScoreInfoEvent? ev = null;
        handler.ScScoreInfo += e => ev = e;

        Assert.True(handler.HandleMessage(conn, 0x5E, reader));
        Assert.Equal(1 + payload.Count, reader.Offset);
        Assert.NotNull(ev);
        Assert.Equal(2, ev!.Value.PlayerIndex);
        Assert.Equal(100f, ev.Value.Score);
        Assert.Equal(5, ev.Value.UnknownLong);
        Assert.Equal(0x10, ev.Value.ClassId);
    }

    [Fact]
    public void UnparsableMessages_RaiseRawScEvent()
    {
        var payload = Encoding.UTF8.GetBytes("map01\0");
        var (handler, reader, conn) = Setup(0x5F, "MapList", payload);
        RawUserMessage? raw = null;
        handler.OnScSpecificMessage += m => raw = m;

        Assert.True(handler.HandleMessage(conn, 0x5F, reader));
        Assert.NotNull(raw);
        Assert.Equal("MapList", raw!.Value.Name);
        Assert.Equal(payload, raw.Value.Data);
    }

    [Fact]
    public void ChangeSky_ParsesNameAndColour()
    {
        var payload = new List<byte>();
        payload.AddRange(Encoding.UTF8.GetBytes("desert\0"));
        payload.AddRange(Coord(64)); payload.AddRange(Coord(48)); payload.AddRange(Coord(32));

        var (handler, reader, conn) = Setup(0x60, "ChangeSky", payload.ToArray());
        ScChangeSkyEvent? ev = null;
        handler.ScChangeSky += e => ev = e;

        Assert.True(handler.HandleMessage(conn, 0x60, reader));
        Assert.NotNull(ev);
        Assert.Equal("desert", ev!.Value.SkyName);
        Assert.Equal(8f, ev.Value.ColorR);
        Assert.Equal(6f, ev.Value.ColorG);
        Assert.Equal(4f, ev.Value.ColorB);
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

        var (handler, reader, conn) = Setup(0x61, "UpdateTime", payload.ToArray());
        ScUpdateTimeEvent? ev = null;
        handler.ScUpdateTime += e => ev = e;

        Assert.True(handler.HandleMessage(conn, 0x61, reader));
        Assert.NotNull(ev);
        Assert.Equal(3, ev!.Value.Channel);
        Assert.Equal(10f, ev.Value.Time);
        Assert.Equal(5f, ev.Value.Duration);
    }
}
