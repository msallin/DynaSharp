using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class DeleteItemResponseTests
{
    [Test]
    public async Task Constructor_NothingSet_AttributesIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new DeleteItemResponse();

        // Assert
        await Assert.That(response.Attributes).IsNull();
    }

    [Test]
    public async Task Constructor_NothingSet_ConsumedCapacityIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new DeleteItemResponse();

        // Assert
        await Assert.That(response.ConsumedCapacity).IsNull();
    }

    [Test]
    public async Task Constructor_NothingSet_ItemCollectionMetricsIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new DeleteItemResponse();

        // Assert
        await Assert.That(response.ItemCollectionMetrics).IsNull();
    }
}
