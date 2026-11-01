using DynaSharp.Mapping;

namespace DynaSharp.AotSmoke;

/// <summary>A document that holds itself.</summary>
[DynamoDbDocument]
internal sealed partial record BasketLine(string Sku)
{
    public List<BasketLine> Parts { get; init; } = [];
}
