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
/// A computed property feeds an index template only with <see cref="ComputedFromAttribute"/>: an update cannot know which stored properties a getter reads, so that attribute names them, and without it the
/// index would go stale.
/// </para>
/// <para>
/// A timestamp is written in UTC: a <see cref="DateTime"/> of kind <see cref="DateTimeKind.Local"/> is converted, and one of kind <see cref="DateTimeKind.Unspecified"/>, which <c>new DateTime(2026, 1, 15)</c> and <c>default</c> have, names no instant, so the write throws <see cref="KeyFormatException"/>.
/// Give it <see cref="DateTimeKind.Utc"/>, or use a <see cref="DateOnly"/>, which has no kind and is written as <c>yyyy-MM-dd</c>. A timestamp takes <c>yyyy</c>, <c>yyyy-MM</c>, <c>yyyy-MM-dd</c>, <c>yyyy-MM-ddTHH</c>, <c>yyyy-MM-ddTHH:mm</c>, <c>s</c>, <c>ms</c>, <c>us</c> or <c>ticks</c>. A bare
/// <c>{At}</c> is <c>ticks</c>, seven fraction digits as in the attribute: it is the only lossless format, so a read can restore the property from the key. Every other format truncates, and
/// <c>{At:ms}</c> is the one that matches keys the AWS SDK for .NET wrote.
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
    /// <remarks>
    /// An item is in an index only with both its keys. So on a sparse index an update that sets one key and does not produce the other throws <see cref="InvalidOperationException"/> when it is sent, naming the other key.
    /// On an index that is not sparse the same update writes the key it can compute and throws nothing: an item that exists keeps its other key, and an item the update creates stays out of the index. An update that can
    /// create an item sets every input of the index, ends in <c>.IfExists()</c>, or is a <c>Put</c>.
    /// </remarks>
    public bool Sparse { get; set; }

    /// <summary>Gets or sets the name of the attribute that holds the index partition key.</summary>
    public string? PartitionKeyAttributeName { get; set; }

    /// <summary>Gets or sets the name of the attribute that holds the index sort key.</summary>
    public string? SortKeyAttributeName { get; set; }

    /// <summary>
    /// Gets or sets the properties, by name, that the index copies besides the keys and the discriminator; an empty list copies keys only, and <see langword="null"/>, the default, copies every attribute.
    /// A typed read of the index needs every required property, so leave one out only for an index you read with <c>Select</c> or without a model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What <c>CreateTable()</c> makes of it.</b> An index copies what its projection names into its own storage on every write of the item, and a read of it returns only that. A list of names makes the projection
    /// <c>INCLUDE</c> with their attributes and the discriminator, an empty list <c>KEYS_ONLY</c> (or <c>INCLUDE</c> of the discriminator alone), and <see langword="null"/> <c>ALL</c>. The keys of the table and of the index are in
    /// every projection. Entities that share an index copy the attributes that any of them names, and one that declares the index without this list makes it <c>ALL</c>.
    /// </para>
    /// <para>
    /// <b>Fixed once created.</b> DynamoDB does not change the projection of an index: deleting the index and creating it again does. <c>CreateTable().IfNotExists()</c> reports an index whose projection differs from
    /// this list as drift, and <c>FailOnDrift()</c> throws for it.
    /// </para>
    /// <para>
    /// <b>Checked.</b> A name that is no stored property is <see href="https://msallin.github.io/DynaSharp/diagnostics.html#entities">DYNA018</see>, and a list that leaves out a property a read requires is
    /// <see href="https://msallin.github.io/DynaSharp/diagnostics.html#entities">DYNA047</see>.
    /// </para>
    /// </remarks>
    public string[]? Include { get; set; }
}
