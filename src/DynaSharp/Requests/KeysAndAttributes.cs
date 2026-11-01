namespace DynaSharp.Requests;

/// <summary>
/// The items to read from one table in a <see cref="BatchGetItemRequest"/>, and how to read them. DynamoDB returns the same
/// structure as <see cref="BatchGetItemResponse.UnprocessedKeys"/>, so a value from there can be sent back unchanged.
/// </summary>
public sealed record KeysAndAttributes
{
    /// <summary>
    /// Gets the primary keys of the items to read: for each item the partition key and, if the table has one, the sort key. A key
    /// that occurs twice in a request makes DynamoDB reject the request. All tables of a request together can name at most 100 keys.
    /// </summary>
    public required IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>> Keys { get; init; }

    /// <summary>
    /// Gets a value indicating whether to use strongly consistent reads for the items of this table. The default eventually
    /// consistent read costs half the capacity but may miss writes from the last second. The setting is per table.
    /// </summary>
    public bool ConsistentRead { get; init; }

    /// <summary>
    /// Gets the attributes to return for the items of this table, for example <c>"#n, Address.City"</c>. All attributes when
    /// <see langword="null"/>. DynamoDB does not return items in any particular order, so include the key attributes to tell the
    /// returned items apart.
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
}
