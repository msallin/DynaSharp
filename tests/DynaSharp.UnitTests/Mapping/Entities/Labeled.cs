using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>A record struct with a property outside its constructor.</summary>
[DynamoDbDocument]
internal partial record struct Labeled(int Id)
{
    public string? Label { get; set; }

    public string Default { get; set; } = "kept";
}
