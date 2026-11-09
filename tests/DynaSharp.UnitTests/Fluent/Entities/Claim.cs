using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// Every kind of other key a sparse index can have: one of several inputs, one that repeats an input of the table key, one with an input only a key holds, and one of literals. BySeal holds the seal of every claim, which ByAudit holds only for an audited one.
/// </summary>
[DynamoDbEntity]
[Discriminator("CLAIM")]
[PartitionKey("CLAIM#{Id}")]
[GlobalIndexKey("ByAgent", PartitionKey = "AGENT#{Agent}", SortKey = "{Floor:d3}-{Desk}", Sparse = true)]
[GlobalIndexKey("ByCase", PartitionKey = "HANDLER#{Handler}", SortKey = "CASE#{Id}", Sparse = true)]
[GlobalIndexKey("ByAudit", PartitionKey = "AUDIT#{Auditor}", SortKey = "SEAL#{Seal}", Sparse = true)]
[GlobalIndexKey("ByClosed", PartitionKey = "CLOSED", SortKey = "{ClosedOn}", Sparse = true)]
[GlobalIndexKey("BySeal", PartitionKey = "SEAL#{Seal}")]
internal sealed partial record Claim
{
    public required string Id { get; init; }

    public string? Agent { get; init; }

    public int? Floor { get; init; }

    public string? Desk { get; init; }

    public string? Handler { get; init; }

    public string? Auditor { get; init; }

    [KeyOnly]
    public required string Seal { get; init; }

    public DateOnly? ClosedOn { get; init; }
}
