using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

#pragma warning disable DYNA033 // The read that throws for an unknown name here is what is under test.

/// <summary>A <c>required</c> member of a lenient enum, which has nothing to fall back on.</summary>
[DynamoDbEntity]
[Discriminator("SQUALL")]
internal sealed partial record Squall
{
    [PartitionKey]
    public required string Id { get; init; }

    public required Wind Wind { get; init; }
}
