namespace DynaSharp.Requests;

/// <summary>
/// The server-side encryption of a table that is created.
/// </summary>
/// <remarks>
/// Every table is encrypted at rest. Without this specification the table uses a key that DynamoDB owns and that costs nothing.
/// With it, the table is encrypted with an AWS KMS key in your account, which costs a KMS request per access and stops the table from
/// working if the key becomes inaccessible (see <see cref="TableStatus.InaccessibleEncryptionCredentials"/>).
/// </remarks>
public sealed record SSESpecification
{
    /// <summary>
    /// Gets a value indicating whether the table is encrypted with an AWS managed KMS key (<see langword="true"/>) or with a key that
    /// DynamoDB owns (<see langword="false"/>, the default, which sends nothing).
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>Gets the type of encryption. <see cref="Requests.SSEType.Unspecified"/> (the default) sends nothing, and DynamoDB uses KMS.</summary>
    public SSEType SSEType { get; init; }

    /// <summary>
    /// Gets the KMS key to use, as a key id, key ARN, alias name or alias ARN, for a customer managed key; <see langword="null"/>
    /// uses the AWS managed key <c>alias/aws/dynamodb</c>. The key must be in the region of the table.
    /// </summary>
    public string? KMSMasterKeyId { get; init; }
}
