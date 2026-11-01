namespace DynaSharp.Requests;

/// <summary>
/// Reads a single item by its full primary key (DynamoDB <c>GetItem</c>).
/// </summary>
public sealed record GetItemRequest
{
    /// <summary>Gets the table name (or table ARN).</summary>
    public required string TableName { get; init; }

    /// <summary>Gets the full primary key: the partition key and, if the table has one, the sort key.</summary>
    public required IReadOnlyDictionary<string, AttributeValue> Key { get; init; }

    /// <summary>
    /// Gets a value indicating whether to use a strongly consistent read. The default eventually consistent read costs
    /// half the capacity but may miss writes from the last second.
    /// </summary>
    public bool ConsistentRead { get; init; }

    /// <summary>
    /// Gets the attributes to return, for example <c>"#n, Address.City"</c>. All attributes when <see langword="null"/>.
    /// Attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or <c>total</c>,
    /// must be aliased with a <c>#placeholder</c> defined in <see cref="ExpressionAttributeNames"/>; written directly, DynamoDB rejects the
    /// request with a <see cref="ValidationException"/>. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    public string? ProjectionExpression { get; init; }

    /// <summary>
    /// Gets the substitutions for <c>#name</c> placeholders in <see cref="ProjectionExpression"/>.
    /// Use it to alias attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or
    /// <c>total</c>, which DynamoDB rejects with a <see cref="ValidationException"/> when an expression uses them directly (the check is
    /// case-insensitive), and names with a dot or other special characters. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ExpressionAttributeNames { get; init; }

    /// <summary>Gets whether to report consumed capacity in <see cref="GetItemResponse.ConsumedCapacity"/>.</summary>
    public ReturnConsumedCapacity ReturnConsumedCapacity { get; init; }
}
