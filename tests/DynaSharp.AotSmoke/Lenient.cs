using DynaSharp.Mapping;

namespace DynaSharp.AotSmoke;

/// <summary>A nullable and a plain property whose converters can count a stored value as missing, one of them explicitly, and a wrapper of [OmitEmpty] around a third.</summary>
[DynamoDbEntity]
[Discriminator("LENIENT")]
internal sealed partial record Lenient
{
    [PartitionKey]
    public required string Id { get; init; }

    [DynamoDbConverter(typeof(LenientPlanConverter))]
    public Plan Plan { get; init; } = Plan.Enterprise;

    [DynamoDbConverter(typeof(LenientPlanConverter))]
    public Plan? MaybePlan { get; init; }

    [DynamoDbConverter(typeof(BlankWeightConverter))]
    public Weight? Weight { get; init; }

    [OmitEmpty]
    [DynamoDbConverter(typeof(DashConverter))]
    public string Text { get; init; } = "initial";

    public int After { get; init; }
}
