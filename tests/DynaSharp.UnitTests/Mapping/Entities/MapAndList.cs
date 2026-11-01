using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

[DynamoDbDocument]
internal sealed partial record MapAndList(
    [property: DynamoDbAttribute("a")] LeafB A,
    [property: DynamoDbAttribute("l")] IReadOnlyList<Pair> L);
