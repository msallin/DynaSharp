namespace DynaSharp.IntegrationTests.Support;

/// <summary>A global secondary index of a <see cref="TableSpec"/>, over attributes of any name and key type, all attributes projected.</summary>
/// <param name="Name">The name of the index.</param>
/// <param name="PartitionKey">The attribute of its partition key.</param>
/// <param name="PartitionKeyType">The type of its partition key.</param>
/// <param name="SortKey">The attribute of its sort key, or <see langword="null"/> for an index without one.</param>
/// <param name="SortKeyType">The type of its sort key.</param>
internal sealed record GlobalIndexSpec(
    string Name,
    string PartitionKey,
    KeyAttributeType PartitionKeyType = KeyAttributeType.String,
    string? SortKey = null,
    KeyAttributeType SortKeyType = KeyAttributeType.String);
