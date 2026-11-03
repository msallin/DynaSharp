using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

#pragma warning disable DYNA033 // The read that throws for an unknown name here is what is under test.

/// <summary>A plain key of a lenient enum, which is read for what it is: an entity with its initial value here would be an entity with another key.</summary>
[DynamoDbEntity]
[Discriminator("VANE")]
internal sealed partial record Vane
{
    [PartitionKey]
    public Wind Direction { get; init; } = Wind.Unknown;
}
