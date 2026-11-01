using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>A document that holds itself, to store nesting at depth.</summary>
[DynamoDbDocument]
internal sealed partial class Parcel
{
    public string Label { get; set; } = string.Empty;

    public List<Parcel> Contents { get; set; } = [];
}
