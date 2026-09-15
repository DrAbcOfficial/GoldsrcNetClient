using GoldsrcNetClient.Core.Io;
using System.Text;

namespace GoldsrcNetClient.Core.Game;

/// <summary>
/// Tracks user message registrations received via <see cref="GoldsrcNetClient.Core.Protocol.ServerMessageType.NewUserMsg"/> (SVC_NEWUSERMSG).
/// Maps user message indices to their names and declared payload sizes so that handlers
/// can dispatch by message name and consume exactly the announced payload length.
/// </summary>
public sealed class UserMessageRegistry
{
    private readonly Dictionary<byte, UserMessageRegistration> _messages = [];

    /// <summary>Number of registered messages.</summary>
    public int Count => _messages.Count;

    /// <summary>
    /// Registers a user message directly (used when replaying a
    /// <c>NewUserMsgMessage</c> into the runtime registry).
    /// </summary>
    public void Register(byte index, string name, byte declaredSize)
    {
        if (name.Length > 0)
            _messages[index] = new UserMessageRegistration(name, declaredSize);
    }

    /// <summary>
    /// Clears all registered messages. Call when a new connection is established.
    /// </summary>
    public void Clear() => _messages.Clear();

    /// <summary>
    /// Registers a user message from the raw <see cref="GoldsrcNetClient.Core.Protocol.ServerMessageType.NewUserMsg"/> payload.
    /// Advances the reader past the consumed bytes.
    /// </summary>
    public unsafe void Register(ref BufferReader reader)
    {
        var msg = reader.ReadStruct<Protocol.NewUserMsgData>();

        int len = 0;
        while (len < 16 && msg.NameData[len] != 0) len++;
        var name = Encoding.UTF8.GetString(msg.NameData, len);

        if (name.Length > 0)
            _messages[msg.Index] = new UserMessageRegistration(name, msg.Size);
    }

    /// <summary>
    /// Gets the user message name for a given index.
    /// </summary>
    /// <returns>The message name, or null if not registered.</returns>
    public string? GetName(byte index) =>
        _messages.TryGetValue(index, out var reg) ? reg.Name : null;

    /// <summary>
    /// Tries to get the user message name for a given index.
    /// </summary>
    public bool TryGetName(byte index, out string? name)
    {
        name = GetName(index);
        return name != null;
    }

    /// <summary>
    /// Tries to get the declared payload size for a user message index.
    /// Returns false when the index is unknown or the message is variable-length
    /// (registered with a size of 0 or 255).
    /// </summary>
    public bool TryGetSize(byte index, out int size)
    {
        size = 0;
        if (!_messages.TryGetValue(index, out var reg))
            return false;
        if (reg.Size == 0 || reg.Size == 0xFF)
            return false;
        size = reg.Size;
        return true;
    }

    /// <summary>
    /// Tries to get the raw declared size byte for a user message index as sent by
    /// <c>svc_newusermsg</c>: 1-254 = fixed payload size, 0 = fixed zero-size,
    /// 255 = variable length (a length byte precedes the payload on the wire).
    /// Returns false when the index is unknown.
    /// </summary>
    public bool TryGetDeclaredSize(byte index, out byte size)
    {
        if (!_messages.TryGetValue(index, out var reg))
        {
            size = 0;
            return false;
        }
        size = reg.Size;
        return true;
    }

    /// <summary>All registered user messages, ordered by index.</summary>
    public IEnumerable<(byte Index, string Name, byte Size)> Entries =>
        _messages.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value.Name, kv.Value.Size));

    private readonly record struct UserMessageRegistration(string Name, byte Size);
}
