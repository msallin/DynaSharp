using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class BatchWriteItemRequestTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_ConsumedCapacityIsNotRequested()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new BatchWriteItemRequest { RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>() };

        // Assert
        await Assert.That(request.ReturnConsumedCapacity).IsEqualTo(ReturnConsumedCapacity.None);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ItemCollectionMetricsAreNotRequested()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new BatchWriteItemRequest { RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>() };

        // Assert
        await Assert.That(request.ReturnItemCollectionMetrics).IsEqualTo(ReturnItemCollectionMetrics.None);
    }
}
