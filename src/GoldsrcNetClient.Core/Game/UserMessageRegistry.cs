using System.Text;
using GoldsrcNetClient.Core.Messages;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Core.Game;

/// <summary>
/// Tracks user message registrations received via <see cref="ServerMessageType.NewUserMsg"/> (SVC_NEWUSERMSG).
/// Maps user message indices to their names so that handlers can dispatch by message name.
/// </summary>
public sealed class UserMessageRegistry
{
    private readonly Dictionary<byte, string> _messages = [];

    /// <summary>Number of registered messages.</summary>
    public int Count => _messages.Count;

    /// <summary>
    /// Clears all registered messages. Call when a new connection is established.
    /// </summary>
    public void Clear() => _messages.Clear();

    /// <summary>
    /// Registers a user message from the raw <see cref="ServerMessageType.NewUserMsg"/> payload.
    /// Advances the reader past the consumed bytes.
    /// </summary>
    public void Register(MessageReader reader)
    {
        int msgSize;
        unsafe { msgSize = sizeof(NewUserMsgData); }
        if (reader.Remaining < msgSize) return;

        unsafe
        {
            fixed (byte* p = &reader.Data[reader.Offset])
            {
                var msg = *(NewUserMsgData*)p;
                reader.Offset += msgSize;

                int len = 0;
                while (len < 16 && msg.NameData[len] != 0) len++;
                var name = Encoding.UTF8.GetString(msg.NameData, len);

                if (name.Length > 0)
                    _messages[msg.Index] = name;
            }
        }
    }

    /// <summary>
    /// Gets the user message name for a given index.
    /// </summary>
    /// <returns>The message name, or null if not registered.</returns>
    public string? GetName(byte index) =>
        _messages.TryGetValue(index, out var name) ? name : null;

    /// <summary>
    /// Tries to get the user message name for a given index.
    /// </summary>
    public bool TryGetName(byte index, out string? name) =>
        _messages.TryGetValue(index, out name);
}
