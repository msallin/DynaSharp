namespace DynaSharp.Requests;

/// <summary>
/// Deletes a single item by its full primary key (DynamoDB <c>DeleteItem</c>).
/// </summary>
/// <remarks>
/// Deleting an item that does not exist is not an error: the call succeeds and changes nothing, unless a
/// <see cref="ConditionExpression"/> requires the item to exist.
/// </remarks>
public sealed record DeleteItemRequest
{
    /// <summary>Gets the table name (or table ARN).</summary>
    public required string TableName { get; init; }

    /// <summary>Gets the full primary key of the item: the partition key and, if the table has one, the sort key.</summary>
    public required IReadOnlyDictionary<string, AttributeValue> Key { get; init; }

    /// <summary>
    /// Gets a condition on the existing item that must hold for the delete to happen, for example
    /// <c>"attribute_exists(pk) AND version = :expected"</c>. When it does not hold, the call throws
    /// <see cref="ConditionalCheckFailedException"/> and the item stays. At most 4 KB.
    /// Attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or <c>total</c>,
    /// must be aliased with a <c>#placeholder</c> defined in <see cref="ExpressionAttributeNames"/>; written directly, DynamoDB rejects the
    /// request with a <see cref="ValidationException"/>. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    public string? ConditionExpression { get; init; }

    /// <summary>
    /// Gets the substitutions for <c>#name</c> placeholders in <see cref="ConditionExpression"/>. A placeholder that the
    /// expression does not use makes DynamoDB reject the request.
    /// Use it to alias attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or
    /// <c>total</c>, which DynamoDB rejects with a <see cref="ValidationException"/> when an expression uses them directly (the check is
    /// case-insensitive), and names with a dot or other special characters. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ExpressionAttributeNames { get; init; }

    /// <summary>
    /// Gets the substitutions for <c>:value</c> placeholders in <see cref="ConditionExpression"/>. A placeholder that the
    /// expression does not use, or that no value defines, makes DynamoDB reject the request.
    /// </summary>
    public IReadOnlyDictionary<string, AttributeValue>? ExpressionAttributeValues { get; init; }

    /// <summary>
    /// Gets what to return in <see cref="DeleteItemResponse.Attributes"/>: <see cref="ReturnValue.None"/> or
    /// <see cref="ReturnValue.AllOld"/>. DynamoDB rejects the other values.
    /// </summary>
    public ReturnValue ReturnValues { get; init; }

    /// <summary>Gets whether a failed condition returns the existing item in <see cref="ConditionalCheckFailedException.Item"/>.</summary>
    public ReturnValuesOnConditionCheckFailure ReturnValuesOnConditionCheckFailure { get; init; }

    /// <summary>Gets whether to report consumed capacity in <see cref="DeleteItemResponse.ConsumedCapacity"/>.</summary>
    public ReturnConsumedCapacity ReturnConsumedCapacity { get; init; }

    /// <summary>
    /// Gets whether to report the size of the item collection in <see cref="DeleteItemResponse.ItemCollectionMetrics"/>. Only
    /// tables with a local secondary index have item collections.
    /// </summary>
    public ReturnItemCollectionMetrics ReturnItemCollectionMetrics { get; init; }
}
