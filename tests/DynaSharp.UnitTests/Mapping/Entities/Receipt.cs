using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>A converter that implements its interface explicitly, as a plain key, as a value and as a nullable value.</summary>
[DynamoDbEntity]
[Discriminator("RECEIPT")]
internal sealed partial record Receipt
{
    [PartitionKey]
    [DynamoDbConverter(typeof(ExplicitCentsConverter))]
    public required Cents Id { get; init; }

    [DynamoDbConverter(typeof(ExplicitCentsConverter))]
    public Cents Net { get; init; }

    [DynamoDbConverter(typeof(ExplicitCentsConverter))]
    public Cents? Gross { get; init; }
}
