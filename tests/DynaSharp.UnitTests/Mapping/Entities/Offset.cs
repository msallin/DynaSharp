using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>Defaults of negative zero, whose sign a literal without a suffix would lose.</summary>
[DynamoDbDocument]
internal sealed partial record Offset(string Name, double Value = -0.0, float Scale = -0f);
