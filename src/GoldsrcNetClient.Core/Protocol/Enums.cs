namespace GoldsrcNetClient.Core.Protocol;

/// <summary>Identifies the type of a received GoldSrc network packet based on its 4-byte header.</summary>
public enum PacketType
{
    /// <summary>Connectionless packet (header = 0xFFFFFFFF). Used for challenge/connect handshake.</summary>
    Connectionless = -1,
    /// <summary>Split/fragmented packet (header = 0xFFFFFFFE). Large messages split across multiple UDP datagrams.</summary>
    Split = -2,
    /// <summary>Connected (sequenced) packet. Used for all in-game communication after the handshake.</summary>
    Connected = 0
}

/// <summary>Connection session state machine steps.</summary>
public enum SessionState
{
    /// <summary>Initial state before DNS resolution.</summary>
    Begin,
    /// <summary>Waiting for the server's challenge response after sending getchallenge.</summary>
    GetChallenge,
    /// <summary>Connect packet sent; waiting for server approval (B-message).</summary>
    Connect0,
    /// <summary>Handshake complete; in-game communication active.</summary>
    Connected
}

/// <summary>Message types the server can send to the client over a connected channel.</summary>
public enum ServerMessageType : byte
{
    /// <summary>Bad/malformed message.</summary>
    Bad = 0x00,
    /// <summary>No operation; skip.</summary>
    Nop = 0x01,
    /// <summary>Server is disconnecting the client; followed by a reason string.</summary>
    Disconnect = 0x02,
    /// <summary>Game event notification.</summary>
    Event = 0x03,
    /// <summary>Protocol version negotiation.</summary>
    Version = 0x04,
    /// <summary>Set the client's view entity.</summary>
    SetView = 0x05,
    /// <summary>Play a sound at a location.</summary>
    Sound = 0x06,
    /// <summary>Current server time.</summary>
    Time = 0x07,
    /// <summary>Print a message to the client console.</summary>
    Print = 0x08,
    /// <summary>Execute console commands on the client ("stufftext").</summary>
    StuffText = 0x09,
    /// <summary>Set client view angles.</summary>
    SetAngle = 0x0A,
    /// <summary>Server info block: protocol, spawn count, worldmap CRC, player slot, etc.</summary>
    ServerInfo = 0x0B,
    /// <summary>Dynamic light style string.</summary>
    LightStyle = 0x0C,
    /// <summary>Update a client's userinfo key-value string.</summary>
    UpdateUserInfo = 0x0D,
    /// <summary>Delta description for a compressed data type.</summary>
    DeltaDescription = 0x0E,
    /// <summary>Client data delta (health, ammo, velocity, etc.).</summary>
    ClientData = 0x0F,
    /// <summary>Stop a playing sound.</summary>
    StopSound = 0x10,
    /// <summary>Server ping measurements.</summary>
    Pings = 0x11,
    /// <summary>Spawn a particle effect.</summary>
    Particle = 0x12,
    /// <summary>Damage indicator.</summary>
    Damage = 0x13,
    /// <summary>Spawn a static (non-moving) entity.</summary>
    SpawnStatic = 0x14,
    /// <summary>Reliable (TCP-like) game event.</summary>
    EventReliable = 0x15,
    /// <summary>Spawn baseline entities (initial full state for delta compression).</summary>
    SpawnBaseline = 0x16,
    /// <summary>Temporary entity (explosions, muzzle flashes, etc.).</summary>
    TempEntity = 0x17,
    /// <summary>Pause/unpause the game.</summary>
    SetPause = 0x18,
    /// <summary>Sign-on state number.</summary>
    SignOnNum = 0x19,
    /// <summary>Center-print a message on screen.</summary>
    CenterPrint = 0x1A,
    /// <summary>Killed monster notification.</summary>
    KilledMonster = 0x1B,
    /// <summary>Found secret notification.</summary>
    FoundSecret = 0x1C,
    /// <summary>Spawn a static sound source.</summary>
    SpawnStaticSound = 0x1D,
    /// <summary>Intermission screen.</summary>
    Intermission = 0x1E,
    /// <summary>Finale/ending sequence.</summary>
    Finale = 0x1F,
    /// <summary>CD audio track change.</summary>
    CdTrack = 0x20,
    /// <summary>Restore saved game state.</summary>
    Restore = 0x21,
    /// <summary>Play a cutscene.</summary>
    Cutscene = 0x22,
    /// <summary>Weapon animation event.</summary>
    WeaponAnim = 0x23,
    /// <summary>Decal name for bullet impacts.</summary>
    DecalName = 0x24,
    /// <summary>Room type (reverb).</summary>
    RoomType = 0x25,
    /// <summary>Add to view angles.</summary>
    AddAngle = 0x26,
    /// <summary>Register a new user message type.</summary>
    NewUserMsg = 0x27,
    /// <summary>Full entity state packet.</summary>
    PacketEntities = 0x28,
    /// <summary>Delta-compressed entity state packet.</summary>
    DeltaPacketEntities = 0x29,
    /// <summary>Explicit choke indication.</summary>
    Choke = 0x2A,
    /// <summary>Resource list (maps, models, sounds the server needs the client to have).</summary>
    ResourceList = 0x2B,
    /// <summary>New movement variables (gravity, friction, etc.).</summary>
    NewMoveVars = 0x2C,
    /// <summary>Server requests a specific resource from the client.</summary>
    ResourceRequest = 0x2D,
    /// <summary>Player customization (model, color).</summary>
    Customization = 0x2E,
    /// <summary>Crosshair angle.</summary>
    CrosshairAngle = 0x2F,
    /// <summary>Fade a playing sound.</summary>
    SoundFade = 0x30,
    /// <summary>File transfer failed notification.</summary>
    FileTxferFailed = 0x31,
    /// <summary>HLTV (Half-Life TV) spectator data.</summary>
    Hltv = 0x32,
    /// <summary>HLTV director commands.</summary>
    Director = 0x33,
    /// <summary>Voice chat codec initialization.</summary>
    VoiceInit = 0x34,
    /// <summary>Voice chat data packet.</summary>
    VoiceData = 0x35,
    /// <summary>Send extra spectator info.</summary>
    SendExtraInfo = 0x36,
    /// <summary>Time scale modification.</summary>
    TimeScale = 0x37,
    /// <summary>Resource download location.</summary>
    ResourceLocation = 0x38,
    /// <summary>Send a cvar value to the client.</summary>
    SendCvarValue = 0x39,
    /// <summary>Send cvar value version 2.</summary>
    SendCvarValue2 = 0x3A,
    /// <summary>Execute config/script on the client.</summary>
    Exec = 0x3B,
    /// <summary>USC (unified server config) message. Used for server-side user configurations.</summary>
    UscMessage = 0x3C,
    /// <summary>First user-registered message type. Messages at or above this value are game-specific.</summary>
    UserMessageStart = 0x40
}

