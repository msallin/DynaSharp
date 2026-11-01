using DynaSharp.IntegrationTests.Backends;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>A row a newer deployment wrote, with a status this one does not know, among rows it reads well: the failure says which row it is.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class PoisonRowTests(Backend backend)
{
    private static readonly Item PoisonKey = new() { ["pk"] = "TENANT#acme", ["sk"] = "INV#2026-09#000002" };

    [Test]
    public async Task Get_RowOfAnotherDeployment_NamesItsKey()
    {
        // Arrange
        await using var sandbox = await StartAsync();
        var invoices = sandbox.Shop.Of<TenantInvoice>();

        // Act
        var failure = await Assert.ThrowsAsync<DynamoDbMappingException>(async () => await invoices.Get(TenantInvoice.Keys.Of("acme", "2026-09", 2)).Consistent().SendAsync());

        // Assert
        await Assert.That(failure!.Key).IsEqualTo(PoisonKey);
    }

    [Test]
    public async Task Query_RowOfAnotherDeployment_NamesItsKey()
    {
        // Arrange
        await using var sandbox = await StartAsync();
        var invoices = sandbox.Shop.Of<TenantInvoice>();

        // Act
        var failure = await Assert.ThrowsAsync<DynamoDbMappingException>(async () => await invoices.Query(TenantInvoice.Keys.Pk.Of("acme")).Consistent().ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure!.Key).IsEqualTo(PoisonKey);
    }

    [Test]
    public async Task Scan_RowOfAnotherDeployment_NamesItsKey()
    {
        // Arrange
        await using var sandbox = await StartAsync();
        var invoices = sandbox.Shop.Of<TenantInvoice>();

        // Act
        var failure = await Assert.ThrowsAsync<DynamoDbMappingException>(async () => await invoices.Scan().Consistent().ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure!.Key).IsEqualTo(PoisonKey);
    }

    [Test]
    public async Task BatchGet_RowOfAnotherDeployment_NamesItsKey()
    {
        // Arrange
        await using var sandbox = await StartAsync();
        var invoices = sandbox.Shop.Of<TenantInvoice>();

        // Act
        var failure = await Assert.ThrowsAsync<DynamoDbMappingException>(async () => await invoices.BatchGet().Key(TenantInvoice.Keys.Of("acme", "2026-09", 1)).Key(TenantInvoice.Keys.Of("acme", "2026-09", 2)).Consistent().SendAsync());

        // Assert
        await Assert.That(failure!.Key).IsEqualTo(PoisonKey);
    }

    [Test]
    public async Task MixedQuery_RowOfAnotherDeployment_NamesItsKey()
    {
        // Arrange
        await using var sandbox = await StartAsync();

        // Act
        var failure = await Assert.ThrowsAsync<DynamoDbMappingException>(async () => await sandbox.Shop.Query(TenantInvoice.Keys.Pk.Of("acme")).Of<TenantInvoice, CaseFile>().Consistent().ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure!.Key).IsEqualTo(PoisonKey);
    }

    private async Task<ShopSandbox> StartAsync()
    {
        var sandbox = await ShopSandbox.StartAsync(backend);
        await sandbox.Shop.Of<TenantInvoice>().Put(new TenantInvoice { TenantId = "acme", Month = "2026-09", Number = 1 }).SendAsync();
        await sandbox.Shop.Put(new Item
        {
            ["pk"] = "TENANT#acme",
            ["sk"] = "INV#2026-09#000002",
            ["_type"] = "TENANT_INVOICE",
            ["Status"] = "Refunded",
            ["DueDate"] = "2026-09-30",
        }).SendAsync();
        return sandbox;
    }
}
