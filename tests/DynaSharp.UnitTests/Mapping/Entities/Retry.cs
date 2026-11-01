using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>A struct that keeps its initializers, which a struct needs a declared constructor for, and only the generated one is.</summary>
[DynamoDbDocument]
internal partial struct Retry
{
    public int Count { get; set; } = 3;

    public string? Mode { get; set; } = "backoff";
}
