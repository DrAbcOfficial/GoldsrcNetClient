namespace GoldsrcNetClient.Core.Protocol;

public enum PacketType
{
    Connectionless = -1,
    Split = -2,
    Connected = 0
}

public enum SessionState
{
    Begin,
    GetChallenge,
    Connect0,
    Connected
}

public enum ServerMessageType : byte
{
    Bad = 0x00,
    Nop = 0x01,
    Disconnect = 0x02,
    Event = 0x03,
    Version = 0x04,
    SetView = 0x05,
    Sound = 0x06,
    Time = 0x07,
    Print = 0x08,
    StuffText = 0x09,
    SetAngle = 0x0A,
    ServerInfo = 0x0B,
    LightStyle = 0x0C,
    UpdateUserInfo = 0x0D,
    DeltaDescription = 0x0E,
    ClientData = 0x0F,
    StopSound = 0x10,
    Pings = 0x11,
    Particle = 0x12,
    Damage = 0x13,
    SpawnStatic = 0x14,
    EventReliable = 0x15,
    SpawnBaseline = 0x16,
    TempEntity = 0x17,
    SetPause = 0x18,
    SignOnNum = 0x19,
    CenterPrint = 0x1A,
    KilledMonster = 0x1B,
    FoundSecret = 0x1C,
    SpawnStaticSound = 0x1D,
    Intermission = 0x1E,
    Finale = 0x1F,
    CdTrack = 0x20,
    Restore = 0x21,
    Cutscene = 0x22,
    WeaponAnim = 0x23,
    DecalName = 0x24,
    RoomType = 0x25,
    AddAngle = 0x26,
    NewUserMsg = 0x27,
    PacketEntities = 0x28,
    DeltaPacketEntities = 0x29,
    Choke = 0x2A,
    ResourceList = 0x2B,
    NewMoveVars = 0x2C,
    ResourceRequest = 0x2D,
    Customization = 0x2E,
    CrosshairAngle = 0x2F,
    SoundFade = 0x30,
    FileTxferFailed = 0x31,
    Hltv = 0x32,
    Director = 0x33,
    VoiceInit = 0x34,
    VoiceData = 0x35,
    SendExtraInfo = 0x36,
    TimeScale = 0x37,
    ResourceLocation = 0x38,
    SendCvarValue = 0x39,
    SendCvarValue2 = 0x3A,
    Exec = 0x3B,
    UserMessageStart = 0x40
}

public enum ClientCommandType : byte
{
    Bad = 0x00,
    Nop = 0x01,
    Move = 0x02,
    StringCmd = 0x03,
    Delta = 0x04,
    ResourceList = 0x05,
    TMove = 0x06,
    FileConsistency = 0x07,
    VoiceData = 0x08,
    Hltv = 0x09,
    CvarValue = 0x0A,
    CvarValue2 = 0x0B
}

public enum DeltaFieldFlag : uint
{
    Byte = 1u << 0,
    Short = 1u << 1,
    Float = 1u << 2,
    Integer = 1u << 3,
    Angle = 1u << 4,
    TimeWindow8 = 1u << 5,
    TimeWindowBig = 1u << 6,
    StringField = 1u << 7,
    Signed = 1u << 31
}

public enum EntityType : byte
{
    Normal = 1 << 0,
    Beam = 1 << 1
}

public enum ResourceFlag : byte
{
    FatalIfMissing = 1 << 0,
    WasMissing = 1 << 1,
    Custom = 1 << 2,
    Requested = 1 << 3,
    Precached = 1 << 4,
    Always = 1 << 5,
    Unk6 = 1 << 6,
    CheckFile = 1 << 7
}

public static class SoundFlags
{
    public const ushort Volume = 1 << 0;
    public const ushort Attenuation = 1 << 1;
    public const ushort LargeIndex = 1 << 2;
    public const ushort Pitch = 1 << 3;
    public const ushort Sentence = 1 << 4;
    public const ushort Stop = 1 << 5;
    public const ushort ChangeVol = 1 << 6;
    public const ushort ChangePitch = 1 << 7;
    public const ushort Spawning = 1 << 8;
}
