namespace DynaSharp.UnitTests;

public sealed class ItemCollectionMetricsTests
{
    [Test]
    public async Task Constructor_NothingSet_ItemCollectionKeyIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var metrics = new ItemCollectionMetrics();

        // Assert
        await Assert.That(metrics.ItemCollectionKey).IsNull();
    }

    [Test]
    public async Task Constructor_NothingSet_SizeEstimateRangeIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var metrics = new ItemCollectionMetrics();

        // Assert
        await Assert.That(metrics.SizeEstimateRangeGB).IsNull();
    }
}
