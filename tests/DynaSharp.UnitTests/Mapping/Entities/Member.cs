using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>A positional record with defaults and a property outside the constructor, which needs both constructors.</summary>
[DynamoDbEntity]
[Discriminator("MEMBER")]
internal sealed partial record Member(
    [property: PartitionKey] string Id,
    string Name,
    int Age = 18,
    decimal Limit = 1.5m,
    string Motto = "hello \"world\"",
    Status Stage = Status.Closed,
    string? Nick = null)
{
    public string Extra { get; init; } = "initial";
}
