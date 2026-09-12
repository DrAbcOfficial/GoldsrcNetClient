using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Munge;
using GoldsrcNetClient.Core.Netchan;
using GoldsrcNetClient.Core.Protocol;
using System.Buffers.Binary;
using System.Net;

namespace GoldsrcNetClient.Test;

public class NetchanChannelTests
{
    /// <summary>Creates a channel whose packets are captured in a list (no real socket).</summary>
    private static (NetchanChannel Channel, List<byte[]> Sent) CreateChannel(bool useEncryption = true, bool longFragmentFields = false)
    {
        List<byte[]> sent = [];
        var channel = new NetchanChannel(
            new IPEndPoint(IPAddress.Loopback, 27015),
            (packet, _, _) => { sent.Add((byte[])packet.ToArray().Clone()); return Task.FromResult(0); },
            useEncryption: useEncryption, longFragmentFields: longFragmentFields);
        return (channel, sent);
    }

    /// <summary>Builds a sequenced packet with a raw message body, Munge2-encrypted as on the wire.</summary>
    private static byte[] MakeInlinePacket(uint seq, uint ack, byte[] body, bool reliable = false)
    {
        uint w1 = seq & MessageConstants.SequenceMask;
        if (reliable)
            w1 |= MessageConstants.SequenceFlagReliable;
        uint w2 = (ack & MessageConstants.SequenceMask) | (ack << 31);
        byte[] packet = new byte[MessageConstants.ConnectedHeadSize + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0), w1);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), w2);
        byte[] encrypted = (byte[])body.Clone();
        MungeEngine.Munge2(encrypted, encrypted.Length, (int)(w1 & 0xFF));
        encrypted.CopyTo(packet, MessageConstants.ConnectedHeadSize);
        return packet;
    }

    /// <summary>Builds a fragment-stream packet carrying one chunk on stream 0.</summary>
    private static byte[] MakeFragmentPacket(uint seq, uint fragId, int offset, byte[] chunk)
    {
        uint w1 = (seq & MessageConstants.SequenceMask) | MessageConstants.SequenceFlagFragment;
        var body = new List<byte> { 0x01 }; // stream 0 active
        body.AddRange(BitConverter.GetBytes(fragId));
        body.AddRange(BitConverter.GetBytes((ushort)offset));
        body.AddRange(BitConverter.GetBytes((ushort)chunk.Length));
        body.Add(0x00); // stream 1 inactive
        body.AddRange(chunk);

        byte[] packet = new byte[MessageConstants.ConnectedHeadSize + body.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0), w1);
        byte[] encrypted = [.. body];
        MungeEngine.Munge2(encrypted, encrypted.Length, (int)(w1 & 0xFF));
        encrypted.CopyTo(packet, MessageConstants.ConnectedHeadSize);
        return packet;
    }

    [Fact]
    public void ReliableMessage_IsDelivered_AndAckClearsPending()
    {
        var (sender, senderSent) = CreateChannel();
        var (receiver, receiverSent) = CreateChannel();
        List<byte[]> received = [];

        sender.SendReliable([(byte)'n', (byte)'e', (byte)'w', 0]);
        var packet = Assert.Single(senderSent);

        receiver.ProcessIncoming(packet, received.Add);
        // The delivered inline stream also contains the svc_nop padding of the
        // 16-byte minimum packet; the payload itself is prefixed.
        var message = Assert.Single(received);
        Assert.Equal([(byte)'n', (byte)'e', (byte)'w', (byte)0], message[..4]);
        Assert.Equal((byte)ServerMessageType.Nop, message[4]);

        // The receiver's ack clears the sender's pending slot: the next reliable
        // message is transmitted immediately instead of being throttled for 500 ms.
        // (Processing the ack also makes the sender emit its own ack-only packet.)
        sender.ProcessIncoming(Assert.Single(receiverSent), _ => { });
        sender.SendReliable([0x02]);

        uint w1 = BinaryPrimitives.ReadUInt32LittleEndian(senderSent[^1].AsSpan(0));
        Assert.Equal(3u, w1 & MessageConstants.SequenceMask);
        Assert.True((w1 & MessageConstants.SequenceFlagReliable) != 0, "second reliable message should ride the next packet");
    }

    [Fact]
    public void DuplicateSequence_IsDropped()
    {
        var (channel, _) = CreateChannel();
        List<byte[]> received = [];

        byte[] packet = MakeInlinePacket(seq: 1, ack: 0, body: [(byte)ServerMessageType.Nop]);
        channel.ProcessIncoming(packet, received.Add);
        channel.ProcessIncoming(packet, received.Add);

        Assert.Single(received);
    }

    [Fact]
    public void OutOfOrderSequence_IsDropped()
    {
        var (channel, _) = CreateChannel();
        List<byte[]> received = [];

        channel.ProcessIncoming(MakeInlinePacket(seq: 3, ack: 0, body: [(byte)ServerMessageType.Nop]), received.Add);
        channel.ProcessIncoming(MakeInlinePacket(seq: 2, ack: 0, body: [(byte)ServerMessageType.Nop]), received.Add);

        Assert.Single(received);
    }

    [Fact]
    public void FragmentedMessage_ReassemblesInOrder()
    {
        var (channel, _) = CreateChannel();
        List<byte[]> received = [];

        byte[] part1 = [0x01, 0x02, 0x03];
        byte[] part2 = [0x04, 0x05];
        const uint totalCount = 2;

        channel.ProcessIncoming(MakeFragmentPacket(seq: 1, fragId: (1u << 16) | totalCount, offset: 0, part1), received.Add);
        Assert.Empty(received); // transfer incomplete

        channel.ProcessIncoming(MakeFragmentPacket(seq: 2, fragId: (2u << 16) | totalCount, offset: 0, part2), received.Add);
        Assert.Equal([[0x01, 0x02, 0x03, 0x04, 0x05]], received);
    }

    [Fact]
    public void GapInFragmentIds_RestartsTransfer()
    {
        var (channel, _) = CreateChannel();
        List<byte[]> received = [];

        byte[] part1 = [0x01, 0x02];
        byte[] part2 = [0x03, 0x04];

        // First transfer of 2 fragments, only chunk 1 arrives.
        channel.ProcessIncoming(MakeFragmentPacket(seq: 1, fragId: (1u << 16) | 2, offset: 0, part1), received.Add);
        // A new transfer announcing 3 fragments resets the stream; the lone id-2
        // chunk is a gap and must be dropped, leaving the transfer incomplete.
        channel.ProcessIncoming(MakeFragmentPacket(seq: 2, fragId: (2u << 16) | 3, offset: 0, part2), received.Add);
        Assert.Empty(received);
    }

    [Fact]
    public void PacketsSmallerThanMinimum_ArePaddedWithSvcNop()
    {
        var (channel, sent) = CreateChannel();

        channel.SendKeepAlive();

        var packet = Assert.Single(sent);
        Assert.True(packet.Length >= NetchanChannel.MinPacketSize);

        // Padding happens before Munge2 encryption — decrypt, then verify.
        uint w1 = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0));
        byte[] body = packet[MessageConstants.ConnectedHeadSize..];
        MungeEngine.UnMunge2(body, body.Length, (int)(w1 & 0xFF));
        for (int i = 0; i < body.Length; i++)
            Assert.Equal((byte)ServerMessageType.Nop, body[i]);
    }

    [Fact]
    public void KeepAlive_AckCarriesIncomingSequenceAndReliableBit()
    {
        var (channel, sent) = CreateChannel();

        // A reliable server packet toggles the channel's incoming reliable counter.
        channel.ProcessIncoming(MakeInlinePacket(seq: 5, ack: 0, body: [(byte)ServerMessageType.Nop], reliable: true), _ => { });
        sent.Clear();

        channel.SendKeepAlive();

        var packet = Assert.Single(sent);
        uint w2 = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4));
        Assert.Equal(5u, w2 & MessageConstants.SequenceMask);
        Assert.Equal(1u, w2 >> 31);
    }

    /// <summary>Builds a sequenced packet with a raw PLAINTEXT body (no Munge2), as sent
    /// by plaintext-netchan engine branches (Sven Co-op).</summary>
    private static byte[] MakePlainInlinePacket(uint seq, uint ack, byte[] body, bool reliable = false)
    {
        uint w1 = seq & MessageConstants.SequenceMask;
        if (reliable)
            w1 |= MessageConstants.SequenceFlagReliable;
        uint w2 = (ack & MessageConstants.SequenceMask) | (ack << 31);
        byte[] packet = new byte[MessageConstants.ConnectedHeadSize + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0), w1);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), w2);
        body.CopyTo(packet, MessageConstants.ConnectedHeadSize);
        return packet;
    }

    /// <summary>Builds a fragment-stream packet with 32-bit startpos/length fields, the
    /// layout the Sven Co-op engine emits.</summary>
    private static byte[] MakeSvenFragmentPacket(uint seq, uint ack, uint fragId, int startpos, byte[] chunk)
    {
        uint w1 = (seq & MessageConstants.SequenceMask)
                  | MessageConstants.SequenceFlagReliable
                  | MessageConstants.SequenceFlagFragment;
        uint w2 = (ack & MessageConstants.SequenceMask) | (ack << 31);
        var body = new List<byte>
        {
            0x01 // stream 0 active
        };
        body.AddRange(BitConverter.GetBytes(fragId));
        body.AddRange(BitConverter.GetBytes(startpos));
        body.AddRange(BitConverter.GetBytes(chunk.Length));
        body.Add(0x00); // stream 1 inactive
        body.AddRange(chunk);

        byte[] packet = new byte[MessageConstants.ConnectedHeadSize + body.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0), w1);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), w2);
        ((byte[])[.. body]).CopyTo(packet, MessageConstants.ConnectedHeadSize);
        return packet;
    }

    [Fact]
    public void SvenFragmentHeader_With32BitFields_IsReassembled()
    {
        // Wire-verified against a real Sven Co-op 5.26 (build 10257) server: the first
        // signon fragment announces startpos=0, length=43 as 32-bit fields. Under the
        // Valve 16-bit layout the same bytes parse as length=0 and the signon stalls.
        var (channel, _) = CreateChannel(useEncryption: false, longFragmentFields: true);
        List<byte[]> received = [];

        byte[] chunk = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66];
        channel.ProcessIncoming(MakeSvenFragmentPacket(seq: 1, ack: 2, fragId: (1u << 16) | 1, startpos: 0, chunk), received.Add);

        Assert.Equal([chunk], received);
    }

    [Fact]
    public void SvenFragmentHeader_ValveParsing_RejectsAndIgnores()
    {
        // The same wire bytes must NOT reassemble under the Valve layout (the parse
        // fails validation and the packet is dropped), matching the pre-fix behavior.
        var (channel, _) = CreateChannel(useEncryption: false, longFragmentFields: false);
        List<byte[]> received = [];

        byte[] chunk = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66];
        channel.ProcessIncoming(MakeSvenFragmentPacket(seq: 1, ack: 2, fragId: (1u << 16) | 1, startpos: 0, chunk), received.Add);

        Assert.Empty(received);
    }

    [Fact]
    public void EncryptionDisabled_RoundTripsPayloadsUnchanged()
    {
        // Sven Co-op netchan: both ends send plaintext; the channel must neither
        // decrypt received payloads nor encrypt outgoing ones.
        var (sender, senderSent) = CreateChannel(useEncryption: false);
        var (receiver, receiverSent) = CreateChannel(useEncryption: false);
        List<byte[]> received = [];

        sender.SendReliable([(byte)'n', (byte)'e', (byte)'w', 0]);
        var packet = Assert.Single(senderSent);
        byte[] body = packet[MessageConstants.ConnectedHeadSize..];
        Assert.Equal([(byte)'n', (byte)'e', (byte)'w', (byte)0], body[..4]);

        receiver.ProcessIncoming(packet, received.Add);
        var message = Assert.Single(received);
        Assert.Equal([(byte)'n', (byte)'e', (byte)'w', (byte)0], message[..4]);
        Assert.Equal((byte)ServerMessageType.Nop, message[4]);

        // An incoming plaintext packet from the server is dispatched verbatim too —
        // an UnMunge2 pass would scramble the first bytes.
        receiver.ProcessIncoming(MakePlainInlinePacket(seq: 2, ack: 1, body: [0x02, (byte)'h', (byte)'i', 0], reliable: true), received.Add);
        Assert.Equal(2, received.Count);
        Assert.Equal([0x02, (byte)'h', (byte)'i', 0], received[1][..4]);
    }
}
