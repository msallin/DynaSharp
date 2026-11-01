using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

[DynamoDbDocument]
internal sealed partial record LeafB([property: DynamoDbAttribute("b")] string B);
