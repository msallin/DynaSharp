using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.IntegrationTests.Support;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>
/// <see cref="DynaSharp.Mapping.VersionAttribute"/> and key templates against what a service does with the conditions and the keys they write: lost updates, a conflict in a
/// transaction, and index membership that a real query sees.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class VersionedWriteTests(Backend backend)
{
    private static readonly DateOnly Day = new(2026, 10, 31);

    private static Ticket.Keys.PrimaryKey Key => Ticket.Keys.Of("t1");

    private static Ticket NewTicket => new() { Id = "t1" };

    [Test]
    public async Task Put_NewItem_StoresTheFirstVersion()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);

        // Act
        var result = await shop.Tickets.Put(NewTicket).SendAsync();
        var stored = await ReadAsync(shop);

        // Assert
        await Assert.That((result.Version, stored.Revision)).IsEqualTo(((long?)1L, 1L));
    }

    [Test]
    public async Task Put_NewItemWhenOneExists_FailsTheCondition()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await shop.Tickets.Put(NewTicket with { Note = "other" }).SendAsync());

        // Assert
        await Assert.That((failure is not null, (await ReadAsync(shop)).Note)).IsEqualTo((true, (string?)null));
    }

    [Test]
    public async Task Put_VersionZeroOverAnItemWithoutAVersionAttribute_FailsTheCondition()
    {
        // Arrange (written by other code, it reads as version 0)
        await using var shop = await ShopSandbox.StartAsync(backend);
        await WriteWithoutAVersionAsync(shop);

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await shop.Tickets.Put(NewTicket).SendAsync());

        // Assert
        await Assert.That((failure is not null, (await ReadAsync(shop)).Note)).IsEqualTo((true, (string?)"foreign"));
    }

    [Test]
    public async Task Put_IgnoreVersionOverAnItemWithoutAVersionAttribute_TakesItOver()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await WriteWithoutAVersionAsync(shop);

        // Act
        await shop.Tickets.Put(NewTicket with { Note = "ours", Revision = 1 }).IgnoreVersion().SendAsync();
        var stored = await ReadAsync(shop);

        // Assert
        await Assert.That((stored.Note, stored.Revision)).IsEqualTo(((string?)"ours", 1L));
    }

    [Test]
    public async Task Update_IfVersionZeroOverAnItemWithoutAVersionAttribute_FailsTheCondition()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await WriteWithoutAVersionAsync(shop);

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await shop.Tickets.Update(Key).Set(Ticket.Attrs.Note, "n").IfVersion(0).SendAsync());

        // Assert
        await Assert.That((failure is not null, (await ReadAsync(shop)).Note)).IsEqualTo((true, (string?)"foreign"));
    }

    [Test]
    public async Task Delete_EntityAtVersionZeroOverAnItemWithoutAVersionAttribute_FailsTheCondition()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await WriteWithoutAVersionAsync(shop);

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await shop.Tickets.Delete(NewTicket).SendAsync());

        // Assert
        await Assert.That((failure is not null, (await ReadAsync(shop)).Note)).IsEqualTo((true, (string?)"foreign"));
    }

    [Test]
    public async Task TransactWrite_CheckIfVersionZeroOverAnItemWithoutAVersionAttribute_CancelsTheTransaction()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await WriteWithoutAVersionAsync(shop);

        // Act
        var failure = await Items.FailureOfAsync<TransactionCanceledException>(async () => await shop.Client.TransactWrite()
            .Check(shop.Tickets, Key).IfVersion(0)
            .Put(shop.Tickets, new Ticket { Id = "t2" })
            .SendAsync());
        var second = await shop.Tickets.Get(Ticket.Keys.Of("t2")).Consistent().ItemAsync();

        // Assert
        await Assert.That((failure is not null, second is null)).IsEqualTo((true, true));
    }

    [Test]
    public async Task Update_IfVersionZeroAndIfNotExists_CreatesTheItemOnce()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Update(Key).Set(Ticket.Attrs.Note, "first").IfNotExists().IfVersion(0).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await shop.Tickets.Update(Key).Set(Ticket.Attrs.Note, "second").IfVersion(0).IfNotExists().SendAsync());

        // Assert
        await Assert.That((failure is not null, (await ReadAsync(shop)).Note)).IsEqualTo((true, (string?)"first"));
    }

    [Test]
    public async Task Query_GeneratedKey_FindsTheItemTheKeyHolds()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket with { Note = "wanted" }).SendAsync();
        await shop.Tickets.Put(new Ticket { Id = "t2", Note = "other" }).SendAsync();

        // Act
        var ticket = await shop.Tickets.Query(Key).Filter(Ticket.Attrs.Note.Eq("wanted")).ItemAsync();
        var filtered = await shop.Tickets.Query(Key).Filter(Ticket.Attrs.Note.Eq("other")).ItemAsync();

        // Assert
        await Assert.That((ticket!.Id, filtered)).IsEqualTo(("t1", (Ticket?)null));
    }

    [Test]
    public async Task Update_SetIfNotNullWithNull_LeavesTheAttribute()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket with { Note = "kept" }).SendAsync();

        // Act
        await shop.Tickets.Update(Key).SetIfNotNull(Ticket.Attrs.Note, null).IfVersion(1).SendAsync();
        var stored = await ReadAsync(shop);

        // Assert
        await Assert.That((stored.Note, stored.Revision)).IsEqualTo(((string?)"kept", 2L));
    }

    [Test]
    public async Task Update_SetIfNotNullWithAValue_SetsTheAttribute()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket with { Note = "old" }).SendAsync();

        // Act
        await shop.Tickets.Update(Key).SetIfNotNull(Ticket.Attrs.Note, "new").IfVersion(1).SendAsync();

        // Assert
        await Assert.That((await ReadAsync(shop)).Note).IsEqualTo("new");
    }

    [Test]
    public async Task Put_ConcurrentWritersOfOneVersion_OneWins()
    {
        // Arrange (everyone holds the version the first put stored)
        const int Writers = 8;
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();
        var held = await ReadAsync(shop);

        // Act
        var writes = Enumerable.Range(0, Writers).Select(writer => TryPutAsync(shop, held with { Note = writer.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        var outcomes = await Task.WhenAll(writes);
        var stored = await ReadAsync(shop);

        // Assert
        await Assert.That((outcomes.Count(static won => won), stored.Revision)).IsEqualTo((1, 2L));
    }

    [Test]
    public async Task Put_ReadThenWriteAgain_MovesTheVersionOn()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();

        // Act
        var result = await shop.Tickets.Put(await ReadAsync(shop) with { Note = "second" }).SendAsync();

        // Assert
        await Assert.That((result.Version, (await ReadAsync(shop)).Note)).IsEqualTo(((long?)2L, (string?)"second"));
    }

    [Test]
    public async Task Put_IgnoreVersionOverAnItem_ReplacesItAndStoresTheVersionOfTheEntity()
    {
        // Arrange (an import brings its own versions, and the item here is ahead of it)
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();
        await shop.Tickets.Put(await ReadAsync(shop)).SendAsync();

        // Act
        var result = await shop.Tickets.Put(NewTicket with { Note = "imported", Revision = 9 }).IgnoreVersion().SendAsync();
        var stored = await ReadAsync(shop);

        // Assert
        await Assert.That((result.Version, stored.Note, stored.Revision)).IsEqualTo(((long?)9L, (string?)"imported", 9L));
    }

    [Test]
    public async Task Put_IgnoreVersionWithAnotherCondition_StillChecksThatCondition()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await shop.Tickets.Put(NewTicket with { Revision = 9 }).IgnoreVersion().IfNotExists().SendAsync());

        // Assert
        await Assert.That((failure is not null, (await ReadAsync(shop)).Revision)).IsEqualTo((true, 1L));
    }

    [Test]
    public async Task TransactWrite_PutIgnoreVersionOverAnItem_ReplacesItAndChecksTheOtherPut()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();

        // Act (the second ticket is new, so version 0 of it holds)
        await shop.Client.TransactWrite()
            .Put(shop.Tickets, NewTicket with { Note = "imported", Revision = 9 }).IgnoreVersion()
            .Put(shop.Tickets, new Ticket { Id = "t2" })
            .SendAsync();
        var stored = await ReadAsync(shop);
        var second = await shop.Tickets.Get(Ticket.Keys.Of("t2")).Consistent().ItemAsync();

        // Assert
        await Assert.That((stored.Note, stored.Revision, second!.Revision)).IsEqualTo(((string?)"imported", 9L, 1L));
    }

    [Test]
    public async Task Update_IfVersionHeld_WritesAndStoresTheNext()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();

        // Act
        var result = await shop.Tickets.Update(Key).Set(Ticket.Attrs.Note, "n").IfVersion(1).SendAsync();
        var stored = await ReadAsync(shop);

        // Assert
        await Assert.That((result.Version, stored.Revision, stored.Note)).IsEqualTo(((long?)2L, 2L, (string?)"n"));
    }

    [Test]
    public async Task Update_StaleVersion_FailsTheConditionAndChangesNothing()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await shop.Tickets.Update(Key).Set(Ticket.Attrs.Note, "n").IfVersion(7).SendAsync());

        // Assert
        await Assert.That((failure is not null, (await ReadAsync(shop)).Revision)).IsEqualTo((true, 1L));
    }

    [Test]
    public async Task Update_VersionZeroOnNoItem_CreatesTheFirstVersion()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);

        // Act
        await shop.Tickets.Update(Key).Set(Ticket.Attrs.Note, "n").Set(Ticket.Attrs.State, OrderStatus.Paid).Set(Ticket.Attrs.Rank, 1).IfVersion(0).SendAsync();
        var stored = await shop.Shop.Get(Ticket.Keys.Pk.Format("t1"), Ticket.Keys.Sk.Format()).Consistent().SendAsync();

        // Assert
        await Assert.That((stored.Item!.GetNumber<long>("Revision"), stored.Item.GetString("gsi1pk"), stored.Item.GetString("gsi1sk"))).IsEqualTo(((long?)1L, (string?)"STATE#Paid", (string?)"001"));
    }

    [Test]
    public async Task Update_SetOfNull_RemovesTheAttribute()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket with { Note = "n" }).SendAsync();

        // Act
        await shop.Tickets.Update(Key).IgnoreVersion().Set(Ticket.Attrs.Note, null!).SendAsync();
        var stored = await shop.Shop.Get(Ticket.Keys.Pk.Format("t1"), Ticket.Keys.Sk.Format()).Consistent().SendAsync();

        // Assert
        await Assert.That(stored.Item!.ContainsKey("Note")).IsFalse();
    }

    [Test]
    public async Task Delete_StaleVersion_FailsTheCondition()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await shop.Tickets.Delete(Key).IfVersion(7).SendAsync());

        // Assert
        await Assert.That((failure is not null, (await shop.Tickets.Get(Key).Consistent().SendAsync()).Found)).IsEqualTo((true, true));
    }

    [Test]
    public async Task Delete_VersionHeld_Deletes()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();
        var held = await ReadAsync(shop);

        // Act
        await shop.Tickets.Delete(Key).IfVersion(held.Revision).SendAsync();

        // Assert
        await Assert.That((await shop.Tickets.Get(Key).Consistent().SendAsync()).Found).IsFalse();
    }

    [Test]
    public async Task Delete_EntityWithTheVersionOfTheItem_Deletes()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();
        var held = await ReadAsync(shop);

        // Act
        await shop.Tickets.Delete(held).SendAsync();

        // Assert
        await Assert.That((await shop.Tickets.Get(Key).Consistent().SendAsync()).Found).IsFalse();
    }

    [Test]
    public async Task Delete_EntityWithAStaleVersion_FailsTheConditionAndKeepsTheItem()
    {
        // Arrange (another writer moved the item on after this one read it)
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();
        var held = await ReadAsync(shop);
        await shop.Tickets.Put(held with { Note = "newer" }).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await shop.Tickets.Delete(held).SendAsync());

        // Assert
        await Assert.That((failure is not null, (await ReadAsync(shop)).Note)).IsEqualTo((true, (string?)"newer"));
    }

    [Test]
    public async Task Delete_EntityOfVersionZeroOnAVersionedItem_FailsTheCondition()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await shop.Tickets.Delete(NewTicket).SendAsync());

        // Assert
        await Assert.That((failure is not null, (await shop.Tickets.Get(Key).Consistent().SendAsync()).Found)).IsEqualTo((true, true));
    }

    [Test]
    public async Task Delete_EntityWithAStaleVersionAndIgnoreVersion_Deletes()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();
        var held = await ReadAsync(shop);
        await shop.Tickets.Put(held).SendAsync();

        // Act
        await shop.Tickets.Delete(held).IgnoreVersion().SendAsync();

        // Assert
        await Assert.That((await shop.Tickets.Get(Key).Consistent().SendAsync()).Found).IsFalse();
    }

    [Test]
    public async Task Delete_IgnoreVersion_DeletesWhateverItHolds()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();

        // Act
        await shop.Tickets.Delete(Key).IgnoreVersion().SendAsync();

        // Assert
        await Assert.That((await shop.Tickets.Get(Key).Consistent().SendAsync()).Found).IsFalse();
    }

    [Test]
    public async Task TransactWrite_VersionConflict_TheMarkerTellsWhichAction()
    {
        // Arrange (the first action holds, the second holds a version that moved on)
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();
        await shop.Tickets.Put(new Ticket { Id = "t2" }).SendAsync();
        var markers = new TransactMarker[3];

        // Act
        var failure = await Items.FailureOfAsync<TransactionCanceledException>(() => SendWithAStaleVersionAsync(shop, markers));

        // Assert
        await Assert.That((failure.ReasonFor(markers[0]), failure.ReasonFor(markers[1]), failure.ReasonFor(markers[2]), (await ReadAsync(shop)).Revision))
            .IsEqualTo((TransactionFailure.None, TransactionFailure.ConditionalCheckFailed, TransactionFailure.None, 1L));
    }

    [Test]
    public async Task TransactWrite_VersionsHeld_AppliesThemAll()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();
        await shop.Tickets.Put(new Ticket { Id = "t2" }).SendAsync();
        await shop.Tickets.Put(new Ticket { Id = "t3" }).SendAsync();

        // Act
        await shop.Client.TransactWrite()
            .Update(shop.Tickets, Key).Set(Ticket.Attrs.Note, "n").IfVersion(1)
            .Put(shop.Tickets, new Ticket { Id = "t2", Note = "m", Revision = 1 })
            .Delete(shop.Tickets, Ticket.Keys.Of("t3")).IfVersion(1)
            .SendAsync();

        // Assert
        var second = (await shop.Tickets.Get(Ticket.Keys.Of("t2")).Consistent().SendAsync()).Item!;
        await Assert.That(((await ReadAsync(shop)).Revision, second.Revision, (await shop.Tickets.Get(Ticket.Keys.Of("t3")).Consistent().SendAsync()).Found)).IsEqualTo((2L, 2L, false));
    }

    [Test]
    public async Task TransactWrite_CheckOfAVersion_CancelsWhenItMovedOn()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<TransactionCanceledException>(async () => await shop.Client.TransactWrite().Check(shop.Tickets, Key).IfVersion(7).SendAsync());

        // Assert
        await Assert.That(failure.Failures.Single().Reason).IsEqualTo(TransactionFailure.ConditionalCheckFailed);
    }

    [Test]
    public async Task BatchWrite_IgnoreVersion_WritesTheVersionAsItStands()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);

        // Act
        await shop.Tickets.BatchWrite().Put(NewTicket with { Revision = 5 }).IgnoreVersion().SendAsync();

        // Assert
        await Assert.That((await ReadAsync(shop)).Revision).IsEqualTo(5L);
    }

    [Test]
    public async Task Update_SetOfAnInput_MovesTheItemInTheIndexSoAQueryFindsIt()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket with { State = OrderStatus.Pending, Rank = 1 }).SendAsync();
        var byState = shop.Tickets.Index(Ticket.Indexes.Gsi1);

        // Act
        await shop.Tickets.Update(Key).Set(Ticket.Attrs.State, OrderStatus.Shipped).IfVersion(1).SendAsync();
        var pending = await Read.ToListAsync(byState.Query(Ticket.Keys.Gsi1Pk.Format(OrderStatus.Pending)).ItemsAsync());
        var shipped = await Read.ToListAsync(byState.Query(Ticket.Keys.Gsi1Pk.Format(OrderStatus.Shipped)).ItemsAsync());

        // Assert
        await Assert.That((pending.Count, shipped.Single().State)).IsEqualTo((0, OrderStatus.Shipped));
    }

    [Test]
    public async Task Update_SetOfBothInputsOfASparseIndex_BringsTheItemIn()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();
        var byOwner = shop.Tickets.Index(Ticket.Indexes.Gsi2);

        // Act
        await shop.Tickets.Update(Key).Set(Ticket.Attrs.Owner, "ann").Set(Ticket.Attrs.Due, Day).IfVersion(1).SendAsync();
        var found = await Read.ToListAsync(byOwner.Query(Ticket.Keys.Gsi2Pk.Format("ann")).ItemsAsync());

        // Assert
        await Assert.That(found.Single().Due).IsEqualTo(Day);
    }

    [Test]
    public async Task Update_SetOfOneKeyOfASparseIndex_ThrowsAndWritesNothing()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<InvalidOperationException>(async () => await shop.Tickets.Update(Key).Set(Ticket.Attrs.Owner, "ann").IfVersion(1).SendAsync());
        var stored = await ReadAsync(shop);

        // Assert
        await Assert.That((failure.Message.Contains("gsi2sk", StringComparison.Ordinal), stored.Owner, stored.Revision)).IsEqualTo((true, (string?)null, 1L));
    }

    [Test]
    public async Task Update_RawPathsOfOneKeyOfASparseIndex_LeaveTheItemOutOfTheIndex()
    {
        // Arrange (what the rule is there for: with one key, the service does not index the item, and nothing says so)
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();
        var byOwner = shop.Tickets.Index(Ticket.Indexes.Gsi2);

        // Act
        await shop.Tickets.Update(Key).Set("Owner", "ann").Set("gsi2pk", Ticket.Keys.Gsi2Pk.Format("ann")).IgnoreVersion().SendAsync();
        var found = await Read.ToListAsync(byOwner.Query(Ticket.Keys.Gsi2Pk.Format("ann")).ItemsAsync());

        // Assert
        await Assert.That(found).IsEmpty();
    }

    [Test]
    public async Task Update_OneKeyOfASparseIndexThatRepeatsTheTableKey_BringsTheItemIn()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var claim = new Claim { Id = "c1" };
        await shop.Claims.Put(claim).SendAsync();
        var byHandler = shop.Claims.Index(Claim.Indexes.Gsi2);

        // Act
        await shop.Claims.Update(Claim.Keys.Of("c1")).Set(Claim.Attrs.Handler, "hal").SendAsync();
        var found = await Read.ToListAsync(byHandler.Query(Claim.Keys.Gsi2Pk.Format("hal")).ItemsAsync());

        // Assert
        await Assert.That(found.Single().Id).IsEqualTo("c1");
    }

    [Test]
    public async Task TransactWrite_SetOfOneKeyOfASparseIndex_ThrowsAtTheSendAndWritesNothing()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<InvalidOperationException>(async () => await shop.Client.TransactWrite().Update(shop.Tickets, Key).Set(Ticket.Attrs.Due, Day).IfVersion(1).SendAsync());

        // Assert
        await Assert.That((failure.Message.Contains("gsi2pk", StringComparison.Ordinal), (await ReadAsync(shop)).Due)).IsEqualTo((true, (DateOnly?)null));
    }

    [Test]
    public async Task Update_RemoveOfAnInputOfASparseIndex_TakesTheItemOutAndWritesNoNull()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Tickets.Put(NewTicket with { Owner = "ann", Due = Day }).SendAsync();
        var byOwner = shop.Tickets.Index(Ticket.Indexes.Gsi2);
        var before = await Read.ToListAsync(byOwner.Query(Ticket.Keys.Gsi2Pk.Format("ann")).ItemsAsync());

        // Act
        await shop.Tickets.Update(Key).Remove(Ticket.Attrs.Owner).IfVersion(1).SendAsync();
        var after = await Read.ToListAsync(byOwner.Query(Ticket.Keys.Gsi2Pk.Format("ann")).ItemsAsync());
        var stored = (await shop.Shop.Get(Ticket.Keys.Pk.Format("t1"), Ticket.Keys.Sk.Format()).Consistent().SendAsync()).Item!;

        // Assert
        await Assert.That((before.Count, after.Count, stored.ContainsKey("gsi2pk"), stored.ContainsKey("gsi2sk"), stored.ContainsKey("Due"))).IsEqualTo((1, 0, false, false, true));
    }

    [Test]
    public async Task Unique_TypedKeyOnce_WritesAMarkerOfTheKeyAlone()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);

        // Act
        await shop.Client.TransactWrite().Unique(shop.Markers, EmailMarker.Keys.Of("a@b.ch"), out _).Put(shop.Tickets, NewTicket).SendAsync();
        var marker = (await shop.Shop.Get(EmailMarker.Keys.Pk.Format("a@b.ch"), EmailMarker.Keys.Sk.Format()).Consistent().SendAsync()).Item!;

        // Assert
        await Assert.That(marker.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Unique_TypedKeyTaken_TheMarkerTellsTheReasonAndNothingIsWritten()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Client.TransactWrite().Unique(shop.Markers, EmailMarker.Keys.Of("a@b.ch"), out _).SendAsync();
        var markers = new TransactMarker[1];

        // Act
        var failure = await Items.FailureOfAsync<TransactionCanceledException>(() => SendWithATakenAddressAsync(shop, markers));

        // Assert
        await Assert.That((failure.ReasonFor(markers[0]), (await shop.Tickets.Get(Key).Consistent().SendAsync()).Found)).IsEqualTo((TransactionFailure.ConditionalCheckFailed, false));
    }

    private static async Task<Ticket> ReadAsync(ShopSandbox shop) => (await shop.Tickets.Get(Key).Consistent().SendAsync()).Item!;

    private static async Task WriteWithoutAVersionAsync(ShopSandbox shop) =>
        await shop.Shop.Put(new Item { ["pk"] = "TICKET#t1", ["sk"] = "TICKET", ["_type"] = "TICKET", ["Id"] = "t1", ["Note"] = "foreign" }).SendAsync();

    private static async Task<bool> TryPutAsync(ShopSandbox shop, Ticket ticket)
    {
        try
        {
            await shop.Tickets.Put(ticket).SendAsync();
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    private static async Task SendWithAStaleVersionAsync(ShopSandbox shop, TransactMarker[] markers)
    {
        await shop.Client.TransactWrite()
            .Update(shop.Tickets, Key, out markers[0]).Set(Ticket.Attrs.Note, "n").IfVersion(1)
            .Put(shop.Tickets, new Ticket { Id = "t2", Revision = 7 }, out markers[1])
            .Put(shop.Tickets, new Ticket { Id = "t3" }, out markers[2])
            .SendAsync();
    }

    private static async Task SendWithATakenAddressAsync(ShopSandbox shop, TransactMarker[] markers)
    {
        await shop.Client.TransactWrite().Unique(shop.Markers, EmailMarker.Keys.Of("a@b.ch"), out markers[0]).Put(shop.Tickets, NewTicket).SendAsync();
    }
}
