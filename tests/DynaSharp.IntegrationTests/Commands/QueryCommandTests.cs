using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Mapping;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;
using static DynaSharp.Expr;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>Query as a command against the emulators: conditions on the sort key, paging, filters that empty pages, tokens, indexes and typed reads.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class QueryCommandTests(Backend backend)
{
    [Test]
    public async Task ItemsAsync_Partition_GivesItsItemsInOrderOfTheSortKey()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 5);
        await sandbox.PutOrdersAsync("b", 3);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Query("a").ItemsAsync());

        // Assert
        await Assert.That(Read.SortKeys(items)).IsEqualTo("ORDER#001,ORDER#002,ORDER#003,ORDER#004,ORDER#005");
    }

    [Test]
    public async Task ItemsAsync_Descending_GivesTheOtherOrder()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 3);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Query("a").Descending().ItemsAsync());

        // Assert
        await Assert.That(Read.SortKeys(items)).IsEqualTo("ORDER#003,ORDER#002,ORDER#001");
    }

    [Test]
    public async Task ItemsAsync_EverySortKeyCondition_SelectsWhatItSays()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 5);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "PROFILE", ["status"] = "OPEN", ["placedAt"] = "x", ["rank"] = 9 }).SendAsync();
        var shop = sandbox.Shop;

        // Act
        var results = new[]
        {
            Read.SortKeys(await Read.ToListAsync(shop.Query("a", SortKey.Eq("ORDER#002")).ItemsAsync())),
            Read.SortKeys(await Read.ToListAsync(shop.Query("a", SortKey.Lt("ORDER#002")).ItemsAsync())),
            Read.SortKeys(await Read.ToListAsync(shop.Query("a", SortKey.Le("ORDER#002")).ItemsAsync())),
            Read.SortKeys(await Read.ToListAsync(shop.Query("a", SortKey.Gt("ORDER#004")).ItemsAsync())),
            Read.SortKeys(await Read.ToListAsync(shop.Query("a", SortKey.Ge("ORDER#005")).ItemsAsync())),
            Read.SortKeys(await Read.ToListAsync(shop.Query("a", SortKey.Between("ORDER#002", "ORDER#003")).ItemsAsync())),
            Read.SortKeys(await Read.ToListAsync(shop.Query("a", SortKey.BeginsWith("PRO")).ItemsAsync())),
        };

        // Assert
        await Assert.That(string.Join(";", results)).IsEqualTo("ORDER#002;ORDER#001;ORDER#001,ORDER#002;ORDER#005,PROFILE;ORDER#005,PROFILE;ORDER#002,ORDER#003;PROFILE");
    }

    [Test]
    public async Task ItemsAsync_LimitBelowTheItems_ReadsThemAllInPages()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 5);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Query("a").Limit(2).ItemsAsync());
        var pages = await Read.ToListAsync(sandbox.Shop.Query("a").Limit(2).PagesAsync());

        // Assert (DynamoDB may add an empty last page: a full page has a next key, because it does not look ahead)
        await Assert.That((items.Count, pages.Sum(static page => page.Count), pages.All(static page => page.Count <= 2), pages.Count >= 3)).IsEqualTo((5, 5, true, true));
    }

    [Test]
    public async Task ItemsAsync_FilteredEmptyPage_FollowsIt()
    {
        // Arrange: only the last item passes, and pages of one make the others come back empty
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 6);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Query("a").Limit(1).Filter(Attr("total").Eq(6)).ItemsAsync());
        var pages = await Read.ToListAsync(sandbox.Shop.Query("a").Limit(1).Filter(Attr("total").Eq(6)).PagesAsync());

        // Assert
        await Assert.That((Read.SortKeys(items), pages[0].Count, pages[0].ScannedCount, pages[0].HasMore)).IsEqualTo(("ORDER#006", 0, 1, true));
    }

    [Test]
    public async Task ItemsAsync_InterpolatedFilterOnReservedWords_Works()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 4);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Query("a").Filter($"{Attr("status")} = {"OPEN"} AND {Attr("total")} > {2} AND begins_with({Attr("kind")}, {"ev"})").ItemsAsync());

        // Assert
        await Assert.That(Read.SortKeys(items)).IsEqualTo("ORDER#004");
    }

    [Test]
    public async Task ItemsAsync_Project_GivesThoseAttributes()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 1);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Query("a").Project("status", "total").ItemsAsync());

        // Assert
        await Assert.That(string.Join(",", items.Single().Keys.Order(StringComparer.Ordinal))).IsEqualTo("status,total");
    }

    [Test]
    public async Task ItemsAsync_Consistent_SeesTheLastWrite()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 1);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Query("a").Consistent().ItemsAsync());

        // Assert
        await Assert.That(items.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ItemsAsync_BreakOutEarly_LeavesTheClientUsable()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 3);
        var first = string.Empty;

        // Act
        await foreach (var item in sandbox.Shop.Query("a").Limit(1).ItemsAsync())
        {
            first = item.GetString("sk");
            break;
        }

        var next = await Read.ToListAsync(sandbox.Shop.Query("a").Limit(1).ItemsAsync());

        // Assert
        await Assert.That((first, next.Count)).IsEqualTo(("ORDER#001", 3));
    }

    [Test]
    public async Task ItemsAsync_Cancelled_Throws()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 3);
        using var cancellation = new CancellationTokenSource();
        var seen = 0;

        // Act
        var failure = await Items.FailureOfAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in sandbox.Shop.Query("a").Limit(1).ItemsAsync(cancellation.Token))
            {
                seen++;
                await cancellation.CancelAsync();
            }
        });

        // Assert
        await Assert.That((failure.CancellationToken == cancellation.Token, seen)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task PageAsync_TokenThroughItsText_WalksThePartition()
    {
        // Arrange: what a web API does: every request builds the query and gets a string from its client
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 5);
        var token = string.Empty;
        List<string?> keys = [];

        // Act
        bool more;
        do
        {
            var page = await sandbox.Shop.Query("a", SortKey.BeginsWith("ORDER#")).Limit(2).PageAsync(ContinuationToken.Parse(token));
            keys.AddRange(page.Items.Select(static item => item.GetString("sk")));
            token = page.Next.ToString();
            more = page.HasMore;
        }
        while (more);

        // Assert
        await Assert.That(string.Join(",", keys)).IsEqualTo("ORDER#001,ORDER#002,ORDER#003,ORDER#004,ORDER#005");
    }

    [Test]
    public async Task PageAsync_Token_HoldsTheRealLastEvaluatedKey()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 3);

        // Act
        var page = await sandbox.Shop.Query("a").Limit(2).PageAsync();
        var response = await sandbox.Client.QueryAsync(new QueryRequest
        {
            TableName = sandbox.TableName,
            KeyConditionExpression = "pk = :pk",
            ExpressionAttributeValues = Items.Values((":pk", "a")),
            Limit = 2,
        });

        // Assert
        await Assert.That(page.Next.ToKey()!.Equals(response.LastEvaluatedKey)).IsTrue();
    }

    [Test]
    public async Task PageAsync_TokenOfAResponse_ContinuesInACommand()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 3);
        var response = await sandbox.Client.QueryAsync(new QueryRequest
        {
            TableName = sandbox.TableName,
            KeyConditionExpression = "pk = :pk",
            ExpressionAttributeValues = Items.Values((":pk", "a")),
            Limit = 2,
        });

        // Act
        var page = await sandbox.Shop.Query("a").PageAsync(ContinuationToken.FromKey(response.LastEvaluatedKey!));

        // Assert
        await Assert.That(Read.SortKeys(page.Items)).IsEqualTo("ORDER#003");
    }

    [Test]
    public async Task PageAsync_TokenOfACommand_ContinuesInARequest()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 3);
        var page = await sandbox.Shop.Query("a").Limit(2).PageAsync();

        // Act
        var response = await sandbox.Client.QueryAsync(new QueryRequest
        {
            TableName = sandbox.TableName,
            KeyConditionExpression = "pk = :pk",
            ExpressionAttributeValues = Items.Values((":pk", "a")),
            ExclusiveStartKey = page.Next.ToKey(),
        });

        // Assert
        await Assert.That(response.Items.Single()["sk"].AsString()).IsEqualTo("ORDER#003");
    }

    [Test]
    public async Task PageAsync_ReturnCapacity_HoldsTheCapacity()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 1);

        // Act
        var page = await sandbox.Shop.Query("a").ReturnCapacity().PageAsync();

        // Assert
        await Assert.That(page.ConsumedCapacity!.CapacityUnits!.Value).IsGreaterThan(0d);
    }

    [Test]
    public async Task PageAsync_ExhaustedPartition_HasNoNext()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 2);

        // Act
        var page = await sandbox.Shop.Query("a").PageAsync();

        // Assert
        await Assert.That((page.Count, page.HasMore, page.Next.ToString())).IsEqualTo((2, false, string.Empty));
    }

    [Test]
    public async Task PageAsync_RejectedRequest_ExplainsItsExpressions()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);

        // Act: a filter on a key is what DynamoDB rejects
        var failure = await Items.FailureOfAsync<ValidationException>(async () => await sandbox.Shop.Query("a").Filter(Attr("pk").Eq("a")).PageAsync());

        // Assert
        await Assert.That((failure.Expressions!.ContainsKey("FilterExpression"), failure.ExpressionNames!["#n0"])).IsEqualTo((true, "pk"));
    }

    [Test]
    public async Task CountAsync_Filter_CountsWhatPasses()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 7);

        // Act
        var all = await sandbox.Shop.Query("a").CountAsync();
        var even = await sandbox.Shop.Query("a").Limit(2).Filter(Attr("kind").Eq("even")).CountAsync();

        // Assert
        await Assert.That((all, even)).IsEqualTo((7, 3));
    }

    [Test]
    public async Task ItemsAsync_GlobalIndex_ReadsAcrossPartitions()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 2);
        await sandbox.PutOrdersAsync("b", 3);
        await sandbox.PutOrdersAsync("c", 1, "CLOSED");
        var byStatus = sandbox.Shop.Index(ReadSandbox.GlobalIndex, "status", "placedAt");

        // Act
        var items = await Read.ToListAsync(byStatus.Query("OPEN", SortKey.Ge("2026-002")).ItemsAsync());

        // Assert
        await Assert.That(string.Join(",", items.Select(static item => item.GetString("pk") + item.GetString("placedAt")).Order(StringComparer.Ordinal))).IsEqualTo("a2026-002,b2026-002,b2026-003");
    }

    [Test]
    public async Task PageAsync_GlobalIndex_TokenCarriesTheKeysOfIndexAndTable()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 3);
        var byStatus = sandbox.Shop.Index(ReadSandbox.GlobalIndex, "status", "placedAt");

        // Act
        var first = await byStatus.Query("OPEN").Limit(2).PageAsync();
        var second = await byStatus.Query("OPEN").Limit(2).PageAsync(ContinuationToken.Parse(first.Next.ToString()));

        // Assert
        await Assert.That((string.Join(",", first.Next.ToKey()!.Keys.Order(StringComparer.Ordinal)), Read.SortKeys(second.Items))).IsEqualTo(("pk,placedAt,sk,status", "ORDER#003"));
    }

    [Test]
    public async Task ItemsAsync_GlobalIndexConsistent_ThrowsBeforeTheRequest()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        var byStatus = sandbox.Shop.Index(ReadSandbox.GlobalIndex, "status", "placedAt");

        // Act
        var failure = await Items.FailureOfAsync<InvalidOperationException>(() => Read.ToListAsync(byStatus.Query("OPEN").Consistent().ItemsAsync()));

        // Assert
        await Assert.That(failure.Message).Contains("global secondary index");
    }

    [Test]
    public async Task ItemsAsync_GlobalIndexConsistentThatIsNotKnownToBeGlobal_IsRejectedByDynamoDb()
    {
        // Arrange (with the partition key of the table it could be a local index, as far as the names say)
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var byType = sandbox.Shop.Index(ShopSandbox.ByPartitionAndType, "pk", "_type");

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => Read.ToListAsync(byType.Query("a").Consistent().ItemsAsync()));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ValidationException");
    }

    [Test]
    public async Task ItemsAsync_LocalIndex_ReadsThePartitionInItsOrder()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 3);
        var byRank = sandbox.Shop.Index(ReadSandbox.LocalIndex, "pk", "rank");

        // Act
        var items = await Read.ToListAsync(byRank.Query("a", SortKey.Le(1)).Consistent().ItemsAsync());

        // Assert (the rank falls as the sort key rises)
        await Assert.That(Read.SortKeys(items)).IsEqualTo("ORDER#003,ORDER#002");
    }

    [Test]
    public async Task CountAsync_Indexes_Count()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 4);

        // Act
        var global = await sandbox.Shop.Index(ReadSandbox.GlobalIndex, "status", "placedAt").Query("OPEN").CountAsync();
        var local = await sandbox.Shop.Index(ReadSandbox.LocalIndex, "pk", "rank").Query("a").CountAsync();

        // Assert
        await Assert.That((global, local)).IsEqualTo((4, 4));
    }

    [Test]
    public async Task ItemsAsync_Entity_GivesItsOwnItemsOfAPartitionOfSeveralTypes()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await profiles.Put(new Profile("alice", "Alice", 3)).SendAsync();
        await sandbox.Shop.Put(new Item { ["pk"] = "USER#alice", ["sk"] = "ORDER#1", ["_type"] = "ORDER", ["fullName"] = "not a profile" }).SendAsync();
        await sandbox.Shop.Put(new Item { ["pk"] = "USER#alice", ["sk"] = "ORDER#2", ["_type"] = "ORDER" }).SendAsync();

        // Act
        var page = await profiles.Query("USER#alice").PageAsync();

        // Assert (the others were read and paid for, which is the point of ScannedCount)
        await Assert.That((page.Items.Single(), page.Count, page.ScannedCount)).IsEqualTo((new Profile("alice", "Alice", 3), 1, 3));
    }

    [Test]
    public async Task ItemsAsync_EntityInAnEmptyPageFollowsTheFilter_ReadsOn()
    {
        // Arrange: the item of the entity sorts last, behind pages of other types
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await sandbox.Shop.Put(new Item { ["pk"] = "USER#alice", ["sk"] = "ORDER#1", ["_type"] = "ORDER" }).SendAsync();
        await sandbox.Shop.Put(new Item { ["pk"] = "USER#alice", ["sk"] = "ORDER#2", ["_type"] = "ORDER" }).SendAsync();
        await profiles.Put(new Profile("alice", "Alice")).SendAsync();

        // Act
        var items = await Read.ToListAsync(profiles.Query("USER#alice").Limit(1).ItemsAsync());

        // Assert
        await Assert.That(items.Single().FullName).IsEqualTo("Alice");
    }

    [Test]
    public async Task ItemsAsync_EntityWithProjection_ReadsWhatTheEntityNeeds()
    {
        // Arrange: the projection names no key and no discriminator, which join it on their own
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await profiles.Put(new Profile("alice", "Alice", 3)).SendAsync();

        // Act
        var items = await Read.ToListAsync(profiles.Query("USER#alice").Project("id", "fullName").ItemsAsync());

        // Assert
        await Assert.That(items.Single()).IsEqualTo(new Profile("alice", "Alice"));
    }

    [Test]
    public async Task ItemsAsync_EntityThatProjectsKeysItself_IsNotRejected()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await profiles.Put(new Profile("alice", "Alice", 3)).SendAsync();

        // Act
        var items = await Read.ToListAsync(profiles.Query("USER#alice").Project("id", "pk", "sk", "_type", "fullName").ItemsAsync());

        // Assert
        await Assert.That(items.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ItemsAsync_OtherTypeThroughTheItemTable_IsThere()
    {
        // Arrange: a table without a schema reads every type of a partition
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Of<Profile>().Put(new Profile("alice", "Alice")).SendAsync();
        await sandbox.Shop.Put(new Item { ["pk"] = "USER#alice", ["sk"] = "ORDER#1", ["_type"] = "ORDER" }).SendAsync();

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Query("USER#alice").ItemsAsync());

        // Assert
        await Assert.That(items.Count).IsEqualTo(2);
    }

    [Test]
    public async Task CountAsync_Entity_CountsItsOwnItems()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Of<Profile>().Put(new Profile("alice", "Alice")).SendAsync();
        await sandbox.Shop.Put(new Item { ["pk"] = "USER#alice", ["sk"] = "ORDER#1", ["_type"] = "ORDER" }).SendAsync();

        // Act
        var count = await sandbox.Shop.Of<Profile>().Query("USER#alice").CountAsync();

        // Assert
        await Assert.That(count).IsEqualTo(1);
    }
}
