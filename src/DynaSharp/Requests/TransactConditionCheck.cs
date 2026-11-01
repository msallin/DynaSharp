namespace DynaSharp.Requests;

/// <summary>
/// Requires a condition on an item that the transaction does not modify, as part of a <see cref="TransactWriteItemsRequest"/>.
/// Named <c>ConditionCheck</c> in the DynamoDB service model.
/// </summary>
/// <remarks>
/// Use it to make a write depend on another item, for example to write an order only while the customer exists. Another action of
/// the same transaction cannot target the checked item.
/// </remarks>
public sealed record TransactConditionCheck
{
    /// <summary>Gets the table name (or table ARN).</summary>
    public required string TableName { get; init; }

    /// <summary>Gets the full primary key of the item to check: the partition key and, if the table has one, the sort key.</summary>
    public required IReadOnlyDictionary<string, AttributeValue> Key { get; init; }

    /// <summary>
    /// Gets the condition that must hold, for example <c>"attribute_exists(pk) AND #s = :active"</c>. When it does not hold, the
    /// whole transaction is cancelled and the reason for this action is <c>ConditionalCheckFailed</c>. A missing item satisfies only
    /// conditions such as <c>attribute_not_exists(...)</c>.
    /// Attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or <c>total</c>,
    /// must be aliased with a <c>#placeholder</c> defined in <see cref="ExpressionAttributeNames"/>; written directly, DynamoDB rejects the
    /// request with a <see cref="ValidationException"/>. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    public required string ConditionExpression { get; init; }

    /// <summary>
    /// Gets the substitutions for <c>#name</c> placeholders in <see cref="ConditionExpression"/>.
    /// Use it to alias attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or
    /// <c>total</c>, which DynamoDB rejects with a <see cref="ValidationException"/> when an expression uses them directly (the check is
    /// case-insensitive), and names with a dot or other special characters. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ExpressionAttributeNames { get; init; }

    /// <summary>Gets the substitutions for <c>:value</c> placeholders in <see cref="ConditionExpression"/>.</summary>
    public IReadOnlyDictionary<string, AttributeValue>? ExpressionAttributeValues { get; init; }

    /// <summary>Gets whether a failed condition returns the existing item in <see cref="CancellationReason.Item"/>.</summary>
    public ReturnValuesOnConditionCheckFailure ReturnValuesOnConditionCheckFailure { get; init; }
}
