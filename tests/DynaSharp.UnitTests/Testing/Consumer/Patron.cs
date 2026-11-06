using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Testing.Consumer;

/// <summary>An entity that declares its table, which the members without a table name open.</summary>
/// <param name="Id">The patron.</param>
/// <param name="Name">How the patron is called.</param>
[DynamoDbEntity(Table = "patrons")]
[Discriminator("PATRON")]
internal sealed partial record Patron(
    [property: PartitionKey(AttributeName = "pk")] string Id,
    string Name);
