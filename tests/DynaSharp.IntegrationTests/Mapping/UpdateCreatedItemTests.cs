using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.IntegrationTests.Support;
using static DynaSharp.Expr;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>
/// <c>UpdateItem</c> is an upsert, so an update of an entity can create the item, with only the attributes it names. Such an item must not be invisible to the typed
/// API: it has its discriminator, and a read takes what the key holds from the key.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class UpdateCreatedItemTests(Backend backend)
{
    private const string Home = "/home";

    private static readonly DateOnly Day = new(2026, 11, 1);

    [Test]
    public async Task Get_ItemAnUpdateCreated_RestoresTheRequiredKeyInputs()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var visits = sandbox.Shop.Of<DailyVisits>();
        await visits.Update(DailyVisits.Keys.Of(Home, Day)).Add(DailyVisits.Attrs.Count, 3L).SendAsync();

        // Act
        var found = await visits.Get(DailyVisits.Keys.Of(Home, Day)).Consistent().ItemAsync();

        // Assert
        await Assert.That(found).IsEqualTo(new DailyVisits { Path = Home, Day = Day, Count = 3 });
    }

    [Test]
    public async Task Query_ItemAnUpdateCreated_FindsIt()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var visits = sandbox.Shop.Of<DailyVisits>();
        await visits.Update(DailyVisits.Keys.Of(Home, Day)).Add(DailyVisits.Attrs.Count, 3L).SendAsync();

        // Act
        var found = await visits.Query(DailyVisits.Keys.Pk.Format(Home)).Consistent().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(found.Single()).IsEqualTo(new DailyVisits { Path = Home, Day = Day, Count = 3 });
    }

    [Test]
    public async Task Update_NoChangeAtAll_CreatesAnItemATypedReadFinds()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var visits = sandbox.Shop.Of<DailyVisits>();

        // Act
        await visits.Update(DailyVisits.Keys.Of(Home, Day)).SendAsync();
        var found = await visits.Scan().Consistent().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(found.Single()).IsEqualTo(new DailyVisits { Path = Home, Day = Day });
    }

    [Test]
    public async Task Update_InATransaction_CreatesAnItemATypedReadFinds()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var visits = sandbox.Shop.Of<DailyVisits>();

        // Act
        await sandbox.Client.TransactWrite().Update(visits, DailyVisits.Keys.Of(Home, Day)).Add(DailyVisits.Attrs.Count, 1L).Update(visits, DailyVisits.Keys.Of("/about", Day)).SendAsync();
        var found = await visits.Scan().Consistent().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", found.Select(static visit => visit.Path).Order(StringComparer.Ordinal))).IsEqualTo("/about,/home");
    }

    [Test]
    public async Task Update_IfExistsOnAnItemThatIsThere_KeepsTheDiscriminatorItHas()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var visits = sandbox.Shop.Of<DailyVisits>();
        await visits.Put(new DailyVisits { Path = Home, Day = Day }).SendAsync();

        // Act
        await visits.Update(DailyVisits.Keys.Of(Home, Day)).Add(DailyVisits.Attrs.Count, 1L).IfExists().SendAsync();

        // Assert
        await Assert.That((await visits.Query(DailyVisits.Keys.Pk.Format(Home)).Consistent().ItemsAsync().ToListAsync()).Single().Count).IsEqualTo(1L);
    }

    [Test]
    public async Task Update_ItemOfAnotherTypeThroughTheWrongTable_LeavesItsDiscriminator()
    {
        // Arrange (a key of attribute values says nothing about the type that is there)
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Customers.Put(new Customer { Id = "alice", Name = "Alice" }).SendAsync();
        var (pk, sk) = (Customer.Keys.Pk.Format("alice"), Customer.Keys.Sk.Format());

        // Act
        await shop.Orders.Update(pk, sk).Add(Order.Attrs.Quantity, 1).SendAsync();
        var stored = await shop.Shop.Get(pk, sk).Consistent().ItemAsync();

        // Assert
        await Assert.That(stored!.GetString("_type")).IsEqualTo("CUSTOMER");
    }

    [Test]
    public async Task Update_OlderItemWithoutADiscriminator_GetsIt()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var visits = sandbox.Shop.Of<DailyVisits>();
        await sandbox.Shop.Put(new Item { ["pk"] = DailyVisits.Keys.Pk.Format(Home), ["sk"] = DailyVisits.Keys.Sk.Format(Day) }).SendAsync();

        // Act
        await visits.Update(DailyVisits.Keys.Of(Home, Day)).Add(DailyVisits.Attrs.Count, 1L).SendAsync();

        // Assert
        await Assert.That(await visits.Scan().Consistent().CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task Update_IfExistsOnNoItem_CreatesNothing()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var visits = sandbox.Shop.Of<DailyVisits>();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await visits.Update(DailyVisits.Keys.Of(Home, Day)).Add(DailyVisits.Attrs.Count, 1L).IfExists().SendAsync());

        // Assert
        await Assert.That((failure is not null, await visits.Scan().Consistent().CountAsync())).IsEqualTo((true, 0));
    }

    [Test]
    public async Task Update_ConditionOnTheDiscriminator_SeesTheItemAsItWas()
    {
        // Arrange (the condition looks at the item before the update, so an older item without one fails it, although the update would write one)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var visits = sandbox.Shop.Of<DailyVisits>();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await visits.Update(DailyVisits.Keys.Of(Home, Day)).If(Attr("_type").Eq("DAILY_VISITS")).SendAsync());
        await visits.Update(DailyVisits.Keys.Of(Home, Day)).If(Attr("_type").NotExists()).SendAsync();
        await visits.Update(DailyVisits.Keys.Of(Home, Day)).If(Attr("_type").Eq("DAILY_VISITS")).SendAsync();

        // Assert
        await Assert.That((failure is not null, await visits.Scan().Consistent().CountAsync())).IsEqualTo((true, 1));
    }

    [Test]
    public async Task Return_UpdatedAttributes_ReadsTheOneThatWasAskedFor()
    {
        // Arrange (UPDATED_NEW returns the discriminator as well)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var visits = sandbox.Shop.Of<DailyVisits>();

        // Act
        var count = await visits.Update(DailyVisits.Keys.Of(Home, Day)).Add(DailyVisits.Attrs.Count, 2L).Return(DailyVisits.Attrs.Count).SendAsync();

        // Assert
        await Assert.That(count).IsEqualTo(2L);
    }

    [Test]
    public async Task Update_RawActionOnTheDiscriminator_IsTheOnlyOne()
    {
        // Arrange (DynamoDB rejects two actions on one attribute)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var visits = sandbox.Shop.Of<DailyVisits>();

        // Act
        await visits.Update(DailyVisits.Keys.Of(Home, Day)).SetIfNotExists("_type", "DAILY_VISITS").SendAsync();
        var stored = await sandbox.ReadAsync(DailyVisits.Keys.Pk.Format(Home), DailyVisits.Keys.Sk.Format(Day));

        // Assert
        await Assert.That(stored!.GetString("_type")).IsEqualTo("DAILY_VISITS");
    }

    [Test]
    public async Task Update_ActionThatReadsTheDiscriminator_IsNoActionOnIt()
    {
        // Arrange (DynamoDB takes a path that one action reads and another writes)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var visits = sandbox.Shop.Of<DailyVisits>();

        // Act
        await visits.Update(DailyVisits.Keys.Of(Home, Day)).Set($"{Attr("copy")} = if_not_exists({Attr("_type")}, {"NONE"})").SendAsync();
        var stored = await sandbox.ReadAsync(DailyVisits.Keys.Pk.Format(Home), DailyVisits.Keys.Sk.Format(Day));

        // Assert
        await Assert.That((stored!.GetString("copy"), stored.GetString("_type"))).IsEqualTo(("NONE", "DAILY_VISITS"));
    }

    [Test]
    public async Task Update_DiscriminatorIsTheSortKeyOfTheTable_LeavesItOut()
    {
        // Arrange (DynamoDB rejects an update of a key)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var notes = sandbox.Shop.Of<SortedByType>();

        // Act
        await notes.Update("a", SortedByType.Discriminator).Set("note", "hello").SendAsync();
        var stored = await sandbox.ReadAsync("a", SortedByType.Discriminator);

        // Assert
        await Assert.That(stored!.GetString("note")).IsEqualTo("hello");
    }

    [Test]
    public async Task Query_IndexKeyedOnTheDiscriminator_HoldsAnItemAnUpdateCreated()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var byType = shop.Customers.Index(ShopSandbox.ByType, "_type", "sk");

        // Act
        await shop.Customers.Update(Customer.Keys.Of("alice")).Set(Customer.Attrs.Name, "Alice").SendAsync();
        var customers = await byType.Query("CUSTOMER").ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(customers.Single()).IsEqualTo(new Customer { Id = "alice", Name = "Alice" });
    }

    [Test]
    public async Task Get_RequiredInputOfALossyFormat_ThrowsNamingTheAttribute()
    {
        // Arrange (a month cannot give back the day, so the key is no source of it)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var summaries = sandbox.Shop.Of<MonthlySummary>();
        var at = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        await summaries.Update(MonthlySummary.Keys.Of("a", at)).Add(MonthlySummary.Attrs.Total, 1).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<DynamoDbMappingException>(async () => await summaries.Get(MonthlySummary.Keys.Of("a", at)).Consistent().SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("At");
    }
}
