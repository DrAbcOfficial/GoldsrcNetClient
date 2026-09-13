namespace GoldsrcNetClient.Core.Delta;

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
/// One field entry as transmitted inside a <c>svc_deltadescription</c> message.
/// The engine sends these delta-encoded against a fixed 7-entry meta table
/// (fieldType 32b, fieldName string, fieldOffset 16b, fieldSize 8b,
/// significant_bits 8b, premultiply/postmultiply 32b as value*4000).
/// </summary>
public struct DeltaFieldDescription
{
    /// <summary>Field name (e.g. "origin[0]").</summary>
    public string FieldName;
    /// <summary>Wire type flags (DT_* values, including the DT_SIGNED bit).</summary>
    public DeltaFieldFlag FieldType;
    /// <summary>Byte offset of the field inside the engine's struct (informational).</summary>
    public int FieldOffset;
    /// <summary>Byte size of the field inside the engine's struct.</summary>
    public int FieldSize;
    /// <summary>Number of significant bits used to encode this field on the wire.</summary>
    public int SignificantBits;
    /// <summary>Premultiply scaling factor (received as value*4000).</summary>
    public float Premultiply;
    /// <summary>Post-multiply scaling factor (received as value*4000).</summary>
    public float PostMultiply;

    /// <summary>Converts this wire description into a <see cref="DeltaField"/> for the skip-parser.</summary>
    public DeltaField ToDeltaField() => new(FieldName, FieldType, (byte)SignificantBits, Premultiply);
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
