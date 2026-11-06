using System.Diagnostics;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Mapping;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>Whether the backends take the schema that <see cref="CreateTableCommand"/> makes from entities, and whether those entities can then use it. The bodies are pinned by the unit tests.</summary>
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
        var byStock = await articles.Index(Article.Indexes.ByStock).Query("office", SortKeyCondition.Lt(10)).ItemsAsync().ToListAsync();
        var byTotal = await purchases.Index(Purchase.Indexes.ByTotal).Query(Purchase.Keys.Pk.Format("ann")).ItemsAsync().ToListAsync();
        var byLines = await purchases.Index(Purchase.Indexes.ByLines).Query(Purchase.Keys.Pk.Format("ann"), SortKeyCondition.Ge(10)).ItemsAsync().ToListAsync();
        var deleted = await purchases.DeleteTableAsync();

        // Assert (the number keys sort as numbers, so 9 is below 10 and 11 above it, which strings would turn around)
        await Assert.That((
            created.Table.TableStatus,
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
    public async Task SendAsync_IfNotExistsAndTheTableExists_ReturnsItAsItIsAndReportsTheIndexesItLacks()
    {
        // Arrange (the table stays what it was, without the indexes the second command asks for, which the result lists)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");
        await sandbox.CreateTableAsync(shop, shop.CreateTable().WaitUntilActive());

        // Act
        var result = await Tables.SendAsync(shop.CreateTable().Including<Shopper>().IfNotExists());

        // Assert
        await Assert.That((result.Table.TableName, result.Table.GlobalSecondaryIndexes.Count, result.Created, string.Join(",", result.Drift.Select(static drift => $"{drift.Kind}:{drift.IndexName}"))))
            .IsEqualTo((shop.Name, 0, false, "IndexMissing:GSI1,IndexMissing:ByEmail"));
    }

    [Test]
    public async Task SendAsync_IfNotExistsOverATableWithAnotherKey_ThrowsNamingBothKeysAndChangesNothing()
    {
        // Arrange (a table created with the partition key Resource, which code that writes pk and sk meets)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var locks = sandbox.Client.Table(Tables.UniqueName(), "Resource");
        await sandbox.CreateTableAsync(locks, locks.CreateTable().WaitUntilActive());
        using var log = new OperationLog(sandbox.Emulator);
        using var client = new DynamoDbClient(sandbox.Emulator.CreateOptions() with { HttpHandler = log });

        // Act
        var failure = await Items.FailureOfAsync<InvalidOperationException>(async () => await Tables.SendAsync(client.Table<Shopper>(locks.Name).CreateTable().IfNotExists().WaitUntilActive().TimeToLive("expires")));

        // Assert
        await Assert.That((failure.Message.StartsWith($"The table {locks.Name} exists with the partition key Resource (String) and no sort key, and this command expects the partition key pk (String) and sort key sk (String).", StringComparison.Ordinal), log.Operations))
            .IsEqualTo((true, "DescribeTable"));
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
        await Assert.That((first.Table.TableStatus, first.Created, second.Table.TableStatus, second.Created)).IsEqualTo((TableStatus.Active, true, TableStatus.Active, false));
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndWaitUntilActiveOnAnActiveTable_SendsOneDescribeTableAndNoCreateTable()
    {
        // Arrange (a role without dynamodb:CreateTable gets through this, and no failed call lands in CloudTrail)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");
        await sandbox.CreateTableAsync(shop, shop.CreateTable().WaitUntilActive());
        using var log = new OperationLog(sandbox.Emulator);
        using var client = new DynamoDbClient(sandbox.Emulator.CreateOptions() with { HttpHandler = log });

        // Act
        await Tables.SendAsync(client.Table(shop.Name, "pk", "sk").CreateTable().IfNotExists().WaitUntilActive());

        // Assert
        await Assert.That(log.Operations).IsEqualTo("DescribeTable");
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndNoTable_DescribesAndCreates()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        using var log = new OperationLog(sandbox.Emulator);
        using var client = new DynamoDbClient(sandbox.Emulator.CreateOptions() with { HttpHandler = log });
        var shop = client.Table(Tables.UniqueName(), "pk", "sk");

        // Act
        await sandbox.CreateTableAsync(shop, shop.CreateTable().IfNotExists());

        // Assert
        await Assert.That(log.Operations).IsEqualTo("DescribeTable,CreateTable");
    }

    [Test]
    public async Task SendAsync_IfNotExistsFromInstancesThatStartTogether_AllGetTheActiveTable()
    {
        // Arrange (all of them may find nothing, and only one create can win)
        const int Instances = 6;
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, Instances).Select(_ => Tables.SendAsync(shop.CreateTable().IfNotExists().WaitUntilActive())));
        await sandbox.CreateTableAsync(shop, shop.CreateTable().IfNotExists());

        // Assert (one create wins, and the others find its table)
        await Assert.That((results.Select(static result => result.Table.TableStatus).Distinct().Single(), results.Count(static result => result.Created))).IsEqualTo((TableStatus.Active, 1));
    }

    [Test]
    public async Task WaitUntilActiveAsync_TableWithoutAnIndexTheEntityDeclares_TimesOutNamingIt()
    {
        // Arrange (an old table, which an entity that was given an index meets)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");
        await sandbox.CreateTableAsync(shop, shop.CreateTable().WaitUntilActive());
        var shoppers = shop.Of<Shopper>();

        // Act
        var failure = await Items.FailureOfAsync<TableNotActiveException>(async () => await shoppers.WaitUntilActiveAsync(TimeSpan.Zero));

        // Assert
        await Assert.That((failure.LastStatus, failure.PendingIndex)).IsEqualTo(((TableStatus?)TableStatus.Active, (string?)"GSI1"));
    }

    [Test]
    public async Task WaitUntilActiveAsync_TableWithTheIndexesOfTheEntity_Returns()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shoppers = sandbox.Client.Table<Shopper>(Tables.UniqueName());
        await sandbox.CreateTableAsync(shoppers, shoppers.CreateTable());

        // Act
        var description = await shoppers.WaitUntilActiveAsync(TimeSpan.FromMinutes(1));

        // Assert
        await Assert.That(description.GlobalSecondaryIndexes.Select(static index => index.IndexStatus).Distinct().Single()).IsEqualTo(IndexStatus.Active);
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndWaitUntilActiveOnATableWithoutADeclaredIndex_FailsWithinSecondsNamingIt()
    {
        // Arrange (an old table that no wait gives the index, so the wait ends long before its five minutes)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");
        await sandbox.CreateTableAsync(shop, shop.CreateTable().WaitUntilActive());
        var started = Stopwatch.GetTimestamp();

        // Act
        var failure = await Items.FailureOfAsync<TableNotActiveException>(async () => await Tables.SendAsync(shop.CreateTable().Including<Ticket>().IfNotExists().WaitUntilActive()));
        var elapsed = Stopwatch.GetElapsedTime(started);

        // Assert
        await Assert.That((failure.PendingIndex, failure.LastStatus, elapsed < TimeSpan.FromMinutes(1))).IsEqualTo(((string?)"ByDesk", (TableStatus?)TableStatus.Active, true));
    }

    [Test]
    [NotInParallel] // DynamoDB Local answers calls around a new index with InternalFailure while other tables change.
    public async Task SendAsync_IfNotExistsAfterUpdateTableAddedTheIndex_WaitsForItAndCreatesNothing()
    {
        // Arrange (the recipe of the README: the index goes in through UpdateTable, and the command that follows waits for it)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");
        await sandbox.CreateTableAsync(shop, shop.CreateTable().WaitUntilActive());
        var command = shop.CreateTable().Including<Ticket>();
        var declared = command.ToRequest();
        var added = declared.GlobalSecondaryIndexes!.Single();
        var keys = declared.AttributeDefinitions.Where(attribute => added.KeySchema.Any(key => key.AttributeName == attribute.AttributeName)).ToList();

        // Act
        await Tables.UpdateAsync(sandbox.Client, new UpdateTableRequest { TableName = shop.Name, AttributeDefinitions = keys, GlobalSecondaryIndexUpdates = [new GlobalSecondaryIndexUpdate { Create = added }] });
        var result = await Tables.SendAsync(command.IfNotExists().WaitUntilActive(Tables.IndexBuildTime));

        // Assert
        await Assert.That((result.Created, result.Table.GlobalSecondaryIndexes.Single().IndexStatus)).IsEqualTo((false, IndexStatus.Active));
    }

    [Test]
    public async Task SendAsync_Stream_CreatesTheTableWithIt()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");

        // Act
        await sandbox.CreateTableAsync(shop, shop.CreateTable().Stream(StreamViewType.NewAndOldImages).WaitUntilActive());
        var stream = (await shop.DescribeAsync()).StreamSpecification;

        // Assert
        await Assert.That((stream?.StreamEnabled, stream?.StreamViewType)).IsEqualTo(((bool?)true, (StreamViewType?)StreamViewType.NewAndOldImages));
    }

    [Test]
    public async Task SendAsync_StreamAndIfNotExistsOnAnExistingTable_LeavesItWithoutOne()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");
        await sandbox.CreateTableAsync(shop, shop.CreateTable().WaitUntilActive());

        // Act
        await Tables.SendAsync(shop.CreateTable().Stream(StreamViewType.KeysOnly).IfNotExists());
        var stream = (await shop.DescribeAsync()).StreamSpecification;

        // Assert
        await Assert.That(stream?.StreamEnabled == true).IsFalse();
    }

    [Test]
    public async Task SendAsync_TimeToLiveOnANewTable_TurnsItOn()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");

        // Act
        var result = await sandbox.CreateTableAsync(shop, shop.CreateTable().TimeToLive("expires"));
        var timeToLive = await DescribeTimeToLiveAsync(sandbox, shop);

        // Assert
        await Assert.That((result.Table.TableStatus, result.Created, result.TimeToLiveEnabled, timeToLive!.TimeToLiveStatus is TimeToLiveStatus.Enabled or TimeToLiveStatus.Enabling, timeToLive.AttributeName))
            .IsEqualTo((TableStatus.Active, true, true, true, (string?)"expires"));
    }

    [Test]
    public async Task SendAsync_EntityWithTimeToLive_TurnsItOnWithoutAClause()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var logins = sandbox.Client.Table<Login>(Tables.UniqueName());

        // Act
        var result = await sandbox.CreateTableAsync(logins, logins.CreateTable());
        var timeToLive = await DescribeTimeToLiveAsync(sandbox, logins);

        // Assert
        await Assert.That((result.TimeToLiveEnabled, timeToLive!.TimeToLiveStatus is TimeToLiveStatus.Enabled or TimeToLiveStatus.Enabling, timeToLive.AttributeName)).IsEqualTo((true, true, (string?)"expires"));
    }

    [Test]
    public async Task SendAsync_TimeToLiveOnATableThatExists_TurnsItOn()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");
        await sandbox.CreateTableAsync(shop, shop.CreateTable().WaitUntilActive());
        var name = new Attr("expires");

        // Act
        var result = await Tables.SendAsync(shop.CreateTable().IfNotExists().TimeToLive(name));

        // Assert
        await Assert.That(((await DescribeTimeToLiveAsync(sandbox, shop))!.AttributeName, result.Created, result.TimeToLiveEnabled)).IsEqualTo(((string?)"expires", false, true));
    }

    [Test]
    public async Task SendAsync_TimeToLiveThatIsOnAlready_ChangesNothing()
    {
        // Arrange (what a program does at every start)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");
        await sandbox.CreateTableAsync(shop, shop.CreateTable().TimeToLive("expires"));
        using var log = new OperationLog(sandbox.Emulator);
        using var client = new DynamoDbClient(sandbox.Emulator.CreateOptions() with { HttpHandler = log });

        // Act
        var result = await Tables.SendAsync(client.Table(shop.Name, "pk", "sk").CreateTable().IfNotExists().TimeToLive("expires"));

        // Assert
        await Assert.That((log.Operations, result.TimeToLiveEnabled)).IsEqualTo(("DescribeTable,DescribeTimeToLive", false));
    }

    [Test]
    public async Task SendAsync_TimeToLiveFromInstancesThatStartTogether_AllGetThrough()
    {
        // Arrange (several of them can find it off, and DynamoDB refuses the update of all but the first)
        const int Instances = 6;
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");
        await sandbox.CreateTableAsync(shop, shop.CreateTable().WaitUntilActive());

        // Act
        await Task.WhenAll(Enumerable.Range(0, Instances).Select(_ => Tables.SendAsync(shop.CreateTable().IfNotExists().TimeToLive("expires"))));

        // Assert
        await Assert.That((await DescribeTimeToLiveAsync(sandbox, shop))!.AttributeName).IsEqualTo("expires");
    }

    [Test]
    public async Task SendAsync_TimeToLiveOnAnotherAttribute_ReportsItAndLeavesItOn()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var shop = sandbox.Client.Table(Tables.UniqueName(), "pk", "sk");
        await sandbox.CreateTableAsync(shop, shop.CreateTable().TimeToLive("expires"));

        // Act
        var result = await Tables.SendAsync(shop.CreateTable().IfNotExists().TimeToLive("other"));

        // Assert
        await Assert.That((result.Drift.Single().Kind, result.TimeToLiveEnabled, (await DescribeTimeToLiveAsync(sandbox, shop))!.AttributeName)).IsEqualTo((TableDriftKind.TimeToLiveDiffers, false, (string?)"expires"));
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

    private static async Task<TimeToLiveDescription?> DescribeTimeToLiveAsync<T>(Sandbox sandbox, Table<T> table)
        where T : IDynamoDbDocument<T> =>
        (await sandbox.Client.DescribeTimeToLiveAsync(new DescribeTimeToLiveRequest { TableName = table.Name })).TimeToLiveDescription;

    // The table goes when it goes, and the call says so: a loop over a bound, without a pause, because each round is a request.
    private static async Task WaitUntilGoneAsync(Table<Item> table)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
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
