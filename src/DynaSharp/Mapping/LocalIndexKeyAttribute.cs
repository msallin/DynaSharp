namespace DynaSharp.Mapping;

/// <summary>
/// Declares the sort key of a local secondary index. A local index shares the partition key of the table, so there is nothing to
/// declare for it, and it requires a table with a sort key. The default attribute name comes from the lowercase index name, so index
/// <c>LSI1</c> uses <c>lsi1sk</c>.
/// </summary>
/// <remarks>The attribute name and the bare placeholder rule work as on <see cref="GlobalIndexKeyAttribute"/>.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class LocalIndexKeyAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="LocalIndexKeyAttribute"/> class.</summary>
    /// <param name="indexName">The DynamoDB name of the index: 3 to 255 of <c>a-z</c>, <c>A-Z</c>, <c>0-9</c>, <c>_</c>, <c>-</c> and <c>.</c>.</param>
    /// <param name="sortKey">The key template of the index sort key.</param>
    public LocalIndexKeyAttribute(string indexName, string sortKey)
    {
        IndexName = indexName;
        SortKey = sortKey;
    }

    /// <summary>Gets the DynamoDB name of the index.</summary>
    public string IndexName { get; }

    /// <summary>Gets the key template of the index sort key.</summary>
    public string SortKey { get; }

    /// <summary>
    /// Gets or sets a value indicating whether items without a value leave the index. When any input of the template is null, the key
    /// attribute is not written. Nullable inputs require this.
    /// </summary>
    public bool Sparse { get; set; }

    /// <summary>Gets or sets the name of the attribute that holds the index sort key.</summary>
    public string? SortKeyAttributeName { get; set; }
}
