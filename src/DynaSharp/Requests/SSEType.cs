namespace DynaSharp.Requests;

/// <summary>
/// The type of server-side encryption of a table.
/// </summary>
/// <remarks>
/// In a <see cref="SSESpecification"/>, <see cref="Unspecified"/> sends nothing and DynamoDB uses <see cref="Kms"/>.
/// </remarks>
public enum SSEType
{
    /// <summary>Not set. In a request it sends nothing; in a response it is a type this library does not know.</summary>
    Unspecified = 0,

    /// <summary>
    /// Encryption with the AES-256 algorithm, DynamoDB name <c>AES256</c>. DynamoDB documents it as not applicable to a request: a
    /// table without a <see cref="SSESpecification"/> is already encrypted at rest with a key that DynamoDB owns.
    /// </summary>
    Aes256,

    /// <summary>Encryption with an AWS KMS key, DynamoDB name <c>KMS</c>.</summary>
    Kms,
}
