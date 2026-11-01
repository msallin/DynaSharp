using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class TransactWriteItemsResponseTests
{
    [Test]
    public async Task Constructor_NothingSet_ConsumedCapacityIsEmpty()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new TransactWriteItemsResponse();

        // Assert
        await Assert.That(response.ConsumedCapacity).IsEmpty();
    }

    [Test]
    public async Task Constructor_NothingSet_ItemCollectionMetricsIsEmpty()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new TransactWriteItemsResponse();

        // Assert
        await Assert.That(response.ItemCollectionMetrics).IsEmpty();
    }
}
