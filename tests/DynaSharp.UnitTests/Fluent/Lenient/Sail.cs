using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

#pragma warning disable DYNA033 // The read that throws for an unknown name here is what is under test.

/// <summary>A document bound through its constructor: a parameter with a default keeps it, and one without has nothing to fall back on.</summary>
/// <param name="Trim">A parameter without a default.</param>
/// <param name="Reef">A parameter with a default.</param>
[DynamoDbDocument]
internal sealed partial record Sail(Wind Trim, Wind Reef = Wind.Gale);
