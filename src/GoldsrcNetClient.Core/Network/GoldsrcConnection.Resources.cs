using GoldsrcNetClient.Core.Messages;
using Microsoft.Extensions.Logging;
using GoldsrcNetClient.Core.Protocol;
using GoldsrcNetClient.Core.Util;

namespace GoldsrcNetClient.Core.Network;

public partial class GoldsrcConnection
{
    private void ProcessResourceList(ConnectionContext ctx, MessageReader reader)
    {
        // Sven Co-op widened two fields vs. Valve GoldSrc (Ghidra: SV_SendResources
        // FUN_01da4200 / consistency FUN_01db83a0 in hw.dll build 10257):
        //   - resource count:       16 bits (Valve/ReHLDS: 12, RESOURCE_INDEX_BITS)
        //   - consistency abs index: 16 bits (Valve/ReHLDS: 10)
        // Everything else matches ReHLDS's SV_SendResources: type 4 bits, name as
        // bit-string, index 12, download size 24, flags 3, MD5 16B if RES_CUSTOM,
        // reserved 1+32B, consistency delta 5 bits with a 1-bit short/abs selector.
        int countBits = _useLongFragmentFields ? 16 : 12;
        int absIndexBits = _useLongFragmentFields ? 16 : 10;

        int bitIdx = reader.Offset * 8;
        uint resourceCount = 0;
        if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref resourceCount, countBits)) return;
        Logger.LogDebug($"[ResourceList] resourceCount={resourceCount}");

        ctx.Resources = new ResourceInfo[resourceCount];
        for (uint i = 0; i < resourceCount; i++)
        {
            var r = new ResourceInfo();
            ctx.Resources[i] = r;

            uint type = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref type, 4)) return;

            if (!BitReader.ReadBitString(reader.Data, ref bitIdx, reader.Size, out r.Name)) return;
            if (r.Name.Length > 64) return;

            uint resIdx = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref resIdx, 12)) return;
            uint dlSize = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref dlSize, 24)) return;
            uint flag = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref flag, 3)) return;
            r.Flag = (byte)flag;

            if ((r.Flag & (byte)ResourceFlag.Custom) != 0)
            {
                byte[] md5 = new byte[16];
                for (int b = 0; b < 16; b++)
                {
                    uint bb = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref bb, 8)) return;
                    md5[b] = (byte)bb;
                }
                r.Md5 = md5;
            }

            uint hasReserved = 0;
            if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref hasReserved, 1)) return;
            if (hasReserved != 0)
            {
                byte[] reserved = new byte[32];
                for (int b = 0; b < 32; b++)
                {
                    uint bb = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref bb, 8)) return;
                    reserved[b] = (byte)bb;
                }
                r.Reserved = reserved;
            }

            r.NeedConsistency = false;
        }

        uint hasConsistency = 0;
        if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref hasConsistency, 1)) return;

        if (hasConsistency != 0)
        {
            int lastIndex = 0;
            while (true)
            {
                uint haveFile = 0;
                if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref haveFile, 1)) return;
                if (haveFile == 0) break;

                uint indexOrDiff = 0;
                if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref indexOrDiff, 1)) return;

                if (indexOrDiff == 0)
                {
                    lastIndex = 0;
                    uint idx = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref idx, absIndexBits)) return;
                    lastIndex = (int)idx;
                }
                else
                {
                    uint diff = 0;
                    if (!BitReader.ReadBits(reader.Data, ref bitIdx, reader.Size, ref diff, 5)) return;
                    lastIndex += (int)diff;
                }

                if (lastIndex < ctx.Resources.Length)
                {
                    ctx.Resources[lastIndex].NeedConsistency = true;
                    Logger.LogDebug($"[ResourceList] consistency required for index {lastIndex}: {ctx.Resources[lastIndex].Name} (flags {ctx.Resources[lastIndex].Flag})");
                }
            }
        }

        reader.Offset = (bitIdx + 7) / 8;
    }
}
