using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>Defaults of parameters whose types are narrower than int, which a literal alone does not fit.</summary>
[DynamoDbDocument]
internal sealed partial record Tally(string Name, byte Small = 1, sbyte Signed = -1, short Step = 2, ushort Wide = 3);
