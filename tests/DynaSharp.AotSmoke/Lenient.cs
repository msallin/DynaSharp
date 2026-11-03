using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.AotSmoke;

/// <summary>A nullable and a plain property whose converters can count a stored value as missing, one of them explicitly, a wrapper of [OmitEmpty] around a third, and the wrapper of the library around a built-in.</summary>
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

    [DynamoDbConverter(typeof(EmptyStringAsMissingConverter<DateTimeOffset, OffsetTimestampConverter>))]
    public DateTimeOffset? Shipped { get; init; }

    public int After { get; init; }
}
