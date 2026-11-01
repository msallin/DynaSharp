namespace DynaSharp.Generators;

/// <summary>
/// How a key is stored, which <c>CreateTable</c> has to declare. The member names are those of <c>ScalarAttributeType</c> in <c>DynaSharp.Requests</c>, because
/// <see cref="EntityEmitter"/> writes them as they are.
/// </summary>
internal enum KeyWireType
{
    /// <summary>A custom converter that does not say what it writes. The runtime treats it as a string.</summary>
    Unknown,

    /// <summary>A string, which every template is.</summary>
    String,

    /// <summary>A number.</summary>
    Number,

    /// <summary>A binary value.</summary>
    Binary,
}
