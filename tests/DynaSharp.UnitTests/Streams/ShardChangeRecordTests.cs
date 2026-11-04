using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Streams;

public sealed class ShardChangeRecordTests
{
    private static readonly ChangeRecord Insert = new() { EventName = OperationType.Insert, EventId = "e-1" };

    [Test]
    public async Task Equals_SameShardAndAnEqualRecord_IsTrue()
    {
        // Arrange
        var left = new ShardChangeRecord("shard-1", Insert);
        var right = new ShardChangeRecord("shard-1", Insert with { });

        // Act
        var equal = (left.Equals(right), left == right, left != right, left.Equals((object)right), left.GetHashCode() == right.GetHashCode());

        // Assert
        await Assert.That(equal).IsEqualTo((true, true, false, true, true));
    }

    [Test]
    [Arguments("shard-2", "e-1")]
    [Arguments("shard-1", "e-2")]
    [Arguments("SHARD-1", "e-1")]
    public async Task Equals_AnotherShardOrRecord_IsFalse(string shardId, string eventId)
    {
        // Arrange
        var left = new ShardChangeRecord("shard-1", Insert);
        var right = new ShardChangeRecord(shardId, Insert with { EventId = eventId });

        // Act
        var equal = (left.Equals(right), left == right, left != right);

        // Assert
        await Assert.That(equal).IsEqualTo((false, false, true));
    }

    [Test]
    public async Task Equals_ObjectOfAnotherTypeOrNull_IsFalse()
    {
        // Arrange
        var change = new ShardChangeRecord("shard-1", Insert);

        // Act
        var equal = (change.Equals("shard-1"), change.Equals(null));

        // Assert
        await Assert.That(equal).IsEqualTo((false, false));
    }

    [Test]
    public async Task SequenceNumber_RecordWithOne_IsItsSequenceNumber()
    {
        // Arrange
        var change = new ShardChangeRecord("shard-1", Insert with { DynamoDb = new StreamRecord { SequenceNumber = "300" } });

        // Act
        var sequenceNumber = change.SequenceNumber;

        // Assert
        await Assert.That(sequenceNumber).IsEqualTo("300");
    }

    [Test]
    public async Task SequenceNumber_RecordWithoutStreamRecordOrDefault_IsNull()
    {
        // Arrange
        var change = new ShardChangeRecord("shard-1", Insert);

        // Act
        var sequenceNumbers = (change.SequenceNumber, default(ShardChangeRecord).SequenceNumber);

        // Assert
        await Assert.That(sequenceNumbers).IsEqualTo(((string?)null, (string?)null));
    }

    [Test]
    public async Task Default_EqualsDefault_AndHashesWithoutAShard()
    {
        // Arrange
        var change = default(ShardChangeRecord);

        // Act
        var result = (change.Equals(default), change.GetHashCode() == default(ShardChangeRecord).GetHashCode());

        // Assert
        await Assert.That(result).IsEqualTo((true, true));
    }

    [Test]
    public async Task Deconstruct_Change_GivesTheShardAndTheRecord()
    {
        // Arrange
        var change = new ShardChangeRecord("shard-1", Insert);

        // Act
        var (shardId, record) = change;

        // Assert
        await Assert.That((shardId, ReferenceEquals(record, Insert))).IsEqualTo(("shard-1", true));
    }

    [Test]
    public async Task With_AnotherShard_KeepsTheRecord()
    {
        // Arrange
        var change = new ShardChangeRecord("shard-1", Insert);

        // Act
        var moved = change with { ShardId = "shard-2" };

        // Assert
        await Assert.That((moved.ShardId, ReferenceEquals(moved.Record, Insert))).IsEqualTo(("shard-2", true));
    }

    [Test]
    public async Task ToString_Change_PrintsTheShardAndTheRecordAsARecordDoes()
    {
        // Arrange
        var change = new ShardChangeRecord("shard-1", Insert);

        // Act
        var text = change.ToString();

        // Assert
        await Assert.That(text).IsEqualTo($"ShardChangeRecord {{ ShardId = shard-1, Record = {Insert} }}");
    }
}
