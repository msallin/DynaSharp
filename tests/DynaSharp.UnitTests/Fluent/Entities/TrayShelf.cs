using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A nested type where <see cref="OmitEmptyAttribute"/> on the type covers a collection, and one property takes it back.</summary>
[DynamoDbDocument]
[OmitEmpty]
internal sealed partial record TrayShelf
{
    public List<string> Items { get; init; } = [];

    [OmitEmpty(false)]
    public List<string> Kept { get; init; } = [];
}
