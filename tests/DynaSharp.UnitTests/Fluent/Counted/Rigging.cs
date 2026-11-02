using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A document bound through its constructor: a parameter with a default keeps it, and one without has nothing to fall back on.</summary>
/// <param name="Keel">A parameter without a default.</param>
/// <param name="Mast">A parameter with a default.</param>
[DynamoDbDocument]
internal sealed partial record Rigging(
    [property: DynamoDbConverter(typeof(LenientPositionConverter))] Position Keel,
    [property: DynamoDbConverter(typeof(LenientPositionConverter))] Position Mast = Position.Stern);
