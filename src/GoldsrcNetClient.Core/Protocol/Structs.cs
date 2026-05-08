using System.Runtime.InteropServices;

namespace GoldsrcNetClient.Core.Protocol;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ServerInfoData
{
    public uint ProtocolVersion;
    public uint SpawnCount;
    public uint Munge3WorldmapCrc;
    public unsafe fixed byte Md5ClientDll[16];
    public byte MaxClients;
    public byte PlayerNumber;
    public byte Unknown0;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct NewMoveVarsData
{
    public float Gravity;
    public float StopSpeed;
    public float MaxSpeed;
    public float SpectatorMaxSpeed;
    public float Accelerate;
    public float AirAccelerate;
    public float WaterAccelerate;
    public float Friction;
    public float EdgeFriction;
    public float WaterFriction;
    public float EntGravity;
    public float Bounce;
    public float StepSize;
    public float MaxVelocity;
    public float ZMax;
    public float WaveHeight;
    public byte Footsteps;
    public float RollAngle;
    public float RollSpeed;
    public float SkyColorR;
    public float SkyColorG;
    public float SkyColorB;
    public float SkyVecX;
    public float SkyVecY;
    public float SkyVecZ;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct NewUserMsgData
{
    public byte Index;
    public byte Size;
    public unsafe fixed byte NameData[16];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct FragHead
{
    public ushort To;
    public ushort At;
    public ushort StartPosition;
    public ushort Size;
}

public struct ResourceInfo
{
    public byte[] Name;
    public byte Flag;
    public byte[] Md5;
    public byte[] Reserved;
    public bool NeedConsistency;

    public ResourceInfo()
    {
        Name = [];
        Flag = 0;
        Md5 = new byte[16];
        Reserved = new byte[32];
        NeedConsistency = false;
    }
}

public struct DeltaField
{
    public string FieldName;
    public DeltaFieldFlag FieldFlag;
    public byte Bits;
    public float Multiplier;

    public DeltaField(string fieldName, DeltaFieldFlag fieldFlag, byte bits, float multiplier)
    {
        FieldName = fieldName;
        FieldFlag = fieldFlag;
        Bits = bits;
        Multiplier = multiplier;
    }
}

public struct DeltaType
{
    public string DeltaName;
    public byte FieldAmount;
    public DeltaField[] Fields;

    public DeltaType(string deltaName, byte fieldAmount, DeltaField[] fields)
    {
        DeltaName = deltaName;
        FieldAmount = fieldAmount;
        Fields = fields;
    }
}

public struct UserMessage
{
    public byte Size;
    public byte[] Name;

    public UserMessage()
    {
        Size = 0;
        Name = new byte[16];
    }
}
