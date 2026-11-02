using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A nested type without <see cref="OmitEmptyAttribute"/> on the type, inside a type that has it, and with it on one property.</summary>
[DynamoDbDocument]
internal sealed partial record CardOffice
{
    public string? City { get; init; }

    [OmitEmpty]
    public string? Room { get; init; }
}
