using DynaSharp.Mapping;

namespace DynaSharp.AotSmoke;

/// <summary>An entity mapped by the generator, with what AOT has to see through: a list, sets, a dictionary, enums, a custom converter and recursion.</summary>
[DynamoDbEntity]
[Discriminator("BASKET")]
internal sealed partial record Basket
{
    [PartitionKey(AttributeName = "pk")]
    public required string Id { get; init; }

    public required Priority Priority { get; init; }

    public IReadOnlyList<BasketLine> Lines { get; init; } = [];

    public HashSet<string> Tags { get; init; } = [];

    public HashSet<Priority> Seen { get; init; } = [];

    public Dictionary<string, int> Counts { get; init; } = [];

    [DynamoDbConverter(typeof(WeightConverter))]
    public Weight Weight { get; init; }
}
