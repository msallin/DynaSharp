using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Operations;

/// <summary>BatchWriteItem and BatchGetItem against the emulators, across tables, with the limits and the unprocessed collections.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class BatchTests(Backend backend)
{
    [Test]
    public async Task BatchWriteItem_PutsInTwoTables_WritesEveryItem()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var first = await sandbox.CreateTableAsync();
        var second = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Client.BatchWriteItemAsync(new BatchWriteItemRequest
        {
            RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>
            {
                [first] = [Put(Items.Item("a", "in first")), Put(Items.Item("b", "in first"))],
                [second] = [Put(Items.Item("c", "in second"))],
            },
        });
        var scanFirst = await sandbox.Client.ScanAsync(new ScanRequest { TableName = first, ConsistentRead = true });
        var scanSecond = await sandbox.Client.ScanAsync(new ScanRequest { TableName = second, ConsistentRead = true });

        // Assert
        await Assert.That((response.UnprocessedItems.Count, scanFirst.Count, scanSecond.Count)).IsEqualTo((0, 2, 1));
    }

    [Test]
    public async Task BatchWriteItem_PutsAndDeletesInOneRequest_AppliesBoth()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("old", "to delete") });

        // Act
        await sandbox.Client.BatchWriteItemAsync(new BatchWriteItemRequest
        {
            RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>
            {
                [table] = [Put(Items.Item("new", "created")), new WriteRequest { DeleteRequest = new DeleteRequest { Key = Items.Key("old") } }],
            },
        });
        var old = await sandbox.Client.GetItemAsync(new GetItemRequest { TableName = table, Key = Items.Key("old"), ConsistentRead = true });
        var created = await sandbox.Client.GetItemAsync(new GetItemRequest { TableName = table, Key = Items.Key("new"), ConsistentRead = true });

        // Assert
        await Assert.That((old.Item, created.Item!["v"].AsString())).IsEqualTo((null, "created"));
    }

    [Test]
    public async Task BatchWriteItem_25Items_WritesAllOfThem()
    {
        // Arrange: 25 is the most a request may hold
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var puts = Enumerable.Range(0, 25).Select(i => Put(Items.Item($"item-{i}", AttributeValue.FromNumber(i)))).ToList();

        // Act
        var response = await sandbox.Client.BatchWriteItemAsync(new BatchWriteItemRequest { RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>> { [table] = puts } });
        var scan = await sandbox.Client.ScanAsync(new ScanRequest { TableName = table, ConsistentRead = true, Select = Select.Count });

        // Assert
        await Assert.That((response.UnprocessedItems.Count, scan.Count)).IsEqualTo((0, 25));
    }

    [Test]
    public async Task BatchWriteItem_26Items_ThrowsValidationException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var puts = Enumerable.Range(0, 26).Select(i => Put(Items.Item($"item-{i}", "x"))).ToList();

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(
            () => sandbox.Client.BatchWriteItemAsync(new BatchWriteItemRequest { RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>> { [table] = puts } }));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ValidationException");
    }

    [Test]
    public async Task BatchWriteItem_SameKeyTwice_ThrowsValidationException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => sandbox.Client.BatchWriteItemAsync(new BatchWriteItemRequest
        {
            RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>
            {
                [table] = [Put(Items.Item("a", "1")), new WriteRequest { DeleteRequest = new DeleteRequest { Key = Items.Key("a") } }],
            },
        }));

        // Assert
        await Assert.That(failure.Message).Contains("duplicates", StringComparison.Ordinal);
    }

    [Test]
    public async Task BatchWriteItem_MissingTable_ThrowsResourceNotFoundException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ResourceNotFoundException>(() => sandbox.Client.BatchWriteItemAsync(new BatchWriteItemRequest
        {
            RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>> { ["table-that-does-not-exist"] = [Put(Items.Item("a", "1"))] },
        }));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ResourceNotFoundException");
    }

    [Test]
    public async Task BatchWriteItem_Total_ReportsCapacityPerTable()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var first = await sandbox.CreateTableAsync();
        var second = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Client.BatchWriteItemAsync(new BatchWriteItemRequest
        {
            RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>
            {
                [first] = [Put(Items.Item("a", "1")), Put(Items.Item("b", "1"))],
                [second] = [Put(Items.Item("c", "1"))],
            },
            ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
        });

        // Assert
        var byTable = response.ConsumedCapacity.ToDictionary(static capacity => capacity.TableName!, static capacity => capacity.CapacityUnits);
        await Assert.That((byTable[first], byTable[second])).IsEqualTo((2.0, 1.0));
    }

    [Test]
    public async Task BatchGetItem_KeysInTwoTables_ReturnsTheItemsPerTable()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var first = await sandbox.CreateTableAsync();
        var second = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = first, Item = Items.Item("a", "in first") });
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = second, Item = Items.Item("b", "in second") });

        // Act
        var response = await sandbox.Client.BatchGetItemAsync(new BatchGetItemRequest
        {
            RequestItems = new Dictionary<string, KeysAndAttributes>
            {
                [first] = new KeysAndAttributes { Keys = [Items.Key("a")], ConsistentRead = true },
                [second] = new KeysAndAttributes { Keys = [Items.Key("b")], ConsistentRead = true },
            },
        });

        // Assert
        await Assert.That((response.Responses[first].Single()["v"].AsString(), response.Responses[second].Single()["v"].AsString(), response.UnprocessedKeys.Count))
            .IsEqualTo(("in first", "in second", 0));
    }

    [Test]
    public async Task BatchGetItem_MissingKeysAndProjection_OmitsMissingItemsAndProjectsTheRest()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var item = Items.Item("a", "value");
        item["other"] = "dropped by the projection";
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = item });

        // Act
        var response = await sandbox.Client.BatchGetItemAsync(new BatchGetItemRequest
        {
            RequestItems = new Dictionary<string, KeysAndAttributes>
            {
                [table] = new KeysAndAttributes { Keys = [Items.Key("a"), Items.Key("missing")], ProjectionExpression = "pk, v", ConsistentRead = true },
            },
        });

        // Assert
        var returned = response.Responses[table];
        await Assert.That(returned.Count).IsEqualTo(1);
        await Assert.That(returned[0].Keys.Order(StringComparer.Ordinal)).IsEquivalentTo(["pk", "v"]);
    }

    [Test]
    public async Task BatchGetItem_NoKeyExists_ReturnsAnEmptyListForTheTable()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Client.BatchGetItemAsync(new BatchGetItemRequest
        {
            RequestItems = new Dictionary<string, KeysAndAttributes> { [table] = new KeysAndAttributes { Keys = [Items.Key("missing")] } },
        });

        // Assert
        await Assert.That(response.Responses[table].Count).IsEqualTo(0);
    }

    [Test]
    public async Task BatchGetItem_Total_ReportsCapacityPerTable()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "1") });

        // Act
        var response = await sandbox.Client.BatchGetItemAsync(new BatchGetItemRequest
        {
            RequestItems = new Dictionary<string, KeysAndAttributes> { [table] = new KeysAndAttributes { Keys = [Items.Key("a")], ConsistentRead = true } },
            ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
        });

        // Assert
        await Assert.That((response.ConsumedCapacity.Single().TableName, response.ConsumedCapacity.Single().CapacityUnits)).IsEqualTo((table, 1.0));
    }

    [Test]
    public async Task BatchGetItem_SameKeyTwice_ThrowsValidationException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => sandbox.Client.BatchGetItemAsync(new BatchGetItemRequest
        {
            RequestItems = new Dictionary<string, KeysAndAttributes> { [table] = new KeysAndAttributes { Keys = [Items.Key("a"), Items.Key("a")] } },
        }));

        // Assert
        await Assert.That(failure.Message).Contains("duplicates", StringComparison.Ordinal);
    }

    [Test]
    [NotInParallel] // Moves 35 MB through the emulator; running beside the other tests slows every one of them down.
    public async Task BatchGetItem_ResponseOver16MB_ReturnsUnprocessedKeysThatAFollowUpFetches()
    {
        // Arrange: 45 items of 390 KB are 17.5 MB; a response holds at most 16 MB, so the rest comes back as unprocessed keys
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var big = new string('x', 390 * 1024);
        const int count = 45;
        foreach (var chunk in Enumerable.Range(0, count).Chunk(20))
        {
            await sandbox.Client.BatchWriteItemAsync(new BatchWriteItemRequest
            {
                RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>> { [table] = [.. chunk.Select(i => Put(Items.Item($"big-{i}", big)))] },
            });
        }

        var keys = Enumerable.Range(0, count).Select(i => (IReadOnlyDictionary<string, AttributeValue>)Items.Key($"big-{i}")).ToList();

        // Act
        var first = await sandbox.Client.BatchGetItemAsync(new BatchGetItemRequest
        {
            RequestItems = new Dictionary<string, KeysAndAttributes> { [table] = new KeysAndAttributes { Keys = keys, ConsistentRead = true } },
        });
        var second = await sandbox.Client.BatchGetItemAsync(new BatchGetItemRequest { RequestItems = first.UnprocessedKeys });

        // Assert
        var unprocessed = first.UnprocessedKeys[table];
        await Assert.That(unprocessed.Keys.Count).IsGreaterThan(0);
        await Assert.That(first.Responses[table].Count + unprocessed.Keys.Count).IsEqualTo(count);
        await Assert.That(unprocessed.ConsistentRead).IsTrue();
        await Assert.That(first.Responses[table].Count + second.Responses[table].Count).IsEqualTo(count);
    }

    private static WriteRequest Put(IReadOnlyDictionary<string, AttributeValue> item) => new() { PutRequest = new PutRequest { Item = item } };
}
