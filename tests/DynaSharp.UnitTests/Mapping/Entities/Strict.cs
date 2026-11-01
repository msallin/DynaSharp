using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>Required members in every position, and a nullable one that must not throw.</summary>
[DynamoDbDocument]
internal sealed partial class Strict
{
    public required string Name { get; init; }

    public required string? Maybe { get; init; } = "kept";

    public required HashSet<string> Must { get; init; }
}
