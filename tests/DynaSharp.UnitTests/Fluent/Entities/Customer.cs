using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A customer in a table shared with other types: a key of a template over a literal, and two indexes on plain attributes, one of them sparse.</summary>
[DynamoDbEntity]
[Discriminator("CUSTOMER")]
[PartitionKey("CUSTOMER#{Id}")]
[SortKey("PROFILE")]
[GlobalIndexKey("ByEmail", PartitionKey = "{Email}", PartitionKeyAttributeName = "email", Sparse = true)]
[GlobalIndexKey("ByName", PartitionKey = "{FullName}", PartitionKeyAttributeName = "fullName", SortKey = "CUSTOMER#{Id}", SortKeyAttributeName = "pk")]
internal sealed partial record Customer(
    [property: KeyOnly] string Id,
    [property: DynamoDbAttribute("fullName")] string FullName,
    [property: DynamoDbAttribute("orderCount")] int OrderCount = 0,
    [property: DynamoDbAttribute("email")] string? Email = null,
    [property: DynamoDbAttribute("note")] string? Note = null);
