using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A nested type with <see cref="OmitEmptyAttribute"/> of its own.</summary>
[DynamoDbDocument]
[OmitEmpty]
internal sealed partial record CardHome
{
    public string? City { get; init; }
}
