namespace DynaSharp.Requests;

/// <summary>
/// Reads every item of a table or index, optionally filtered (DynamoDB <c>Scan</c>).
/// </summary>
/// <remarks>
/// <para>
/// A scan reads at most 1 MB of data per call, and at most <see cref="Limit"/> items, before the <see cref="FilterExpression"/> is
/// applied. When it stops early, the response carries a <see cref="ScanResponse.LastEvaluatedKey"/>: pass it as
/// <see cref="ExclusiveStartKey"/> of the next request to continue. A page can be empty and still have a
/// <see cref="ScanResponse.LastEvaluatedKey"/>, because the filter removed everything that was read; only a response without one is
/// the last page.
/// </para>
/// <para>
/// The read capacity consumed is that of everything read, not of what the filter returns. Scanning a large table is slow and
/// expensive and can starve other requests of provisioned capacity; prefer a query whenever the key is known. To scan faster,
/// split the table into segments with <see cref="TotalSegments"/> and read each with its own sequence of requests, in parallel.
/// </para>
/// </remarks>
public sealed record ScanRequest
{
    /// <summary>Gets the table name (or table ARN). With <see cref="IndexName"/>, the table the index belongs to.</summary>
    public required string TableName { get; init; }

    /// <summary>
    /// Gets the name of the local or global secondary index to scan instead of the table. A global secondary index is eventually
    /// consistent, so it can miss recent writes.
    /// </summary>
    public string? IndexName { get; init; }

    /// <summary>
    /// Gets a condition that DynamoDB applies to the items after reading them and before returning them, for example
    /// <c>"#status = :active"</c>. Filtering does not reduce the read capacity consumed or the 1 MB read limit, only the data that
    /// is returned; a selective filter produces small or empty pages.
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
    /// Gets the substitutions for <c>#name</c> placeholders in the filter and projection expressions. A placeholder that no
    /// expression uses makes DynamoDB reject the request.
    /// Use it to alias attribute names that are DynamoDB reserved words, such as <c>name</c>, <c>status</c>, <c>size</c>, <c>order</c> or
    /// <c>total</c>, which DynamoDB rejects with a <see cref="ValidationException"/> when an expression uses them directly (the check is
    /// case-insensitive), and names with a dot or other special characters. See the <see href="https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html">list of reserved words</see>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ExpressionAttributeNames { get; init; }

    /// <summary>
    /// Gets the substitutions for <c>:value</c> placeholders in the filter expression. A placeholder that the expression does not
    /// use, or that no value defines, makes DynamoDB reject the request.
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
    /// Gets a value indicating whether to use strongly consistent reads. The default eventually consistent read costs half the
    /// capacity but may miss recent writes. A consistent scan contains every write that completed before it began, but it is not a
    /// snapshot: writes during the scan may or may not be seen. Not supported on global secondary indexes: DynamoDB rejects it with a
    /// <see cref="ValidationException"/>.
    /// </summary>
    public bool ConsistentRead { get; init; }

    /// <summary>
    /// Gets the key of the last item of the previous page: the <see cref="ScanResponse.LastEvaluatedKey"/> of the previous
    /// response, unchanged. In a parallel scan it must be used with the same <see cref="Segment"/> that returned it. No sets are
    /// allowed in it.
    /// </summary>
    public IReadOnlyDictionary<string, AttributeValue>? ExclusiveStartKey { get; init; }

    /// <summary>
    /// Gets the number of segments the scan is divided into, from 1 to 1,000,000; one per worker of a parallel scan. 1 is a
    /// sequential scan. Set it together with <see cref="Segment"/>; the same value must be used for all requests of a scan.
    /// <see langword="null"/> sends nothing.
    /// </summary>
    public int? TotalSegments { get; init; }

    /// <summary>
    /// Gets the zero-based segment this request reads, from 0 to <see cref="TotalSegments"/> minus 1. Set it together with
    /// <see cref="TotalSegments"/>. Each segment is a separate sequence of pages with its own
    /// <see cref="ScanResponse.LastEvaluatedKey"/>. <see langword="null"/> sends nothing.
    /// </summary>
    public int? Segment { get; init; }

    /// <summary>Gets whether to report consumed capacity in <see cref="ScanResponse.ConsumedCapacity"/>.</summary>
    public ReturnConsumedCapacity ReturnConsumedCapacity { get; init; }
}
