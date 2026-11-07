using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using static DynaSharp.Expr;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>Scan as a command against the emulators: pages, segments, parallel segments, indexes, typed reads, and the two calls about a table.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class ScanCommandTests(Backend backend)
{
    [Test]
    public async Task ItemsAsync_Table_GivesEveryItem()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 2);
        await sandbox.PutOrdersAsync("b", 3);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Scan().ItemsAsync());

        // Assert
        await Assert.That(items.Count).IsEqualTo(5);
    }

    [Test]
    public async Task ItemsAsync_LimitPerPage_ReadsEverything()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 5);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Scan().Limit(2).ItemsAsync());

        // Assert
        await Assert.That(items.Count).IsEqualTo(5);
    }

    [Test]
    public async Task ItemsAsync_FilteredEmptyPages_AreFollowed()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 5);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Scan().Limit(1).Filter(Attr("total").Eq(5)).ItemsAsync());
        var pages = await Read.ToListAsync(sandbox.Shop.Scan().Limit(1).Filter(Attr("total").Eq(5)).PagesAsync());

        // Assert
        await Assert.That((items.Count, pages.Any(static page => page.Count == 0 && page.HasMore))).IsEqualTo((1, true));
    }

    [Test]
    public async Task ItemsAsync_InterpolatedFilter_Works()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 4);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Scan().Filter($"begins_with({Attr("sk")}, {"ORDER#"}) AND {Attr("total")} > {3}").Project("sk", "total").ItemsAsync());

        // Assert
        await Assert.That((Read.SortKeys(items), items.Single().Count)).IsEqualTo(("ORDER#004", 2));
    }

    [Test]
    public async Task PageAsync_TokenThroughItsText_WalksTheTable()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 5);
        var token = string.Empty;
        var count = 0;

        // Act
        bool more;
        do
        {
            var page = await sandbox.Shop.Scan().Limit(2).PageAsync(ContinuationToken.Parse(token));
            count += page.Count;
            token = page.Next.ToString();
            more = page.HasMore;
        }
        while (more);

        // Assert
        await Assert.That(count).IsEqualTo(5);
    }

    [Test]
    public async Task CountAsync_Filter_CountsWhatPasses()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 3);
        await sandbox.PutOrdersAsync("b", 4);

        // Act
        var all = await sandbox.Shop.Scan().CountAsync();
        var even = await sandbox.Shop.Scan().Limit(2).Filter(Attr("kind").Eq("even")).CountAsync();

        // Assert
        await Assert.That((all, even)).IsEqualTo((7, 3));
    }

    [Test]
    public async Task ItemsAsync_GlobalIndex_GivesItsItems()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 2);
        await sandbox.Shop.Put(new Item { ["pk"] = "b", ["sk"] = "not in the index" }).SendAsync();

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Index(ReadSandbox.GlobalIndex, "status", "placedAt").Scan().ItemsAsync());

        // Assert
        await Assert.That(items.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ItemsAsync_Entity_GivesItsOwnItems()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Of<Profile>().Put(new Profile("alice", "Alice")).SendAsync();
        await sandbox.Shop.Of<Profile>().Put(new Profile("bob", "Bob")).SendAsync();
        await sandbox.Shop.Put(new Item { ["pk"] = "USER#alice", ["sk"] = "ORDER#1", ["_type"] = "ORDER" }).SendAsync();

        // Act
        var page = await sandbox.Shop.Of<Profile>().Scan().PageAsync();

        // Assert
        await Assert.That((page.Count, page.ScannedCount)).IsEqualTo((2, 3));
    }

    [Test]
    public async Task ItemsAsync_Segments_TogetherGiveEveryItemOnce()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await WriteManyPartitionsAsync(sandbox, 30);
        var seen = new List<string?>();

        // Act
        for (var segment = 0; segment < 4; segment++)
        {
            seen.AddRange((await Read.ToListAsync(sandbox.Shop.Scan().Segment(segment, of: 4).ItemsAsync())).Select(static item => item.GetString("pk")));
        }

        // Assert
        await Assert.That((seen.Count, seen.Distinct().Count())).IsEqualTo((30, 30));
    }

    [Test]
    public async Task PageAsync_Segment_ContinuesInItsSegment()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await WriteManyPartitionsAsync(sandbox, 30);
        var count = 0;

        // Act
        for (var segment = 0; segment < 2; segment++)
        {
            var token = default(ContinuationToken);
            bool more;
            do
            {
                var page = await sandbox.Shop.Scan().Segment(segment, of: 2).Limit(4).PageAsync(token);
                count += page.Count;
                token = page.Next;
                more = page.HasMore;
            }
            while (more);
        }

        // Assert
        await Assert.That(count).IsEqualTo(30);
    }

    [Test]
    public async Task ItemsAsync_Parallel_GivesEveryItemOnce()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await WriteManyPartitionsAsync(sandbox, 40);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Scan().Limit(3).Parallel(4).ItemsAsync());

        // Assert
        await Assert.That((items.Count, items.Select(static item => item.GetString("pk")).Distinct().Count())).IsEqualTo((40, 40));
    }

    [Test]
    public async Task ItemsAsync_ParallelWithFilter_FollowsEmptyPages()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await WriteManyPartitionsAsync(sandbox, 20);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Scan().Limit(1).Filter(Attr("number").Eq(7)).Parallel(3).ItemsAsync());

        // Assert
        await Assert.That(items.Single().GetString("pk")).IsEqualTo("p7");
    }

    [Test]
    public async Task ItemsAsync_ParallelOfMoreSegmentsThanItems_LeavesEmptySegmentsAlone()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 1);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Scan().Parallel(16).ItemsAsync());

        // Assert
        await Assert.That(items.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ItemsAsync_ParallelOfAnEntity_GivesItsOwnItems()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await profiles.Put(new Profile("alice", "Alice")).SendAsync();
        await profiles.Put(new Profile("bob", "Bob")).SendAsync();
        await sandbox.Shop.Put(new Item { ["pk"] = "USER#alice", ["sk"] = "ORDER#1", ["_type"] = "ORDER" }).SendAsync();

        // Act
        var items = await Read.ToListAsync(profiles.Scan().Project("id", "fullName").Parallel(3).ItemsAsync());

        // Assert
        await Assert.That(string.Join(",", items.Select(static profile => profile.Id).Order(StringComparer.Ordinal))).IsEqualTo("alice,bob");
    }

    [Test]
    public async Task ItemsAsync_ParallelOfAGlobalIndex_GivesItsItems()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 6);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Index(ReadSandbox.GlobalIndex, "status", "placedAt").Scan().Parallel(2).ItemsAsync());

        // Assert
        await Assert.That(items.Count).IsEqualTo(6);
    }

    [Test]
    public async Task ItemsAsync_ParallelBreakOutEarly_LeavesTheClientUsable()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await WriteManyPartitionsAsync(sandbox, 20);

        // Act
        await foreach (var unused in sandbox.Shop.Scan().Limit(1).Parallel(4).ItemsAsync())
        {
            break;
        }

        var count = await sandbox.Shop.Scan().CountAsync();

        // Assert
        await Assert.That(count).IsEqualTo(20);
    }

    [Test]
    public async Task ItemsAsync_ParallelOnAMissingTable_SurfacesTheFailureOnce()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        var missing = sandbox.Client.Table(Tables.UniqueName(), "pk");

        // Act
        var failure = await Items.FailureOfAsync<ResourceNotFoundException>(() => Read.ToListAsync(missing.Scan().Parallel(4).ItemsAsync()));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ResourceNotFoundException");
    }

    [Test]
    public async Task ItemsAsync_ParallelCancelled_StopsEverySegment()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await WriteManyPartitionsAsync(sandbox, 20);
        using var cancellation = new CancellationTokenSource();
        var token = default(CancellationToken);

        // Act
        try
        {
            await foreach (var unused in sandbox.Shop.Scan().Limit(1).Parallel(4).ItemsAsync(cancellation.Token))
            {
                await cancellation.CancelAsync();
            }
        }
        catch (OperationCanceledException exception)
        {
            token = exception.CancellationToken;
        }

        // Assert
        await Assert.That(token == cancellation.Token).IsTrue();
    }

    [Test]
    public async Task DescribeTableAsync_Table_DescribesIt()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);

        // Act
        var description = await sandbox.Shop.DescribeTableAsync();

        // Assert
        await Assert.That((description.TableName, description.TableStatus, description.GlobalSecondaryIndexes.Single().IndexName)).IsEqualTo((sandbox.TableName, TableStatus.Active, ReadSandbox.GlobalIndex));
    }

    [Test]
    public async Task DescribeTableAsync_Name_ReturnsTheDescriptionOfTheTable()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);

        // Act
        var response = await sandbox.Client.DescribeTableAsync(sandbox.TableName);

        // Assert
        await Assert.That((response.Table!.TableName, response.Table.TableStatus)).IsEqualTo(((string?)sandbox.TableName, TableStatus.Active));
    }

    [Test]
    public async Task WaitUntilActiveAsync_ActiveTable_ReturnsItsDescription()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);

        // Act
        var description = await sandbox.Shop.WaitUntilActiveAsync(TimeSpan.FromMinutes(1));

        // Assert
        await Assert.That(description.TableStatus).IsEqualTo(TableStatus.Active);
    }

    [Test]
    public async Task WaitUntilActiveAsync_MissingTable_TimesOutWithTheCause()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        var missing = sandbox.Client.Table(Tables.UniqueName(), "pk");

        // Act
        var failure = await Items.FailureOfAsync<TableNotActiveException>(async () => await missing.WaitUntilActiveAsync(TimeSpan.Zero));

        // Assert
        await Assert.That((failure.InnerException is ResourceNotFoundException, failure.TableName, failure.LastStatus)).IsEqualTo((true, missing.Name, (TableStatus?)null));
    }

    private static async Task WriteManyPartitionsAsync(ReadSandbox sandbox, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await sandbox.Shop.Put(new Item { ["pk"] = $"p{i}", ["sk"] = "s", ["number"] = i }).SendAsync();
        }
    }
}
