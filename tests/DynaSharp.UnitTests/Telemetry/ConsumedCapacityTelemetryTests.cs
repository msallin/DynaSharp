using DynaSharp.Telemetry;

namespace DynaSharp.UnitTests.Telemetry;

public sealed class ConsumedCapacityTelemetryTests
{
    [Test]
    public async Task ToList_NoCapacity_ReturnsNull()
    {
        // Arrange
        ConsumedCapacity? capacity = null;

        // Act
        var list = ConsumedCapacityTelemetry.ToList(capacity);

        // Assert
        await Assert.That(list).IsNull();
    }

    [Test]
    public async Task ToList_Capacity_ReturnsAListWithJustThatCapacity()
    {
        // Arrange
        var capacity = new ConsumedCapacity { CapacityUnits = 1 };

        // Act
        var list = ConsumedCapacityTelemetry.ToList(capacity);

        // Assert
        await Assert.That(list!.Single()).IsSameReferenceAs(capacity);
    }

    [Test]
    public async Task ToAttributeValue_EmptyCapacity_IsAnEmptyObject()
    {
        // Arrange
        IReadOnlyList<ConsumedCapacity> capacities = [new ConsumedCapacity()];

        // Act
        var documents = ConsumedCapacityTelemetry.ToAttributeValue(capacities);

        // Assert
        await Assert.That(documents.Single()).IsEqualTo("{}");
    }

    [Test]
    public async Task ToAttributeValue_EveryMember_FollowsTheShapeOfTheDynamoDbResponse()
    {
        // Arrange
        IReadOnlyList<ConsumedCapacity> capacities =
        [
            new ConsumedCapacity
            {
                TableName = "Orders",
                CapacityUnits = 6,
                ReadCapacityUnits = 4.5,
                WriteCapacityUnits = 1.5,
                Table = new CapacityDetail { CapacityUnits = 2, ReadCapacityUnits = 1, WriteCapacityUnits = 1 },
                LocalSecondaryIndexes = new Dictionary<string, CapacityDetail> { ["byDate"] = new() { CapacityUnits = 1 } },
                GlobalSecondaryIndexes = new Dictionary<string, CapacityDetail> { ["byUser"] = new() { WriteCapacityUnits = 0.5 }, ["byDay"] = new() },
            },
        ];

        // Act
        var documents = ConsumedCapacityTelemetry.ToAttributeValue(capacities);

        // Assert
        await Assert.That(documents.Single()).IsEqualTo(
            """{"CapacityUnits":6,"ReadCapacityUnits":4.5,"WriteCapacityUnits":1.5,"TableName":"Orders","Table":{"CapacityUnits":2,"ReadCapacityUnits":1,"WriteCapacityUnits":1},"LocalSecondaryIndexes":{"byDate":{"CapacityUnits":1}},"GlobalSecondaryIndexes":{"byUser":{"WriteCapacityUnits":0.5},"byDay":{}}}""");
    }

    [Test]
    public async Task ToAttributeValue_TwoCapacities_ReturnsOneDocumentEach()
    {
        // Arrange (an operation on several tables reports one capacity per table)
        IReadOnlyList<ConsumedCapacity> capacities =
        [
            new ConsumedCapacity { TableName = "A", CapacityUnits = 1 },
            new ConsumedCapacity { TableName = "B", CapacityUnits = 2 },
        ];

        // Act
        var documents = ConsumedCapacityTelemetry.ToAttributeValue(capacities);

        // Assert
        await Assert.That(string.Join('|', documents)).IsEqualTo("""{"CapacityUnits":1,"TableName":"A"}|{"CapacityUnits":2,"TableName":"B"}""");
    }
}
