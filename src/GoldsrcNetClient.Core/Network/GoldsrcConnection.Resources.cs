using GoldsrcNetClient.Core.Io;
using GoldsrcNetClient.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// Bit-packed parser for the server's <c>svc_resourcelist</c>. The raw payload
/// is echoed back on svc_resourcerequest (real-client behavior).
/// </summary>
public partial class GoldsrcConnection
{
    private void ProcessResourceList(ConnectionContext ctx, ref BufferReader reader)
    {
        // Field widths follow the engine branch (Ghidra: SV_SendResources
        // FUN_01da4200 / consistency FUN_01db83a0 in hw.dll build 10257):
        // Sven widened resource count and the consistency absolute index to
        // 16 bits (Valve/ReHLDS: 12 and 10, RESOURCE_INDEX_BITS). Everything
        // else matches ReHLDS's SV_SendResources: type 4 bits, name as
        // bit-string, index 12, download size 24, flags 3, MD5 16B if RES_CUSTOM,
        // reserved 1+32B, consistency delta 5 bits with a 1-bit short/abs selector.
        int countBits = _variant.ResourceIndexBits;
        int absIndexBits = _variant.ConsistencyIndexBits;

        uint resourceCount = reader.ReadBits(countBits);
        Logger.LogDebug($"[ResourceList] resourceCount={resourceCount}");

        ctx.Resources = new ResourceInfo[resourceCount];
        for (uint i = 0; i < resourceCount; i++)
        {
            var r = new ResourceInfo();
            ctx.Resources[i] = r;

            reader.ReadBits(4); // type

            r.Name = reader.ReadBitString();
            if (r.Name.Length > 64)
                throw new InvalidDataException("Resource name exceeds 64 bytes.");

            reader.ReadBits(12); // resource index
            reader.ReadBits(24); // download size
            uint flag = reader.ReadBits(3);
            r.Flag = (byte)flag;

            if ((r.Flag & (byte)ResourceFlag.Custom) != 0)
                r.Md5 = ReadFixedBytes(ref reader, 16);

            uint hasReserved = reader.ReadBits(1);
            if (hasReserved != 0)
                r.Reserved = ReadFixedBytes(ref reader, 32);

            r.NeedConsistency = false;
        }

        uint hasConsistency = reader.ReadBits(1);

        if (hasConsistency != 0)
        {
            int lastIndex = 0;
            while (true)
            {
                uint haveFile = reader.ReadBits(1);
                if (haveFile == 0) break;

                uint indexOrDiff = reader.ReadBits(1);

                if (indexOrDiff == 0)
                {
                    lastIndex = (int)reader.ReadBits(absIndexBits);
                }
                else
                {
                    lastIndex += (int)reader.ReadBits(5);
                }

                if (lastIndex < ctx.Resources.Length)
                {
                    ctx.Resources[lastIndex].NeedConsistency = true;
                    Logger.LogDebug($"[ResourceList] consistency required for index {lastIndex}: {ctx.Resources[lastIndex].Name} (flags {ctx.Resources[lastIndex].Flag})");
                }
            }
        }

        reader.Align();
    }

    /// <summary>Reads <paramref name="count"/> raw bytes from the bitstream.</summary>
    private static byte[] ReadFixedBytes(ref BufferReader reader, int count)
    {
        var bytes = new byte[count];
        for (int b = 0; b < count; b++)
            bytes[b] = (byte)reader.ReadBits(8);
        return bytes;
    }
}
