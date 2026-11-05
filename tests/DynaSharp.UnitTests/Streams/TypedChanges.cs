using DynaSharp.Requests;
using DynaSharp.Streams;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Fluent.Mix;

namespace DynaSharp.UnitTests.Streams;

/// <summary>Records of a table of the entities of a mix, and a table with a stream on a fake, for the tests of typed changes.</summary>
internal static class TypedChanges
{
    /// <summary>Gets the keys every record of these tests has.</summary>
    public static Item RecordKeys => new() { ["pk"] = "a", ["sk"] = "1" };

    /// <summary>Gets an item that says it is a beta and lacks its required <c>Amount</c>, which a beta cannot read.</summary>
    public static Item BrokenBeta => new() { ["pk"] = "a", ["sk"] = "1", ["_type"] = "BETA" };

    /// <summary>An alpha at the key of <see cref="RecordKeys"/>.</summary>
    public static Alpha AlphaValue(string name = "first") => new() { Pk = "a", Sk = "1", Name = name };

    /// <summary>A beta at the key of <see cref="RecordKeys"/>.</summary>
    public static Beta BetaValue(int amount = 7) => new() { Pk = "a", Sk = "1", Amount = amount };

    /// <summary>Makes a record with the keys of <see cref="RecordKeys"/>, the sequence number 101 and the given images.</summary>
    public static ChangeRecord Record(OperationType name, Item? oldImage, Item? newImage, bool withKeys = true) => new()
    {
        EventName = name,
        DynamoDb = new StreamRecord
        {
            Keys = withKeys ? RecordKeys : null,
            OldImage = oldImage,
            NewImage = newImage,
            SequenceNumber = "101",
            StreamViewType = StreamViewType.NewAndOldImages,
        },
    };

    /// <summary>Makes a fake on a fixed clock with the table <paramref name="name"/>, keyed by <c>pk</c> and <c>sk</c>, with a stream of the view.</summary>
    public static FakeDynamoDb Shop(string name, StreamViewType view = StreamViewType.NewAndOldImages)
    {
        var fake = new FakeDynamoDb(new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        fake.CreateTable(new CreateTableRequest
        {
            TableName = name,
            AttributeDefinitions = [new("pk", ScalarAttributeType.String), new("sk", ScalarAttributeType.String)],
            KeySchema = [new("pk", KeyType.Hash), new("sk", KeyType.Range)],
            BillingMode = BillingMode.PayPerRequest,
            StreamSpecification = new StreamSpecification { StreamEnabled = true, StreamViewType = view },
        });
        return fake;
    }
}
