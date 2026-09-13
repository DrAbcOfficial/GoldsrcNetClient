using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Network;
using System.Buffers.Binary;

namespace GoldsrcNetClient.Test;

public class SplitPacketReassemblerTests
{
    /// <summary>Builds one UDP split fragment in the Sven Co-op 10-byte header layout:
    /// [u32 0xFFFFFFFE][i32 setId][u8 count][u8 number].</summary>
    private static byte[] MakeFragment(int setId, int fragmentNumber, int fragmentCount, byte[] payload)
    {
        byte[] packet = new byte[MessageConstants.SplitHeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, MessageConstants.SplitMarker);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), setId);
        packet[8] = (byte)fragmentCount;
        packet[9] = (byte)fragmentNumber;
        payload.CopyTo(packet, MessageConstants.SplitHeaderSize);
        return packet;
    }

    /// <summary>Splits one datagram the way the engine transport would.</summary>
    private static List<byte[]> MakeSplitSet(int setId, byte[] datagram)
    {
        int count = (datagram.Length + MessageConstants.SplitPayloadSize - 1) / MessageConstants.SplitPayloadSize;
        var fragments = new List<byte[]>();
        for (int i = 0; i < count; i++)
        {
            int size = Math.Min(MessageConstants.SplitPayloadSize, datagram.Length - i * MessageConstants.SplitPayloadSize);
            fragments.Add(MakeFragment(setId, i, count, datagram[(i * MessageConstants.SplitPayloadSize)..(i * MessageConstants.SplitPayloadSize + size)]));
        }
        return fragments;
    }

    [Fact]
    public void Fragments_ReassembleIntoOriginalDatagram()
    {
        var reassembler = new SplitPacketReassembler();
        byte[] datagram = new byte[3000];
        Random.Shared.NextBytes(datagram);

        var fragments = MakeSplitSet(setId: 7, datagram);
        Assert.Equal(3, fragments.Count);

        Assert.Null(reassembler.TryAdd(fragments[0]));
        Assert.Null(reassembler.TryAdd(fragments[1]));
        byte[]? whole = reassembler.TryAdd(fragments[2]);
        Assert.NotNull(whole);

        Assert.Equal(datagram.Length, whole.Length);
        Assert.Equal(datagram, whole);
    }

    [Fact]
    public void RealWorldSizes_1400_1400_790_Reassemble()
    {
        // Wire-verified against a Sven Co-op 5.26 server (build 10257): split set
        // 0xA2 arrived as 1400 + 1400 + 790 byte fragments carrying a 3560-byte
        // netchan packet. Dropping these was the root cause of the intermittent
        // "Reliable channel overflowed" drop.
        var reassembler = new SplitPacketReassembler();
        byte[] datagram = new byte[3560];
        Random.Shared.NextBytes(datagram);

        var fragments = MakeSplitSet(setId: 0xA2, datagram);
        Assert.Equal([1400, 1400, 790], fragments.Select(f => f.Length));

        Assert.Null(reassembler.TryAdd(fragments[0]));
        Assert.Null(reassembler.TryAdd(fragments[1]));
        byte[]? whole = reassembler.TryAdd(fragments[2]);
        Assert.NotNull(whole);
        Assert.Equal(datagram, whole);
    }

    [Fact]
    public void RealWorldHeaderBytes_ParseAsSvenLayout()
    {
        // Captured from the wire: the fragment's first payload bytes decode as a
        // valid netchan header (seq with reliable+fragment flags), proving the
        // 10-byte header interpretation. Non-final fragments always carry a full
        // SplitPayloadSize payload (the receiver's offset math relies on it).
        var reassembler = new SplitPacketReassembler();
        byte[] head = Convert.FromHexString("3E0000C09C010000" + "0110001000000000");
        byte[] payload = [.. head, .. new byte[MessageConstants.SplitPayloadSize - head.Length]];
        byte[] fragment = MakeFragment(setId: 0x8B, fragmentNumber: 0, fragmentCount: 2, payload);

        Assert.Null(reassembler.TryAdd(fragment)); // incomplete set, but accepted

        byte[] tail = [0x00, 0x01, 0x02];
        byte[] completion = MakeFragment(setId: 0x8B, fragmentNumber: 1, fragmentCount: 2, tail);
        byte[]? whole = reassembler.TryAdd(completion);
        Assert.NotNull(whole);
        Assert.Equal(MessageConstants.SplitPayloadSize + tail.Length, whole.Length);
        // Netchan header of the reassembled packet: seq = 0xC000003E.
        Assert.Equal(0xC000003Eu, BinaryPrimitives.ReadUInt32LittleEndian(whole));
        Assert.Equal(head.AsSpan(8).ToArray(), whole.AsSpan(8, 8).ToArray());
    }

    [Fact]
    public void OutOfOrderFragments_StillReassemble()
    {
        var reassembler = new SplitPacketReassembler();
        byte[] datagram = new byte[3000];
        Random.Shared.NextBytes(datagram);

        var fragments = MakeSplitSet(setId: 3, datagram);
        Assert.Null(reassembler.TryAdd(fragments[2]));
        Assert.Null(reassembler.TryAdd(fragments[0]));
        byte[]? whole = reassembler.TryAdd(fragments[1]);
        Assert.NotNull(whole);
        Assert.Equal(datagram, whole);
    }

    [Fact]
    public void DuplicateFragment_IsIgnored()
    {
        var reassembler = new SplitPacketReassembler();
        byte[] datagram = new byte[3000];
        Random.Shared.NextBytes(datagram);

        var fragments = MakeSplitSet(setId: 9, datagram);
        Assert.Null(reassembler.TryAdd(fragments[0]));
        Assert.Null(reassembler.TryAdd(fragments[0])); // duplicate, ignored
        Assert.Null(reassembler.TryAdd(fragments[1]));
        Assert.Null(reassembler.TryAdd(fragments[1])); // duplicate, ignored
        byte[]? whole = reassembler.TryAdd(fragments[2]);
        Assert.NotNull(whole);
        Assert.Equal(datagram, whole);
    }

    [Fact]
    public void NewSetId_DiscardsPartialOldSet()
    {
        var reassembler = new SplitPacketReassembler();
        byte[] datagram = new byte[3000];
        Random.Shared.NextBytes(datagram);

        var stale = MakeSplitSet(setId: 1, datagram);
        Assert.Null(reassembler.TryAdd(stale[0])); // one fragment of set 1

        var fresh = MakeSplitSet(setId: 2, datagram);
        Assert.Null(reassembler.TryAdd(fresh[0]));
        Assert.Null(reassembler.TryAdd(fresh[1]));
        byte[]? whole = reassembler.TryAdd(fresh[2]);
        Assert.NotNull(whole);
        Assert.Equal(datagram, whole);
    }

    [Fact]
    public void MalformedFragments_AreRejected()
    {
        var reassembler = new SplitPacketReassembler();
        byte[] payload = new byte[100];

        // fragment count 0
        Assert.Null(reassembler.TryAdd(MakeFragment(1, 0, 0, payload)));
        // fragment number >= count
        Assert.Null(reassembler.TryAdd(MakeFragment(1, 3, 2, payload)));
        // count > 15
        Assert.Null(reassembler.TryAdd(MakeFragment(1, 0, 16, payload)));
        // truncated header
        Assert.Null(reassembler.TryAdd(new byte[8]));
    }
}
