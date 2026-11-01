namespace DynaSharp.Requests;

/// <summary>
/// Changes the settings of a table: its billing mode and capacity, its stream, its global secondary indexes, its storage class and
/// its deletion protection (DynamoDB <c>UpdateTable</c>).
/// </summary>
/// <remarks>
/// <para>
/// Set what should change and leave the rest unset: an unset member sends nothing and keeps the current setting. DynamoDB applies
/// the change asynchronously. The response describes the table in the state <see cref="TableStatus.Updating"/>, and the table accepts
/// no further structural change until it is <see cref="TableStatus.Active"/> again; poll <c>DescribeTable</c>. A new global secondary
/// index additionally needs its own <see cref="IndexStatus.Active"/> before it can be queried.
/// </para>
/// <para>
/// DynamoDB limits what one request may combine. It rejects a request that changes nothing, and in general at most one global
/// secondary index may be created or deleted per request (an index can be updated together with other changes). Capacity can be
/// lowered only a few times a day, and the billing mode can be switched only once every 24 hours. Settings that this request does
/// not cover (server-side encryption, replicas, on-demand throughput limits and warm throughput) are not available here.
/// </para>
/// </remarks>
public sealed record UpdateTableRequest
{
    /// <summary>Gets the table name (or table ARN).</summary>
    public required string TableName { get; init; }

    /// <summary>
    /// Gets the types of the key attributes that the changes introduce, namely those of a new global secondary index. Define an
    /// attribute only if it is not defined already. <see langword="null"/> or empty sends nothing.
    /// </summary>
    public IReadOnlyList<AttributeDefinition>? AttributeDefinitions { get; init; }

    /// <summary>
    /// Gets the new billing mode. <see cref="Requests.BillingMode.Unspecified"/> (the default) sends nothing. Switching to
    /// <see cref="Requests.BillingMode.Provisioned"/> requires <see cref="ProvisionedThroughput"/> (and one for each global secondary
    /// index); switching to <see cref="Requests.BillingMode.PayPerRequest"/> does not take capacity.
    /// </summary>
    public BillingMode BillingMode { get; init; }

    /// <summary>
    /// Gets the new capacity of the table, for a table with <see cref="Requests.BillingMode.Provisioned"/> capacity. <see langword="null"/>
    /// keeps the current capacity.
    /// </summary>
    public ProvisionedThroughput? ProvisionedThroughput { get; init; }

    /// <summary>
    /// Gets the changes to the global secondary indexes: create, update or delete. <see langword="null"/> or empty changes none.
    /// </summary>
    public IReadOnlyList<GlobalSecondaryIndexUpdate>? GlobalSecondaryIndexUpdates { get; init; }

    /// <summary>
    /// Gets the new stream settings. <see langword="null"/> keeps them. To switch a stream on, set
    /// <see cref="StreamSpecification.StreamEnabled"/> and a view type; to switch it off, set only
    /// <see cref="StreamSpecification.StreamEnabled"/> to <see langword="false"/>.
    /// </summary>
    public StreamSpecification? StreamSpecification { get; init; }

    /// <summary>
    /// Gets the new storage class. <see cref="Requests.TableClass.Unspecified"/> (the default) sends nothing and keeps the current class.
    /// </summary>
    public TableClass TableClass { get; init; }

    /// <summary>
    /// Gets whether the table is protected from deletion. <see langword="null"/> keeps the current setting; unlike in
    /// <see cref="CreateTableRequest.DeletionProtectionEnabled"/>, <see langword="false"/> is sent, because it is how the protection
    /// is switched off.
    /// </summary>
    public bool? DeletionProtectionEnabled { get; init; }
}
