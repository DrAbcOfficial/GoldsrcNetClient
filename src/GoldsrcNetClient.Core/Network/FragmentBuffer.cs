using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Core.Network;

/// <summary>
/// Reassembly buffer for GoldSrc split-packet (fragmented) messages.
/// Stores incoming fragments and exposes a callback invoked when reassembly completes.
/// Useful for manual fragment reassembly outside the built-in connection pipeline.
/// </summary>
public class FragmentBuffer
{
    /// <summary>The accumulated fragment data.</summary>
    public List<byte> Data { get; private set; } = [];

    /// <summary>Current write offset within the reassembled data.</summary>
    public int Offset { get; set; }

    /// <summary>Whether a fragment stream is currently active.</summary>
    public bool Active { get; set; }

    /// <summary>Callback invoked when the fragment stream completes.</summary>
    public Action? Callback { get; set; }

    /// <summary>
    /// Starts a new fragment stream, clearing any previous data.
    /// </summary>
    /// <param name="cb">Optional completion callback.</param>
    public void Start(Action? cb = null)
    {
        Active = true;
        Data.Clear();
        Offset = 0;
        Callback = cb;
    }

    /// <summary>Appends raw fragment data to the reassembly buffer.</summary>
    /// <param name="fragData">The fragment payload bytes.</param>
    public void AddData(byte[] fragData)
    {
        Data.AddRange(fragData);
        Offset += fragData.Length;
    }

    /// <summary>Resets the buffer, deactivating the fragment stream.</summary>
    public void Reset()
    {
        Active = false;
        Data.Clear();
        Offset = 0;
        Callback = null;
    }
}
