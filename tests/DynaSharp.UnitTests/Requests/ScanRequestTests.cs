using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class ScanRequestTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_ReadIsEventuallyConsistent()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new ScanRequest { TableName = "Orders" };

        // Assert
        await Assert.That(request.ConsistentRead).IsFalse();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_SelectIsLeftToDynamoDb()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new ScanRequest { TableName = "Orders" };

        // Assert
        await Assert.That(request.Select).IsEqualTo(SelectKind.Unspecified);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ScanIsNotParallel()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new ScanRequest { TableName = "Orders" };

        // Assert
        await Assert.That((request.Segment, request.TotalSegments)).IsEqualTo(((int?)null, (int?)null));
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_LimitIsUnset()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new ScanRequest { TableName = "Orders" };

        // Assert
        await Assert.That(request.Limit).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ConsumedCapacityIsNotRequested()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new ScanRequest { TableName = "Orders" };

        // Assert
        await Assert.That(request.ReturnConsumedCapacity).IsEqualTo(ReturnConsumedCapacity.None);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ExpressionsIndexAndStartKeyAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new ScanRequest { TableName = "Orders" };

        // Assert
        await Assert.That((request.IndexName, request.FilterExpression, request.ProjectionExpression, request.ExclusiveStartKey)).IsEqualTo(((string?)null, (string?)null, (string?)null, (IReadOnlyDictionary<string, AttributeValue>?)null));
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ExpressionMapsAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new ScanRequest { TableName = "Orders" };

        // Assert
        await Assert.That((request.ExpressionAttributeNames, request.ExpressionAttributeValues)).IsEqualTo(((IReadOnlyDictionary<string, string>?)null, (IReadOnlyDictionary<string, AttributeValue>?)null));
    }

    [Test]
    public async Task Constructor_SegmentZero_IsKeptAsASegment()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new ScanRequest { TableName = "Orders", Segment = 0, TotalSegments = 1 };

        // Assert
        await Assert.That(request.Segment).IsEqualTo(0);
    }
}
