namespace DynaSharp.Generators;

/// <summary>A global or local secondary index.</summary>
/// <param name="IndexName">The DynamoDB name.</param>
/// <param name="PartitionKey">The partition key. For a local index it is the table's own, which the table already writes.</param>
/// <param name="SortKey">The sort key, or null.</param>
/// <param name="Sparse">Whether all key attributes of the index are left out when any input is null.</param>
/// <param name="IsLocal">Whether the index is local.</param>
/// <param name="NullableInputs">The properties, by <see cref="PropertyModel.Name"/>, that decide whether a sparse index is written.</param>
/// <param name="Include">The attributes the projection names besides the keys and the discriminator, or null for all of them.</param>
internal sealed record IndexModel(
    string IndexName,
    KeyModel PartitionKey,
    KeyModel? SortKey,
    bool Sparse,
    bool IsLocal,
    EquatableArray<string> NullableInputs,
    EquatableArray<string>? Include)
{
    /// <summary>Returns the keys of the index, the partition key first.</summary>
    /// <returns>The keys.</returns>
    public IEnumerable<KeyModel> Keys() => SortKey is null ? [PartitionKey] : [PartitionKey, SortKey];

    /// <summary>Returns the keys the index writes itself: those of <see cref="Keys"/> but the partition key of a local index, which is the table's.</summary>
    /// <returns>The keys.</returns>
    public IEnumerable<KeyModel> OwnKeys() => IsLocal ? Keys().Skip(1) : Keys();
}
