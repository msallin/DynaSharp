using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>What a visit was announced with. The computed key input of <see cref="Visit"/> reads inside it, which is the case a path by handle has to reach.</summary>
[DynamoDbDocument]
internal sealed partial record VisitMessage
{
    public required DateTimeOffset Arrival { get; init; }

    public string? Sender { get; init; }
}
