using System.Runtime.InteropServices;

namespace GoldsrcNetClient.Core.Protocol;

/// <summary>
/// Server info data sent during the initial connection handshake.
/// Marshalled directly from the raw packet bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ServerInfoData
{
    /// <summary>Network protocol version the server is running.</summary>
    public uint ProtocolVersion;
    /// <summary>Number of map changes / spawn cycles since the server started.</summary>
    public uint SpawnCount;
    /// <summary>Encrypted worldmap CRC (must be decrypted with <see cref="Munge.MungeEngine.UnMunge3"/>).</summary>
    public uint Munge3WorldmapCrc;
    /// <summary>MD5 hash of the server's client.dll (16 bytes).</summary>
    public unsafe fixed byte Md5ClientDll[16];
    /// <summary>Maximum number of clients allowed on the server.</summary>
    public byte MaxClients;
    /// <summary>This client's player slot index (0-based).</summary>
    public byte PlayerNumber;
    /// <summary>Unknown/padding byte.</summary>
    public byte Unknown0;
}

/// <summary>
/// Movement physics variables sent by the server.
/// Marshalled directly from the raw packet bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct NewMoveVarsData
{
    /// <summary>Gravity multiplier.</summary>
    public float Gravity;
    /// <summary>Speed at which the player stops when no input is given.</summary>
    public float StopSpeed;
    /// <summary>Maximum movement speed.</summary>
    public float MaxSpeed;
    /// <summary>Maximum spectator movement speed.</summary>
    public float SpectatorMaxSpeed;
    /// <summary>Ground acceleration.</summary>
    public float Accelerate;
    /// <summary>Air acceleration.</summary>
    public float AirAccelerate;
    /// <summary>Water acceleration.</summary>
    public float WaterAccelerate;
    /// <summary>Ground friction.</summary>
    public float Friction;
    /// <summary>Edge friction.</summary>
    public float EdgeFriction;
    /// <summary>Water friction.</summary>
    public float WaterFriction;
    /// <summary>Entity gravity multiplier.</summary>
    public float EntGravity;
    /// <summary>Bounce factor.</summary>
    public float Bounce;
    /// <summary>Maximum step height.</summary>
    public float StepSize;
    /// <summary>Maximum velocity cap.</summary>
    public float MaxVelocity;
    /// <summary>Maximum Z coordinate.</summary>
    public float ZMax;
    /// <summary>Wave height for water surfaces.</summary>
    public float WaveHeight;
    /// <summary>Whether footstep sounds are enabled.</summary>
    public byte Footsteps;
    /// <summary>Roll angle for screen tilt effects.</summary>
    public float RollAngle;
    /// <summary>Roll speed.</summary>
    public float RollSpeed;
    /// <summary>Sky color red component.</summary>
    public float SkyColorR;
    /// <summary>Sky color green component.</summary>
    public float SkyColorG;
    /// <summary>Sky color blue component.</summary>
    public float SkyColorB;
    /// <summary>Sky vector X component.</summary>
    public float SkyVecX;
    /// <summary>Sky vector Y component.</summary>
    public float SkyVecY;
    /// <summary>Sky vector Z component.</summary>
    public float SkyVecZ;
}

/// <summary>
/// Registration data for a new user message type.
/// Marshalled directly from the raw packet bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct NewUserMsgData
{
    /// <summary>User message index.</summary>
    public byte Index;
    /// <summary>Size of the message data in bytes (or 0xFF for variable size).</summary>
    public byte Size;
    /// <summary>Message name (up to 16 bytes, null-terminated).</summary>
    public unsafe fixed byte NameData[16];
}

/// <summary>
/// Header for a single fragment within a split packet.
/// Marshalled directly from the raw packet bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct FragHead
{
    /// <summary>Total number of fragments in this stream.</summary>
    public ushort To;
    /// <summary>Index of this fragment (0-based).</summary>
    public ushort At;
    /// <summary>Byte offset where this fragment's data starts in the reassembled message.</summary>
    public ushort StartPosition;
    /// <summary>Size of this fragment's payload in bytes.</summary>
    public ushort Size;
}

/// <summary>
/// Describes a single resource (map, model, sound, etc.) in the server's resource list.
/// </summary>
public struct ResourceInfo
{
    /// <summary>Resource filename/path as raw bytes.</summary>
    public byte[] Name;
    /// <summary>Resource flags (see <see cref="ResourceFlag"/>).</summary>
    public byte Flag;
    /// <summary>Optional MD5 hash for custom resources (16 bytes).</summary>
    public byte[] Md5;
    /// <summary>Optional reserved data (32 bytes).</summary>
    public byte[] Reserved;
    /// <summary>Whether the server requires a consistency check for this file.</summary>
    public bool NeedConsistency;

    /// <summary>Initializes a new <see cref="ResourceInfo"/> with default empty arrays.</summary>
    public ResourceInfo()
    {
        Name = [];
        Flag = 0;
        Md5 = new byte[16];
        Reserved = new byte[32];
        NeedConsistency = false;
    }
}

/// <summary>
/// Describes a single field in a delta-compressed data type.
/// </summary>
public struct DeltaField
{
    /// <summary>Field name (e.g. "origin[0]", "health").</summary>
    public string FieldName;
    /// <summary>Bit flags describing the field's type and encoding.</summary>
    public DeltaFieldFlag FieldFlag;
    /// <summary>Number of bits used to encode this field.</summary>
    public byte Bits;
    /// <summary>Multiplier applied to the decoded value.</summary>
    public float Multiplier;

    /// <summary>Creates a new delta field descriptor.</summary>
    /// <param name="fieldName">Field name string.</param>
    /// <param name="fieldFlag">Type and encoding flags.</param>
    /// <param name="bits">Bit count for the encoded value.</param>
    /// <param name="multiplier">Decoding multiplier.</param>
    public DeltaField(string fieldName, DeltaFieldFlag fieldFlag, byte bits, float multiplier)
    {
        FieldName = fieldName;
        FieldFlag = fieldFlag;
        Bits = bits;
        Multiplier = multiplier;
    }
}

/// <summary>
/// Describes a complete delta-compressed data type (entity state, client data, etc.).
/// </summary>
public struct DeltaType
{
    /// <summary>Name of this delta type (e.g. "entity_state_t", "clientdata_t").</summary>
    public string DeltaName;
    /// <summary>Number of fields in <see cref="Fields"/>.</summary>
    public byte FieldAmount;
    /// <summary>Array of field descriptors.</summary>
    public DeltaField[] Fields;

    /// <summary>Creates a new delta type descriptor.</summary>
    /// <param name="deltaName">Type name string.</param>
    /// <param name="fieldAmount">Number of fields.</param>
    /// <param name="fields">Array of field descriptors.</param>
    public DeltaType(string deltaName, byte fieldAmount, DeltaField[] fields)
    {
        DeltaName = deltaName;
        FieldAmount = fieldAmount;
        Fields = fields;
    }
}

/// <summary>
/// Represents a registered user message type from the server.
/// </summary>
public struct UserMessage
{
    /// <summary>Size of the message data (or 0xFF for variable size).</summary>
    public byte Size;
    /// <summary>Message name as raw bytes (up to 16).</summary>
    public byte[] Name;

    /// <summary>Initializes a new <see cref="UserMessage"/> with default values.</summary>
    public UserMessage()
    {
        Size = 0;
        Name = new byte[16];
    }
}
