namespace DynaSharp.Generators;

/// <summary>What an entity has beyond a document.</summary>
/// <param name="Discriminator">The value of the discriminator attribute, or null when the entity opted out.</param>
/// <param name="DiscriminatorAttribute">The attribute that holds it, <c>_type</c> by default.</param>
/// <param name="PartitionKey">The partition key.</param>
/// <param name="SortKey">The sort key, or null.</param>
/// <param name="Indexes">The indexes, global ones first, each in declaration order.</param>
/// <param name="VersionProperty">The <c>[Version]</c> property, by <see cref="PropertyModel.Name"/>, or null.</param>
/// <param name="ComputedInputs">The computed properties with <c>[ComputedFrom]</c>, for what a key says of them at run time and in a build.</param>
/// <param name="TimeToLiveAttribute">The attribute of the <c>[TimeToLive]</c> property, or null.</param>
/// <param name="TableName">The table the entity declares, its own or the default of its assembly, or null.</param>
internal sealed record EntityModel(
    string? Discriminator,
    string DiscriminatorAttribute,
    KeyModel PartitionKey,
    KeyModel? SortKey,
    EquatableArray<IndexModel> Indexes,
    string? VersionProperty,
    EquatableArray<ComputedInput> ComputedInputs,
    string? TimeToLiveAttribute,
    string? TableName)
{
    /// <summary>Returns the keys of the table, the partition key first.</summary>
    /// <returns>The keys.</returns>
    public IEnumerable<KeyModel> TableKeys() => SortKey is null ? [PartitionKey] : [PartitionKey, SortKey];

    /// <summary>Returns every key of the table and its indexes.</summary>
    /// <returns>The keys.</returns>
    public IEnumerable<KeyModel> Keys() => TableKeys().Concat(Indexes.SelectMany(static index => index.Keys()));

    /// <summary>Whether a property feeds a key of the table, itself or through a computed input.</summary>
    /// <param name="property">The property.</param>
    /// <returns>Whether an update of it by handle throws, because DynamoDB cannot update a key.</returns>
    public bool FeedsTableKey(string property) =>
        TableKeys().Any(key => key.Holds(property) || ComputedInputs.Any(input => input.Dependencies.Contains(property) && key.Holds(input.Property)));

    /// <summary>The keys of indexes that a computed input feeds, for that input and for each property it is derived from.</summary>
    /// <param name="property">A property a handle writes.</param>
    /// <returns>The computed input, the index and the key, for each key the property reaches through a getter.</returns>
    public IEnumerable<(string Input, string Index, KeyModel Key)> IndexKeysDerivedFrom(string property) =>
        from input in ComputedInputs.Where(input => input.Property == property || input.Dependencies.Contains(property))
        from index in Indexes
        from key in index.Keys()
        where key.Holds(input.Property)
        select (input.Property, index.IndexName, key);
}
