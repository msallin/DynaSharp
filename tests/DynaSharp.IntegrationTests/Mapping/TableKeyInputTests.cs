using DynaSharp.IntegrationTests.Backends;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>
/// An index key made of an input of the table key: the update was given that key, so it writes the index key without reading the item, and a status transition moves the item between the
/// partitions of the index.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class TableKeyInputTests(Backend backend)
{
    private static readonly DateOnly DueDate = new(2026, 9, 30);

    private static TenantInvoice Open => new() { TenantId = "acme", Month = "2026-09", Number = 2, Status = TenantInvoiceStatus.Open, DueDate = DueDate };

    [Test]
    public async Task Update_StatusTransition_MovesTheItemBetweenIndexPartitions()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var invoices = sandbox.Shop.Of<TenantInvoice>();
        await invoices.Put(Open).SendAsync();

        // Act
        await invoices.Update(TenantInvoice.Keys.Of("acme", "2026-09", 2)).Set(TenantInvoice.Attrs.Status, TenantInvoiceStatus.Paid).SendAsync();

        // Assert
        await Assert.That(await PartitionsAsync(invoices)).IsEqualTo((0, 1));
    }

    [Test]
    public async Task Update_StatusTransitionOnRawKeyStrings_MovesTheItemBetweenIndexPartitions()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var invoices = sandbox.Shop.Of<TenantInvoice>();
        await invoices.Put(Open).SendAsync();

        // Act
        await invoices.Update(TenantInvoice.Keys.Pk.Format("acme"), TenantInvoice.Keys.Sk.Format("2026-09", 2)).Set(TenantInvoice.Attrs.Status, TenantInvoiceStatus.Paid).SendAsync();

        // Assert
        await Assert.That(await PartitionsAsync(invoices)).IsEqualTo((0, 1));
    }

    [Test]
    public async Task TransactWrite_StatusTransition_MovesTheItemBetweenIndexPartitions()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var invoices = sandbox.Shop.Of<TenantInvoice>();
        await invoices.Put(Open).SendAsync();

        // Act
        await sandbox.Client.TransactWrite().Update(invoices, TenantInvoice.Keys.Of("acme", "2026-09", 2)).Set(TenantInvoice.Attrs.Status, TenantInvoiceStatus.Paid).SendAsync();

        // Assert
        await Assert.That(await PartitionsAsync(invoices)).IsEqualTo((0, 1));
    }

    [Test]
    public async Task TransactWrite_StatusTransitionOnRawKeyStrings_MovesTheItemBetweenIndexPartitions()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var invoices = sandbox.Shop.Of<TenantInvoice>();
        await invoices.Put(Open).SendAsync();

        // Act
        await sandbox.Client.TransactWrite().Update(invoices, TenantInvoice.Keys.Pk.Format("acme"), TenantInvoice.Keys.Sk.Format("2026-09", 2)).Set(TenantInvoice.Attrs.Status, TenantInvoiceStatus.Paid).SendAsync();

        // Assert
        await Assert.That(await PartitionsAsync(invoices)).IsEqualTo((0, 1));
    }

    [Test]
    public async Task Update_SparseKeyBesideATableKeyInput_TakesTheItemIntoTheIndex()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var files = sandbox.Shop.Of<CaseFile>();

        // Act
        await files.Update(CaseFile.Keys.Of("c1")).Set(CaseFile.Attrs.Handler, "ann").SendAsync();
        var found = await files.Index(CaseFile.Indexes.Gsi2).Query(CaseFile.Keys.Gsi2Pk.Of("ann"), SortKey.Eq(CaseFile.Keys.Gsi2Sk.Format("c1"))).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(found.Single().Id).IsEqualTo("c1");
    }

    private static async Task<(int Open, int Paid)> PartitionsAsync(Table<TenantInvoice> invoices)
    {
        var index = invoices.Index(TenantInvoice.Indexes.Gsi1);
        var open = await index.Query(TenantInvoice.Keys.Gsi1Pk.Of("acme", TenantInvoiceStatus.Open)).CountAsync();
        var paid = await index.Query(TenantInvoice.Keys.Gsi1Pk.Of("acme", TenantInvoiceStatus.Paid)).CountAsync();
        return (open, paid);
    }
}
