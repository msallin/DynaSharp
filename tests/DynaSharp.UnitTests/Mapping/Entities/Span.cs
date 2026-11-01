using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>A positional record struct.</summary>
[DynamoDbDocument]
internal readonly partial record struct Span(int Start, int End = 10);
