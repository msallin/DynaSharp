using System.ComponentModel;
using DynaSharp.Requests;

namespace DynaSharp.Mapping;

/// <summary>A secondary index as an entity type declares it.</summary>
/// <param name="indexName">The name of the index.</param>
/// <param name="partitionKeyName">The attribute name of the partition key of the index. For a local index it is the partition key of the table.</param>
/// <param name="sortKeyName">The attribute name of the sort key of the index, or <see langword="null"/> if it has none.</param>
/// <param name="sparse">Whether the type leaves the key attributes out when an input is missing, so that only some items are in the index.</param>
/// <param name="isLocal">Whether the index is local, which shares the partition key of the table and exists only if the table is created with it.</param>
/// <param name="partitionKeyType">How the partition key is stored. <see cref="ScalarAttributeType.Unknown"/> says nothing, see <see cref="DynamoDbEntityInfo.PartitionKeyType"/>.</param>
/// <param name="sortKeyType">How the sort key is stored, with <see cref="ScalarAttributeType.Unknown"/> if there is none or it does not say.</param>
/// <param name="partitionKeyBuilder">The name of the key builder of the partition key under <c>Keys</c>, or <see langword="null"/> for metadata that does not name it.</param>
/// <param name="include">The attributes the index copies besides the keys and the discriminator, empty for keys only, or <see langword="null"/> for every attribute.</param>
/// <remarks>Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package. A type written by hand that makes one has to follow the package.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class DynamoDbIndexInfo(
    string indexName,
    string partitionKeyName,
    string? sortKeyName,
    bool sparse,
    bool isLocal,
    ScalarAttributeType partitionKeyType,
    ScalarAttributeType sortKeyType,
    string? partitionKeyBuilder,
    IReadOnlyList<string>? include = null)
{
    /// <summary>Gets the name of the index.</summary>
    public string IndexName { get; } = indexName;

    /// <summary>Gets the attribute name of the partition key of the index. For a local index it is the partition key of the table.</summary>
    public string PartitionKeyName { get; } = partitionKeyName;

    /// <summary>Gets the attribute name of the sort key of the index, or <see langword="null"/> if it has none.</summary>
    public string? SortKeyName { get; } = sortKeyName;

    /// <summary>Gets a value indicating whether the type leaves the key attributes out when an input is missing, so that only some items are in the index.</summary>
    public bool Sparse { get; } = sparse;

    /// <summary>Gets a value indicating whether the index is local, which shares the partition key of the table and exists only if the table is created with it.</summary>
    public bool IsLocal { get; } = isLocal;

    /// <summary>Gets how the partition key is stored, see <see cref="DynamoDbEntityInfo.PartitionKeyType"/>.</summary>
    public ScalarAttributeType PartitionKeyType { get; } = partitionKeyType;

    /// <summary>Gets how the sort key is stored, with <see cref="ScalarAttributeType.Unknown"/> if there is none or it does not say.</summary>
    public ScalarAttributeType SortKeyType { get; } = sortKeyType;

    /// <summary>Gets the name of the key builder of the partition key under <c>Keys</c>, for a message that says which one to use, or <see langword="null"/> for metadata that does not name it.</summary>
    public string? PartitionKeyBuilder { get; } = partitionKeyBuilder;

    /// <summary>Gets the attributes the index copies besides the keys and the discriminator, empty for keys only, or <see langword="null"/> for every attribute.</summary>
    public IReadOnlyList<string>? Include { get; } = include;
}
