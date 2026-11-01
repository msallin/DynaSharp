using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class GetItemRequestTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_ReadIsEventuallyConsistent()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new GetItemRequest { TableName = "Orders", Key = new Dictionary<string, AttributeValue>() };

        // Assert
        await Assert.That(request.ConsistentRead).IsFalse();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ConsumedCapacityIsNotRequested()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new GetItemRequest { TableName = "Orders", Key = new Dictionary<string, AttributeValue>() };

        // Assert
        await Assert.That(request.ReturnConsumedCapacity).IsEqualTo(ReturnConsumedCapacity.None);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_OptionalMembersAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new GetItemRequest { TableName = "Orders", Key = new Dictionary<string, AttributeValue>() };

        // Assert
        await Assert.That((request.ProjectionExpression, request.ExpressionAttributeNames)).IsEqualTo(((string?)null, (IReadOnlyDictionary<string, string>?)null));
    }
}
