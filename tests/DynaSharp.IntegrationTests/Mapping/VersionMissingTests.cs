using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary><see cref="VersionMissing.Allow"/> against what a service does with its conditions: a table that keeps items written without a version.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class VersionMissingTests(Backend backend)
{
    private static Heirloom.Keys.PrimaryKey Key => Heirloom.Keys.Of("h1");

    private static Heirloom New => new() { Id = "h1", Note = "ours" };

    [Test]
    public async Task Put_AtZeroOverAnItemWithoutAVersion_TakesItOverAndTheNextWriteIsOrdinary()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();
        await WriteWithoutAVersionAsync(shop);
        var held = await ReadAsync(heirlooms);

        // Act
        var first = await heirlooms.Put(held with { Note = "ours" }).SendAsync();
        var second = await heirlooms.Put(await ReadAsync(heirlooms) with { Note = "again" }).SendAsync();

        // Assert
        await Assert.That((held.Revision, first.Version, second.Version, (await ReadAsync(heirlooms)).Note)).IsEqualTo((0L, (long?)1L, (long?)2L, (string?)"again"));
    }

    [Test]
    public async Task Put_AtZeroOnNoItem_Creates()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();

        // Act
        var result = await heirlooms.Put(New).SendAsync();

        // Assert
        await Assert.That((result.Version, (await ReadAsync(heirlooms)).Revision)).IsEqualTo(((long?)1L, 1L));
    }

    [Test]
    public async Task Put_AtZeroOverAnItemWithAVersion_FailsTheCondition()
    {
        // Arrange (this is what is left of the lock: two writers at 0 on an old item, and one wins)
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();
        await WriteWithoutAVersionAsync(shop);
        await heirlooms.Put(New).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await heirlooms.Put(New with { Note = "late" }).SendAsync());

        // Assert
        await Assert.That((failure is not null, (await ReadAsync(heirlooms)).Note)).IsEqualTo((true, (string?)"ours"));
    }

    [Test]
    public async Task Update_IfVersionZeroOverAnItemWithoutAVersion_KeepsTheRestAndStoresTheFirst()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();
        await WriteWithoutAVersionAsync(shop);

        // Act
        var result = await heirlooms.Update(Key).Set(Heirloom.Attrs.Room, "attic").IfVersion(0).SendAsync();
        var stored = await ReadAsync(heirlooms);

        // Assert
        await Assert.That((result.Version, stored.Revision, stored.Note, stored.Room)).IsEqualTo(((long?)1L, 1L, (string?)"foreign", (string?)"attic"));
    }

    [Test]
    public async Task Delete_EntityAtZeroOfAnItemWithoutAVersion_Deletes()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();
        await WriteWithoutAVersionAsync(shop);

        // Act
        await heirlooms.Delete(await ReadAsync(heirlooms)).SendAsync();

        // Assert
        await Assert.That((await heirlooms.Get(Key).Consistent().SendAsync()).Found).IsFalse();
    }

    [Test]
    public async Task TransactWrite_CheckIfVersionZeroOfAnItemWithoutAVersion_LetsTheTransactionThrough()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();
        await WriteWithoutAVersionAsync(shop);

        // Act
        await shop.Client.TransactWrite().Check(heirlooms, Key).IfVersion(0).Put(heirlooms, New with { Id = "h2" }).SendAsync();

        // Assert
        await Assert.That((await heirlooms.Get(Heirloom.Keys.Of("h2")).Consistent().SendAsync()).Found).IsTrue();
    }

    [Test]
    public async Task TransactWrite_PutAtZeroOverAnItemWithAVersion_CancelsTheTransaction()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();
        await heirlooms.Put(New).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<TransactionCanceledException>(async () => await shop.Client.TransactWrite().Put(heirlooms, New with { Note = "late" }).SendAsync());

        // Assert
        await Assert.That(failure.Failures.Single().Reason).IsEqualTo(TransactionFailure.ConditionalCheckFailed);
    }

    [Test]
    public async Task Put_IfNotExistsAtZeroOverAnItemWithoutAVersion_FailsTheCondition()
    {
        // Arrange (the caller asked for a free key)
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();
        await WriteWithoutAVersionAsync(shop);

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await heirlooms.Put(New).IfNotExists().SendAsync());

        // Assert
        await Assert.That((failure is not null, (await ReadAsync(heirlooms)).Note)).IsEqualTo((true, (string?)"foreign"));
    }

    [Test]
    public async Task Put_IfExistsAtZeroOverAnItemWithoutAVersion_ReplacesIt()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();
        await WriteWithoutAVersionAsync(shop);

        // Act
        var result = await heirlooms.Put(New).IfExists().SendAsync();

        // Assert
        await Assert.That((result.Version, (await ReadAsync(heirlooms)).Note)).IsEqualTo(((long?)1L, (string?)"ours"));
    }

    [Test]
    public async Task Put_IfExistsAtZeroOnNoItem_FailsTheCondition()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await heirlooms.Put(New).IfExists().SendAsync());

        // Assert
        await Assert.That((failure is not null, (await heirlooms.Get(Key).Consistent().SendAsync()).Found)).IsEqualTo((true, false));
    }

    [Test]
    public async Task Put_IfExistsAtZeroOverAnItemWithAVersion_FailsTheCondition()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();
        await heirlooms.Put(New).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await heirlooms.Put(New with { Note = "late" }).IfExists().SendAsync());

        // Assert
        await Assert.That((failure is not null, (await ReadAsync(heirlooms)).Note)).IsEqualTo((true, (string?)"ours"));
    }

    [Test]
    public async Task Put_AtZeroOverAnItemThatHoldsZero_StoresOneAndASecondPutAtZeroFails()
    {
        // Arrange (what SaveAsync of the AWS SDK leaves behind, whose first version is 0)
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();
        await WriteAtVersionZeroAsync(shop);
        var held = await ReadAsync(heirlooms);

        // Act
        var first = await heirlooms.Put(held with { Note = "ours" }).SendAsync();
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await heirlooms.Put(held with { Note = "late" }).SendAsync());
        var stored = await ReadAsync(heirlooms);

        // Assert
        await Assert.That((held.Revision, first.Version, stored.Revision, stored.Note, failure is not null)).IsEqualTo((0L, (long?)1L, 1L, (string?)"ours", true));
    }

    [Test]
    public async Task Update_IfVersionZeroOverAnItemThatHoldsZero_StoresOne()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();
        await WriteAtVersionZeroAsync(shop);

        // Act
        var result = await heirlooms.Update(Key).Set(Heirloom.Attrs.Room, "attic").IfVersion(0).SendAsync();
        var stored = await ReadAsync(heirlooms);

        // Assert
        await Assert.That((result.Version, stored.Revision, stored.Note, stored.Room)).IsEqualTo(((long?)1L, 1L, (string?)"foreign", (string?)"attic"));
    }

    [Test]
    public async Task Delete_EntityAtZeroOfAnItemThatHoldsZero_Deletes()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();
        await WriteAtVersionZeroAsync(shop);

        // Act
        await heirlooms.Delete(await ReadAsync(heirlooms)).SendAsync();

        // Assert
        await Assert.That((await heirlooms.Get(Key).Consistent().SendAsync()).Found).IsFalse();
    }

    [Test]
    public async Task TransactWrite_PutAtZeroOverAnItemThatHoldsZero_StoresOne()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();
        await WriteAtVersionZeroAsync(shop);

        // Act
        await shop.Client.TransactWrite().Put(heirlooms, New).SendAsync();

        // Assert
        await Assert.That((await ReadAsync(heirlooms)).Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Put_IfExistsAtZeroOverAnItemThatHoldsZero_ReplacesIt()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();
        await WriteAtVersionZeroAsync(shop);

        // Act
        var result = await heirlooms.Put(New).IfExists().SendAsync();

        // Assert
        await Assert.That((result.Version, (await ReadAsync(heirlooms)).Note)).IsEqualTo(((long?)1L, (string?)"ours"));
    }

    [Test]
    public async Task Put_IfNotExistsAtZeroOverAnItemThatHoldsZero_FailsTheCondition()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();
        await WriteAtVersionZeroAsync(shop);

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await heirlooms.Put(New).IfNotExists().SendAsync());

        // Assert
        await Assert.That((failure is not null, (await ReadAsync(heirlooms)).Note)).IsEqualTo((true, (string?)"foreign"));
    }

    [Test]
    public async Task Select_VersionOfAnItemWithoutOne_GivesZero()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();
        await WriteWithoutAVersionAsync(shop);

        // Act
        var selected = await heirlooms.Get(Key).Consistent().Select(Heirloom.Attrs.Revision).SendAsync();

        // Assert
        await Assert.That((selected.Found, selected.Value)).IsEqualTo((true, 0L));
    }

    [Test]
    public async Task Select_VersionOfAnItemWithOne_GivesIt()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var heirlooms = shop.Shop.Of<Heirloom>();
        await heirlooms.Put(New).SendAsync();

        // Act
        var selected = await heirlooms.Get(Key).Consistent().Select(Heirloom.Attrs.Revision).SendAsync();

        // Assert
        await Assert.That(selected.Value).IsEqualTo(1L);
    }

    private static async Task<Heirloom> ReadAsync(Table<Heirloom> heirlooms) => (await heirlooms.Get(Key).Consistent().SendAsync()).Item!;

    private static async Task WriteWithoutAVersionAsync(ShopSandbox shop) =>
        await shop.Shop.Put(new Item { ["pk"] = "HEIRLOOM#h1", ["sk"] = "HEIRLOOM", ["_type"] = "HEIRLOOM", ["Id"] = "h1", ["Note"] = "foreign" }).SendAsync();

    private static async Task WriteAtVersionZeroAsync(ShopSandbox shop) =>
        await shop.Shop.Put(new Item { ["pk"] = "HEIRLOOM#h1", ["sk"] = "HEIRLOOM", ["_type"] = "HEIRLOOM", ["Id"] = "h1", ["Note"] = "foreign", ["Revision"] = 0 }).SendAsync();
}
