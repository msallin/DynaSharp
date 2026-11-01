namespace DynaSharp;

/// <summary>
/// The data type of an <see cref="AttributeValue"/>, one per DynamoDB type descriptor.
/// </summary>
public enum AttributeType
{
    /// <summary>No type. Only a <see langword="default"/> <see cref="AttributeValue"/> has it; DynamoDB never returns it.</summary>
    None = 0,

    /// <summary>String, descriptor <c>S</c>.</summary>
    String,

    /// <summary>Number, descriptor <c>N</c>. Transferred as text.</summary>
    Number,

    /// <summary>Binary, descriptor <c>B</c>. Transferred as base64.</summary>
    Binary,

    /// <summary>Boolean, descriptor <c>BOOL</c>.</summary>
    Boolean,

    /// <summary>Null, descriptor <c>NULL</c>.</summary>
    Null,

    /// <summary>List of attribute values of any type, descriptor <c>L</c>.</summary>
    List,

    /// <summary>Map from attribute name to attribute value, descriptor <c>M</c>.</summary>
    Map,

    /// <summary>Set of strings, descriptor <c>SS</c>.</summary>
    StringSet,

    /// <summary>Set of numbers, descriptor <c>NS</c>.</summary>
    NumberSet,

    /// <summary>Set of binary values, descriptor <c>BS</c>.</summary>
    BinarySet,
}
