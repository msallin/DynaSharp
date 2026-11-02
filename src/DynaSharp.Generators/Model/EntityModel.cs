namespace DynaSharp.Generators;

/// <summary>What an entity has beyond a document.</summary>
/// <param name="Discriminator">The value of the discriminator attribute, or null when the entity opted out.</param>
/// <param name="DiscriminatorAttribute">The attribute that holds it, <c>_type</c> by default.</param>
/// <param name="PartitionKey">The partition key.</param>
/// <param name="SortKey">The sort key, or null.</param>
/// <param name="Indexes">The indexes, global ones first, each in declaration order.</param>
/// <param name="VersionProperty">The <c>[Version]</c> property, by <see cref="PropertyModel.Name"/>, or null.</param>
/// <param name="ComputedInputs">The computed properties with <c>[ComputedFrom]</c>, for what a key says of them at run time and in a build.</param>
internal sealed record EntityModel(
    string? Discriminator,
    string DiscriminatorAttribute,
    KeyModel PartitionKey,
    KeyModel? SortKey,
    EquatableArray<IndexModel> Indexes,
    string? VersionProperty,
    EquatableArray<ComputedInput> ComputedInputs)
{
    /// <summary>Returns every key of the table and its indexes.</summary>
    /// <returns>The keys.</returns>
    public IEnumerable<KeyModel> Keys() =>
        new[] { PartitionKey, SortKey }.Concat(Indexes.SelectMany(static index => new[] { index.PartitionKey, index.SortKey })).OfType<KeyModel>();
}
