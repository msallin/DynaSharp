using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>A struct that keeps its initializers, which a struct needs a declared constructor for, and only the generated one is.</summary>
[DynamoDbDocument]
internal partial struct Retry
{
    public int Count { get; set; } = 3;

#pragma warning disable DYNA035 // The tests pin that a read keeps the initializer, which is what DYNA035 warns of.
    public string? Mode { get; set; } = "backoff";
#pragma warning restore DYNA035
}
