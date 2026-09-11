using GoldsrcNetClient.Core.Munge;

namespace GoldsrcNetClient.Test;

public class MungeTests
{
    [Fact]
    public void Munge2_EncryptDecrypt_Roundtrip()
    {
        byte[] original = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        byte[] data = (byte[])original.Clone();

        MungeEngine.Munge2(data, data.Length, 42);
        Assert.NotEqual(original, data);

        MungeEngine.UnMunge2(data, data.Length, 42);
        Assert.Equal(original, data);
    }

    [Fact]
    public void Munge3_EncryptDecrypt_Roundtrip()
    {
        byte[] original = [0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80];
        byte[] data = (byte[])original.Clone();

        MungeEngine.Munge3(data, data.Length, 0xFF);
        Assert.NotEqual(original, data);

        MungeEngine.UnMunge3(data, data.Length, 0xFF);
        Assert.Equal(original, data);
    }
}

public class MungeExtendedTests
{
    [Fact]
    public void Munge1_EncryptDecrypt_Roundtrip()
    {
        byte[] original = [0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89];
        byte[] data = (byte[])original.Clone();

        MungeEngine.Munge1(data, data.Length, 0x55);
        Assert.NotEqual(original, data);

        MungeEngine.UnMunge1(data, data.Length, 0x55);
        Assert.Equal(original, data);
    }

    [Fact]
    public void Munge1_ZeroSequence()
    {
        byte[] original = [0x11, 0x22, 0x33, 0x44];
        byte[] data = (byte[])original.Clone();

        MungeEngine.Munge1(data, data.Length, 0);
        Assert.NotEqual(original, data);

        MungeEngine.UnMunge1(data, data.Length, 0);
        Assert.Equal(original, data);
    }

    [Fact]
    public void Munge2_VaryingSize_4Bytes()
    {
        byte[] original = [0xAA, 0xBB, 0xCC, 0xDD];
        byte[] data = (byte[])original.Clone();

        MungeEngine.Munge2(data, data.Length, 100);
        MungeEngine.UnMunge2(data, data.Length, 100);
        Assert.Equal(original, data);
    }

    [Fact]
    public void Munge2_VaryingSize_16Bytes()
    {
        byte[] original = new byte[16];
        for (int i = 0; i < 16; i++) original[i] = (byte)(i * 17);
        byte[] data = (byte[])original.Clone();

        MungeEngine.Munge2(data, data.Length, 255);
        MungeEngine.UnMunge2(data, data.Length, 255);
        Assert.Equal(original, data);
    }

    [Fact]
    public void Munge2_VaryingSize_5Bytes_NonAligned()
    {
        byte[] original = [0x01, 0x02, 0x03, 0x04, 0x05];
        byte[] data = (byte[])original.Clone();

        MungeEngine.Munge2(data, data.Length, 199);
        MungeEngine.UnMunge2(data, data.Length, 199);
        Assert.Equal(original, data);
    }

    [Fact]
    public void Munge3_VaryingSequence()
    {
        for (int seq = 0; seq < 256; seq++)
        {
            byte[] original = [0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE];
            byte[] data = (byte[])original.Clone();

            MungeEngine.Munge3(data, data.Length, seq);
            MungeEngine.UnMunge3(data, data.Length, seq);
            Assert.Equal(original, data);
        }
    }

    [Fact]
    public void Munge2_LargeData_100Bytes()
    {
        byte[] original = new byte[100];
        for (int i = 0; i < 100; i++) original[i] = (byte)i;
        byte[] data = (byte[])original.Clone();

        MungeEngine.Munge2(data, data.Length, 42);
        Assert.False(original.SequenceEqual(data));

        MungeEngine.UnMunge2(data, data.Length, 42);
        Assert.Equal(original, data);
    }

    [Fact]
    public void Munge2_EmptyData()
    {
        byte[] data = [];
        MungeEngine.Munge2(data, data.Length, 0);
        MungeEngine.UnMunge2(data, data.Length, 0);
        Assert.Empty(data);
    }

    [Fact]
    public void Munge3_SingleByte_NonAligned()
    {
        byte[] original = [0x7F];
        byte[] data = (byte[])original.Clone();

        MungeEngine.Munge3(data, data.Length, 0xAB);
        MungeEngine.UnMunge3(data, data.Length, 0xAB);
        Assert.Equal(original, data);
    }
}
