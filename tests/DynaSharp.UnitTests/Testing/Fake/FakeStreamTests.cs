using System.Numerics;
using DynaSharp.Requests;
using DynaSharp.Streams;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// What the Developer Guide says of DynamoDB Streams: a record per change of an item, the images of the view type, none for a write that changes nothing, 24 hours of retention.
// The sizes are those of the example of the Developer Guide (a table keyed by the number Id): 26 bytes for the INSERT, 59 for the MODIFY and 38 for the REMOVE.
public sealed class FakeStreamTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Constructor_Table_NamesTheStreamAndItsShardAsDynamoDbDoes()
    {
        // Arrange
        var table = Table();

        // Act
        var stream = new FakeStream(table, StreamViewType.KeysOnly, "2026-01-01T00:00:00.000", Start, 7, shardSuffix: 255);

        // Assert
        await Assert.That((stream.Arn, stream.Shards[0].Id, stream.TableName, stream.IsEnabled, stream.Shards[0].EndingSequenceNumber, stream.Shards[0].LastSequenceNumber))
            .IsEqualTo(("arn:aws:dynamodb:us-east-1:123456789012:table/Forum/stream/2026-01-01T00:00:00.000", "shardId-00000001767225600000-000000ff", "Forum", true, (BigInteger?)null, new BigInteger(7)));
    }

    [Test]
    public async Task Commit_InsertModifyRemove_RecordsThemWithTheSizesOfTheDeveloperGuide()
    {
        // Arrange
        var stream = Stream(StreamViewType.NewAndOldImages);
        var inserted = Message("New item!");
        var modified = Message("This item has changed");
        stream.Stage(null, inserted);
        stream.Stage(inserted, modified);
        stream.Stage(modified, null);

        // Act
        stream.Commit(Counter(), Start, byTimeToLive: false);

        // Assert
        await Assert.That(stream.Shards[0].Records.Select(static record => (record.EventName, record.SizeBytes)))
            .IsEquivalentTo([(OperationType.Insert, 26L), (OperationType.Modify, 59L), (OperationType.Remove, 38L)]);
    }

    [Test]
    public async Task Commit_Insert_HoldsTheKeysAndTheNewImage()
    {
        // Arrange
        var stream = Stream(StreamViewType.NewAndOldImages);
        stream.Stage(null, Message("New item!"));

        // Act
        stream.Commit(Counter(), Start, byTimeToLive: false);

        // Assert
        var record = stream.Shards[0].Records.Single();
        await Assert.That((record.Keys, record.NewImage, record.OldImage, record.ApproximateCreationDateTime, record.EventId.Length))
            .IsEqualTo((new Item { ["Id"] = AttributeValue.FromNumber(101) }, Message("New item!"), (Item?)null, Start, 32));
    }

    [Test]
    [Arguments(StreamViewType.KeysOnly, false, false)]
    [Arguments(StreamViewType.NewImage, true, false)]
    [Arguments(StreamViewType.OldImage, false, true)]
    [Arguments(StreamViewType.NewAndOldImages, true, true)]
    public async Task Commit_Modify_HoldsTheImagesOfTheViewType(StreamViewType viewType, bool hasNew, bool hasOld)
    {
        // Arrange
        var stream = Stream(viewType);
        stream.Stage(Message("a"), Message("b"));

        // Act
        stream.Commit(Counter(), Start, byTimeToLive: false);

        // Assert
        var record = stream.Shards[0].Records.Single();
        await Assert.That((record.NewImage is not null, record.OldImage is not null, record.Keys.Count)).IsEqualTo((hasNew, hasOld, 1));
    }

    [Test]
    public async Task Commit_WriteThatChangesNothing_RecordsNothing()
    {
        // Arrange: the Developer Guide: a PutItem or UpdateItem that does not change any data in an item writes no stream record.
        var stream = Stream(StreamViewType.NewAndOldImages);
        stream.Stage(Message("same"), Message("same"));

        // Act
        stream.Commit(Counter(), Start, byTimeToLive: false);

        // Assert
        await Assert.That(stream.Shards[0].Records.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Commit_TwoChanges_TakeAscendingSequenceNumbersAndTheTimeOfTheCommit()
    {
        // Arrange
        var stream = Stream(StreamViewType.KeysOnly);
        stream.Stage(null, Message("a"));
        stream.Stage(Message("a"), Message("b"));

        // Act
        stream.Commit(Counter(), Start.AddSeconds(5), byTimeToLive: true);

        // Assert
        await Assert.That(stream.Shards[0].Records.Select(static record => (record.SequenceNumber, record.ApproximateCreationDateTime, record.ByTimeToLive)))
            .IsEquivalentTo([(new BigInteger(1), Start.AddSeconds(5), true), (new BigInteger(2), Start.AddSeconds(5), true)]);
    }

    [Test]
    public async Task Commit_AgainAfterACommit_RecordsNothingTwice()
    {
        // Arrange
        var stream = Stream(StreamViewType.KeysOnly);
        var next = Counter();
        stream.Stage(null, Message("a"));
        stream.Commit(next, Start, byTimeToLive: false);

        // Act
        stream.Commit(next, Start, byTimeToLive: false);

        // Assert
        await Assert.That(stream.Shards[0].Records.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Discard_StagedChanges_RecordsNone()
    {
        // Arrange
        var stream = Stream(StreamViewType.KeysOnly);
        stream.Stage(null, Message("a"));

        // Act
        stream.Discard();
        stream.Commit(Counter(), Start, byTimeToLive: false);

        // Assert
        await Assert.That(stream.Shards[0].Records.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Stage_ClosedStream_TakesNothing()
    {
        // Arrange
        var stream = Stream(StreamViewType.KeysOnly);
        stream.Close(Start);

        // Act
        stream.Stage(null, Message("a"));
        stream.Commit(Counter(), Start, byTimeToLive: false);

        // Assert
        await Assert.That(stream.Shards[0].Records.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Close_StreamWithRecords_EndsTheShardAtItsLastRecord()
    {
        // Arrange
        var stream = Stream(StreamViewType.KeysOnly);
        stream.Stage(null, Message("a"));
        stream.Commit(() => 42, Start, byTimeToLive: false);

        // Act
        stream.Close(Start.AddHours(1));

        // Assert
        await Assert.That((stream.IsEnabled, stream.ClosedAt, stream.Shards[0].EndingSequenceNumber)).IsEqualTo((false, (DateTimeOffset?)Start.AddHours(1), (BigInteger?)42));
    }

    [Test]
    public async Task Close_Twice_KeepsTheFirstClose()
    {
        // Arrange: an empty shard ends where it starts.
        var stream = Stream(StreamViewType.KeysOnly);
        stream.Close(Start);

        // Act
        stream.Close(Start.AddHours(1));

        // Assert
        await Assert.That((stream.ClosedAt, stream.Shards[0].EndingSequenceNumber)).IsEqualTo(((DateTimeOffset?)Start, (BigInteger?)stream.Shards[0].StartingSequenceNumber));
    }

    [Test]
    [Arguments(false, 48, false)]
    [Arguments(true, 0, false)]
    [Arguments(true, 23, false)]
    [Arguments(true, 24, true)]
    public async Task IsGone_HoursAfterTheClose_IsTrueFrom24Hours(bool closed, int hours, bool gone)
    {
        // Arrange
        var stream = Stream(StreamViewType.KeysOnly);
        if (closed)
        {
            stream.Close(Start);
        }

        // Act
        var result = stream.IsGone(Start.AddHours(hours));

        // Assert
        await Assert.That(result).IsEqualTo(gone);
    }

    [Test]
    [Arguments(0, 0)]
    [Arguments(24, 1)]
    [Arguments(25, 2)]
    public async Task TrimHorizon_RecordsOfTwoHours_TrimsThoseOf24Hours(int hoursLater, int horizon)
    {
        // Arrange: one record at the start and one an hour later.
        var stream = Stream(StreamViewType.KeysOnly);
        var next = Counter();
        stream.Stage(null, Message("a"));
        stream.Commit(next, Start, byTimeToLive: false);
        stream.Stage(Message("a"), Message("b"));
        stream.Commit(next, Start.AddHours(1), byTimeToLive: false);

        // Act
        var result = stream.Shards[0].TrimHorizon(Start.AddHours(hoursLater));

        // Assert
        await Assert.That(result).IsEqualTo(horizon);
    }

    [Test]
    public async Task Constructor_NullTable_ThrowsArgumentNullException()
    {
        // Arrange
        FakeTable table = null!;

        // Act
        var act = () => new FakeStream(table, StreamViewType.KeysOnly, "label", Start, 0, 1);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Commit_NullSequence_ThrowsArgumentNullException()
    {
        // Arrange
        var stream = Stream(StreamViewType.KeysOnly);

        // Act
        var act = () => stream.Commit(null!, Start, byTimeToLive: false);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    private static FakeTable Table() =>
        new("Forum", new KeySchema("Id", ScalarAttributeType.Number), [new AttributeDefinition("Id", ScalarAttributeType.Number)], Start);

    private static FakeStream Stream(StreamViewType viewType) => new(Table(), viewType, "2026-01-01T00:00:00.000", Start, 0, 1);

    private static Item Message(string message) => new() { ["Message"] = message, ["Id"] = AttributeValue.FromNumber(101) };

    private static Func<BigInteger> Counter()
    {
        var next = BigInteger.Zero;
        return () => ++next;
    }
}
