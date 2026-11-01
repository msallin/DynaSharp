using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

[DynamoDbDocument]
internal sealed partial record KeyedSet(
    [property: DynamoDbAttribute("pk")] string Pk,
    [property: DynamoDbAttribute("attr")] HashSet<string> Attr);
