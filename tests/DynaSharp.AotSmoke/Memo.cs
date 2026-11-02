using DynaSharp.Mapping;

namespace DynaSharp.AotSmoke;

/// <summary>A generated entity with <see cref="OmitEmptyAttribute"/>: a handle whose converter wraps another through a type parameter, which AOT has to keep.</summary>
[DynamoDbEntity]
[Discriminator("MEMO")]
[PartitionKey("MEMO#{Id}")]
[OmitEmpty]
internal sealed partial record Memo
{
    public required string Id { get; init; }

    public string? Text { get; init; }

    public List<string> Lines { get; init; } = [];

    public HashSet<string> Labels { get; init; } = [];

    public string[] Names { get; init; } = [];
}
