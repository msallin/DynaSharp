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
}
