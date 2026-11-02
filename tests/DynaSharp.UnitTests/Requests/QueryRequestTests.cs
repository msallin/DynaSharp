using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class QueryRequestTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_ReadIsEventuallyConsistent()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new QueryRequest { TableName = "Orders", KeyConditionExpression = "pk = :pk" };

        // Assert
        await Assert.That(request.ConsistentRead).IsFalse();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_SelectIsLeftToDynamoDb()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new QueryRequest { TableName = "Orders", KeyConditionExpression = "pk = :pk" };

        // Assert
        await Assert.That(request.Select).IsEqualTo(SelectKind.Unspecified);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_LimitAndDirectionAreUnset()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new QueryRequest { TableName = "Orders", KeyConditionExpression = "pk = :pk" };

        // Assert
        await Assert.That((request.Limit, request.ScanIndexForward)).IsEqualTo(((int?)null, (bool?)null));
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ConsumedCapacityIsNotRequested()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new QueryRequest { TableName = "Orders", KeyConditionExpression = "pk = :pk" };

        // Assert
        await Assert.That(request.ReturnConsumedCapacity).IsEqualTo(ReturnConsumedCapacity.None);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ExpressionsIndexAndStartKeyAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new QueryRequest { TableName = "Orders", KeyConditionExpression = "pk = :pk" };

        // Assert
        await Assert.That((request.IndexName, request.FilterExpression, request.ProjectionExpression, request.ExclusiveStartKey)).IsEqualTo(((string?)null, (string?)null, (string?)null, (IReadOnlyDictionary<string, AttributeValue>?)null));
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ExpressionMapsAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new QueryRequest { TableName = "Orders", KeyConditionExpression = "pk = :pk" };

        // Assert
        await Assert.That((request.ExpressionAttributeNames, request.ExpressionAttributeValues)).IsEqualTo(((IReadOnlyDictionary<string, string>?)null, (IReadOnlyDictionary<string, AttributeValue>?)null));
    }

    [Test]
    public async Task Constructor_ZeroLimit_IsKeptForDynamoDbToValidate()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new QueryRequest { TableName = "Orders", KeyConditionExpression = "pk = :pk", Limit = 0 };

        // Assert
        await Assert.That(request.Limit).IsEqualTo(0);
    }
}
