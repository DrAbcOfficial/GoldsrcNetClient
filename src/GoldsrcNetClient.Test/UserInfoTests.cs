using GoldsrcNetClient.Core.Network;

namespace GoldsrcNetClient.Test;

public class UserInfoTests
{
    [Fact]
    public void DefaultUserInfo_HasExpectedKeys()
    {
        using var conn = new GoldsrcConnection();
        Assert.Equal("GoldsrcNetClient", conn.GetUserInfo("name"));
        Assert.Equal("48", conn.GetUserInfo("protocol"));
        // A high rate keeps the server's reliable fragment stream (signon batch)
        // draining fast; a slow rate lets the server-side reliable buffer overflow.
        Assert.Equal("100000", conn.GetUserInfo("rate"));
        Assert.Equal("1024", conn.GetUserInfo("cl_dlmax"));
        Assert.Null(conn.GetUserInfo("nonexistent"));
    }

    [Fact]
    public void SetUserInfo_UpdatesExistingKey()
    {
        using var conn = new GoldsrcConnection();
        conn.SetUserInfo("name", "TestPlayer");
        Assert.Equal("TestPlayer", conn.GetUserInfo("name"));
    }

    [Fact]
    public void SetUserInfo_AddsNewKey()
    {
        using var conn = new GoldsrcConnection();
        conn.SetUserInfo("topcolor", "255");
        Assert.Equal("255", conn.GetUserInfo("topcolor"));
    }

    [Fact]
    public void SetUserInfo_CaseInsensitive()
    {
        using var conn = new GoldsrcConnection();
        conn.SetUserInfo("NAME", "CapitalName");
        Assert.Equal("CapitalName", conn.GetUserInfo("name"));
        Assert.Equal("CapitalName", conn.GetUserInfo("NAME"));
    }

    [Fact]
    public void GetUserInfo_ReturnsNullForMissingKey()
    {
        using var conn = new GoldsrcConnection();
        Assert.Null(conn.GetUserInfo("missing_key"));
    }

    [Fact]
    public void SetUserInfo_PreservesOtherKeys()
    {
        using var conn = new GoldsrcConnection();
        conn.SetUserInfo("name", "Changed");
        Assert.Equal("Changed", conn.GetUserInfo("name"));
        Assert.Equal("48", conn.GetUserInfo("protocol"));
        Assert.Equal("1", conn.GetUserInfo("cl_lc"));
    }

    [Fact]
    public void SetUserInfo_RebuildsValidUserInfoFormat()
    {
        using var conn = new GoldsrcConnection();
        conn.SetUserInfo("name", "Player");
        var info = conn.UserInfo;
        Assert.StartsWith("\\", info);
        var parts = info.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(parts.Length % 2 == 0, "UserInfo should have even number of parts (key+value pairs)");
    }

    [Fact]
    public void UserInfo_CanBeReplacedDirectly()
    {
        using var conn = new GoldsrcConnection();
        conn.UserInfo = "\\name\\DirectSet\\protocol\\48";
        Assert.Equal("DirectSet", conn.GetUserInfo("name"));
        Assert.Equal("48", conn.GetUserInfo("protocol"));
    }

    [Fact]
    public void SetUserInfo_MultipleKeys_BuildsCorrectly()
    {
        using var conn = new GoldsrcConnection();
        conn.SetUserInfo("k1", "v1");
        conn.SetUserInfo("k2", "v2");
        Assert.Equal("v1", conn.GetUserInfo("k1"));
        Assert.Equal("v2", conn.GetUserInfo("k2"));
    }
}
