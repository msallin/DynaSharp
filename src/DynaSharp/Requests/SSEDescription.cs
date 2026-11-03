namespace DynaSharp.Requests;

/// <summary>
/// The server-side encryption of a table with an AWS KMS key, as DynamoDB reports it.
/// </summary>
/// <remarks>
/// DynamoDB reports it only for a table encrypted with a KMS key, the AWS managed key <c>alias/aws/dynamodb</c> or a customer managed key; a table
/// encrypted with the key that DynamoDB owns, the default, has none.
/// </remarks>
public sealed record SSEDescription
{
    /// <summary>Gets the state of the encryption. <see cref="SSEStatus.Unknown"/> if DynamoDB reported none or one this library does not know.</summary>
    public SSEStatus Status { get; init; }

    /// <summary>Gets the type of encryption, <see cref="Requests.SSEType.Kms"/> in practice. <see cref="Requests.SSEType.Unspecified"/> if DynamoDB reported none or one this library does not know.</summary>
    public SSEType SSEType { get; init; }

    /// <summary>
    /// Gets the ARN of the KMS key that encrypts the table: the key behind the alias, also when the table was created with an alias or a key id.
    /// <see langword="null"/> if DynamoDB did not report it.
    /// </summary>
    public string? KMSMasterKeyArn { get; init; }

    /// <summary>
    /// Gets when DynamoDB found the KMS key inaccessible, or <see langword="null"/> while it is accessible. From then the table is
    /// <see cref="TableStatus.InaccessibleEncryptionCredentials"/>, and DynamoDB archives it seven days later unless the key becomes accessible again.
    /// </summary>
    public DateTimeOffset? InaccessibleEncryptionDateTime { get; init; }
}
