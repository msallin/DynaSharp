using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

[DynamoDbDocument]
internal sealed partial record SiblingLeaves([property: DynamoDbAttribute("b")] string B, [property: DynamoDbAttribute("c")] string C);
