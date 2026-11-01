using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Mapping;
using DynaSharp.IntegrationTests.Support;
using static DynaSharp.Expr;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>Several entity types in one read on the emulators: the adjacency list of a customer, an overloaded global index, a scan, and the items of other types that the filter drops.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class MixedReadTests(Backend backend)
{
    private const string AlicePartition = "CUSTOMER#alice";

    private static readonly DateTimeOffset Noon = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ItemsAsync_AdjacencyList_ReadsEveryItemAsItsType()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await PutAliceAsync(sandbox);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Query(AlicePartition).Of<Customer, Order>().ItemsAsync());

        // Assert (ORDER# sorts before PROFILE)
        await Assert.That(string.Join(",", items.Select(static item => item.Match(static customer => customer.Name, static order => order.Total.ToString(System.Globalization.CultureInfo.InvariantCulture))))).IsEqualTo("12.5,20.5,Alice");
    }

    [Test]
    public async Task PageAsync_ItemOfAnotherType_IsReadAndFilteredOut()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await PutAliceAsync(sandbox);
        await sandbox.Shop.Put(new Item { ["pk"] = AlicePartition, ["sk"] = "NOTE#1", ["_type"] = "NOTE" }).SendAsync();

        // Act
        var page = await sandbox.Shop.Query(AlicePartition).Of<Customer, Order>().PageAsync();

        // Assert
        await Assert.That((page.Count, page.ScannedCount)).IsEqualTo((3, 4));
    }

    [Test]
    public async Task ItemsAsync_ThreeTypes_ReadsEveryItemAsItsType()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await PutAliceAsync(sandbox);
        await sandbox.Shop.Of<Refund>().Put(NewRefund("alice", "r1", 3m)).SendAsync();

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Query(AlicePartition).Of<Customer, Order, Refund>().ItemsAsync());

        // Assert
        await Assert.That((items.Count(static item => item.Is(out Order? _)), items.Count(static item => item.Is(out Refund? _)), items.Count(static item => item.Is(out Customer? _)))).IsEqualTo((2, 1, 1));
    }

    [Test]
    public async Task ItemsAsync_OverloadedGlobalIndex_ReadsBothTypesInTheOrderOfTheIndex()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await PutAliceAsync(sandbox);
        await sandbox.Shop.Of<Refund>().Put(NewRefund("alice", "r1", 15m)).SendAsync();
        await sandbox.Shop.Of<Refund>().Put(NewRefund("bob", "r2", 30m)).SendAsync();

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Index("GSI1", "gsi1pk", "gsi1sk").Query("STATUS#Paid").Of<Order, Refund>().ItemsAsync());

        // Assert
        await Assert.That(string.Join(",", items.Select(Show))).IsEqualTo("O12.5,R15,O20.5,R30");
    }

    [Test]
    public async Task ItemsAsync_IndexWithTheDiscriminatorAsPartitionKey_ReadsThatType()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await PutAliceAsync(sandbox);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Index(ShopSandbox.ByType, "_type", "sk").Query("ORDER").Of<Customer, Order>().ItemsAsync());

        // Assert
        await Assert.That(items.Count(static item => item.Is(out Order? _))).IsEqualTo(2);
    }

    [Test]
    public async Task ItemsAsync_IndexWithTheDiscriminatorAsSortKey_ReadsThePartition()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await PutAliceAsync(sandbox);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Index(ShopSandbox.ByPartitionAndType, "pk", "_type").Query(AlicePartition).Of<Customer, Order>().ItemsAsync());

        // Assert (CUSTOMER sorts before ORDER)
        await Assert.That((items.Count, items[0].Is(out Customer? _))).IsEqualTo((3, true));
    }

    [Test]
    public async Task ItemsAsync_IndexWithTheDiscriminatorAsSortKeyAndACondition_ReadsWhatTheConditionSays()
    {
        // Arrange (the condition is what keeps a third type out, because this query can have no filter on the discriminator)
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await PutAliceAsync(sandbox);
        await sandbox.Shop.Put(new Item { ["pk"] = AlicePartition, ["sk"] = "VOUCHER#1", ["_type"] = "VOUCHER" }).SendAsync();

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Index(ShopSandbox.ByPartitionAndType, "pk", "_type").Query(AlicePartition, SortKey.Between("CUSTOMER", "ORDER")).Of<Customer, Order>().ItemsAsync());

        // Assert
        await Assert.That(items.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ItemsAsync_IndexWithTheDiscriminatorAsSortKeyAndAThirdTypeInThePartition_Throws()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await PutAliceAsync(sandbox);
        await sandbox.Shop.Put(new Item { ["pk"] = AlicePartition, ["sk"] = "NOTE#1", ["_type"] = "NOTE" }).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<DynamoDbMappingException>(() => Read.ToListAsync(sandbox.Shop.Index(ShopSandbox.ByPartitionAndType, "pk", "_type").Query(AlicePartition).Of<Customer, Order>().ItemsAsync()));

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task ItemsAsync_ScanOfAnIndexWithTheDiscriminatorAsPartitionKey_KeepsTheFilter()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await PutAliceAsync(sandbox);
        await sandbox.Shop.Of<Refund>().Put(NewRefund("alice", "r1", 3m)).SendAsync();

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Index(ShopSandbox.ByType, "_type", "sk").Scan().Of<Customer, Order>().ItemsAsync());

        // Assert
        await Assert.That(items.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ItemsAsync_Filter_HoldsBesideTheDiscriminators()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await PutAliceAsync(sandbox);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Query(AlicePartition).Filter(Attr("Total").Gt(15m)).Of<Customer, Order>().Filter($"{Attr("Quantity")} = {0}").ItemsAsync());

        // Assert
        await Assert.That(items.Single().Is(out Order? order) && order.Total == 20.5m).IsTrue();
    }

    [Test]
    public async Task ItemsAsync_Projection_ReadsWhatTheTypesNeedOfIt()
    {
        // Arrange: a projection of the required attributes, to which the read adds the discriminator and the keys
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await sandbox.Shop.Of<Refund>().Put(NewRefund("alice", "r1", 15m)).SendAsync();
        await sandbox.Customers.Put(new Customer { Id = "alice", Name = "Alice" }).SendAsync();

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Query(AlicePartition).Of<Customer, Refund>().Project("Name", "Id", "CustomerId", "RefundId", "Status", "Total").ItemsAsync());

        // Assert
        await Assert.That(items.Select(static item => item.Match(static customer => customer.Name, static refund => refund.RefundId)).Order(StringComparer.Ordinal)).IsEquivalentTo(["Alice", "r1"]);
    }

    [Test]
    public async Task PageAsync_Limit_ContinuesWithTheToken()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await PutAliceAsync(sandbox);

        // Act
        var first = await sandbox.Shop.Query(AlicePartition).Of<Customer, Order>().Limit(2).PageAsync();
        var second = await sandbox.Shop.Query(AlicePartition).Of<Customer, Order>().Limit(2).PageAsync(ContinuationToken.Parse(first.Next.ToString()));

        // Assert
        await Assert.That((first.Count, second.Count, second.Items[0].Is(out Customer? _))).IsEqualTo((2, 1, true));
    }

    [Test]
    public async Task PagesAsync_Descending_GivesEveryPage()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await PutAliceAsync(sandbox);

        // Act
        var pages = await Read.ToListAsync(sandbox.Shop.Query(AlicePartition).Of<Customer, Order>().Descending().Limit(1).PagesAsync());

        // Assert
        await Assert.That((pages.Sum(static page => page.Count), pages[0].Items[0].Is(out Customer? _))).IsEqualTo((3, true));
    }

    [Test]
    public async Task ItemsAsync_Scan_ReadsTheTypesOfTheWholeTable()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await PutAliceAsync(sandbox);
        await sandbox.Shop.Put(new Item { ["pk"] = "X", ["sk"] = "Y", ["_type"] = "NOTE" }).SendAsync();

        // Act
        var plain = await Read.ToListAsync(sandbox.Shop.Scan().Of<Customer, Order>().ItemsAsync());
        var parallel = await Read.ToListAsync(sandbox.Shop.Scan().Of<Customer, Order>().Parallel(2).ItemsAsync());

        // Assert
        await Assert.That((plain.Count, parallel.Count)).IsEqualTo((3, 3));
    }

    private static string Show(Mixed<Order, Refund> item) =>
        item.Match(static order => "O" + order.Total.ToString(System.Globalization.CultureInfo.InvariantCulture), static refund => "R" + refund.Total.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static Refund NewRefund(string customer, string id, decimal total) => new() { CustomerId = customer, RefundId = id, Status = OrderStatus.Paid, Total = total };

    private static async Task PutAliceAsync(ShopSandbox sandbox)
    {
        await sandbox.Customers.Put(new Customer { Id = "alice", Name = "Alice" }).SendAsync();
        await sandbox.Orders.Put(new Order { CustomerId = "alice", PlacedAt = Noon, OrderId = new Guid("11111111-1111-1111-1111-111111111111"), Status = OrderStatus.Paid, Total = 12.5m }).SendAsync();
        await sandbox.Orders.Put(new Order { CustomerId = "alice", PlacedAt = Noon.AddDays(1), OrderId = new Guid("22222222-2222-2222-2222-222222222222"), Status = OrderStatus.Paid, Total = 20.5m }).SendAsync();
    }
}
