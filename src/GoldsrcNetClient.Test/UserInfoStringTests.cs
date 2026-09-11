using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Test;

public class UserInfoStringTests
{
    [Fact]
    public void Parse_ExtractsPairs()
    {
        var info = new UserInfoString("\\name\\Player\\protocol\\48");
        Assert.Equal("Player", info.Get("name"));
        Assert.Equal("48", info.Get("protocol"));
    }

    [Fact]
    public void Parse_EmptyOrNull_YieldsNoPairs()
    {
        Assert.Empty(new UserInfoString("").Values);
        Assert.Empty(new UserInfoString(null).Values);
    }

    [Fact]
    public void Get_MissingKey_ReturnsNull()
    {
        var info = new UserInfoString("\\name\\Player");
        Assert.Null(info.Get("rate"));
    }

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        var info = new UserInfoString("\\name\\Player");
        Assert.Equal("Player", info.Get("NAME"));
    }

    [Fact]
    public void Set_UpdatesExistingKeyInPlace()
    {
        var info = new UserInfoString("\\name\\Player\\rate\\100000");
        info.Set("name", "Changed");
        Assert.Equal("Changed", info.Get("name"));
        Assert.Equal("100000", info.Get("rate"));
    }

    [Fact]
    public void Set_AddsNewKey()
    {
        var info = new UserInfoString("\\name\\Player");
        info.Set("topcolor", "1");
        Assert.Equal("1", info.Get("topcolor"));
    }

    [Fact]
    public void ToString_StartsWithBackslash_AndHasEvenParts()
    {
        var info = new UserInfoString("\\name\\Player\\rate\\25000");
        info.Set("protocol", "48");
        var raw = info.ToString();

        Assert.StartsWith("\\", raw);
        var parts = raw.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(parts.Length % 2 == 0);
    }

    [Fact]
    public void ToString_PreservesInsertionOrder()
    {
        var info = new UserInfoString();
        info.Set("name", "A");
        info.Set("rate", "1");
        info.Set("model", "gordon");
        Assert.Equal("\\name\\A\\rate\\1\\model\\gordon", info.ToString());
    }

    [Fact]
    public void Roundtrip_ParseBuildParse_IsStable()
    {
        const string raw = "\\name\\GoldsrcNetClient\\protocol\\48\\cl_lc\\1\\cl_lw\\1\\cl_updaterate\\60\\rate\\100000\\hltv\\0";
        var rebuilt = new UserInfoString(new UserInfoString(raw).ToString()).ToString();
        Assert.Equal(raw, rebuilt);
    }

    [Fact]
    public void Parse_IgnoresLeadingEmptySegment()
    {
        var info = new UserInfoString("garbage\\name\\Player");
        Assert.Equal("Player", info.Get("name"));
    }
}
