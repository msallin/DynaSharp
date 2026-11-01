using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Operations;

/// <summary>Scan against the emulators: filters, limits, pagination and parallel segments.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class ScanTests(Backend backend)
{
    [Test]
    public async Task Scan_Table_ReturnsEveryItem()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 4);
        await Items.PutSequenceAsync(sandbox.Client, table, "b", 3);

        // Act
        var response = await sandbox.Client.ScanAsync(new ScanRequest { TableName = table, ConsistentRead = true });

        // Assert
        await Assert.That((response.Items.Count, response.Count, response.ScannedCount, response.LastEvaluatedKey)).IsEqualTo((7, 7, 7, null));
    }

    [Test]
    public async Task Scan_FilterExpression_ReturnsMatchesAndCountsEverythingScanned()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 10);

        // Act
        var response = await sandbox.Client.ScanAsync(new ScanRequest
        {
            TableName = table,
            FilterExpression = "v >= :low",
            ExpressionAttributeValues = Items.Values((":low", AttributeValue.FromNumber(8))),
        });

        // Assert
        await Assert.That((response.Count, response.ScannedCount)).IsEqualTo((3, 10));
    }

    [Test]
    public async Task Scan_Limit_ReturnsAtMostLimitItemsAndAKey()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 6);

        // Act
        var response = await sandbox.Client.ScanAsync(new ScanRequest { TableName = table, Limit = 4 });

        // Assert
        await Assert.That((response.Items.Count, response.LastEvaluatedKey is not null)).IsEqualTo((4, true));
    }

    [Test]
    public async Task Scan_FollowingLastEvaluatedKey_ReturnsEveryItemExactlyOnce()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 5);
        await Items.PutSequenceAsync(sandbox.Client, table, "b", 5);

        // Act
        var seen = await ScanAllAsync(sandbox.Client, new ScanRequest { TableName = table, Limit = 3 });

        // Assert
        await Assert.That(seen.Count).IsEqualTo(10);
        await Assert.That(seen.Distinct().Count()).IsEqualTo(10);
    }

    [Test]
    public async Task Scan_FilterThatRemovesEverythingOnAPage_ReturnsAnEmptyPageWithAKey()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 6);

        // Act: the first page reads two items, none passes the filter
        var page = await sandbox.Client.ScanAsync(new ScanRequest
        {
            TableName = table,
            Limit = 2,
            FilterExpression = "v > :threshold",
            ExpressionAttributeValues = Items.Values((":threshold", AttributeValue.FromNumber(100))),
        });

        // Assert
        await Assert.That((page.Items.Count, page.ScannedCount, page.LastEvaluatedKey is not null)).IsEqualTo((0, 2, true));
    }

    [Test]
    public async Task Scan_ParallelSegments_TogetherReturnEveryItemExactlyOnce()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        for (var partition = 0; partition < 10; partition++)
        {
            await Items.PutSequenceAsync(sandbox.Client, table, $"p{partition}", 3);
        }

        // Act
        const int segments = 4;
        var perSegment = await Task.WhenAll(Enumerable.Range(0, segments).Select(segment =>
            ScanAllAsync(sandbox.Client, new ScanRequest { TableName = table, TotalSegments = segments, Segment = segment, Limit = 7 })));
        var all = perSegment.SelectMany(static keys => keys).ToList();

        // Assert
        await Assert.That(all.Count).IsEqualTo(30);
        await Assert.That(all.Distinct().Count()).IsEqualTo(30);
    }

    [Test]
    public async Task Scan_SegmentNotBelowTotalSegments_ThrowsValidationException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(
            () => sandbox.Client.ScanAsync(new ScanRequest { TableName = table, TotalSegments = 2, Segment = 2 }));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ValidationException");
    }

    [Test]
    public async Task Scan_SelectCount_ReturnsTheCountWithoutItems()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 4);

        // Act
        var response = await sandbox.Client.ScanAsync(new ScanRequest { TableName = table, Select = Select.Count });

        // Assert
        await Assert.That((response.Items.Count, response.Count)).IsEqualTo((0, 4));
    }

    [Test]
    public async Task Scan_GlobalSecondaryIndex_ReturnsItemsFromTheIndex()
    {
        // Arrange: only items that carry the index keys appear in the index
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 3);
        var withoutIndexKeys = Items.Key("a", 99);
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = withoutIndexKeys });

        // Act
        var response = await sandbox.Client.ScanAsync(new ScanRequest { TableName = table, IndexName = Tables.GlobalIndexName });

        // Assert
        await Assert.That(response.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Scan_EmptyTable_ReturnsAnEmptyResult()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Client.ScanAsync(new ScanRequest { TableName = table });

        // Assert
        await Assert.That((response.Items.Count, response.Count, response.ScannedCount, response.LastEvaluatedKey)).IsEqualTo((0, 0, 0, null));
    }

    // Returns "pk/sk" of every item the scan reports, following the pages to the end.
    private static async Task<List<string>> ScanAllAsync(DynamoDbClient client, ScanRequest request)
    {
        var keys = new List<string>();
        IReadOnlyDictionary<string, AttributeValue>? start = null;
        do
        {
            var page = await client.ScanAsync(request with { ExclusiveStartKey = start });
            keys.AddRange(page.Items.Select(static item => $"{item["pk"].AsString()}/{item["sk"].AsNumber()}"));
            start = page.LastEvaluatedKey;
        }
        while (start is not null);

        return keys;
    }
}
