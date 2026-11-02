using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A nested document: it has no key, so it is no entity.</summary>
[DynamoDbDocument]
internal sealed partial record Address([property: DynamoDbAttribute("city")] string City);
