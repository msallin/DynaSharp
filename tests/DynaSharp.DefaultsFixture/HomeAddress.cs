using DynaSharp.Mapping;

namespace DynaSharp.DefaultsFixture;

/// <summary>A nested document, which the defaults reach too.</summary>
[DynamoDbDocument]
public sealed partial record HomeAddress
{
    public string ZipCode { get; init; } = "none";

    public IReadOnlyList<string> StreetLines { get; init; } = [];
}
