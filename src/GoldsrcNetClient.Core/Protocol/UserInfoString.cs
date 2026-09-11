using System.Text;

namespace GoldsrcNetClient.Core.Protocol;

/// <summary>
/// GoldSrc backslash-delimited userinfo string (<c>\key\value\key\value\...</c>).
/// Parses, edits, and serializes the string sent in the connect handshake and
/// updated by the server via <see cref="ServerMessageType.UpdateUserInfo"/>.
/// Keys are matched case-insensitively, mirroring the engine's userinfo handling.
/// </summary>
public sealed class UserInfoString
{
    private readonly Dictionary<string, string> _values;

    /// <summary>Initializes an empty userinfo string.</summary>
    public UserInfoString() : this("") { }

    /// <summary>Parses an existing userinfo string; null or empty yields no pairs.</summary>
    public UserInfoString(string? raw) => _values = Parse(raw);

    /// <summary>The parsed pairs, in insertion order.</summary>
    public IReadOnlyDictionary<string, string> Values => _values;

    /// <summary>Gets a value by key (case-insensitive); <c>null</c> when absent.</summary>
    public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

    /// <summary>Sets or adds a key-value pair (key matching is case-insensitive).</summary>
    public void Set(string key, string value) => _values[key] = value;

    /// <summary>Serializes back to the wire format: one backslash before every key and value.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in _values)
            sb.Append('\\').Append(key).Append('\\').Append(value);
        return sb.ToString();
    }

    /// <summary>Splits a raw userinfo string into an ordered, case-insensitive dictionary.</summary>
    public static Dictionary<string, string> Parse(string? raw)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(raw))
            return values;

        var parts = raw.Split('\\');
        for (int i = 1; i + 1 < parts.Length; i += 2)
            values[parts[i]] = parts[i + 1];
        return values;
    }
}
