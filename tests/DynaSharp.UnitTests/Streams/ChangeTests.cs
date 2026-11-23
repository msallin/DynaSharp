using DynaSharp.Streams;
using DynaSharp.UnitTests.Fluent.Mix;

namespace DynaSharp.UnitTests.Streams;

public sealed class ChangeTests
{
    [Test]
    public async Task Id_RecordWithoutAnEventId_IsTheSequenceNumberOfTheRecord()
    {
        // Arrange
        var record = new ChangeRecord { DynamoDb = new StreamRecord { SequenceNumber = "100" } };
        var change = new Change<Alpha> { Kind = OperationType.Insert, Record = record };

        // Act
        var id = change.Id;

        // Assert
        await Assert.That(id).IsEqualTo("100");
    }

    [Test]
    public async Task At_RecordWithATime_IsTheTimeOfTheRecord()
    {
        // Arrange
        var at = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
        var change = new Change<Alpha> { Kind = OperationType.Insert, Record = new ChangeRecord { DynamoDb = new StreamRecord { ApproximateCreationDateTime = at } } };

        // Act
        var value = change.At;

        // Assert
        await Assert.That(value).IsEqualTo(at);
    }

    [Test]
    public async Task KeysAndSequenceNumber_Record_AreThoseOfTheRecord()
    {
        // Arrange
        var keys = new Item { ["pk"] = "a" };
        var change = new Change<Alpha> { Kind = OperationType.Insert, Record = new ChangeRecord { DynamoDb = new StreamRecord { Keys = keys, SequenceNumber = "101" } } };

        // Act
        var values = (change.Keys, change.SequenceNumber);

        // Assert
        await Assert.That(values).IsEqualTo(((IReadOnlyDictionary<string, AttributeValue>?)keys, "101"));
    }

    [Test]
    public async Task KeysAndSequenceNumber_RecordWithoutData_AreNull()
    {
        // Arrange
        var change = new Change<Alpha> { Kind = OperationType.Insert, Record = new ChangeRecord() };

        // Act
        var values = (change.Keys, change.SequenceNumber);

        // Assert
        await Assert.That(values).IsEqualTo(((IReadOnlyDictionary<string, AttributeValue>?)null, (string?)null));
    }
}
