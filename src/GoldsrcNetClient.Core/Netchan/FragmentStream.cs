namespace GoldsrcNetClient.Core.Netchan;

/// <summary>
/// Reassembly state for one netchan fragment stream. GoldSrc fragments must
/// arrive with sequentially increasing buffer ids starting at 1; out-of-order
/// fragments invalidate the whole transfer so the sender's retransmission can
/// refill it.
/// </summary>
public sealed class FragmentStream
{
    private readonly List<byte[]> _chunks = [];

    /// <summary>Whether a fragment transfer is currently being reassembled on this stream.</summary>
    public bool Active { get; private set; }

    /// <summary>Total number of fragments expected for the current transfer.</summary>
    public int TotalCount { get; private set; }

    /// <summary>True when all expected fragments have arrived.</summary>
    public bool IsComplete => TotalCount > 0 && _chunks.Count >= TotalCount;

    /// <summary>
    /// Stores one fragment chunk. The fragid packs the fragment id in the high
    /// 16 bits and the transfer's total fragment count in the low 16 bits; a new
    /// total count restarts the transfer, gaps are dropped until retransmission.
    /// </summary>
    public void Push(uint fragId, byte[] chunk)
    {
        int id = (int)((fragId >> 16) & 0xFFFF);
        int totalCount = (int)(fragId & 0xFFFF);

        if (!Active || TotalCount != totalCount)
        {
            Reset();
            Active = true;
            TotalCount = totalCount;
        }

        if (id < 1 || totalCount < 1 || id > totalCount)
            return;

        if (id == _chunks.Count + 1)
            _chunks.Add(chunk);
        else if (id <= _chunks.Count)
            _chunks[id - 1] = chunk; // duplicate retransmission: overwrite
    }

    /// <summary>Concatenates the received chunks in id order.</summary>
    public byte[] Assemble()
    {
        int total = 0;
        foreach (var chunk in _chunks)
            total += chunk.Length;

        var assembled = new byte[total];
        int pos = 0;
        foreach (var chunk in _chunks)
        {
            Array.Copy(chunk, 0, assembled, pos, chunk.Length);
            pos += chunk.Length;
        }
        return assembled;
    }

    /// <summary>Clears the reassembly state.</summary>
    public void Reset()
    {
        Active = false;
        TotalCount = 0;
        _chunks.Clear();
    }
}
