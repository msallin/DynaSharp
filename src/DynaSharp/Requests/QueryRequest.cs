namespace DynaSharp.Requests;

/// <summary>
/// Reads the items of one partition, optionally narrowed by a sort key condition, from a table or an index (DynamoDB <c>Query</c>).
/// </summary>
/// <remarks>
/// <para>
/// A query reads at most 1 MB of data per call, and at most <see cref="Limit"/> items, before the <see cref="FilterExpression"/>
/// is applied. When it stops early, the response carries a <see cref="QueryResponse.LastEvaluatedKey"/>: pass it as
/// <see cref="ExclusiveStartKey"/> of the next request to continue. A page can be empty and still have a
/// <see cref="QueryResponse.LastEvaluatedKey"/>, because the filter removed everything that was read; only a response without
/// one is the last page.
/// </para>
/// <para>
/// Results are ordered by the sort key (numbers numerically, strings and binary by their UTF-8 or unsigned bytes), ascending unless
/// <see cref="ScanIndexForward"/> is <see langword="false"/>.
/// </para>
/// </remarks>
public sealed record QueryRequest
{
    /// <summary>Gets the table name (or table ARN). With <see cref="IndexName"/>, the table the index belongs to.</summary>
    public required string TableName { get; init; }

    /// <summary>
    /// Gets the condition that selects the items: an equality test on the partition key, optionally combined with <c>AND</c> and
    /// one test on the sort key (<c>=</c>, <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>, <c>BETWEEN</c> or
    /// <c>begins_with</c>), for example <c>"pk = :pk AND begins_with(sk, :prefix)"</c>. Use placeholders (<c>#name</c>,
    /// <c>:value</c>) for names that are reserved words and for all values. <c>begins_with</c> does not work on numbers.
    /// Attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or <c>total</c>,
    /// must be aliased with a <c>#placeholder</c> defined in <see cref="ExpressionAttributeNames"/>; written directly, DynamoDB rejects the
    /// request with a <see cref="ValidationException"/>. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    /// <remarks>
    /// DynamoDB's own service model leaves this optional because of a legacy parameter that this library does not offer; without
    /// it a query is always rejected, so it is required here.
    /// </remarks>
    public required string KeyConditionExpression { get; init; }

    /// <summary>
    /// Gets the name of the local or global secondary index to query instead of the table. Items are then ordered by the
    /// sort key of the index. A global secondary index is eventually consistent, so it can miss recent writes.
    /// </summary>
    public string? IndexName { get; init; }

    /// <summary>
    /// Gets a condition that DynamoDB applies to the items after reading them and before returning them, for example
    /// <c>"#status = :active"</c>. It cannot use the key attributes. Filtering does not reduce the read capacity consumed or the
    /// 1 MB read limit, only the data that is returned; a selective filter produces small or empty pages.
    /// Attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or <c>total</c>,
    /// must be aliased with a <c>#placeholder</c> defined in <see cref="ExpressionAttributeNames"/>; written directly, DynamoDB rejects the
    /// request with a <see cref="ValidationException"/>. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    public string? FilterExpression { get; init; }

    /// <summary>
    /// Gets the attributes to return, for example <c>"#n, Address.City, Tags[0]"</c>. All attributes when
    /// <see langword="null"/>. It does not reduce the read capacity consumed, which is based on the size of the items read.
    /// Attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or <c>total</c>,
    /// must be aliased with a <c>#placeholder</c> defined in <see cref="ExpressionAttributeNames"/>; written directly, DynamoDB rejects the
    /// request with a <see cref="ValidationException"/>. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    public string? ProjectionExpression { get; init; }

    /// <summary>
    /// Gets the substitutions for <c>#name</c> placeholders in the key condition, filter and projection expressions. A
    /// placeholder that no expression uses makes DynamoDB reject the request.
    /// Use it to alias attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or
    /// <c>total</c>, which DynamoDB rejects with a <see cref="ValidationException"/> when an expression uses them directly (the check is
    /// case-insensitive), and names with a dot or other special characters. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ExpressionAttributeNames { get; init; }

    /// <summary>
    /// Gets the substitutions for <c>:value</c> placeholders in the key condition and filter expressions. A placeholder that no
    /// expression uses, or that no value defines, makes DynamoDB reject the request.
    /// </summary>
    public IReadOnlyDictionary<string, AttributeValue>? ExpressionAttributeValues { get; init; }

    /// <summary>
    /// Gets which attributes to return. <see cref="DynaSharp.Select.Unspecified"/> sends nothing; see <see cref="DynaSharp.Select"/> for what
    /// DynamoDB then does and which combinations with <see cref="ProjectionExpression"/> are valid.
    /// </summary>
    public Select Select { get; init; }

    /// <summary>
    /// Gets the maximum number of items to read (not the number of items returned, which the filter can reduce). DynamoDB
    /// requires at least 1 and rejects other values with a <see cref="ValidationException"/>; this library passes the value on. The
    /// 1 MB limit applies as well. <see langword="null"/> sends nothing: the limit is then only the 1 MB.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Gets a value indicating whether to use a strongly consistent read. The default eventually consistent read costs half the
    /// capacity but may miss writes from the last second. Not supported on global secondary indexes: DynamoDB rejects it with a
    /// <see cref="ValidationException"/>.
    /// </summary>
    public bool ConsistentRead { get; init; }

    /// <summary>
    /// Gets the direction of the traversal by sort key: <see langword="true"/> or <see langword="null"/> ascending (DynamoDB default),
    /// <see langword="false"/> descending. For example, <see langword="false"/> with a <see cref="Limit"/> of 1 reads the item with the
    /// highest sort key.
    /// </summary>
    public bool? ScanIndexForward { get; init; }

    /// <summary>
    /// Gets the key of the last item of the previous page: the <see cref="QueryResponse.LastEvaluatedKey"/> of the previous
    /// response, unchanged. The query continues with the next item. For an index query it contains the key attributes of the table
    /// and of the index; no sets are allowed in it.
    /// </summary>
    public IReadOnlyDictionary<string, AttributeValue>? ExclusiveStartKey { get; init; }

    /// <summary>Gets whether to report consumed capacity in <see cref="QueryResponse.ConsumedCapacity"/>.</summary>
    public ReturnConsumedCapacity ReturnConsumedCapacity { get; init; }
}
