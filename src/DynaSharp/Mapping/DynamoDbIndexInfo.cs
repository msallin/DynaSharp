namespace DynaSharp.Mapping;

/// <summary>A secondary index as an entity type declares it.</summary>
/// <param name="IndexName">The name of the index.</param>
/// <param name="PartitionKeyName">The attribute name of the partition key of the index.</param>
/// <param name="SortKeyName">The attribute name of the sort key of the index, or <see langword="null"/> if it has none.</param>
/// <param name="Sparse">Whether the type leaves the key attributes out when an input is missing, so that only some items are in the index.</param>
/// <remarks>Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package. A type written by hand that makes one has to follow the package.</remarks>
public sealed record DynamoDbIndexInfo(string IndexName, string PartitionKeyName, string? SortKeyName, bool Sparse);
