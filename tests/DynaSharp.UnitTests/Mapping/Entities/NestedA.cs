using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

[DynamoDbDocument]
internal sealed partial record NestedA
{
    [DynamoDbAttribute("a")]
    public required SiblingLeaves A { get; init; }
}
