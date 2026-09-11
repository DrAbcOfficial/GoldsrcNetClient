using GoldsrcNetClient.Core.Munge;

namespace GoldsrcNetClient.Test;

/// <summary>
/// Pins the worldmap-CRC echo protocol against the server-side reference
/// (ReHLDS): SV_SendServerinfo munges the CRC with COM_Munge3 key
/// (-1 - playernum) &amp; 0xFF; SV_Spawn_f unmunges our echo with COM_UnMunge2
/// key (-1 - spawncount) &amp; 0xFF; SV_CheckMapDifferences drops the client
/// (as a fake "Reliable channel overflowed") if the recovered value differs
/// from the real worldmap CRC. All keys are 8-bit — a 32-bit key silently
/// corrupts the round trip.
/// </summary>
public class SpawnCrcProtocolTests
{
    private static byte[] ServerMungeWorldmapCrc(uint worldmapCrc, int playernum)
    {
        byte[] buf = BitConverter.GetBytes((int)worldmapCrc);
        MungeEngine.Munge3(buf, 4, (-1 - playernum) & 0xFF);
        return buf;
    }

    private static uint ClientUnmungeWorldmapCrc(byte[] wire, int playernum)
    {
        byte[] buf = (byte[])wire.Clone();
        MungeEngine.UnMunge3(buf, 4, (-1 - playernum) & 0xFF);
        return BitConverter.ToUInt32(buf);
    }

    private static byte[] ClientMungeSpawnCrc(uint worldmapCrc, uint spawnCount, bool eightBitKey)
    {
        byte[] buf = BitConverter.GetBytes((int)worldmapCrc);
        int key = eightBitKey ? (-1 - (int)spawnCount) & 0xFF : ~(int)spawnCount;
        MungeEngine.Munge2(buf, 4, key);
        return buf;
    }

    private static uint ServerUnmungeSpawnCrc(byte[] wire, uint spawnCount)
    {
        byte[] buf = (byte[])wire.Clone();
        MungeEngine.UnMunge2(buf, 4, (-1 - (int)spawnCount) & 0xFF);
        return BitConverter.ToUInt32(buf);
    }

    [Theory]
    [InlineData(0x1A2B3C4Du, 0, 1139u)]
    [InlineData(0xDEADBEEFu, 5, 1150u)]
    [InlineData(0x00000000u, 23, 1u)]
    [InlineData(0xFFFFFFFFu, 12, 256u)]
    public void SpawnCrc_Roundtrip_With8BitKeys_RecoversWorldmapCrc(uint worldmapCrc, int playernum, uint spawnCount)
    {
        byte[] wireServerInfo = ServerMungeWorldmapCrc(worldmapCrc, playernum);
        uint clientCrc = ClientUnmungeWorldmapCrc(wireServerInfo, playernum);
        Assert.Equal(worldmapCrc, clientCrc);

        byte[] wireSpawn = ClientMungeSpawnCrc(clientCrc, spawnCount, eightBitKey: true);
        uint serverRecovered = ServerUnmungeSpawnCrc(wireSpawn, spawnCount);
        Assert.Equal(worldmapCrc, serverRecovered);
    }

    [Fact]
    public void SpawnCrc_With32BitKey_DoesNotRoundtrip()
    {
        // Regression guard: the pre-fix client used the full 32-bit ~spawnCount
        // as the Munge2 key. The server unmunges with the 8-bit key, recovers a
        // corrupted crcValue, and SV_CheckMapDifferences flags the channel as
        // overflowed within ~5 seconds ("Reliable channel overflowed").
        const uint worldmapCrc = 0x1A2B3C4D;
        const uint spawnCount = 1150;

        byte[] wireSpawn = ClientMungeSpawnCrc(worldmapCrc, spawnCount, eightBitKey: false);
        uint serverRecovered = ServerUnmungeSpawnCrc(wireSpawn, spawnCount);
        Assert.NotEqual(worldmapCrc, serverRecovered);
    }

    [Fact]
    public void ServerInfo_With32BitUnMunge3Key_DoesNotRecoverWorldmapCrc()
    {
        // The pre-fix client also unmunged svc_serverinfo with the full 32-bit
        // ~playernum, so the stored WorldmapCrc was garbage before the spawn
        // echo was even built.
        const uint worldmapCrc = 0x1A2B3C4D;
        const int playernum = 5;

        byte[] wire = ServerMungeWorldmapCrc(worldmapCrc, playernum);
        byte[] buf = (byte[])wire.Clone();
        MungeEngine.UnMunge3(buf, 4, ~playernum);
        Assert.NotEqual(worldmapCrc, BitConverter.ToUInt32(buf));
    }
}
