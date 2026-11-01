namespace DynaSharp.Requests;

/// <summary>
/// Changes the attributes of an existing item, or creates the item if no item has the key, as part of a
/// <see cref="TransactWriteItemsRequest"/>, with the effect of <c>UpdateItem</c>. Named <c>Update</c> in the DynamoDB service model.
/// </summary>
public sealed record TransactUpdate
{
    /// <summary>Gets the table name (or table ARN).</summary>
    public required string TableName { get; init; }

    /// <summary>Gets the full primary key of the item: the partition key and, if the table has one, the sort key. Key attributes cannot be updated.</summary>
    public required IReadOnlyDictionary<string, AttributeValue> Key { get; init; }

    /// <summary>
    /// Gets the actions to apply, in the syntax of <see cref="UpdateItemRequest.UpdateExpression"/>, for example
    /// <c>"SET visits = visits + :one REMOVE tempFlag"</c>. At most 4 KB. Like <c>UpdateItem</c> this creates the item when it does
    /// not exist, unless <see cref="ConditionExpression"/> forbids it.
    /// Attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or <c>total</c>,
    /// must be aliased with a <c>#placeholder</c> defined in <see cref="ExpressionAttributeNames"/>; written directly, DynamoDB rejects the
    /// request with a <see cref="ValidationException"/>. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    public required string UpdateExpression { get; init; }

    /// <summary>
    /// Gets a condition on the existing item that must hold, for example <c>"attribute_exists(pk) AND version = :expected"</c>.
    /// When it does not hold, the whole transaction is cancelled and the reason for this action is <c>ConditionalCheckFailed</c>.
    /// Attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or <c>total</c>,
    /// must be aliased with a <c>#placeholder</c> defined in <see cref="ExpressionAttributeNames"/>; written directly, DynamoDB rejects the
    /// request with a <see cref="ValidationException"/>. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    public string? ConditionExpression { get; init; }

    /// <summary>
    /// Gets the substitutions for <c>#name</c> placeholders in the update and condition expressions.
    /// Use it to alias attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or
    /// <c>total</c>, which DynamoDB rejects with a <see cref="ValidationException"/> when an expression uses them directly (the check is
    /// case-insensitive), and names with a dot or other special characters. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ExpressionAttributeNames { get; init; }

    /// <summary>Gets the substitutions for <c>:value</c> placeholders in the update and condition expressions.</summary>
    public IReadOnlyDictionary<string, AttributeValue>? ExpressionAttributeValues { get; init; }

    /// <summary>Gets whether a failed condition returns the existing item in <see cref="CancellationReason.Item"/>.</summary>
    public ReturnValuesOnConditionCheckFailure ReturnValuesOnConditionCheckFailure { get; init; }
}
