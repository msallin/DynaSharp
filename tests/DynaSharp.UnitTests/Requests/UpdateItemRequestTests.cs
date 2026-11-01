using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class UpdateItemRequestTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_ReturnsNothing()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new UpdateItemRequest { TableName = "Orders", Key = new Dictionary<string, AttributeValue>() };

        // Assert
        await Assert.That(request.ReturnValues).IsEqualTo(ReturnValue.None);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ReturnsNoItemOnConditionFailure()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new UpdateItemRequest { TableName = "Orders", Key = new Dictionary<string, AttributeValue>() };

        // Assert
        await Assert.That(request.ReturnValuesOnConditionCheckFailure).IsEqualTo(ReturnValuesOnConditionCheckFailure.None);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ConsumedCapacityIsNotRequested()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new UpdateItemRequest { TableName = "Orders", Key = new Dictionary<string, AttributeValue>() };

        // Assert
        await Assert.That(request.ReturnConsumedCapacity).IsEqualTo(ReturnConsumedCapacity.None);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ItemCollectionMetricsAreNotRequested()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new UpdateItemRequest { TableName = "Orders", Key = new Dictionary<string, AttributeValue>() };

        // Assert
        await Assert.That(request.ReturnItemCollectionMetrics).IsEqualTo(ReturnItemCollectionMetrics.None);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ConditionIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new UpdateItemRequest { TableName = "Orders", Key = new Dictionary<string, AttributeValue>() };

        // Assert
        await Assert.That(request.ConditionExpression).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ExpressionMapsAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new UpdateItemRequest { TableName = "Orders", Key = new Dictionary<string, AttributeValue>() };

        // Assert
        await Assert.That((request.ExpressionAttributeNames, request.ExpressionAttributeValues)).IsEqualTo(((IReadOnlyDictionary<string, string>?)null, (IReadOnlyDictionary<string, AttributeValue>?)null));
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_UpdateExpressionIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new UpdateItemRequest { TableName = "Orders", Key = new Dictionary<string, AttributeValue>() };

        // Assert
        await Assert.That(request.UpdateExpression).IsNull();
    }
}
