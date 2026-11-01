using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Vertical partitioning</see>: what changes often or is read alone goes into an item of its own in the same partition, because a read and a write pay for a whole item. The stock of a
/// product with a long manual then costs one unit to change, and one query still reads the product whole.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class VerticalPartitioningTests(Backend backend)
{
    private const string Keyboard = "KB-01";

    private static readonly string Manual = new('m', 20_000);

    [Test]
    public async Task Update_TheStock_CostsWhatTheSmallItemCostsAndNotTheManual()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await PutProductAsync(sandbox);
        var wholeItem = sandbox.Shop.Of<UnsplitProduct>();
        await wholeItem.Put(new UnsplitProduct { Sku = Keyboard, Manual = Manual, Stock = 5 }).SendAsync();

        // Act
        var split = await sandbox.Shop.Of<ProductStock>().Update(ProductStock.Keys.Of(Keyboard)).Add(ProductStock.Attrs.Quantity, -1).ReturnCapacity().SendAsync();
        var whole = await wholeItem.Update(UnsplitProduct.Keys.Of(Keyboard)).Add(UnsplitProduct.Attrs.Stock, -1).ReturnCapacity().SendAsync();

        // Assert
        await Assert.That((split.ConsumedCapacity!.CapacityUnits, whole.ConsumedCapacity!.CapacityUnits > 10)).IsEqualTo((1.0, true));
    }

    [Test]
    public async Task Get_OneSection_ReadsNoneOfTheOthers()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await PutProductAsync(sandbox);

        // Act
        var stock = await sandbox.Shop.Of<ProductStock>().Get(ProductStock.Keys.Of(Keyboard)).Consistent().ReturnCapacity().SendAsync();

        // Assert
        await Assert.That((stock.Item!.Quantity, stock.ConsumedCapacity!.CapacityUnits)).IsEqualTo((5, 1.0));
    }

    [Test]
    public async Task Query_ThePartition_ReadsTheProductWhole()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await PutProductAsync(sandbox);

        // Act
        var sections = await sandbox.Shop.Query(ProductSummary.Keys.Pk.Of(Keyboard)).Of<ProductSummary, ProductStock, ProductManual>().ItemsAsync().ToListAsync();

        // Assert
        var read = sections.Select(static section => section.Match(static summary => summary.Name, static stock => stock.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture), static manual => manual.Text.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        await Assert.That(string.Join(",", read)).IsEqualTo("20000,5,Keyboard");
    }

    private static async Task PutProductAsync(CommandSandbox sandbox) =>
        await sandbox.Shop.Of<ProductSummary>().BatchWrite()
            .Put(new ProductSummary { Sku = Keyboard, Name = "Keyboard" })
            .Put(sandbox.Shop.Of<ProductStock>(), new ProductStock { Sku = Keyboard, Quantity = 5 })
            .Put(sandbox.Shop.Of<ProductManual>(), new ProductManual { Sku = Keyboard, Text = Manual })
            .SendAsync();
}

[DynamoDbEntity]
[Discriminator("PRODUCT_SUMMARY")]
[PartitionKey("PRODUCT#{Sku}")]
[SortKey("SUMMARY")]
internal sealed partial record ProductSummary
{
    public required string Sku { get; init; }

    public required string Name { get; init; }
}

[DynamoDbEntity]
[Discriminator("PRODUCT_STOCK")]
[PartitionKey("PRODUCT#{Sku}")]
[SortKey("STOCK")]
internal sealed partial record ProductStock
{
    public required string Sku { get; init; }

    public int Quantity { get; init; }
}

[DynamoDbEntity]
[Discriminator("PRODUCT_MANUAL")]
[PartitionKey("PRODUCT#{Sku}")]
[SortKey("MANUAL")]
internal sealed partial record ProductManual
{
    public required string Sku { get; init; }

    public required string Text { get; init; }
}

/// <summary>The same product in one item, to compare with.</summary>
[DynamoDbEntity]
[Discriminator("PRODUCT_WHOLE")]
[PartitionKey("WHOLE#{Sku}")]
[SortKey("DATA")]
internal sealed partial record UnsplitProduct
{
    public required string Sku { get; init; }

    public required string Manual { get; init; }

    public int Stock { get; init; }
}
