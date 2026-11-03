namespace DynaSharp.Requests;

/// <summary>
/// The state of the server-side encryption of a table with an AWS KMS key.
/// </summary>
/// <remarks>
/// A table encrypted with the key that DynamoDB owns reports no state: its <see cref="TableDescription.SSEDescription"/> is <see langword="null"/>.
/// </remarks>
public enum SSEStatus
{
    /// <summary>A state this library does not know yet, or none was reported.</summary>
    Unknown = 0,

    /// <summary>Encryption with the KMS key is being switched on.</summary>
    Enabling,

    /// <summary>The table is encrypted with the KMS key.</summary>
    Enabled,

    /// <summary>Encryption with the KMS key is being switched off, back to the key that DynamoDB owns.</summary>
    Disabling,

    /// <summary>The table is not encrypted with a KMS key.</summary>
    Disabled,

    /// <summary>The table is switching to another KMS key.</summary>
    Updating,
}
