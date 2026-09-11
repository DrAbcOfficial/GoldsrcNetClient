using GoldsrcNetClient.Core.Delta;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Test;

public class DeltaDefinitionsTests
{
    [Fact]
    public void Find_Event_ReturnsCorrectType()
    {
        var dt = DeltaDefinitions.Find("event_t");
        Assert.True(dt.HasValue);
        Assert.Equal(0x0E, dt.Value.FieldAmount);
        Assert.Equal("event_t", dt.Value.DeltaName);
    }

    [Fact]
    public void Find_ClientData_ReturnsCorrectType()
    {
        var dt = DeltaDefinitions.Find("clientdata_t");
        Assert.True(dt.HasValue);
        Assert.Equal(0x32, dt.Value.FieldAmount);
    }

    [Fact]
    public void Find_Unknown_ReturnsNull()
    {
        var dt = DeltaDefinitions.Find("nonexistent_t");
        Assert.False(dt.HasValue);
    }
}

public class DeltaDefinitionsExtendedTests
{
    [Fact]
    public void All_HasSevenEntries()
    {
        Assert.Equal(7, DeltaDefinitions.All.Length);
    }

    [Fact]
    public void Find_WeaponData_ReturnsCorrectType()
    {
        var dt = DeltaDefinitions.Find("weapon_data_t");
        Assert.True(dt.HasValue);
        Assert.Equal(0x14, dt.Value.FieldAmount);
        Assert.Equal("weapon_data_t", dt.Value.DeltaName);
    }

    [Fact]
    public void Find_EntityState_ReturnsCorrectType()
    {
        var dt = DeltaDefinitions.Find("entity_state_t");
        Assert.True(dt.HasValue);
        Assert.Equal(0x34, dt.Value.FieldAmount);
    }

    [Fact]
    public void Find_EntityStatePlayer_ReturnsCorrectType()
    {
        var dt = DeltaDefinitions.Find("entity_state_player_t");
        Assert.True(dt.HasValue);
        Assert.Equal(0x31, dt.Value.FieldAmount);
    }

    [Fact]
    public void Find_UserCmd_ReturnsCorrectType()
    {
        var dt = DeltaDefinitions.Find("usercmd_t");
        Assert.True(dt.HasValue);
        Assert.Equal(0x0F, dt.Value.FieldAmount);
    }

    [Fact]
    public void Find_CustomEntityState_ReturnsCorrectType()
    {
        var dt = DeltaDefinitions.Find("custom_entity_state_t");
        Assert.True(dt.HasValue);
        Assert.Equal(0x13, dt.Value.FieldAmount);
    }

    [Fact]
    public void EntityStatePlayer_HasAimentField()
    {
        var dt = DeltaDefinitions.EntityStatePlayer;
        Assert.Contains(dt.Fields, f => f.FieldName == "aiment");
        var aiment = dt.Fields.First(f => f.FieldName == "aiment");
        ulong flag = (ulong)aiment.FieldFlag;
        ulong cleaned = flag & ~(ulong)DeltaFieldFlag.Signed & ~(ulong)DeltaFieldFlag.StringField & ~(ulong)DeltaFieldFlag.Byte & ~(ulong)DeltaFieldFlag.Short & ~(ulong)DeltaFieldFlag.Float & ~(ulong)DeltaFieldFlag.Angle & ~(ulong)DeltaFieldFlag.TimeWindow8 & ~(ulong)DeltaFieldFlag.TimeWindowBig;
        Assert.Equal((ulong)DeltaFieldFlag.Integer, cleaned);
    }

    [Fact]
    public void EntityState_HasBeamEndpos()
    {
        var dt = DeltaDefinitions.EntityState;
        Assert.Contains(dt.Fields, f => f.FieldName == "endpos[0]");
        Assert.Contains(dt.Fields, f => f.FieldName == "startpos[0]");
    }

    [Fact]
    public void ClientData_HasHealth()
    {
        var dt = DeltaDefinitions.ClientData;
        Assert.Contains(dt.Fields, f => f.FieldName == "health");
        Assert.Contains(dt.Fields, f => f.FieldName == "flags");
    }

    [Fact]
    public void StaticFields_Exist()
    {
        Assert.Equal("event_t", DeltaDefinitions.Event.DeltaName);
        Assert.Equal("weapon_data_t", DeltaDefinitions.WeaponData.DeltaName);
        Assert.Equal("usercmd_t", DeltaDefinitions.UserCmd.DeltaName);
        Assert.Equal("custom_entity_state_t", DeltaDefinitions.CustomEntityState.DeltaName);
        Assert.Equal("entity_state_player_t", DeltaDefinitions.EntityStatePlayer.DeltaName);
        Assert.Equal("entity_state_t", DeltaDefinitions.EntityState.DeltaName);
        Assert.Equal("clientdata_t", DeltaDefinitions.ClientData.DeltaName);
    }
}
