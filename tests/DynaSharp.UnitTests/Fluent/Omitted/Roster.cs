using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Every shape of a property that a converter can leave out.</summary>
[DynamoDbEntity]
[Discriminator("ROSTER")]
internal sealed partial record Roster
{
    [PartitionKey]
    public required string Id { get; init; }

    [DynamoDbConverter(typeof(OmittedListConverter))]
    public List<string> Tags { get; init; } = [];

    [DynamoDbConverter(typeof(OmittedListConverter))]
    public List<string>? MaybeTags { get; init; }

    [DynamoDbConverter(typeof(ExplicitZeroConverter))]
    public int Score { get; init; }

    [DynamoDbConverter(typeof(ExplicitZeroConverter))]
    public int? MaybeScore { get; init; }

    [DynamoDbConverter(typeof(ClassZeroConverter))]
    public int Rank { get; init; }

    public Amount Total { get; init; }

    public Amount? MaybeTotal { get; init; }

    [DynamoDbConverter(typeof(NullableHandleConverter<int, ExplicitZeroConverter>))]
    public int Wrapped { get; init; }

    [DynamoDbConverter(typeof(NullableReferenceHandleConverter<List<string>, OmittedListConverter>))]
    public List<string> WrappedTags { get; init; } = [];

    [DynamoDbConverter(typeof(OmitEmptyConverter<StringConverter>))]
    public string Named { get; init; } = "named";

    [OmitEmpty]
    [DynamoDbConverter(typeof(NoneOmittedConverter))]
    public string? Note { get; init; }

    [DynamoDbConverter(typeof(SilentConverter))]
    public int Silent { get; init; }

    public Shelving? Shelf { get; init; }

    public List<Amount> Amounts { get; init; } = [];
}
