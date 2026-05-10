using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Network;

namespace GoldsrcNetClient.Core.Game;

/// <summary>
/// Server message handler for Sven Co-op.
/// Extends <see cref="HalfLifeMessageHandler"/> with Sven Co-op specific user messages.
/// </summary>
/// <remarks>
/// <para>Sven Co-op uses most Half-Life Deathmatch user messages plus its own
/// co-op specific messages: Camera, CameraMouse, CbElec, CreateBlood, Fog,
/// GargSplash, Gib, SporeTrail, ToxicCloud.</para>
///
/// <para>For messages without documented structures, a generic event with the
/// message name and raw data is raised via <see cref="OnScSpecificMessage"/>.</para>
/// </remarks>
public class SvenCoopMessageHandler : HalfLifeMessageHandler
{
    #region SC-specific Events

    /// <summary>Raised when fog settings change (also available in CS).</summary>
    public event Action<FogEvent>? Fog;

    /// <summary>Raised for SC-specific messages with raw data when structure is unknown.</summary>
    public event Action<RawUserMessage>? OnScSpecificMessage;

    #endregion

    /// <inheritdoc />
    protected override bool DispatchUserMessage(GoldsrcConnection connection, byte index, string name, MessageReader reader)
    {
        switch (name)
        {
            case "Fog":            ParseFogSc(reader); return true;
            case "Camera":         ParseScRaw(reader, name); return true;
            case "CameraMouse":    ParseScRaw(reader, name); return true;
            case "CbElec":         ParseScRaw(reader, name); return true;
            case "CreateBlood":    ParseScRaw(reader, name); return true;
            case "GargSplash":     ParseScRaw(reader, name); return true;
            case "Gib":            ParseScRaw(reader, name); return true;
            case "SporeTrail":     ParseScRaw(reader, name); return true;
            case "ToxicCloud":     ParseScRaw(reader, name); return true;
            default: return base.DispatchUserMessage(connection, index, name, reader);
        }
    }

    /// <summary>Fog: byte R, G, B, Density</summary>
    protected virtual void ParseFogSc(MessageReader r)
    {
        var ev = new FogEvent(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
        Fog?.Invoke(ev);
    }

    /// <summary>Raises <see cref="OnScSpecificMessage"/> with the remaining data for unrecognized SC-specific messages.</summary>
    protected virtual void ParseScRaw(MessageReader r, string name)
    {
        byte[] data = new byte[r.Remaining];
        Array.Copy(r.Data, r.Offset, data, 0, data.Length);
        r.Offset = r.Size;
        var msg = new RawUserMessage(0, name, data);
        OnScSpecificMessage?.Invoke(msg);
    }
}
