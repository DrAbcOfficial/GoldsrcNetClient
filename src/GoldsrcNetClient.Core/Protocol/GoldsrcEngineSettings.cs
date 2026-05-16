namespace GoldsrcNetClient.Core.Protocol;

/// <summary>
/// Configurable engine behavior settings for a <see cref="Network.GoldsrcConnection"/>.
/// Modify these values before or during a connection to customize protocol behavior.
/// </summary>
public class GoldsrcEngineSettings
{
    /// <summary>Network protocol version to use. Default 48 (compatible with HL25/GoldSrc).</summary>
    public int ProtocolVersion { get; set; } = 48;

    /// <summary>Interval in milliseconds between move/keepalive packet sends. Default 100ms.</summary>
    public int MoveIntervalMs { get; set; } = 100;

    /// <summary>Default UserInfo string template. Uses GoldSrc backslash-delimited key-value format.</summary>
    public string DefaultUserInfo { get; set; } =
        "\\name\\GoldsrcNetClient\\protocol\\48\\cl_lc\\1\\cl_lw\\1\\cl_updaterate\\60\\rate\\20000\\hltv\\0";

    /// <summary>Default values returned when the server queries cvar values via SendCvarValue/SendCvarValue2.</summary>
    public Dictionary<string, string> CvarDefaultValues { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cl_lc"] = "1",
        ["cl_lw"] = "1",
        ["cl_updaterate"] = "1",
        ["rate"] = "20000",
        ["name"] = "GoldsrcNetClient",
        ["topcolor"] = "0",
        ["bottomcolor"] = "0",
        ["model"] = "gordon",
        ["_cl_autowepswitch"] = "1",
        ["cl_dlmax"] = "80",
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
