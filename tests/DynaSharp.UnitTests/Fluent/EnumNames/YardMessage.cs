using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent.EnumNames;

/// <summary>A message of a yard, whose partition key starts with the stored name of its owner, and whose sort key holds the owner as a number, which ignores the name.</summary>
[DynamoDbEntity]
[Discriminator("YARD_MESSAGE")]
[PartitionKey("{Owner}#{OwnerKey:raw}")]
[SortKey("MSG#{Kind:d1}#{Id}")]
internal sealed partial record YardMessage
{
    public required MessageOwner Owner { get; init; }

    [KeyOnly]
    public required string OwnerKey { get; init; }

    [KeyOnly]
    public required MessageOwner Kind { get; init; }

    [KeyOnly]
    public required string Id { get; init; }

    public Priority Priority { get; init; } = Priority.Normal;

    public HashSet<Priority> Seen { get; init; } = [];
}
