using DynaSharp.Requests;

namespace DynaSharp.Mapping;

/// <summary>A secondary index as an entity type declares it.</summary>
/// <param name="IndexName">The name of the index.</param>
/// <param name="PartitionKeyName">The attribute name of the partition key of the index. For a local index it is the partition key of the table.</param>
/// <param name="SortKeyName">The attribute name of the sort key of the index, or <see langword="null"/> if it has none.</param>
/// <param name="Sparse">Whether the type leaves the key attributes out when an input is missing, so that only some items are in the index.</param>
/// <param name="IsLocal">Whether the index is local, which shares the partition key of the table and exists only if the table is created with it.</param>
/// <param name="PartitionKeyType">How the partition key is stored. <see cref="ScalarAttributeType.Unknown"/> says nothing, see <see cref="DynamoDbEntityInfo.PartitionKeyType"/>.</param>
/// <param name="SortKeyType">How the sort key is stored, with <see cref="ScalarAttributeType.Unknown"/> if there is none or it does not say.</param>
/// <remarks>Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package. A type written by hand that makes one has to follow the package.</remarks>
public sealed record DynamoDbIndexInfo(
    string IndexName,
    string PartitionKeyName,
    string? SortKeyName,
    bool Sparse,
    bool IsLocal = false,
    ScalarAttributeType PartitionKeyType = ScalarAttributeType.Unknown,
    ScalarAttributeType SortKeyType = ScalarAttributeType.Unknown)
{
    /// <summary>Gets the name of the key builder of the partition key under <c>Keys</c>, for a message that says which one to use, or <see langword="null"/> for a plain key, which has none.</summary>
    public string? PartitionKeyBuilder { get; init; }
}
