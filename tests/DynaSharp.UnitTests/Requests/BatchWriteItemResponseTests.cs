using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class BatchWriteItemResponseTests
{
    [Test]
    public async Task Constructor_NothingSet_UnprocessedItemsIsEmpty()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new BatchWriteItemResponse();

        // Assert
        await Assert.That(response.UnprocessedItems).IsEmpty();
    }

    [Test]
    public async Task Constructor_NothingSet_ItemCollectionMetricsIsEmpty()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new BatchWriteItemResponse();

        // Assert
        await Assert.That(response.ItemCollectionMetrics).IsEmpty();
    }

    [Test]
    public async Task Constructor_NothingSet_ConsumedCapacityIsEmpty()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new BatchWriteItemResponse();

        // Assert
        await Assert.That(response.ConsumedCapacity).IsEmpty();
    }
}
