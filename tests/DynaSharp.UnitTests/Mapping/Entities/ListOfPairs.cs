using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

[DynamoDbDocument]
internal sealed partial record ListOfPairs([property: DynamoDbAttribute("l")] IReadOnlyList<Pair> L);
