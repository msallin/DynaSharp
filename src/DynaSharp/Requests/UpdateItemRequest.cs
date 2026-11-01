namespace DynaSharp.Requests;

/// <summary>
/// Changes the attributes of an existing item, or creates the item if no item has the key (DynamoDB <c>UpdateItem</c>).
/// </summary>
/// <remarks>
/// The update is atomic and changes only the attributes the <see cref="UpdateExpression"/> names; everything else stays as it is.
/// Without an <see cref="UpdateExpression"/> a missing item is created with only its key.
/// </remarks>
public sealed record UpdateItemRequest
{
    /// <summary>Gets the table name (or table ARN).</summary>
    public required string TableName { get; init; }

    /// <summary>Gets the full primary key of the item: the partition key and, if the table has one, the sort key. Key attributes cannot be updated.</summary>
    public required IReadOnlyDictionary<string, AttributeValue> Key { get; init; }

    /// <summary>
    /// Gets the actions to apply, in up to four clauses that may each appear once and be combined, for example
    /// <c>"SET #n = :name, visits = visits + :one REMOVE tempFlag ADD tags :newTags"</c>. An attribute path can appear in only one
    /// action. The expression can be at most 4 KB.
    /// Attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or <c>total</c>,
    /// must be aliased with a <c>#placeholder</c> defined in <see cref="ExpressionAttributeNames"/>; written directly, DynamoDB rejects the
    /// request with a <see cref="ValidationException"/>. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><c>SET</c> assigns values, replacing existing ones. It can compute, for example <c>a = a + :n</c>, <c>b = if_not_exists(b, :zero)</c> or <c>list = list_append(list, :more)</c>, and it works on nested paths such as <c>address.city</c> or <c>items[2]</c>.</description></item>
    /// <item><description><c>REMOVE</c> deletes attributes, or elements of a list.</description></item>
    /// <item><description><c>ADD</c> adds a number to a number attribute (a missing attribute counts as 0, which makes it an atomic counter) or elements to a set. It only works on top-level attributes.</description></item>
    /// <item><description><c>DELETE</c> removes elements from a set. It only works on top-level attributes.</description></item>
    /// </list>
    /// The result of the update must not make the item larger than 400 KB. Values are written as placeholders (<c>:name</c>) defined in
    /// <see cref="ExpressionAttributeValues"/>.
    /// </remarks>
    public string? UpdateExpression { get; init; }

    /// <summary>
    /// Gets a condition on the existing item that must hold for the update to be applied, for example
    /// <c>"attribute_exists(pk) AND version = :expected"</c>. When it does not hold, the call throws
    /// <see cref="ConditionalCheckFailedException"/> and nothing changes. A missing item satisfies only conditions such as
    /// <c>attribute_not_exists(...)</c>. At most 4 KB.
    /// Attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or <c>total</c>,
    /// must be aliased with a <c>#placeholder</c> defined in <see cref="ExpressionAttributeNames"/>; written directly, DynamoDB rejects the
    /// request with a <see cref="ValidationException"/>. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    public string? ConditionExpression { get; init; }

    /// <summary>
    /// Gets the substitutions for <c>#name</c> placeholders in the update and condition expressions. Use them for attribute names
    /// that are reserved words (such as <c>Name</c> or <c>Status</c>) or contain special characters. A placeholder that no
    /// expression uses makes DynamoDB reject the request.
    /// Use it to alias attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or
    /// <c>total</c>, which DynamoDB rejects with a <see cref="ValidationException"/> when an expression uses them directly (the check is
    /// case-insensitive), and names with a dot or other special characters. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ExpressionAttributeNames { get; init; }

    /// <summary>
    /// Gets the substitutions for <c>:value</c> placeholders in the update and condition expressions. A placeholder that no
    /// expression uses, or that no value defines, makes DynamoDB reject the request.
    /// </summary>
    public IReadOnlyDictionary<string, AttributeValue>? ExpressionAttributeValues { get; init; }

    /// <summary>
    /// Gets what to return in <see cref="UpdateItemResponse.Attributes"/>. The default returns nothing. There is no extra cost
    /// and the values are strongly consistent.
    /// </summary>
    public ReturnValue ReturnValues { get; init; }

    /// <summary>Gets whether a failed condition returns the existing item in <see cref="ConditionalCheckFailedException.Item"/>.</summary>
    public ReturnValuesOnConditionCheckFailure ReturnValuesOnConditionCheckFailure { get; init; }

    /// <summary>Gets whether to report consumed capacity in <see cref="UpdateItemResponse.ConsumedCapacity"/>.</summary>
    public ReturnConsumedCapacity ReturnConsumedCapacity { get; init; }

    /// <summary>
    /// Gets whether to report the size of the item collection in <see cref="UpdateItemResponse.ItemCollectionMetrics"/>. Only
    /// tables with a local secondary index have item collections.
    /// </summary>
    public ReturnItemCollectionMetrics ReturnItemCollectionMetrics { get; init; }
}
