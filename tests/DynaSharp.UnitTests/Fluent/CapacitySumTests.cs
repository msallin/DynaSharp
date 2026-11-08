using DynaSharp.Fluent;

namespace DynaSharp.UnitTests.Fluent;

public sealed class CapacitySumTests
{
    private static readonly CapacityDetail Two = new() { CapacityUnits = 2 };

    [Test]
    public async Task Add_DetailOfTheTableOnlyInTheSum_KeepsIt()
    {
        // Arrange
        var sum = new ConsumedCapacity { TableName = "Shop", CapacityUnits = 2, Table = Two };
        var next = new ConsumedCapacity { TableName = "Shop", CapacityUnits = 1 };

        // Act
        var total = CapacitySum.Add(sum, next);

        // Assert
        await Assert.That(total?.Table?.CapacityUnits).IsEqualTo(2.0);
    }

    [Test]
    public async Task Add_IndexesOnlyInTheNextResponse_TakesThem()
    {
        // Arrange
        var sum = new ConsumedCapacity { TableName = "Shop", CapacityUnits = 1 };
        var next = new ConsumedCapacity { TableName = "Shop", CapacityUnits = 2, GlobalSecondaryIndexes = new Dictionary<string, CapacityDetail> { ["ByStatus"] = Two } };

        // Act
        var total = CapacitySum.Add(sum, next);

        // Assert
        await Assert.That(total?.GlobalSecondaryIndexes?["ByStatus"].CapacityUnits).IsEqualTo(2.0);
    }
}
