namespace GoldsrcNetClient.Core.Protocol;

/// <summary>
/// Wire-format dialect of one GoldSrc engine branch. All protocol machinery
/// (netchan, server-message parsers, signon flow) consumes this abstraction
/// instead of branching on game identities, inverting the dependency between
/// the transport layer and the game/mod layer: supporting a new engine branch
/// means implementing (or configuring) this interface and exposing it from an
/// <see cref="GoldsrcNetClient.Core.Game.IGameProfile"/> (its
/// <see cref="GoldsrcNetClient.Core.Game.IGameProfile.EngineVariant"/> property) —
/// no changes inside the protocol code.
/// </summary>
public interface IEngineVariant
{
    /// <summary>
    /// Whether netchan payloads are Munge2-encrypted with the sequence's low byte
    /// as key. Standard Valve GoldSrc branches call COM_Munge2/COM_UnMunge2
    /// unconditionally in the netchan; the Sven Co-op branch ships the munge code
    /// with zero call sites (verified in hw.dll — tables present as dead code), so
    /// its netchan traffic is plaintext in both directions. Encrypting towards a
    /// Sven server yields <c>Bad command character in client command</c> kicks;
    /// not decrypting a Valve-server stream yields garbage payloads.
    /// </summary>
    bool NetchanEncryption { get; }

    /// <summary>
    /// Netchan fragment headers carry 32-bit startpos/length fields. The Sven
    /// Co-op branch widened these from Valve's 16-bit fields (wire-verified: a
    /// 1-of-1 fragment announcing its 43-byte BZ2-wrapped serverinfo parses as a
    /// zero-length fragment under the Valve layout).
    /// </summary>
    bool LongFragmentFields { get; }

    /// <summary>
    /// Bitstream coordinates are Sven's raw 32-bit 16.16 fixed point (maps up to
    /// ±32768 units); false for Valve's 18-bit bit coordinate (±4096).
    /// </summary>
    bool WideCoordinates { get; }

    /// <summary>
    /// The serverinfo worldmap CRC is the plaintext true CRC (Sven; the real
    /// client echoes it back verbatim in the spawn command). False for Valve,
    /// which Munge3-encrypts it with key <c>(-1 - playernum) &amp; 0xFF</c> —
    /// un-munging a plaintext CRC (or the reverse) fails the server's periodic
    /// SV_CheckMapDifferences and gets the connection dropped as a fake
    /// "Reliable channel overflowed".
    /// </summary>
    bool PlaintextWorldmapCrc { get; }

    /// <summary>
    /// The <c>spawn &lt;count&gt; &lt;crc&gt;</c> command carries the worldmap CRC
    /// as-is (Sven). False for Valve, which re-munges it with key
    /// <c>(-1 - spawncount) &amp; 0xFF</c>.
    /// </summary>
    bool PlaintextSpawnCrc { get; }

    /// <summary>
    /// The legacy <c>svc_foundsecret</c> slot carries a string payload (Sven's
    /// engine repurposes it inside SV_New_f, written before the serverinfo
    /// banner); on Valve branches the slot is unused and carries no payload.
    /// </summary>
    bool FoundSecretHasString { get; }

    /// <summary>Width of the delta bitmap byte-count prefix (Valve 3 bits,
    /// Sven 4 — DELTA_WriteDelta @ hw.dll 0x1d44f10, Ghidra-verified).</summary>
    int DeltaByteCountBits { get; }

    /// <summary>Entity-index bit width in spawn baselines and svc_sound
    /// (Valve 11 bits = 2048 edicts, Sven 13 bits = 8192).</summary>
    int EntityIndexBits { get; }

    /// <summary>svc_resourcelist resource-count field width (Valve 12, Sven 16).</summary>
    int ResourceIndexBits { get; }

    /// <summary>svc_resourcelist consistency absolute-index field width (Valve 10, Sven 16).</summary>
    int ConsistencyIndexBits { get; }
}

/// <summary>
/// Configurable <see cref="IEngineVariant"/> implementation with Valve-GoldSrc
/// defaults. Compose a custom engine-branch dialect by setting only the
/// properties that differ:
/// <code>
/// var myBranch = new EngineVariant { NetchanEncryption = false, EntityIndexBits = 13 };
/// </code>
/// </summary>
public sealed class EngineVariant : IEngineVariant
{
    /// <inheritdoc />
    public bool NetchanEncryption { get; init; } = true;

    /// <inheritdoc />
    public bool LongFragmentFields { get; init; }

    /// <inheritdoc />
    public bool WideCoordinates { get; init; }

    /// <inheritdoc />
    public bool PlaintextWorldmapCrc { get; init; }

    /// <inheritdoc />
    public bool PlaintextSpawnCrc { get; init; }

    /// <inheritdoc />
    public bool FoundSecretHasString { get; init; }

    /// <inheritdoc />
    public int DeltaByteCountBits { get; init; } = 3;

    /// <inheritdoc />
    public int EntityIndexBits { get; init; } = 11;

    /// <inheritdoc />
    public int ResourceIndexBits { get; init; } = 12;

    /// <inheritdoc />
    public int ConsistencyIndexBits { get; init; } = 10;
}

/// <summary>The built-in engine-branch dialects.</summary>
public static class EngineVariants
{
    /// <summary>Standard Valve GoldSrc branch (Half-Life, Counter-Strike, ReHLDS).</summary>
    public static readonly EngineVariant Valve = new();

    /// <summary>Sven Co-op engine branch (build 10257): plaintext netchan,
    /// widened fragment/resource/entity fields, wide coordinates.</summary>
    public static readonly EngineVariant SvenCoop = new()
    {
        NetchanEncryption = false,
        LongFragmentFields = true,
        WideCoordinates = true,
        PlaintextWorldmapCrc = true,
        PlaintextSpawnCrc = true,
        FoundSecretHasString = true,
        DeltaByteCountBits = 4,
        EntityIndexBits = 13,
        ResourceIndexBits = 16,
        ConsistencyIndexBits = 16,
    };
}
