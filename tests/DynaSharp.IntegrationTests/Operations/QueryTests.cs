using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Operations;

/// <summary>Query against the emulators: ordering, limits, pagination, filters that empty a page, projections and indexes.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class QueryTests(Backend backend)
{
    [Test]
    public async Task Query_Partition_ReturnsItemsInAscendingSortKeyOrder()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 5);
        await Items.PutSequenceAsync(sandbox.Client, table, "other", 3);

        // Act
        var response = await sandbox.Client.QueryAsync(ByPartition(table, "a") with { ConsistentRead = true });

        // Assert
        await Assert.That(string.Join(",", response.Items.Select(static item => item["sk"].AsNumber<int>()))).IsEqualTo("1,2,3,4,5");
        await Assert.That((response.Count, response.ScannedCount, response.LastEvaluatedKey)).IsEqualTo((5, 5, null));
    }

    [Test]
    public async Task Query_ScanIndexForwardFalse_ReturnsItemsInDescendingSortKeyOrder()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 5);

        // Act
        var response = await sandbox.Client.QueryAsync(ByPartition(table, "a") with { ScanIndexForward = false });

        // Assert
        await Assert.That(string.Join(",", response.Items.Select(static item => item["sk"].AsNumber<int>()))).IsEqualTo("5,4,3,2,1");
    }

    [Test]
    public async Task Query_SortKeyBetween_ReturnsTheRangeInclusive()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 10);

        // Act
        var response = await sandbox.Client.QueryAsync(new QueryRequest
        {
            TableName = table,
            KeyConditionExpression = "pk = :pk AND sk BETWEEN :low AND :high",
            ExpressionAttributeValues = Items.Values((":pk", "a"), (":low", AttributeValue.FromNumber(3)), (":high", AttributeValue.FromNumber(6))),
        });

        // Assert
        await Assert.That(string.Join(",", response.Items.Select(static item => item["sk"].AsNumber<int>()))).IsEqualTo("3,4,5,6");
    }

    [Test]
    public async Task Query_LimitBelowMatches_ReturnsLimitItemsAndTheKeyOfTheLastOne()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 5);

        // Act
        var response = await sandbox.Client.QueryAsync(ByPartition(table, "a") with { Limit = 2 });

        // Assert
        await Assert.That((response.Items.Count, response.LastEvaluatedKey!["pk"].AsString(), response.LastEvaluatedKey["sk"].AsNumber<int>())).IsEqualTo((2, "a", 2));
    }

    [Test]
    public async Task Query_FollowingLastEvaluatedKey_ReturnsEveryItemExactlyOnce()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 7);
        var seen = new List<int>();
        var pages = 0;

        // Act
        IReadOnlyDictionary<string, AttributeValue>? start = null;
        do
        {
            var page = await sandbox.Client.QueryAsync(ByPartition(table, "a") with { Limit = 3, ExclusiveStartKey = start });
            seen.AddRange(page.Items.Select(static item => item["sk"].AsNumber<int>()));
            start = page.LastEvaluatedKey;
            pages++;
        }
        while (start is not null);

        // Assert
        await Assert.That(string.Join(",", seen)).IsEqualTo("1,2,3,4,5,6,7");
        await Assert.That(pages).IsEqualTo(3);
    }

    [Test]
    public async Task Query_DescendingPagination_ContinuesFromTheLastKey()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 5);
        var first = await sandbox.Client.QueryAsync(ByPartition(table, "a") with { Limit = 2, ScanIndexForward = false });

        // Act
        var second = await sandbox.Client.QueryAsync(ByPartition(table, "a") with { Limit = 2, ScanIndexForward = false, ExclusiveStartKey = first.LastEvaluatedKey });

        // Assert
        await Assert.That(string.Join(",", second.Items.Select(static item => item["sk"].AsNumber<int>()))).IsEqualTo("3,2");
    }

    [Test]
    public async Task Query_FilterThatRemovesEverythingOnAPage_ReturnsAnEmptyPageWithAKey()
    {
        // Arrange: Limit counts the items read, not the items that pass the filter. Only sk 5 passes.
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 5);

        // Act
        var page = await sandbox.Client.QueryAsync(FilteredToTheLastItem(table) with { Limit = 2 });

        // Assert
        await Assert.That((page.Items.Count, page.Count, page.ScannedCount, page.LastEvaluatedKey is not null)).IsEqualTo((0, 0, 2, true));
    }

    [Test]
    public async Task Query_FilterWithPagination_FindsTheMatchBehindEmptyPages()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 5);
        var found = new List<int>();
        var emptyPages = 0;

        // Act
        IReadOnlyDictionary<string, AttributeValue>? start = null;
        do
        {
            var page = await sandbox.Client.QueryAsync(FilteredToTheLastItem(table) with { Limit = 2, ExclusiveStartKey = start });
            found.AddRange(page.Items.Select(static item => item["sk"].AsNumber<int>()));
            emptyPages += page.Items.Count == 0 ? 1 : 0;
            start = page.LastEvaluatedKey;
        }
        while (start is not null);

        // Assert
        await Assert.That((string.Join(",", found), emptyPages)).IsEqualTo(("5", 2));
    }

    [Test]
    public async Task Query_SelectCount_ReturnsTheCountWithoutItems()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 4);

        // Act
        var response = await sandbox.Client.QueryAsync(ByPartition(table, "a") with { Select = SelectKind.Count });

        // Assert
        await Assert.That((response.Items.Count, response.Count, response.ScannedCount)).IsEqualTo((0, 4, 4));
    }

    [Test]
    public async Task Query_ProjectionExpression_ReturnsOnlyTheProjectedAttributes()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 2);

        // Act
        var response = await sandbox.Client.QueryAsync(ByPartition(table, "a") with { ProjectionExpression = "sk, v" });

        // Assert
        await Assert.That(response.Items[0].Keys.Order(StringComparer.Ordinal)).IsEquivalentTo(["sk", "v"]);
    }

    [Test]
    public async Task Query_GlobalSecondaryIndex_ReturnsItemsInIndexOrder()
    {
        // Arrange: the index orders by gsi_sk, a string, so "s10" sorts before "s2"
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 10);

        // Act
        var response = await sandbox.Client.QueryAsync(new QueryRequest
        {
            TableName = table,
            IndexName = Tables.GlobalIndexName,
            KeyConditionExpression = "gsi_pk = :g AND gsi_sk BETWEEN :low AND :high",
            ExpressionAttributeValues = Items.Values((":g", "g"), (":low", "s2"), (":high", "s4")),
        });

        // Assert
        await Assert.That(string.Join(",", response.Items.Select(static item => item["gsi_sk"].AsString()))).IsEqualTo("s2,s3,s4");
    }

    [Test]
    public async Task Query_ConsistentReadOnGlobalSecondaryIndex_ThrowsValidationException()
    {
        // Arrange: global secondary indexes are eventually consistent only
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => sandbox.Client.QueryAsync(new QueryRequest
        {
            TableName = table,
            IndexName = Tables.GlobalIndexName,
            KeyConditionExpression = "gsi_pk = :g",
            ExpressionAttributeValues = Items.Values((":g", "g")),
            ConsistentRead = true,
        }));

        // Assert
        await Assert.That(failure.Message).Contains("Consistent reads are not supported on global secondary indexes");
    }

    [Test]
    public async Task Query_PartitionWithoutItems_ReturnsAnEmptyResult()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);

        // Act
        var response = await sandbox.Client.QueryAsync(ByPartition(table, "nobody"));

        // Assert
        await Assert.That((response.Items.Count, response.Count, response.ScannedCount, response.LastEvaluatedKey)).IsEqualTo((0, 0, 0, null));
    }

    [Test]
    public async Task Query_MalformedKeyCondition_ThrowsValidationException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => sandbox.Client.QueryAsync(new QueryRequest
        {
            TableName = table,
            KeyConditionExpression = "pk ==",
            ExpressionAttributeValues = Items.Values((":pk", "a")),
        }));

        // Assert
        await Assert.That(failure.Message).Contains("Invalid KeyConditionExpression");
    }

    private static QueryRequest FilteredToTheLastItem(string table) => new()
    {
        TableName = table,
        KeyConditionExpression = "pk = :pk",
        FilterExpression = "v > :threshold",
        ExpressionAttributeValues = Items.Values((":pk", "a"), (":threshold", AttributeValue.FromNumber(4))),
    };

    private static QueryRequest ByPartition(string table, string pk) => new()
    {
        TableName = table,
        KeyConditionExpression = "pk = :pk",
        ExpressionAttributeValues = Items.Values((":pk", pk)),
    };
}
