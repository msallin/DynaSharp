namespace DynaSharp.Requests;

/// <summary>
/// The type of a key attribute. Keys can only be strings, numbers or binary, which DynamoDB and the AWS SDKs call <c>S</c>, <c>N</c> and <c>B</c>.
/// </summary>
public enum ScalarAttributeType
{
    /// <summary>A type this library does not know. Never valid in a request.</summary>
    Unknown = 0,

    /// <summary>String, <c>S</c>.</summary>
    String,

    /// <summary>Number, <c>N</c>.</summary>
    Number,

    /// <summary>Binary, <c>B</c>.</summary>
    Binary,
}
