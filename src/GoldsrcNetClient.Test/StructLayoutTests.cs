using GoldsrcNetClient.Core.Delta;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Test;

public class StructLayoutTests
{
    [Fact]
    public void ServerInfoData_Size()
    {
        unsafe
        {
            int size = sizeof(ServerInfoData);
            Assert.Equal(31, size);
        }
    }

    [Fact]
    public void DeltaType_Construction()
    {
        var fields = new DeltaField[] { new("test", DeltaFieldFlag.Float, 16, 1.0f) };
        var dt = new DeltaType("test_t", 1, fields);

        Assert.Equal("test_t", dt.DeltaName);
        Assert.Equal(1, dt.FieldAmount);
        Assert.Equal("test", dt.Fields[0].FieldName);
    }

    [Fact]
    public void ResourceInfo_Defaults()
    {
        var ri = new ResourceInfo();
        Assert.Empty(ri.Name);
        Assert.Equal(0, ri.Flag);
        Assert.Equal(16, ri.Md5.Length);
        Assert.Equal(32, ri.Reserved.Length);
        Assert.False(ri.NeedConsistency);
    }
}
