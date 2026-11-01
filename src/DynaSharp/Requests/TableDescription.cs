using DynaSharp.Streams;

namespace DynaSharp.Requests;

/// <summary>
/// The definition and state of a table. Only the commonly needed subset of what DynamoDB returns.
/// </summary>
public sealed record TableDescription
{
    /// <summary>Gets the table name.</summary>
    public required string TableName { get; init; }

    /// <summary>
    /// Gets the table ARN. DynamoDB sends it with every description: <see langword="null"/> only if the response omits it.
    /// </summary>
    public string? TableArn { get; init; }

    /// <summary>Gets the lifecycle state.</summary>
    public TableStatus TableStatus { get; init; }

    /// <summary>Gets the primary key: the partition key, followed by the sort key if there is one.</summary>
    public IReadOnlyList<KeySchemaElement> KeySchema { get; init; } = [];

    /// <summary>Gets the types of the key attributes of the table and its indexes.</summary>
    public IReadOnlyList<AttributeDefinition> AttributeDefinitions { get; init; } = [];

    /// <summary>Gets the approximate item count. DynamoDB updates it about every six hours.</summary>
    public long ItemCount { get; init; }

    /// <summary>Gets the approximate table size in bytes. DynamoDB updates it about every six hours.</summary>
    public long TableSizeBytes { get; init; }

    /// <summary>
    /// Gets when the table was created. DynamoDB sends it with every description: <see langword="null"/> only if the response omits it.
    /// </summary>
    public DateTimeOffset? CreationDateTime { get; init; }

    /// <summary>
    /// Gets the billing mode. <see langword="null"/> if DynamoDB did not report it, which it may omit for a table that always had
    /// provisioned capacity.
    /// </summary>
    public BillingModeSummary? BillingModeSummary { get; init; }

    /// <summary>
    /// Gets the capacity of the table. <see langword="null"/> if DynamoDB did not report it; a table with
    /// <see cref="BillingMode.PayPerRequest"/> capacity reports zero units.
    /// </summary>
    public ProvisionedThroughputDescription? ProvisionedThroughput { get; init; }

    /// <summary>
    /// Gets the storage class. <see cref="Requests.TableClass.Unspecified"/> if DynamoDB did not report one, which it may omit for a table that
    /// always had the standard class.
    /// </summary>
    public TableClass TableClass { get; init; }

    /// <summary>Gets the local secondary indexes of the table. Empty if it has none.</summary>
    public IReadOnlyList<LocalSecondaryIndexDescription> LocalSecondaryIndexes { get; init; } = [];

    /// <summary>Gets the global secondary indexes of the table, each with its state. Empty if it has none.</summary>
    public IReadOnlyList<GlobalSecondaryIndexDescription> GlobalSecondaryIndexes { get; init; } = [];

    /// <summary>Gets the stream settings, or <see langword="null"/> for a table that never had a stream.</summary>
    public StreamSpecification? StreamSpecification { get; init; }

    /// <summary>
    /// Gets the ARN of the most recent stream of the table: the <c>StreamArn</c> to read it with <see cref="DynamoDbStreamsClient"/>.
    /// <see langword="null"/> for a table that never had a stream. It stays set after streams are switched off.
    /// </summary>
    public string? LatestStreamArn { get; init; }

    /// <summary>
    /// Gets the label (a timestamp) of the most recent stream. Together with the table name it identifies the stream; the ARN ends
    /// with it. <see langword="null"/> for a table that never had a stream.
    /// </summary>
    public string? LatestStreamLabel { get; init; }

    /// <summary>Gets a value indicating whether the table is protected from deletion. <see langword="false"/> if DynamoDB did not say.</summary>
    public bool DeletionProtectionEnabled { get; init; }
}
