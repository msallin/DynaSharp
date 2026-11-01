using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

internal enum TicketState
{
    Open,
    Closed,
}

/// <summary>
/// A generated entity with a version and every way a property can feed a key: a composite table key, an index whose sort key has two inputs, a sparse one, and a
/// local one.
/// </summary>
[DynamoDbEntity]
[Discriminator("TICKET")]
[PartitionKey("TICKET#{Id}")]
[SortKey("AT#{OpenedAt}")]
[GlobalIndexKey("ByState", PartitionKey = "STATE#{State}", SortKey = "{Rank:d3}-{Queue}")]
[GlobalIndexKey("ByOwner", PartitionKey = "OWNER#{Owner}", SortKey = "DUE#{Due}", Sparse = true)]
[LocalIndexKey("ByTitle", "TITLE#{Title}")]
internal sealed partial record Ticket
{
    public required string Id { get; init; }

    public required DateOnly OpenedAt { get; init; }

    public TicketState State { get; init; }

    public string Queue { get; init; } = "main";

    public int Rank { get; init; }

    public string? Owner { get; init; }

    public DateOnly? Due { get; init; }

    public string Title { get; init; } = "untitled";

    public string? Note { get; init; }

    public int Votes { get; init; }

    public Address? Place { get; init; }

    [Version]
    public int Revision { get; init; }
}
