using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Core.Network;

public class FragmentBuffer
{
    public List<byte> Data { get; private set; } = [];
    public int Offset { get; set; }
    public bool Active { get; set; }
    public Action? Callback { get; set; }

    public void Start(Action? cb = null)
    {
        Active = true;
        Data.Clear();
        Offset = 0;
        Callback = cb;
    }

    public void AddData(byte[] fragData)
    {
        Data.AddRange(fragData);
        Offset += fragData.Length;
    }

    public void Reset()
    {
        Active = false;
        Data.Clear();
        Offset = 0;
        Callback = null;
    }
}
