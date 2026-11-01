using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

[DynamoDbDocument]
internal sealed partial record Line
{
    public required string Sku { get; init; }

    public required int Quantity { get; init; }
}
