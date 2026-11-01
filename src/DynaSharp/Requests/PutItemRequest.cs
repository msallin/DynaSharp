namespace DynaSharp.Requests;

/// <summary>
/// Creates an item or replaces an existing item with the same primary key entirely (DynamoDB <c>PutItem</c>).
/// </summary>
public sealed record PutItemRequest
{
    /// <summary>Gets the table name (or table ARN).</summary>
    public required string TableName { get; init; }

    /// <summary>Gets the item, including all key attributes. At most 400 KB.</summary>
    public required IReadOnlyDictionary<string, AttributeValue> Item { get; init; }

    /// <summary>
    /// Gets a condition that must hold for the write to succeed, for example <c>"attribute_not_exists(pk)"</c>.
    /// When it does not hold, the call throws <see cref="ConditionalCheckFailedException"/>.
    /// Attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or <c>total</c>,
    /// must be aliased with a <c>#placeholder</c> defined in <see cref="ExpressionAttributeNames"/>; written directly, DynamoDB rejects the
    /// request with a <see cref="ValidationException"/>. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    public string? ConditionExpression { get; init; }

    /// <summary>
    /// Gets the substitutions for <c>#name</c> placeholders in <see cref="ConditionExpression"/>.
    /// Use it to alias attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or
    /// <c>total</c>, which DynamoDB rejects with a <see cref="ValidationException"/> when an expression uses them directly (the check is
    /// case-insensitive), and names with a dot or other special characters. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ExpressionAttributeNames { get; init; }

    /// <summary>Gets the substitutions for <c>:value</c> placeholders in <see cref="ConditionExpression"/>.</summary>
    public IReadOnlyDictionary<string, AttributeValue>? ExpressionAttributeValues { get; init; }

    /// <summary>Gets what to return in <see cref="PutItemResponse.Attributes"/>: <see cref="ReturnValue.None"/> or <see cref="ReturnValue.AllOld"/>.</summary>
    public ReturnValue ReturnValues { get; init; }

    /// <summary>Gets whether a failed condition returns the existing item in <see cref="ConditionalCheckFailedException.Item"/>.</summary>
    public ReturnValuesOnConditionCheckFailure ReturnValuesOnConditionCheckFailure { get; init; }

    /// <summary>Gets whether to report consumed capacity in <see cref="PutItemResponse.ConsumedCapacity"/>.</summary>
    public ReturnConsumedCapacity ReturnConsumedCapacity { get; init; }

    /// <summary>
    /// Gets whether to report the size of the item collection in <see cref="PutItemResponse.ItemCollectionMetrics"/>. Only tables
    /// with a local secondary index have item collections.
    /// </summary>
    public ReturnItemCollectionMetrics ReturnItemCollectionMetrics { get; init; }
}
