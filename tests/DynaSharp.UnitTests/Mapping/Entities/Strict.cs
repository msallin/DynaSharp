using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>Required members in every position, and a nullable one that must not throw.</summary>
[DynamoDbDocument]
internal sealed partial class Strict
{
    public required string Name { get; init; }

#pragma warning disable DYNA035 // The tests pin that a read keeps the initializer, which is what DYNA035 warns of.
    public required string? Maybe { get; init; } = "kept";
#pragma warning restore DYNA035

    public required HashSet<string> Must { get; init; }
}
