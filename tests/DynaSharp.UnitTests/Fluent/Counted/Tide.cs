using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An enum whose converter is on the type, which goes for its elements in a list or a set too.</summary>
[DynamoDbConverter(typeof(TideConverter))]
internal enum Tide
{
    Slack,
    High,
    Low,
}
