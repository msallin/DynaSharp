using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A nested type, whose writer is generated like that of an entity.</summary>
[DynamoDbDocument]
internal sealed partial record Shelving
{
    [DynamoDbConverter(typeof(OmittedListConverter))]
    public List<string> Labels { get; init; } = [];
}