/// <summary>Command types the client can send to the server.</summary>
public enum ClientCommandType : byte
{
    /// <summary>Bad command.</summary>
    Bad = 0x00,
    /// <summary>No-op.</summary>
    Nop = 0x01,
    /// <summary>Client movement input (usercmd_t).</summary>
    Move = 0x02,
    /// <summary>String command (console command sent as a string).</summary>
    StringCmd = 0x03,
    /// <summary>Delta update command.</summary>
    Delta = 0x04,
    /// <summary>Resource list request.</summary>
    ResourceList = 0x05,
    /// <summary>TMove (HLTV-related).</summary>
    TMove = 0x06,
    /// <summary>File consistency check response.</summary>
    FileConsistency = 0x07,
    /// <summary>Voice data from client.</summary>
    VoiceData = 0x08,
    /// <summary>HLTV command.</summary>
    Hltv = 0x09,
    /// <summary>Cvar value response.</summary>
    CvarValue = 0x0A,
    /// <summary>Cvar value version 2 response.</summary>
    CvarValue2 = 0x0B
}

/// <summary>Bit flags describing the type and encoding of a delta-compressed field.</summary>
public enum DeltaFieldFlag : uint
{
    /// <summary>Single-byte unsigned integer.</summary>
    Byte = 1u << 0,
    /// <summary>Two-byte unsigned integer.</summary>
    Short = 1u << 1,
    /// <summary>Floating-point value.</summary>
    Float = 1u << 2,
    /// <summary>Integer value (4 bytes).</summary>
    Integer = 1u << 3,
    /// <summary>Angle (special compression).</summary>
    Angle = 1u << 4,
    /// <summary>Time window with 8-bit precision.</summary>
    TimeWindow8 = 1u << 5,
    /// <summary>Time window with larger precision.</summary>
    TimeWindowBig = 1u << 6,
    /// <summary>Null-terminated string field.</summary>
    StringField = 1u << 7,
    /// <summary>Value is signed (OR'd with the type flag).</summary>
    Signed = 1u << 31
}

/// <summary>Entity type flags used in spawn baseline packets.</summary>
public enum EntityType : byte
{
    /// <summary>Standard entity.</summary>
    Normal = 1 << 0,
    /// <summary>Beam entity (laser, lightning).</summary>
    Beam = 1 << 1
}

/// <summary>Resource flags sent in the server's resource list.</summary>
public enum ResourceFlag : byte
{
    /// <summary>Disconnect if this resource is missing.</summary>
    FatalIfMissing = 1 << 0,
    /// <summary>Resource was missing at connect time.</summary>
    WasMissing = 1 << 1,
    /// <summary>Custom resource with MD5 hash.</summary>
    Custom = 1 << 2,
    /// <summary>Resource has been requested.</summary>
    Requested = 1 << 3,
    /// <summary>Resource is precached by the engine.</summary>
    Precached = 1 << 4,
    /// <summary>Always send this resource.</summary>
    Always = 1 << 5,
    /// <summary>Unknown flag 6.</summary>
    Unk6 = 1 << 6,
    /// <summary>Check file consistency.</summary>
    CheckFile = 1 << 7
}

/// <summary>Bitmask flags used in Sound message delta encoding.</summary>
public static class SoundFlags
{
    /// <summary>Volume field present.</summary>
    public const ushort Volume = 1 << 0;
    /// <summary>Attenuation field present.</summary>
    public const ushort Attenuation = 1 << 1;
    /// <summary>Sound index uses 16-bit encoding.</summary>
    public const ushort LargeIndex = 1 << 2;
    /// <summary>Pitch field present.</summary>
    public const ushort Pitch = 1 << 3;
    /// <summary>Sentence index (not raw sound).</summary>
    public const ushort Sentence = 1 << 4;
    /// <summary>Stop the sound.</summary>
    public const ushort Stop = 1 << 5;
    /// <summary>Change volume only (no restart).</summary>
    public const ushort ChangeVol = 1 << 6;
    /// <summary>Change pitch only (no restart).</summary>
    public const ushort ChangePitch = 1 << 7;
    /// <summary>Sound is spawned (not played from an entity).</summary>
    public const ushort Spawning = 1 << 8;
}
