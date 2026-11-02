using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>
/// Indexes that have the discriminator as a key. DynamoDB refuses a filter on a key attribute of what a query reads, so the filter of a typed query
/// stays out, while a scan takes it on any attribute.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class DiscriminatorKeyTests(Backend backend)
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Query_IndexWithTheDiscriminatorAsPartitionKey_ReadsTheItemsOfThatValue()
    {
        // Arrange
        await using var shop = await SeededAsync();
        var byType = shop.Customers.Index(ShopSandbox.ByType, "_type", "sk");

        // Act
        var customers = await Read.ToListAsync(byType.Query("CUSTOMER").ItemsAsync());

        // Assert
        await Assert.That(customers.Single().Id).IsEqualTo("alice");
    }

    [Test]
    public async Task Query_IndexWithTheDiscriminatorAsSortKey_ReadsTheConditionOnIt()
    {
        // Arrange
        await using var shop = await SeededAsync();
        var byType = shop.Orders.Index(ShopSandbox.ByPartitionAndType, "pk", "_type");

        // Act
        var orders = await Read.ToListAsync(byType.Query(Order.Keys.Pk.Format("alice"), SortKeyCondition.Eq("ORDER")).ItemsAsync());

        // Assert
        await Assert.That(orders.Single().CustomerId).IsEqualTo("alice");
    }

    [Test]
    public async Task Scan_IndexWithTheDiscriminatorAsPartitionKey_KeepsTheFilter()
    {
        // Arrange
        await using var shop = await SeededAsync();
        var byType = shop.Customers.Index(ShopSandbox.ByType, "_type", "sk");

        // Act
        var customers = await Read.ToListAsync(byType.Scan().ItemsAsync());

        // Assert
        await Assert.That(customers.Single().Id).IsEqualTo("alice");
    }

    private async Task<ShopSandbox> SeededAsync()
    {
        var shop = await ShopSandbox.StartAsync(backend);
        await shop.Customers.Put(new Customer { Id = "alice", Name = "Alice" }).SendAsync();
        await shop.Orders.Put(new Order { CustomerId = "alice", PlacedAt = Noon, OrderId = Guid.Empty, Status = OrderStatus.Paid, Total = 1m }).SendAsync();
        return shop;
    }
}
