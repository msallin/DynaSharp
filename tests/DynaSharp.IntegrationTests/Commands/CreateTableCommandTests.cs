using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>Whether the emulators take the schema that <see cref="CreateTableCommand"/> makes from entities, and whether those entities can then use it. The bodies are pinned by the unit tests.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class CreateTableCommandTests(Backend backend)
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task CreateTable_ThreeEntitiesOverloadingAnIndex_EveryEntityWritesAndEveryIndexReadsThenTheTableIsDeleted()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var purchases = sandbox.Client.Table<Purchase>(Tables.UniqueName());
        var shoppers = purchases.Of<Shopper>();
        var articles = purchases.Of<Article>();

        // Act
        var created = await sandbox.CreateTableAsync(purchases, purchases.CreateTable().Including<Shopper>().Including<Article>().WaitUntilActive());
        await purchases.Put(new Purchase { CustomerId = "ann", Number = 1, Status = "OPEN", PlacedAt = Noon, Total = 12.5m, Lines = 3, Parcel = "P-1" }).SendAsync();
        await purchases.Put(new Purchase { CustomerId = "ann", Number = 2, Status = "OPEN", PlacedAt = Noon.AddHours(1), Total = 7m, Lines = 11 }).SendAsync();
        await shoppers.Put(new Shopper { Id = "ann", Tier = "GOLD", Name = "Ann", Email = "ann@example.ch" }).SendAsync();
        await articles.Put(new Article { Sku = "KB-01", Category = "office", Stock = 12 }).SendAsync();
        await articles.Put(new Article { Sku = "MS-02", Category = "office", Stock = 9 }).SendAsync();

        var byStatus = await purchases.Index(Purchase.Indexes.Gsi1).Query(Purchase.Keys.Gsi1Pk.Format("OPEN")).ItemsAsync().ToListAsync();
        var byTier = await shoppers.Index(Shopper.Indexes.Gsi1).Query(Shopper.Keys.Gsi1Pk.Format("GOLD")).ItemsAsync().ToListAsync();
        var byCategory = await articles.Index(Article.Indexes.Gsi1).Query(Article.Keys.Gsi1Pk.Format("office")).ItemsAsync().ToListAsync();
        var byParcel = await purchases.Index(Purchase.Indexes.ByParcel).Query(Purchase.Keys.ByParcelPk.Format("P-1")).ItemsAsync().ToListAsync();
        var byEmail = await shoppers.Index(Shopper.Indexes.ByEmail).Query(Shopper.Keys.ByEmailPk.Format("ann@example.ch")).ItemsAsync().ToListAsync();
        var byStock = await articles.Index(Article.Indexes.ByStock).Query("office", SortKey.Lt(10)).ItemsAsync().ToListAsync();
        var byTotal = await purchases.Index(Purchase.Indexes.ByTotal).Query(Purchase.Keys.Pk.Format("ann")).ItemsAsync().ToListAsync();
        var byLines = await purchases.Index(Purchase.Indexes.ByLines).Query(Purchase.Keys.Pk.Format("ann"), SortKey.Ge(10)).ItemsAsync().ToListAsync();
        var deleted = await purchases.DeleteTableAsync();

        // Assert (the number keys sort as numbers, so 9 is below 10 and 11 above it, which strings would turn around)
        await Assert.That((
            created.TableStatus,
            string.Join(",", byStatus.Select(static item => item.Number)),
            byTier.Single().Name,
            string.Join(",", byCategory.Select(static item => item.Sku)),
            byParcel.Single().Number,
            byEmail.Single().Id,
            byStock.Single().Sku,
            string.Join(",", byTotal.Select(static item => item.Number)),
            byLines.Single().Number,
            deleted.TableName))
            .IsEqualTo((TableStatus.Active, "1,2", "Ann", "KB-01,MS-02", 1, "ann", "MS-02", "2,1", 2, purchases.Name));
    }

    [Test]
    public async Task SendAsync_NumberAndBinaryKeys_TheKeysOfTheEntityFitTheTable()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var readings = sandbox.Client.Table<Reading>(Tables.UniqueName());
        byte[] stamp = [1, 2, 3];

        // Act
        await sandbox.CreateTableAsync(readings, readings.CreateTable().WaitUntilActive());
        await readings.Put(new Reading { Sensor = 7, Stamp = stamp, Value = 21 }).SendAsync();
        var read = await readings.Get(Reading.Keys.Of(7, stamp)).Consistent().ItemAsync();

        // Assert
        await Assert.That(read?.Value).IsEqualTo(21);
    }

    [Test]
    public async Task SendAsync_TableWithoutAnEntity_CreatesStringKeys()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");

        // Act
        await sandbox.CreateTableAsync(shop, shop.CreateTable().WaitUntilActive());
        var description = await shop.DescribeAsync();

        // Assert
        await Assert.That(string.Join(",", description.AttributeDefinitions.Select(static attribute => $"{attribute.AttributeName}:{attribute.AttributeType}"))).IsEqualTo("pk:String,sk:String");
    }

    [Test]
    public async Task SendAsync_TableWithoutAnEntityIncludingOne_HasItsIndexes()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");

        // Act
        await sandbox.CreateTableAsync(shop, shop.CreateTable().Including<Shopper>().WaitUntilActive());
        await shop.Of<Shopper>().Put(new Shopper { Id = "ann", Tier = "GOLD", Name = "Ann", Email = "ann@example.ch" }).SendAsync();
        var found = await shop.Of<Shopper>().Index(Shopper.Indexes.ByEmail).Query(Shopper.Keys.ByEmailPk.Format("ann@example.ch")).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(found.Single().Id).IsEqualTo("ann");
    }

    [Test]
    [NotInParallel] // DynamoDB Local answers this table with InternalFailure, over and over, while other tables change.
    public async Task SendAsync_Provisioned_HasTheCapacityWithAGlobalAndALocalIndex()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var purchases = sandbox.Client.Table<Purchase>(Tables.UniqueName());

        // Act
        await sandbox.CreateTableAsync(purchases, purchases.CreateTable().Provisioned(read: 5, write: 7).WaitUntilActive());
        var capacity = (await purchases.DescribeAsync()).ProvisionedThroughput;

        // Assert
        await Assert.That((capacity?.ReadCapacityUnits, capacity?.WriteCapacityUnits)).IsEqualTo(((long?)5, (long?)7));
    }

    [Test]
    [NotInParallel] // As above.
    public async Task SendAsync_Provisioned_GivesEachGlobalIndexThatCapacity()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.GlobalIndexCapacity);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var purchases = sandbox.Client.Table<Purchase>(Tables.UniqueName());

        // Act
        await sandbox.CreateTableAsync(purchases, purchases.CreateTable().Provisioned(read: 5, write: 7).WaitUntilActive());
        var indexes = (await purchases.DescribeAsync()).GlobalSecondaryIndexes;

        // Assert
        await Assert.That(string.Join(",", indexes.Select(static index => $"{index.ProvisionedThroughput?.ReadCapacityUnits}/{index.ProvisionedThroughput?.WriteCapacityUnits}"))).IsEqualTo("5/7,5/7");
    }

    [Test]
    public async Task SendAsync_TableExists_ThrowsResourceInUse()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");
        await sandbox.CreateTableAsync(shop, shop.CreateTable().WaitUntilActive());

        // Act
        var failure = await Items.FailureOfAsync<ResourceInUseException>(async () => await Tables.SendAsync(shop.CreateTable()));

        // Assert
        await Assert.That(failure.OperationName).IsEqualTo("CreateTable");
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndTheTableExists_ReturnsItAsItIs()
    {
        // Arrange (it does not compare schemas: the table stays what it was, without the index the second command asks for)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");
        await sandbox.CreateTableAsync(shop, shop.CreateTable().WaitUntilActive());

        // Act
        var description = await Tables.SendAsync(shop.CreateTable().Including<Shopper>().IfNotExists());

        // Assert
        await Assert.That((description.TableName, description.GlobalSecondaryIndexes.Count)).IsEqualTo((shop.Name, 0));
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndWaitUntilActive_SameCallTwice_ReturnsAnActiveTableBothTimes()
    {
        // Arrange (what a program does at every start)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");

        // Act
        var first = await sandbox.CreateTableAsync(shop, shop.CreateTable().WaitUntilActive().IfNotExists());
        var second = await Tables.SendAsync(shop.CreateTable().WaitUntilActive().IfNotExists());

        // Assert
        await Assert.That((first.TableStatus, second.TableStatus)).IsEqualTo((TableStatus.Active, TableStatus.Active));
    }

    [Test]
    public async Task ToRequest_ChangedWithWith_CreatesWhatTheCommandDoesNotModel()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");
        var request = shop.CreateTable().ToRequest() with { StreamSpecification = new StreamSpecification { StreamEnabled = true, StreamViewType = StreamViewType.NewImage } };

        // Act
        await sandbox.CreateTableAsync(request);
        var description = await shop.DescribeAsync();

        // Assert
        await Assert.That(description.StreamSpecification?.StreamEnabled == true).IsTrue();
    }

    [Test]
    public async Task DeleteTableAsync_MissingTable_ThrowsResourceNotFound()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var missing = sandbox.Client.Table(Tables.UniqueName(), "pk");

        // Act
        var failure = await Items.FailureOfAsync<ResourceNotFoundException>(async () => await missing.DeleteTableAsync());

        // Assert
        await Assert.That(failure.OperationName).IsEqualTo("DeleteTable");
    }

    [Test]
    public async Task DeleteTableAsync_Table_TakesItsItemsWithIt()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");
        await sandbox.CreateTableAsync(shop, shop.CreateTable().WaitUntilActive());
        await shop.Put(new Item { ["pk"] = "a", ["sk"] = "b" }).SendAsync();

        // Act
        await shop.DeleteTableAsync();
        await WaitUntilGoneAsync(shop);
        await sandbox.CreateTableAsync(shop, shop.CreateTable().WaitUntilActive());
        var again = await shop.Get("a", "b").Consistent().SendAsync();

        // Assert
        await Assert.That(again.Found).IsFalse();
    }

    // The table goes when it goes, and the call says so: a loop over a bound, without a pause, because each round is a request.
    private static async Task WaitUntilGoneAsync(Table<Item> table)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        try
        {
            while (true)
            {
                await table.DescribeAsync(timeout.Token);
            }
        }
        catch (ResourceNotFoundException)
        {
            // Gone.
        }
    }
}
