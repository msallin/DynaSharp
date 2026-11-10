using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Serialization;
using static DynaSharp.Expr;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>
/// <c>Select(handle)</c> against what a service answers, on a table that holds several entities: a customer and its orders share a partition, so a read has to tell them apart. What comes back
/// for a projection that matches nothing is the service's to say, which is why these run here and not on a stub.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class SelectTests(Backend backend)
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid First = new("00000000-0000-0000-0000-000000000001");

    private static readonly Guid Second = new("00000000-0000-0000-0000-000000000002");

    private static readonly Order.Keys.PrimaryKey FirstOrder = Order.Keys.Of("alice", Noon, First);

    [Test]
    public async Task Get_ItemWithTheAttribute_GivesTheValue()
    {
        // Arrange
        await using var shop = await SeededAsync();

        // Act
        var total = await shop.Orders.Get(FirstOrder).Consistent().Select(Order.Attrs.Total).SendAsync();

        // Assert
        await Assert.That((total.Found, total.Value)).IsEqualTo((true, 12.5m));
    }

    [Test]
    public async Task Get_NoItem_IsNotFoundForAHandleThatCannotBeNull()
    {
        // Arrange
        await using var shop = await SeededAsync();

        // Act
        var total = await shop.Orders.Get(Order.Keys.Of("alice", Noon, Guid.Empty)).Consistent().Select(Order.Attrs.Total).SendAsync();

        // Assert
        await Assert.That(total.Found).IsFalse();
    }

    [Test]
    public async Task Get_ItemWithoutANullableAttribute_IsFoundWithNull()
    {
        // Arrange
        await using var shop = await SeededAsync();

        // Act
        var tracking = await shop.Orders.Get(FirstOrder).Consistent().Select(Order.Attrs.TrackingId).SendAsync();

        // Assert
        await Assert.That((tracking.Found, tracking.Value)).IsEqualTo((true, (Guid?)null));
    }

    [Test]
    public async Task Get_ItemWithoutAnAttributeOnATableWithoutAnEntity_IsFoundWithNull()
    {
        // Arrange (no discriminator comes back beside the attribute, so the projection matches nothing of the item)
        await using var shop = await SeededAsync();
        var pk = Order.Keys.Pk.Format("alice");
        var sk = Order.Keys.Sk.Format(Noon, First);

        // Act
        var note = await shop.Shop.Get(pk, sk).Consistent().Select(new Attr<int?, NullableConverter<int, NumberConverter<int>>>("Nope")).SendAsync();

        // Assert
        await Assert.That((note.Found, note.Value)).IsEqualTo((true, (int?)null));
    }

    [Test]
    public async Task Get_ItemWithoutAnAttributeThatCannotBeNull_ThrowsAMappingFailure()
    {
        // Arrange
        await using var shop = await SeededAsync();
        var pk = Order.Keys.Pk.Format("alice");
        var sk = Order.Keys.Sk.Format(Noon, First);

        // Act
        var failure = await Items.FailureOfAsync<DynamoDbMappingException>(async () => await shop.Shop.Get(pk, sk).Consistent().Select(Attr<int>("Nope")).SendAsync());

        // Assert
        await Assert.That((failure.AttributeName, failure.Key?.GetString("pk"))).IsEqualTo(("Nope", pk));
    }

    [Test]
    public async Task Get_ItemOfAnotherType_ThrowsAsTheTypedReadDoes()
    {
        // Arrange (the keys of the customer, read as an order)
        await using var shop = await SeededAsync();
        var orders = shop.Orders;
        var pk = Customer.Keys.Pk.Format("alice");
        var sk = Customer.Keys.Sk.Format();

        // Act
        var selected = await Items.FailureOfAsync<DynamoDbMappingException>(async () => await orders.Get(new StoredKey<Order>(pk, sk)).Consistent().Select(Order.Attrs.Quantity).SendAsync());
        var typed = await Items.FailureOfAsync<DynamoDbMappingException>(async () => await orders.Get(new StoredKey<Order>(pk, sk)).Consistent().ItemAsync());

        // Assert
        await Assert.That((selected.Message, selected.AttributeName)).IsEqualTo((typed.Message, typed.AttributeName));
    }

    [Test]
    public async Task Query_PartitionOfTwoEntities_GivesTheValuesOfItsOwnType()
    {
        // Arrange
        await using var shop = await SeededAsync();

        // Act
        var totals = await Read.ToListAsync(shop.Orders.Query(Order.Keys.Pk.Of("alice")).Consistent().Select(Order.Attrs.Total).ValuesAsync());
        var names = await Read.ToListAsync(shop.Customers.Query(Customer.Keys.Pk.Of("alice")).Consistent().Select(Customer.Attrs.Name).ValuesAsync());

        // Assert
        await Assert.That((totals.Count, totals.Sum(), names.Single())).IsEqualTo((2, 19.5m, "Alice"));
    }

    [Test]
    public async Task Query_PartitionOfTwoEntities_SendsOnlyTheAttribute()
    {
        // Arrange
        await using var shop = await SeededAsync();

        // Act
        var json = shop.Orders.Query(Order.Keys.Pk.Of("alice")).Select(Order.Attrs.Total).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ProjectionExpression\":\"#n2\"");
    }

    [Test]
    public async Task Query_ClausesBeforeTheSelect_Apply()
    {
        // Arrange
        await using var shop = await SeededAsync();

        // Act
        var totals = await Read.ToListAsync(shop.Orders.Query(Order.Keys.Pk.Of("alice")).Filter(Order.Attrs.Quantity.Gt(1)).Descending().Consistent().Select(Order.Attrs.Total).ValuesAsync());

        // Assert
        await Assert.That(totals.Single()).IsEqualTo(7m);
    }

    [Test]
    public async Task ValueAsync_NewestFirst_GivesTheFirstValue()
    {
        // Arrange
        await using var shop = await SeededAsync();

        // Act
        var newest = await shop.Orders.Query(Order.Keys.Pk.Of("alice")).Descending().Limit(1).Consistent().Select(Order.Attrs.Total).ValueAsync();

        // Assert
        await Assert.That((newest.Found, newest.Value)).IsEqualTo((true, 7m));
    }

    [Test]
    public async Task ValueAsync_NoMatch_IsNotFound()
    {
        // Arrange
        await using var shop = await SeededAsync();

        // Act
        var none = await shop.Orders.Query(Order.Keys.Pk.Of("nobody")).Select(Order.Attrs.Total).ValueAsync();

        // Assert
        await Assert.That(none.Found).IsFalse();
    }

    [Test]
    public async Task PageAsync_OnePerPage_GoesOnWithTheToken()
    {
        // Arrange
        await using var shop = await SeededAsync();

        // Act
        var first = await shop.Orders.Query(Order.Keys.Pk.Of("alice")).Limit(1).Consistent().Select(Order.Attrs.Total).PageAsync();
        var second = await shop.Orders.Query(Order.Keys.Pk.Of("alice")).Limit(2).Consistent().Select(Order.Attrs.Total).PageAsync(first.Next);

        // Assert
        await Assert.That((first.Items.Single(), first.HasMore, second.Items.Single())).IsEqualTo((12.5m, true, 7m));
    }

    [Test]
    public async Task Get_SeveralAttributes_GivesThemAsATuple()
    {
        // Arrange
        await using var shop = await SeededAsync();

        // Act
        var order = await shop.Orders.Get(FirstOrder).Consistent().Select(Order.Attrs.Total).And(Order.Attrs.TrackingId).SendAsync();

        // Assert
        await Assert.That((order.Found, order.Value)).IsEqualTo((true, (12.5m, (Guid?)null)));
    }

    [Test]
    public async Task Query_SeveralAttributesInAPartitionOfTwoEntities_GivesTheTuplesOfItsOwnType()
    {
        // Arrange
        await using var shop = await SeededAsync();

        // Act
        var orders = await Read.ToListAsync(shop.Orders.Query(Order.Keys.Pk.Of("alice")).Consistent().Select(Order.Attrs.Total).And(Order.Attrs.TrackingId).ValuesAsync());

        // Assert
        await Assert.That((orders.Count, orders.Sum(static order => order.Item1), orders.Count(static order => order.Item2 == Second))).IsEqualTo((2, 19.5m, 1));
    }

    [Test]
    public async Task Scan_TableOfTwoEntities_GivesTheValuesOfItsOwnType()
    {
        // Arrange
        await using var shop = await SeededAsync();

        // Act
        var totals = await Read.ToListAsync(shop.Orders.Scan().Consistent().Select(Order.Attrs.Total).ValuesAsync());

        // Assert
        await Assert.That(totals.Sum()).IsEqualTo(19.5m);
    }

    [Test]
    public async Task Scan_PagesOfTwoEntities_CountOnlyItsOwnType()
    {
        // Arrange
        await using var shop = await SeededAsync();

        // Act
        var page = await shop.Orders.Scan().Consistent().Select(Order.Attrs.Total).PageAsync();

        // Assert
        await Assert.That((page.Count, page.ScannedCount)).IsEqualTo((2, 3));
    }

    [Test]
    public async Task Scan_NullableAttribute_GivesNullForAnItemWithoutIt()
    {
        // Arrange
        await using var shop = await SeededAsync();

        // Act
        var tracking = await Read.ToListAsync(shop.Orders.Scan().Consistent().Select(Order.Attrs.TrackingId).ValuesAsync());

        // Assert
        await Assert.That((tracking.Count, tracking.Count(static id => id is null), tracking.Contains(Second))).IsEqualTo((2, 1, true));
    }

    [Test]
    public async Task Query_IndexKeyedByTheDiscriminator_ReadsTheValueAndProjectsTheDiscriminator()
    {
        // Arrange
        await using var shop = await SeededAsync();
        var byType = shop.Customers.Index(ShopSandbox.ByType, "_type", "sk");

        // Act
        var names = await Read.ToListAsync(byType.QueryByDiscriminator().Select(Customer.Attrs.Name).ValuesAsync());

        // Assert
        await Assert.That(names.Single()).IsEqualTo("Alice");
    }

    [Test]
    public async Task Query_IndexKeyedByTheDiscriminatorAtAnotherValue_ThrowsAsTheTypedReadDoes()
    {
        // Arrange
        await using var shop = await SeededAsync();
        var byType = shop.Customers.Index(ShopSandbox.ByType, "_type", "sk");

        // Act
        var selected = await Items.FailureOfAsync<DynamoDbMappingException>(async () => await Read.ToListAsync(byType.Query(new PartitionKey<Customer>("ORDER")).Select(Customer.Attrs.OrderCount).ValuesAsync()));
        var typed = await Items.FailureOfAsync<DynamoDbMappingException>(async () => await Read.ToListAsync(byType.Query(new PartitionKey<Customer>("ORDER")).ItemsAsync()));

        // Assert
        await Assert.That((selected.Message, selected.AttributeName)).IsEqualTo((typed.Message, typed.AttributeName));
    }

    [Test]
    public async Task Get_NestedPath_ReadsThroughTheMap()
    {
        // Arrange
        await using var shop = await SeededAsync();

        // Act
        var city = await shop.Orders.Get(Order.Keys.Of("alice", Noon.AddHours(1), Second)).Consistent().Select(Order.Attrs.ShipTo.At(Address.Attrs.City)).SendAsync();

        // Assert
        await Assert.That(city.Value).IsEqualTo("Bern");
    }

    [Test]
    public async Task Get_NestedPathWithoutItsMap_FollowsTheHandleOfTheLeaf()
    {
        // Arrange
        await using var shop = await SeededAsync();
        var orders = shop.Orders;

        // Act
        var failure = await Items.FailureOfAsync<DynamoDbMappingException>(async () => await orders.Get(FirstOrder).Consistent().Select(Order.Attrs.ShipTo.At(Address.Attrs.City)).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("ShipTo.City");
    }

    [Test]
    public async Task Get_HandleOfAConverter_ReadsThroughIt()
    {
        // Arrange (a Put wrote the order, so its sort key input has an attribute; DYNA209 reports a handle of it, and a variable is no such place)
        await using var shop = await SeededAsync();
        var placedAt = Order.Attrs.PlacedAt;

        // Act
        var status = await shop.Orders.Get(FirstOrder).Consistent().Select(Order.Attrs.Status).SendAsync();
        var placed = await shop.Orders.Get(FirstOrder).Consistent().Select(placedAt).SendAsync();

        // Assert
        await Assert.That((status.Value, placed.Value)).IsEqualTo((OrderStatus.Paid, Noon));
    }

    [Test]
    public async Task Get_EmptySet_IsEmpty()
    {
        // Arrange
        await using var shop = await SeededAsync();

        // Act
        var tags = await shop.Orders.Get(FirstOrder).Consistent().Select(Order.Attrs.Tags).SendAsync();

        // Assert
        await Assert.That(tags.Value.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Get_EmptyStringWithOmitEmpty_ReadsAsNullWhenAWriteLeftItOut()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var memos = shop.Shop.Of<Memo>();
        await memos.Put(new Memo { Id = "m1", Text = string.Empty }).SendAsync();

        // Act
        var text = await memos.Get(Memo.Keys.Of("m1")).Consistent().Select(Memo.Attrs.Text).SendAsync();

        // Assert
        await Assert.That((text.Found, text.Value)).IsEqualTo((true, (string?)null));
    }

    [Test]
    public async Task Put_EmptyListThatItsConverterStoresAsASet_LeavesItOutAndWorks()
    {
        // Arrange (DynamoDB rejects {"SS":[]}, so this request only gets through without the attribute)
        await using var shop = await ShopSandbox.StartAsync(backend);
        var things = shop.Shop.Of<LabeledThing>();

        // Act
        await things.Put(new LabeledThing { Id = "a" }).SendAsync();
        await things.Put(new LabeledThing { Id = "b", Tags = ["red"] }).SendAsync();
        var empty = await things.Get(LabeledThing.Keys.Of("a")).Consistent().ItemAsync();
        var filled = await things.Get(LabeledThing.Keys.Of("b")).Consistent().ItemAsync();

        // Assert
        await Assert.That((empty!.Tags.Count, filled!.Tags.Single())).IsEqualTo((0, "red"));
    }

    [Test]
    public async Task Get_KeyInputOfAnItemAnUpdateCreated_ThrowsWhereTheEntityRestoresIt()
    {
        // Arrange (DYNA209 reports this where it sees a handle, and a variable is no such place)
        await using var shop = await ShopSandbox.StartAsync(backend);
        var visits = shop.Shop.Of<DailyVisits>();
        var key = DailyVisits.Keys.Of("/home", new DateOnly(2026, 11, 1));
        await visits.Update(key).Add(DailyVisits.Attrs.Count, 3L).SendAsync();
        var path = DailyVisits.Attrs.Path;

        // Act
        var failure = await Items.FailureOfAsync<DynamoDbMappingException>(async () => await visits.Get(key).Consistent().Select(path).SendAsync());
        var entity = await visits.Get(key).Consistent().ItemAsync();

        // Assert
        await Assert.That((failure.AttributeName, entity?.Path)).IsEqualTo(("Path", "/home"));
    }

    [Test]
    public async Task Get_KeyInputOfAnItemAPutWrote_GivesTheValue()
    {
        // Arrange
        await using var shop = await SeededAsync();
        var customerId = Order.Attrs.CustomerId;

        // Act
        var customer = await shop.Orders.Get(FirstOrder).Consistent().Select(customerId).SendAsync();

        // Assert
        await Assert.That(customer.Value).IsEqualTo("alice");
    }

    private async Task<ShopSandbox> SeededAsync()
    {
        var shop = await ShopSandbox.StartAsync(backend);
        await shop.Customers.Put(new Customer { Id = "alice", Name = "Alice" }).SendAsync();
        await shop.Orders.Put(new Order { CustomerId = "alice", PlacedAt = Noon, OrderId = First, Status = OrderStatus.Paid, Total = 12.5m, Quantity = 1 }).SendAsync();
        await shop.Orders.Put(new Order
        {
            CustomerId = "alice",
            PlacedAt = Noon.AddHours(1),
            OrderId = Second,
            Status = OrderStatus.Shipped,
            Total = 7m,
            Quantity = 2,
            TrackingId = Second,
            ShipTo = new Address("Bern", "3000"),
        }).SendAsync();
        return shop;
    }
}
