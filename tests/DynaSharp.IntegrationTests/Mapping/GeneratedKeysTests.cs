using DynaSharp.IntegrationTests.Backends;
using DynaSharp.Serialization;
using TUnit.Assertions.Enums;
using static DynaSharp.Expr;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>
/// A realistic single-table model on both emulators: customers and their orders in one partition, composite keys, a global index, a sparse one
/// and a local one, addressed through <c>Keys</c> and <c>Attrs</c>, with everything generated.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class GeneratedKeysTests(Backend backend)
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid First = new("11111111-1111-1111-1111-111111111111");

    private static readonly Guid Second = new("22222222-2222-2222-2222-222222222222");

    private static readonly Guid Tracking = new("33333333-3333-3333-3333-333333333333");

    private static readonly decimal[] Amounts = [100m, -5.5m, 0m, -9_999_999_999.99m, 12.49m, -1000.5m, 0.01m, -12.49m, 9_999_999_999.99m, -0.01m, 12.5m, -12.5m];

    private static Attr<string, StringConverter> ShipToCity => Order.Attrs.ShipTo.At(Address.Attrs.City);

    [Test]
    public async Task Get_GeneratedKey_ReadsWhatPutWrote()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var order = NewOrder("alice", First) with { Quantity = 3, ShipTo = new Address("Bern", "3000"), Tags = ["gift"] };
        await shop.Orders.Put(order).SendAsync();

        // Act
        var read = (await shop.Orders.Get(Order.Keys.Of("alice", order.PlacedAt, First)).Consistent().SendAsync()).Item!;

        // Assert
        await Assert.That((read.OrderId, read.Quantity, read.ShipTo, read.Tags.Single())).IsEqualTo((First, 3, new Address("Bern", "3000"), "gift"));
    }

    [Test]
    public async Task Get_NamedArguments_FindsTheSameItem()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First)).SendAsync();

        // Act
        var read = await shop.Orders.Get(Order.Keys.Of(orderId: First, placedAt: Noon, customerId: "alice")).Consistent().SendAsync();

        // Assert
        await Assert.That(read.Found).IsTrue();
    }

    [Test]
    public async Task Put_Order_StoresEveryKeyAttributeOfTheBuilders()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var order = NewOrder("alice", First) with { Total = -12.5m, TrackingId = Tracking, ShippedAt = Noon };

        // Act
        await shop.Orders.Put(order).SendAsync();
        var stored = (await shop.Shop.Get(Order.Keys.Pk.Format("alice"), Order.Keys.Sk.Format(Noon, First)).Consistent().SendAsync()).Item!;

        // Assert
        await Assert.That((stored.GetString("gsi1pk"), stored.GetString("gsi1sk"), stored.GetString("gsi2pk"), stored.GetString("gsi2sk"), stored.GetString("lsi1sk")))
            .IsEqualTo(("STATUS#Paid", "N9999999987.49", "TRACK#33333333-3333-3333-3333-333333333333", "2026-10-31T12:00:00.000Z", "TOTAL#N9999999987.49"));
    }

    [Test]
    public async Task Put_StoredKeys_ParseBackToTheValues()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First)).SendAsync();

        // Act
        var stored = (await shop.Shop.Get(Order.Keys.Pk.Format("alice"), Order.Keys.Sk.Format(Noon, First)).Consistent().SendAsync()).Item!;
        var parts = Order.Keys.Sk.Parse(stored.GetString("sk")!);

        // Assert
        await Assert.That((parts.PlacedAt, parts.OrderId, Order.Keys.Pk.Parse(stored.GetString("pk")!).CustomerId)).IsEqualTo((Noon, First, "alice"));
    }

    [Test]
    public async Task Query_ItemsOfAPartition_ShowsTheLiteralSortKeyBesideTheOrders()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Customers.Put(new Customer { Id = "alice", Name = "Alice" }).SendAsync();
        await shop.Orders.Put(NewOrder("alice", First)).SendAsync();

        // Act
        var partition = await shop.Shop.Query(Customer.Keys.Pk.Format("alice")).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(partition.Select(item => item.GetString("_type")!)).IsEquivalentTo(["ORDER", "CUSTOMER"]);
    }

    [Test]
    public async Task Query_TypedTable_SeesOnlyItsTypeInASharedPartition()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Customers.Put(new Customer { Id = "alice", Name = "Alice" }).SendAsync();
        await shop.Orders.Put(NewOrder("alice", First)).SendAsync();

        // Act
        var orders = await shop.Orders.Query(Order.Keys.Pk.Format("alice")).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(orders.Select(order => order.OrderId)).IsEquivalentTo([First]);
    }

    [Test]
    public async Task Query_SortKeyPrefix_FindsOneTypeInASharedPartition()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Customers.Put(new Customer { Id = "alice", Name = "Alice" }).SendAsync();
        await shop.Orders.Put(NewOrder("alice", First)).SendAsync();
        await shop.Orders.Put(NewOrder("alice", Second)).SendAsync();

        // Act
        var orders = await shop.Orders.Query(Order.Keys.Pk.Format("alice"), SortKey.BeginsWith(Order.Keys.Sk.Prefix())).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(orders.Select(order => order.OrderId)).IsEquivalentTo([First, Second]);
    }

    [Test]
    public async Task Query_PrefixThroughAField_DoesNotMatchAnotherValueOfIt()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First)).SendAsync();
        await shop.Orders.Put(NewOrder("alice", Second) with { PlacedAt = Noon.AddMilliseconds(1) }).SendAsync();

        // Act
        var orders = await shop.Orders.Query(Order.Keys.Pk.Format("alice"), SortKey.BeginsWith(Order.Keys.Sk.Prefix(Noon))).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(orders.Select(order => order.OrderId)).IsEquivalentTo([First]);
    }

    [Test]
    public async Task Query_BetweenPrefixAndUpperBound_IncludesTheKeysAtTheUpperBound()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        DateTimeOffset[] days = [Noon, Noon.AddDays(1), Noon.AddDays(2), Noon.AddDays(3)];
        foreach (var day in days)
        {
            await shop.Orders.Put(NewOrder("alice", First) with { PlacedAt = day }).SendAsync();
            await shop.Orders.Put(NewOrder("alice", Second) with { PlacedAt = day }).SendAsync();
        }

        // Act
        var orders = await shop.Orders.Query(Order.Keys.Pk.Format("alice"), SortKey.Between(Order.Keys.Sk.Prefix(days[1]), Order.Keys.Sk.UpperBound(days[2]))).ItemsAsync().ToListAsync();

        // Assert (both orders of both days; Prefix(days[2]) as the upper bound would have excluded them)
        await Assert.That(orders.Count).IsEqualTo(4);
    }

    [Test]
    public async Task Query_BetweenTwoPrefixes_ExcludesTheKeysAtTheUpperBound()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First)).SendAsync();
        await shop.Orders.Put(NewOrder("alice", Second) with { PlacedAt = Noon.AddDays(1) }).SendAsync();

        // Act
        var orders = await shop.Orders.Query(Order.Keys.Pk.Format("alice"), SortKey.Between(Order.Keys.Sk.Prefix(Noon), Order.Keys.Sk.Prefix(Noon.AddDays(1)))).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(orders.Select(order => order.OrderId)).IsEquivalentTo([First]);
    }

    [Test]
    public async Task Query_UpperBoundOfAWholePartitionOfStrings_IsAboveEveryValue()
    {
        // Arrange (the highest value a key may hold, just below the scalar that the bound reserves)
        await using var shop = await ShopSandbox.StartAsync(backend);
        var highest = char.ConvertFromUtf32(0x10FFFE);
        await shop.Comments.Put(new Comment { OrderId = First, Author = "ann" }).SendAsync();
        await shop.Comments.Put(new Comment { OrderId = First, Author = highest }).SendAsync();

        // Act
        var comments = await shop.Comments.Query(Comment.Keys.Pk.Format(First), SortKey.Between(Comment.Keys.Sk.Prefix(), Comment.Keys.Sk.UpperBound())).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(comments.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Query_PrefixPartial_MatchesLongerValuesOnPurpose()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        foreach (var author in new[] { "ann", "anna", "bob" })
        {
            await shop.Comments.Put(new Comment { OrderId = First, Author = author }).SendAsync();
        }

        // Act
        var comments = await shop.Comments.Query(Comment.Keys.Pk.Format(First), SortKey.BeginsWith(Comment.Keys.Sk.PrefixPartial("an"))).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(comments.Select(comment => comment.Author)).IsEquivalentTo(["ann", "anna"]);
    }

    [Test]
    public async Task Query_FilterByHandle_KeepsTheOrdersThatPass()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First) with { Quantity = 1 }).SendAsync();
        await shop.Orders.Put(NewOrder("alice", Second) with { Quantity = 5, Status = OrderStatus.Shipped }).SendAsync();

        // Act
        var orders = await shop.Orders.Query(Order.Keys.Pk.Format("alice"), SortKey.BeginsWith(Order.Keys.Sk.Prefix()))
            .Filter(Order.Attrs.Status.Eq(OrderStatus.Shipped).And(Order.Attrs.Quantity.Gt(2)))
            .ItemsAsync()
            .ToListAsync();

        // Assert
        await Assert.That(orders.Select(order => order.OrderId)).IsEquivalentTo([Second]);
    }

    [Test]
    public async Task Query_ProjectionOfHandles_ReadsOnlyThoseAttributes()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First) with { Quantity = 3 }).SendAsync();

        // Act
        var items = await shop.Shop.Query(Order.Keys.Pk.Format("alice")).Project(Order.Attrs.Status, Order.Attrs.Quantity).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.Single().Count, items.Single().GetString("Status"))).IsEqualTo((2, "Paid"));
    }

    [Test]
    public async Task Scan_FilterAndProjectionByHandles_FindsTheOrder()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First) with { Quantity = 3 }).SendAsync();
        await shop.Orders.Put(NewOrder("bob", First) with { Quantity = 1 }).SendAsync();

        // Act
        var items = await shop.Shop.Scan().Filter(Order.Attrs.Quantity.Gt(2)).Project(Order.Attrs.CustomerId).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(items.Single().GetString("CustomerId")).IsEqualTo("alice");
    }

    [Test]
    public async Task Query_GlobalIndex_ReturnsTheOrdersOfAStatusByAmountIncludingNegatives()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First) with { Total = 100m }).SendAsync();
        await shop.Orders.Put(NewOrder("bob", First) with { Total = -5.5m }).SendAsync();
        await shop.Orders.Put(NewOrder("carol", First) with { Total = 20m }).SendAsync();
        await shop.Orders.Put(NewOrder("dave", First) with { Total = 1m, Status = OrderStatus.Pending }).SendAsync();

        // Act
        var paid = await shop.Orders.Index("GSI1").Query(Order.Keys.Gsi1Pk.Format(OrderStatus.Paid)).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(paid.Select(order => order.CustomerId).ToArray()).IsEquivalentTo(["bob", "carol", "alice"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Query_GlobalIndexRange_TakesAFixedPointSortKey()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First) with { Total = 100m }).SendAsync();
        await shop.Orders.Put(NewOrder("bob", First) with { Total = -5.5m }).SendAsync();
        await shop.Orders.Put(NewOrder("carol", First) with { Total = 20m }).SendAsync();

        // Act
        var paid = await shop.Orders.Index("GSI1").Query(Order.Keys.Gsi1Pk.Format(OrderStatus.Paid), SortKey.Between(Order.Keys.Gsi1Sk.Format(-6m), Order.Keys.Gsi1Sk.Format(20m))).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(paid.Select(order => order.CustomerId)).IsEquivalentTo(["bob", "carol"]);
    }

    [Test]
    public async Task Query_SparseIndex_HoldsOnlyTheOrdersWithAllInputs()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First)).SendAsync();
        await shop.Orders.Put(NewOrder("bob", First) with { TrackingId = Tracking }).SendAsync();
        await shop.Orders.Put(NewOrder("carol", First) with { TrackingId = Tracking, ShippedAt = Noon }).SendAsync();

        // Act
        var tracked = await shop.Orders.Index("GSI2").Query(Order.Keys.Gsi2Pk.Format(Tracking)).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(tracked.Select(order => order.CustomerId)).IsEquivalentTo(["carol"]);
    }

    [Test]
    public async Task Query_GlobalIndexAcrossZero_ReturnsTheNumericOrder()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await PutOrdersOfAmountsAsync(shop);

        // Act
        var paid = await shop.Orders.Index("GSI1").Query(Order.Keys.Gsi1Pk.Format(OrderStatus.Paid)).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(paid.Select(order => order.Total).ToArray()).IsEquivalentTo(Amounts.Order(), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Query_GlobalIndexDescending_ReturnsTheReverseNumericOrder()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await PutOrdersOfAmountsAsync(shop);

        // Act
        var paid = await shop.Orders.Index("GSI1").Query(Order.Keys.Gsi1Pk.Format(OrderStatus.Paid)).Descending().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(paid.Select(order => order.Total).ToArray()).IsEquivalentTo(Amounts.OrderDescending(), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Query_GlobalIndexBetweenTwoNegatives_ReturnsTheRangeInclusive()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await PutOrdersOfAmountsAsync(shop);

        // Act
        var paid = await shop.Orders.Index("GSI1")
            .Query(Order.Keys.Gsi1Pk.Format(OrderStatus.Paid), SortKey.Between(Order.Keys.Gsi1Sk.Format(-1000.5m), Order.Keys.Gsi1Sk.Format(-12.49m)))
            .ItemsAsync()
            .ToListAsync();

        // Assert
        await Assert.That(paid.Select(order => order.Total).ToArray()).IsEquivalentTo(Amounts.Where(amount => amount is >= -1000.5m and <= -12.49m).Order(), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Query_LocalIndexBeginsWithTheSign_FindsTheNegativeAmounts()
    {
        // Arrange (a Prefix ends on a whole field, so the sign is written out: it is the on-disk contract of the format)
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First) with { Total = -3m }).SendAsync();
        await shop.Orders.Put(NewOrder("alice", Second) with { Total = 3m }).SendAsync();

        // Act
        var orders = await shop.Orders.Index("LSI1").Query(Order.Keys.Pk.Format("alice"), SortKey.BeginsWith(Order.Keys.Lsi1Sk.Prefix() + "N")).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(orders.Select(order => order.Total)).IsEquivalentTo([-3m]);
    }

    [Test]
    public async Task Put_OneInputOfASparseIndexMissing_StoresNoKeyOfTheIndex()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);

        // Act
        await shop.Orders.Put(NewOrder("alice", First) with { TrackingId = Tracking }).SendAsync();
        var stored = (await shop.Shop.Get(Order.Keys.Pk.Format("alice"), Order.Keys.Sk.Format(Noon, First)).Consistent().SendAsync()).Item!;

        // Assert
        await Assert.That((stored.ContainsKey("gsi2pk"), stored.ContainsKey("gsi2sk"), stored.ContainsKey("TrackingId"))).IsEqualTo((false, false, true));
    }

    [Test]
    public async Task Put_WithoutTheInputs_TakesTheOrderOutOfTheSparseIndex()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var tracked = NewOrder("alice", First) with { TrackingId = Tracking, ShippedAt = Noon };
        await shop.Orders.Put(tracked).SendAsync();

        // Act
        await shop.Orders.Put(tracked with { TrackingId = null, ShippedAt = null }).SendAsync();
        var left = await shop.Orders.Index("GSI2").Query(Order.Keys.Gsi2Pk.Format(Tracking)).CountAsync();

        // Assert
        await Assert.That(left).IsEqualTo(0);
    }

    [Test]
    public async Task Scan_SparseIndex_SeesOnlyItsMembers()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Customers.Put(new Customer { Id = "alice", Name = "Alice" }).SendAsync();
        await shop.Orders.Put(NewOrder("alice", First)).SendAsync();
        await shop.Orders.Put(NewOrder("bob", First) with { TrackingId = Tracking, ShippedAt = Noon }).SendAsync();

        // Act (as items, so that the filter on the type cannot be what keeps the others out)
        var page = await shop.Shop.Index("GSI2", "gsi2pk", "gsi2sk").Scan().PageAsync();

        // Assert
        await Assert.That((page.ScannedCount, page.Items.Single().GetString("CustomerId"))).IsEqualTo((1, "bob"));
    }

    [Test]
    public async Task Query_LocalIndex_OrdersAPartitionByAmount()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First) with { Total = 100m }).SendAsync();
        await shop.Orders.Put(NewOrder("alice", Second) with { Total = -1m }).SendAsync();

        // Act
        var orders = await shop.Orders.Index("LSI1").Query(Order.Keys.Pk.Format("alice"), SortKey.Between(Order.Keys.Lsi1Sk.Prefix(), Order.Keys.Lsi1Sk.UpperBound())).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(orders.Select(order => order.OrderId).ToArray()).IsEquivalentTo([Second, First], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Get_KeyOnlyProperties_AreRestoredFromTheKeys()
    {
        // Arrange (ticks keep the instant exactly)
        await using var shop = await ShopSandbox.StartAsync(backend);
        var at = Noon.AddTicks(1234);
        await shop.Visits.Put(new Visit { Visitor = "alice", At = at, Page = "/home" }).SendAsync();

        // Act
        var read = (await shop.Visits.Get(Visit.Keys.Of("alice", at)).Consistent().SendAsync()).Item!;

        // Assert
        await Assert.That(read).IsEqualTo(new Visit { Visitor = "alice", At = at, Page = "/home" });
    }

    [Test]
    public async Task Query_TypedProjectionOfHandles_KeepsWhatTheReadNeeds()
    {
        // Arrange (the keys join the projection, and they are all that [KeyOnly] properties have)
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Visits.Put(new Visit { Visitor = "alice", At = Noon, Page = "/home" }).SendAsync();

        // Act
        var visits = await shop.Visits.Query(Visit.Keys.Pk.Format("alice")).Project(Visit.Attrs.Page).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(visits.Single()).IsEqualTo(new Visit { Visitor = "alice", At = Noon, Page = "/home" });
    }

    [Test]
    public async Task Put_KeyOnlyProperties_HaveNoAttributeOfTheirOwn()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Visits.Put(new Visit { Visitor = "alice", At = Noon, Page = "/home" }).SendAsync();

        // Act
        var stored = (await shop.Shop.Get(Visit.Keys.Pk.Format("alice"), Visit.Keys.Sk.Format(Noon)).Consistent().SendAsync()).Item!;

        // Assert
        await Assert.That((stored.ContainsKey("Visitor"), stored.ContainsKey("At"))).IsEqualTo((false, false));
    }

    [Test]
    public async Task Update_ThroughHandles_SetsAddsAndChecks()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First) with { Quantity = 1, Tags = ["a"] }).SendAsync();

        // Act
        var result = await shop.Orders.Update(Order.Keys.Of("alice", Noon, First))
            .Set(Order.Attrs.ShipTo, new Address("Bern", "3000"))
            .Add(Order.Attrs.Quantity, 2)
            .Add(Order.Attrs.Tags, ["b"])
            .If(Order.Attrs.Status.Eq(OrderStatus.Paid))
            .ReturnNew()
            .SendAsync();

        // Assert
        await Assert.That((result.Attributes!.ShipTo, result.Attributes.Quantity, result.Attributes.Tags.Order().ToArray().Length)).IsEqualTo((new Address("Bern", "3000"), 3, 2));
    }

    [Test]
    public async Task Update_NestedHandle_ChangesOneAttributeOfTheDocument()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First) with { ShipTo = new Address("Bern", "3000") }).SendAsync();

        // Act
        var result = await shop.Orders.Update(Order.Keys.Of("alice", Noon, First))
            .Set(ShipToCity, "Thun")
            .If(ShipToCity.Eq("Bern"))
            .ReturnNew()
            .SendAsync();

        // Assert
        await Assert.That(result.Attributes!.ShipTo).IsEqualTo(new Address("Thun", "3000"));
    }

    [Test]
    public async Task Update_RemoveByHandle_LeavesTheAttributeOut()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First) with { ShipTo = new Address("Bern", "3000") }).SendAsync();

        // Act
        var result = await shop.Orders.Update(Order.Keys.Of("alice", Noon, First)).Remove(Order.Attrs.ShipTo).ReturnNew().SendAsync();

        // Assert
        await Assert.That(result.Attributes!.ShipTo).IsNull();
    }

    [Test]
    public async Task Update_RawPathBesideHandles_StaysPossible()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First)).SendAsync();
        var path = "audit";

        // Act
        await shop.Orders.Update(Order.Keys.Of("alice", Noon, First)).Set(path, "x").If(Attr("audit").NotExists()).Add(Order.Attrs.Quantity, 1).SendAsync();
        var stored = (await shop.Shop.Get(Order.Keys.Pk.Format("alice"), Order.Keys.Sk.Format(Noon, First)).Consistent().SendAsync()).Item!;

        // Assert
        await Assert.That(stored.GetString("audit")).IsEqualTo("x");
    }

    [Test]
    public async Task Update_ConditionThatFails_ChangesNothing()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First)).SendAsync();

        // Act
        async Task Act() => await shop.Orders.Update(Order.Keys.Of("alice", Noon, First)).Add(Order.Attrs.Quantity, 1).If(Order.Attrs.Status.Eq(OrderStatus.Shipped)).SendAsync();

        // Assert
        await Assert.That(Act).ThrowsExactly<ConditionalCheckFailedException>();
    }

    [Test]
    public async Task Update_TypedReturn_GivesTheCounter()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Customers.Put(new Customer { Id = "alice", Name = "Alice", OrderCount = 41 }).SendAsync();

        // Act
        var count = await shop.Customers.Update(Customer.Keys.Of("alice"))
            .Add(Customer.Attrs.OrderCount, 1)
            .If(Customer.Attrs.OrderCount.Lt(100))
            .MaxAttempts(1)
            .Return(Customer.Attrs.OrderCount)
            .SendAsync();

        // Assert
        await Assert.That(count).IsEqualTo(42);
    }

    [Test]
    public async Task Delete_GeneratedKeyWithACondition_RemovesTheItem()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First)).SendAsync();

        // Act
        await shop.Orders.Delete(Order.Keys.Of("alice", Noon, First)).If(Order.Attrs.Total.Gt(0m)).SendAsync();
        var read = await shop.Orders.Get(Order.Keys.Of("alice", Noon, First)).Consistent().SendAsync();

        // Assert
        await Assert.That(read.Found).IsFalse();
    }

    [Test]
    public async Task Get_ProjectionOfHandles_ReadsOnlyThoseAttributes()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Orders.Put(NewOrder("alice", First) with { ShipTo = new Address("Bern", "3000") }).SendAsync();

        // Act
        var read = await shop.Shop.Get(Order.Keys.Pk.Format("alice"), Order.Keys.Sk.Format(Noon, First))
            .Consistent()
            .Project(Order.Attrs.Status, Order.Attrs.ShipTo.At(Address.Attrs.City))
            .SendAsync();

        // Assert
        await Assert.That((read.Item!.Count, read.Item.GetString("Status"), read.Item.GetMap("ShipTo")!.GetString("City"))).IsEqualTo((2, "Paid", "Bern"));
    }

    [Test]
    public async Task Get_KeyWithAnInvalidValue_FailsBeforeARequest()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);

        // Act
        async Task Act() => await shop.Comments.Get(Comment.Keys.Of(First, "a#b")).SendAsync();

        // Assert
        await Assert.That(Act).ThrowsExactly<KeyFormatException>();
    }

    private static async Task PutOrdersOfAmountsAsync(ShopSandbox shop)
    {
        foreach (var (amount, index) in Amounts.Select((amount, index) => (amount, index)))
        {
            await shop.Orders.Put(NewOrder($"customer{index}", First) with { Total = amount }).SendAsync();
        }
    }

    private static Order NewOrder(string customer, Guid id) => new()
    {
        CustomerId = customer,
        PlacedAt = Noon,
        OrderId = id,
        Status = OrderStatus.Paid,
        Total = 12.5m,
    };
}
