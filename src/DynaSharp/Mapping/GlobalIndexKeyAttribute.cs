namespace DynaSharp.Mapping;

/// <summary>
/// Declares the keys of a global secondary index. The default attribute names come from the lowercase index name, so index
/// <c>GSI1</c> uses <c>gsi1pk</c> and <c>gsi1sk</c>. Index keys are computed on every full write.
/// </summary>
/// <remarks>
/// <para>
/// A template that is one bare placeholder, with an attribute name equal to that property's attribute, writes nothing extra: the
/// property's own attribute is the index key, so a number stays an <c>N</c> key. An attribute shared with another key, as in an
/// inverted index, is allowed when both use the same template.
/// </para>
/// <para>
/// Computed properties cannot feed an index template: an update cannot know which stored properties a getter reads, so the index
/// would go stale.
/// </para>
/// <para>
/// A timestamp is written in UTC and takes <c>yyyy</c>, <c>yyyy-MM</c>, <c>yyyy-MM-dd</c>, <c>yyyy-MM-ddTHH</c>, <c>yyyy-MM-ddTHH:mm</c>, <c>s</c>, <c>ms</c>, <c>us</c> or <c>ticks</c>. A bare
/// <c>{At}</c> is <c>ms</c>: it keeps milliseconds, so two times within one millisecond give one key, unless the rest of the key differs. Only <c>ticks</c> is lossless.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class GlobalIndexKeyAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="GlobalIndexKeyAttribute"/> class.</summary>
    /// <param name="indexName">The DynamoDB name of the index: 3 to 255 of <c>a-z</c>, <c>A-Z</c>, <c>0-9</c>, <c>_</c>, <c>-</c> and <c>.</c>.</param>
    public GlobalIndexKeyAttribute(string indexName)
    {
        IndexName = indexName;
    }

    /// <summary>Gets the DynamoDB name of the index.</summary>
    public string IndexName { get; }

    /// <summary>Gets or sets the key template of the index partition key. It is required.</summary>
    public string? PartitionKey { get; set; }

    /// <summary>Gets or sets the key template of the index sort key, or null for an index without one.</summary>
    public string? SortKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether items without a value leave the index. When any input of any template of the index is
    /// null, none of the index's key attributes is written, because one alone would be stored uselessly. Nullable inputs require this.
    /// </summary>
    public bool Sparse { get; set; }

    /// <summary>Gets or sets the name of the attribute that holds the index partition key.</summary>
    public string? PartitionKeyAttributeName { get; set; }

    /// <summary>Gets or sets the name of the attribute that holds the index sort key.</summary>
    public string? SortKeyAttributeName { get; set; }
}
