namespace DynaSharp.Requests;

/// <summary>
/// Creates a table (DynamoDB <c>CreateTable</c>).
/// </summary>
/// <remarks>
/// <para>
/// Table creation is asynchronous: the response describes the table in the state <see cref="TableStatus.Creating"/>, and the table
/// can be used only when <c>DescribeTable</c> reports <see cref="TableStatus.Active"/>. Poll with a delay of a second or more.
/// </para>
/// <para>
/// Only the attributes that are key attributes of the table or of an index are defined, in <see cref="AttributeDefinitions"/>;
/// DynamoDB is schemaless for everything else, and rejects a definition that no key uses.
/// </para>
/// <para>
/// Without <see cref="BillingMode"/> the table has provisioned capacity, and <see cref="ProvisionedThroughput"/> is then required.
/// With <see cref="BillingMode.PayPerRequest"/> it must not be set.
/// </para>
/// </remarks>
public sealed record CreateTableRequest
{
    /// <summary>
    /// Gets the types of the attributes that are keys of the table or of any of its indexes. Each key attribute is defined exactly
    /// once, and an attribute that is no key is not defined.
    /// </summary>
    public required IReadOnlyList<AttributeDefinition> AttributeDefinitions { get; init; }

    /// <summary>
    /// Gets the table name: 3 to 255 characters of letters, digits, underscore, hyphen and dot, unique per account and region.
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// Gets the primary key: the partition key (<see cref="KeyType.Hash"/>), followed by the sort key (<see cref="KeyType.Range"/>)
    /// if the table has one. It cannot be changed afterwards.
    /// </summary>
    public required IReadOnlyList<KeySchemaElement> KeySchema { get; init; }

    /// <summary>
    /// Gets the local secondary indexes, at most five. They can only be defined here. <see langword="null"/> or empty defines none.
    /// </summary>
    public IReadOnlyList<LocalSecondaryIndex>? LocalSecondaryIndexes { get; init; }

    /// <summary>
    /// Gets the global secondary indexes, at most 20 on creation. <see langword="null"/> or empty defines none. More can be added
    /// to an existing table later, one at a time.
    /// </summary>
    public IReadOnlyList<GlobalSecondaryIndex>? GlobalSecondaryIndexes { get; init; }

    /// <summary>
    /// Gets how the table is billed. <see cref="Requests.BillingMode.Unspecified"/> (the default) sends nothing, and DynamoDB creates a
    /// table with provisioned capacity.
    /// </summary>
    public BillingMode BillingMode { get; init; }

    /// <summary>
    /// Gets the capacity of the table. Required with <see cref="Requests.BillingMode.Provisioned"/> (also when the mode is not set),
    /// and not allowed with <see cref="Requests.BillingMode.PayPerRequest"/>.
    /// </summary>
    public ProvisionedThroughput? ProvisionedThroughput { get; init; }

    /// <summary>Gets whether the table has a stream, and what its records contain. <see langword="null"/> creates a table without a stream.</summary>
    public StreamSpecification? StreamSpecification { get; init; }

    /// <summary>Gets the server-side encryption. <see langword="null"/> uses the default key that DynamoDB owns.</summary>
    public SSESpecification? SSESpecification { get; init; }

    /// <summary>Gets the tags to attach to the table. <see langword="null"/> or empty attaches none.</summary>
    public IReadOnlyList<TableTag>? Tags { get; init; }

    /// <summary>
    /// Gets the storage class. <see cref="Requests.TableClass.Unspecified"/> (the default) sends nothing, and DynamoDB uses the standard
    /// class.
    /// </summary>
    public TableClass TableClass { get; init; }

    /// <summary>
    /// Gets a value indicating whether the table is protected from deletion: <c>DeleteTable</c> then fails until the protection is
    /// switched off. <see langword="false"/> (the default) sends nothing.
    /// </summary>
    public bool DeletionProtectionEnabled { get; init; }
}
