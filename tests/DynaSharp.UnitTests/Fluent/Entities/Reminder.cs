using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A sparse index with a computed input that can be null, which decides as any nullable input does.</summary>
[DynamoDbEntity]
[Discriminator("REMINDER")]
[PartitionKey("REMINDER#{Id}")]
[GlobalIndexKey("ByDue", PartitionKey = "QUEUE#{Queue}", SortKey = "{DueDay}", Sparse = true)]
internal sealed partial record Reminder
{
    public required string Id { get; init; }

    public string? Queue { get; init; }

    public DateTimeOffset? Due { get; init; }

    [ComputedFrom(nameof(Due))]
    public DateOnly? DueDay => Due is { } due ? DateOnly.FromDateTime(due.UtcDateTime) : null;
}
