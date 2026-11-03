using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>A document in <see cref="Logbook"/>, whose own write names its own property.</summary>
[DynamoDbDocument]
public sealed partial record LogEntry
{
    public DateTime At { get; init; } = Logbook.Utc;
}
