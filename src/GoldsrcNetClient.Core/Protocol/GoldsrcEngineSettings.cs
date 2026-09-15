namespace GoldsrcNetClient.Core.Protocol;

/// <summary>
/// Configurable engine behavior settings for a <see cref="Network.GoldsrcConnection"/>.
/// Modify these values before or during a connection to customize protocol behavior.
/// </summary>
public class GoldsrcEngineSettings
{
    /// <summary>Canonical default userinfo template. Single source shared by
    /// <see cref="DefaultUserInfo"/> and <see cref="Game.BaseGameLoginProvider.DefaultUserInfo"/>:
    /// a high <c>rate</c> keeps the server's reliable fragment stream draining fast;
    /// the server clamps it to <c>sv_maxrate</c> anyway.</summary>
    public const string DefaultUserInfoTemplate =
        "\\name\\GoldsrcNetClient\\protocol\\48\\cl_lc\\1\\cl_lw\\1\\cl_dlmax\\1024\\cl_updaterate\\60\\rate\\100000\\hltv\\0";

    /// <summary>Network protocol version to use. Default 48 (compatible with HL25/GoldSrc).</summary>
    public int ProtocolVersion { get; set; } = 48;

    /// <summary>Diagnostic stream dump path. When set, every connected message stream
    /// is appended to this file as a length-prefixed record. The stream is opened
    /// lazily on the first dumped message, so the path can also be assigned right
    /// after construction; an open failure disables dumping for the connection.</summary>
    public string? MessageDumpPath { get; set; }

    /// <summary>Interval in milliseconds between move/keepalive packet sends. Default 10ms
    /// (~100 packets/s, matching a real client's frame cadence). Two server-side
    /// constraints make a steady, sub-50ms cadence mandatory: the server stops
    /// transmitting to a client it hasn't heard from within <c>sv_failuretime</c>
    /// (default 0.05 s), and its reliable channel advances at most one queued
    /// message per received client packet (netchan lock-step). A slow or jittery
    /// packet rate therefore throttles the reliable drain, <c>netchan.message</c>
    /// fills up, and the server drops us with <c>Reliable channel overflowed</c>.</summary>
    public int MoveIntervalMs { get; set; } = 10;

    /// <summary>Default UserInfo string template (initialized from <see cref="DefaultUserInfoTemplate"/>).
    /// Uses GoldSrc backslash-delimited key-value format.</summary>
    public string DefaultUserInfo { get; set; } = DefaultUserInfoTemplate;

    /// <summary>Default values returned when the server queries cvar values via SendCvarValue/SendCvarValue2.</summary>
    public Dictionary<string, string> CvarDefaultValues { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cl_lc"] = "1",
        ["cl_lw"] = "1",
        ["cl_updaterate"] = "60",
        ["rate"] = "100000",
        ["name"] = "GoldsrcNetClient",
        ["topcolor"] = "0",
        ["bottomcolor"] = "0",
        ["model"] = "gordon",
        ["_cl_autowepswitch"] = "1",
        ["cl_dlmax"] = "1024",
        ["hltv"] = "0",
    };

    /// <summary>Default fallback value when a cvar is not found in <see cref="CvarDefaultValues"/>.</summary>
    public string DefaultCvarFallback { get; set; } = "0";

    /// <summary>
    /// Resolves a default cvar value for the given name.
    /// Checks <see cref="CvarDefaultValues"/> first, then falls back to <see cref="DefaultCvarFallback"/>.
    /// </summary>
    /// <param name="name">Cvar name to look up.</param>
    /// <returns>The default value for the given cvar.</returns>
    public string GetDefaultCvarValue(string name)
    {
        if (CvarDefaultValues.TryGetValue(name, out var value))
            return value;
        return DefaultCvarFallback;
    }
}
