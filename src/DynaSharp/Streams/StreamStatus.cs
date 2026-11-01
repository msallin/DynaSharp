namespace DynaSharp.Streams;

/// <summary>
/// The state of a stream.
/// </summary>
public enum StreamStatus
{
    /// <summary>A state this library does not know yet, or none was reported.</summary>
    Unknown = 0,

    /// <summary>The stream is being enabled and cannot be read yet, DynamoDB name <c>ENABLING</c>.</summary>
    Enabling,

    /// <summary>The stream is enabled and can be read, DynamoDB name <c>ENABLED</c>.</summary>
    Enabled,

    /// <summary>The stream is being disabled, DynamoDB name <c>DISABLING</c>.</summary>
    Disabling,

    /// <summary>Streams are disabled on the table, DynamoDB name <c>DISABLED</c>. The records stay readable for 24 hours.</summary>
    Disabled,
}
