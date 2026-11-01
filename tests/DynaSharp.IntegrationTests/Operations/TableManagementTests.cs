using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Operations;

/// <summary>ListTables, DescribeTimeToLive and UpdateTable against the emulators.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class TableManagementTests(Backend backend)
{
    [Test]
    public async Task ListTables_NewTables_ListsThemAmongTheOthers()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var first = await sandbox.CreateTableAsync();
        var second = await sandbox.CreateTableAsync();

        // Act
        var names = await ListAllAsync(sandbox.Client, limit: null);

        // Assert
        await Assert.That(names).Contains(first);
        await Assert.That(names).Contains(second);
    }

    [Test]
    public async Task ListTables_LimitOne_ReturnsOneNameAndTheMarkerForTheNextPage()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        await sandbox.CreateTableAsync();
        await sandbox.CreateTableAsync();

        // Act
        var page = await sandbox.Client.ListTablesAsync(new ListTablesRequest { Limit = 1 });

        // Assert
        await Assert.That((page.TableNames.Count, page.LastEvaluatedTableName)).IsEqualTo((1, (string?)page.TableNames[0]));
    }

    [Test]
    public async Task ListTables_PagedByTwo_ReturnsEveryTableOnceInOrder()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var created = new[] { await sandbox.CreateTableAsync(), await sandbox.CreateTableAsync(), await sandbox.CreateTableAsync() };

        // Act
        var names = await ListAllAsync(sandbox.Client, limit: 2);

        // Assert
        await Assert.That(names.Intersect(created).Count()).IsEqualTo(3);
        await Assert.That(names.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(names.Count);
        await Assert.That(names.SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ListTables_StartingAfterATable_ContinuesAfterIt()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        await sandbox.CreateTableAsync();
        await sandbox.CreateTableAsync();
        var all = await ListAllAsync(sandbox.Client, limit: null);

        // Act
        var rest = await sandbox.Client.ListTablesAsync(new ListTablesRequest { ExclusiveStartTableName = all[0] });

        // Assert
        await Assert.That(rest.TableNames).DoesNotContain(all[0]);
    }

    [Test]
    public async Task ListTables_LimitAboveTheMaximum_ThrowsValidationException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => sandbox.Client.ListTablesAsync(new ListTablesRequest { Limit = 101 }));

        // Assert
        await Assert.That((failure.ErrorCode, failure.OperationName, failure.TableName)).IsEqualTo(("ValidationException", (string?)"ListTables", (string?)null));
    }

    [Test]
    public async Task DescribeTimeToLive_TableWithoutTimeToLive_IsDisabledWithoutAttribute()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Client.DescribeTimeToLiveAsync(new DescribeTimeToLiveRequest { TableName = table });

        // Assert
        await Assert.That(response.TimeToLiveDescription).IsEqualTo(new TimeToLiveDescription { TimeToLiveStatus = TimeToLiveStatus.Disabled });
    }

    [Test]
    public async Task DescribeTimeToLive_AfterEnabling_ReportsTheAttribute()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.UpdateTimeToLiveAsync(new UpdateTimeToLiveRequest
        {
            TableName = table,
            TimeToLiveSpecification = new TimeToLiveSpecification { Enabled = true, AttributeName = "expires" },
        });

        // Act
        var description = (await sandbox.Client.DescribeTimeToLiveAsync(new DescribeTimeToLiveRequest { TableName = table })).TimeToLiveDescription;

        // Assert
        await Assert.That((description!.TimeToLiveStatus is TimeToLiveStatus.Enabled or TimeToLiveStatus.Enabling, description.AttributeName)).IsEqualTo((true, (string?)"expires"));
    }

    [Test]
    public async Task DescribeTimeToLive_MissingTable_ThrowsResourceNotFoundException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ResourceNotFoundException>(() => sandbox.Client.DescribeTimeToLiveAsync(new DescribeTimeToLiveRequest { TableName = "table-that-does-not-exist" }));

        // Assert
        await Assert.That((failure.ErrorCode, failure.OperationName, failure.TableName)).IsEqualTo(("ResourceNotFoundException", (string?)"DescribeTimeToLive", (string?)"table-that-does-not-exist"));
    }

    [Test]
    public async Task UpdateTable_OnDemandToProvisioned_ReportsTheCapacity()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var response = await Tables.UpdateAsync(sandbox.Client, new UpdateTableRequest
        {
            TableName = table,
            BillingMode = BillingMode.Provisioned,
            ProvisionedThroughput = new ProvisionedThroughput { ReadCapacityUnits = 5, WriteCapacityUnits = 6 },
        });
        var description = await Tables.WaitUntilAsync(sandbox.Client, table, Tables.IsSettled);

        // Assert
        await Assert.That(response.TableDescription!.TableName).IsEqualTo(table);
        await Assert.That(description.ProvisionedThroughput).IsEqualTo(new ProvisionedThroughputDescription { ReadCapacityUnits = 5, WriteCapacityUnits = 6 });
    }

    [Test]
    public async Task UpdateTable_ProvisionedToOnDemand_ReportsTheBillingMode()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await CreateProvisionedAsync(sandbox);

        // Act
        await Tables.UpdateAsync(sandbox.Client, new UpdateTableRequest { TableName = table, BillingMode = BillingMode.PayPerRequest });
        var description = await Tables.WaitUntilAsync(sandbox.Client, table, Tables.IsSettled);

        // Assert
        await Assert.That(description.BillingModeSummary!.BillingMode).IsEqualTo(BillingMode.PayPerRequest);
    }

    [Test]
    public async Task UpdateTable_NewProvisionedThroughput_ReportsIt()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await CreateProvisionedAsync(sandbox);

        // Act
        await Tables.UpdateAsync(sandbox.Client, new UpdateTableRequest
        {
            TableName = table,
            ProvisionedThroughput = new ProvisionedThroughput { ReadCapacityUnits = 10, WriteCapacityUnits = 20 },
        });
        var description = await Tables.WaitUntilAsync(sandbox.Client, table, Tables.IsSettled);

        // Assert
        await Assert.That(description.ProvisionedThroughput).IsEqualTo(new ProvisionedThroughputDescription { ReadCapacityUnits = 10, WriteCapacityUnits = 20 });
    }

    [Test]
    public async Task UpdateTable_EnableStream_TableHasAStream()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        await Tables.UpdateAsync(sandbox.Client, new UpdateTableRequest
        {
            TableName = table,
            StreamSpecification = new StreamSpecification { StreamEnabled = true, StreamViewType = StreamViewType.NewAndOldImages },
        });
        var description = await Tables.WaitUntilAsync(sandbox.Client, table, Tables.IsSettled);

        // Assert
        await Assert.That(description.StreamSpecification).IsEqualTo(new StreamSpecification { StreamEnabled = true, StreamViewType = StreamViewType.NewAndOldImages });
        await Assert.That(description.LatestStreamArn).Contains($"table/{table}/stream/", StringComparison.Ordinal);
    }

    [Test]
    public async Task UpdateTable_DisableStream_StreamIsOff()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));

        // Act
        await Tables.UpdateAsync(sandbox.Client, new UpdateTableRequest { TableName = table, StreamSpecification = new StreamSpecification { StreamEnabled = false } });
        var description = await Tables.WaitUntilAsync(sandbox.Client, table, Tables.IsSettled);

        // Assert
        await Assert.That(description.StreamSpecification?.StreamEnabled).IsNotEqualTo(true);
    }

    [Test]
    [NotInParallel] // DynamoDB Local answers calls around a new index with InternalFailure while other tables change.
    public async Task UpdateTable_CreateGlobalSecondaryIndex_IndexFillsFromTheTableAndCanBeQueried()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.PartitionOnly);
        foreach (var (pk, group) in new[] { ("a", "red"), ("b", "blue"), ("c", "red") })
        {
            await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = new Dictionary<string, AttributeValue> { ["pk"] = pk, ["color"] = group } });
        }

        // Act
        await Tables.UpdateAsync(sandbox.Client, new UpdateTableRequest
        {
            TableName = table,
            AttributeDefinitions = [new AttributeDefinition("color", ScalarAttributeType.String)],
            GlobalSecondaryIndexUpdates =
            [
                new GlobalSecondaryIndexUpdate
                {
                    Create = new GlobalSecondaryIndex
                    {
                        IndexName = "by-color",
                        KeySchema = [new KeySchemaElement("color", KeyType.Hash)],
                        Projection = new IndexProjection { ProjectionType = ProjectionType.All },
                    },
                },
            ],
        });
        var description = await Tables.WaitUntilAsync(sandbox.Client, table, Tables.IsSettled);
        var red = await sandbox.Client.QueryAsync(new QueryRequest
        {
            TableName = table,
            IndexName = "by-color",
            KeyConditionExpression = "color = :color",
            ExpressionAttributeValues = Items.Values((":color", "red")),
        });

        // Assert
        await Assert.That(description.GlobalSecondaryIndexes.Select(static index => index.IndexName!)).IsEquivalentTo(["by-color"]);
        await Assert.That(red.Items.Select(static item => item["pk"].AsString()).Order(StringComparer.Ordinal)).IsEquivalentTo(["a", "c"]);
    }

    [Test]
    [NotInParallel] // DynamoDB Local answers calls around a new index with InternalFailure while other tables change.
    public async Task WaitUntilActiveAsync_IndexAddedByUpdateTable_ReturnsWhenItCanBeQueried()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var name = await sandbox.CreateTableAsync(TableSpec.PartitionOnly);
        var table = sandbox.Client.Table(name, "pk");
        foreach (var (pk, group) in new[] { ("a", "red"), ("b", "blue"), ("c", "red") })
        {
            await table.Put(new Item { ["pk"] = pk, ["color"] = group }).SendAsync();
        }

        // Act
        await Tables.UpdateAsync(sandbox.Client, new UpdateTableRequest
        {
            TableName = name,
            AttributeDefinitions = [new AttributeDefinition("color", ScalarAttributeType.String)],
            GlobalSecondaryIndexUpdates =
            [
                new GlobalSecondaryIndexUpdate
                {
                    Create = new GlobalSecondaryIndex
                    {
                        IndexName = "by-color",
                        KeySchema = [new KeySchemaElement("color", KeyType.Hash)],
                        Projection = new IndexProjection { ProjectionType = ProjectionType.All },
                    },
                },
            ],
        });
        var description = await table.WaitUntilActiveAsync(TimeSpan.FromMinutes(1));
        var red = await table.Index("by-color", "color").Query("red").CountAsync();

        // Assert
        await Assert.That((description.GlobalSecondaryIndexes.Single().IndexStatus, red)).IsEqualTo((IndexStatus.Active, 2));
    }

    [Test]
    [NotInParallel] // DynamoDB Local answers calls around a new index with InternalFailure while other tables change.
    public async Task UpdateTable_CreateGlobalSecondaryIndexOnProvisionedTable_ReportsTheIndexCapacity()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.GlobalIndexCapacity);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await CreateProvisionedAsync(sandbox);

        // Act
        await Tables.UpdateAsync(sandbox.Client, new UpdateTableRequest
        {
            TableName = table,
            AttributeDefinitions = [new AttributeDefinition("color", ScalarAttributeType.String)],
            GlobalSecondaryIndexUpdates =
            [
                new GlobalSecondaryIndexUpdate
                {
                    Create = new GlobalSecondaryIndex
                    {
                        IndexName = "by-color",
                        KeySchema = [new KeySchemaElement("color", KeyType.Hash)],
                        Projection = new IndexProjection { ProjectionType = ProjectionType.KeysOnly },
                        ProvisionedThroughput = new ProvisionedThroughput { ReadCapacityUnits = 2, WriteCapacityUnits = 3 },
                    },
                },
            ],
        });
        var description = await Tables.WaitUntilAsync(sandbox.Client, table, Tables.IsSettled);

        // Assert
        var index = description.GlobalSecondaryIndexes.Single();
        await Assert.That((index.IndexName, index.Projection!.ProjectionType, index.ProvisionedThroughput)).IsEqualTo(("by-color", ProjectionType.KeysOnly, (ProvisionedThroughputDescription?)new ProvisionedThroughputDescription { ReadCapacityUnits = 2, WriteCapacityUnits = 3 }));
    }

    [Test]
    public async Task UpdateTable_UpdateGlobalSecondaryIndex_ReportsTheNewIndexCapacity()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.GlobalIndexCapacity);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await CreateProvisionedAsync(sandbox, withIndex: true);

        // Act
        await Tables.UpdateAsync(sandbox.Client, new UpdateTableRequest
        {
            TableName = table,
            GlobalSecondaryIndexUpdates =
            [
                new GlobalSecondaryIndexUpdate
                {
                    Update = new UpdateGlobalSecondaryIndexAction
                    {
                        IndexName = Tables.GlobalIndexName,
                        ProvisionedThroughput = new ProvisionedThroughput { ReadCapacityUnits = 7, WriteCapacityUnits = 8 },
                    },
                },
            ],
        });
        var description = await Tables.WaitUntilAsync(sandbox.Client, table, Tables.IsSettled);

        // Assert
        await Assert.That(description.GlobalSecondaryIndexes.Single().ProvisionedThroughput).IsEqualTo(new ProvisionedThroughputDescription { ReadCapacityUnits = 7, WriteCapacityUnits = 8 });
    }

    [Test]
    public async Task UpdateTable_DeleteGlobalSecondaryIndex_IndexIsGone()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(new TableSpec(WithGlobalIndex: true));

        // Act
        await Tables.UpdateAsync(sandbox.Client, new UpdateTableRequest
        {
            TableName = table,
            GlobalSecondaryIndexUpdates = [new GlobalSecondaryIndexUpdate { Delete = new DeleteGlobalSecondaryIndexAction { IndexName = Tables.GlobalIndexName } }],
        });
        var description = await Tables.WaitUntilAsync(sandbox.Client, table, static description => description.TableStatus == TableStatus.Active && description.GlobalSecondaryIndexes.Count == 0);

        // Assert
        await Assert.That(description.GlobalSecondaryIndexes).IsEmpty();
    }

    [Test]
    public async Task UpdateTable_DeletionProtectionOn_PreventsDeleteTableUntilItIsOff()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await Tables.UpdateAsync(sandbox.Client, new UpdateTableRequest { TableName = table, DeletionProtectionEnabled = true });

        // Act
        var protectedDescription = await Tables.WaitUntilAsync(sandbox.Client, table, Tables.IsSettled);
        var failure = await Items.FailureOfAsync<ValidationException>(() => sandbox.Client.DeleteTableAsync(new DeleteTableRequest { TableName = table }));
        await Tables.UpdateAsync(sandbox.Client, new UpdateTableRequest { TableName = table, DeletionProtectionEnabled = false });
        var unprotectedDescription = await Tables.WaitUntilAsync(sandbox.Client, table, Tables.IsSettled);

        // Assert
        await Assert.That((protectedDescription.DeletionProtectionEnabled, failure.OperationName, unprotectedDescription.DeletionProtectionEnabled)).IsEqualTo((true, (string?)"DeleteTable", false));
    }

    [Test]
    public async Task UpdateTable_TableClass_ReportsTheNewClass()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.UpdateTableClass);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        await Tables.UpdateAsync(sandbox.Client, new UpdateTableRequest { TableName = table, TableClass = TableClass.StandardInfrequentAccess });
        var description = await Tables.WaitUntilAsync(sandbox.Client, table, Tables.IsSettled);

        // Assert
        await Assert.That(description.TableClass).IsEqualTo(TableClass.StandardInfrequentAccess);
    }

    [Test]
    public async Task UpdateTable_MissingTable_ThrowsResourceNotFoundException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ResourceNotFoundException>(() => sandbox.Client.UpdateTableAsync(new UpdateTableRequest
        {
            TableName = "table-that-does-not-exist",
            BillingMode = BillingMode.PayPerRequest,
        }));

        // Assert
        await Assert.That((failure.ErrorCode, failure.OperationName, failure.TableName)).IsEqualTo(("ResourceNotFoundException", (string?)"UpdateTable", (string?)"table-that-does-not-exist"));
    }

    [Test]
    public async Task UpdateTable_NothingToChange_ThrowsValidationException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => sandbox.Client.UpdateTableAsync(new UpdateTableRequest { TableName = table }));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ValidationException");
    }

    private static async Task<string> CreateProvisionedAsync(Sandbox sandbox, bool withIndex = false)
    {
        var name = Tables.UniqueName();
        await sandbox.CreateTableAsync(ProvisionedTable(name, withIndex));
        return name;
    }

    // A table with provisioned capacity, optionally with the usual global index.
    private static CreateTableRequest ProvisionedTable(string name, bool withIndex = false) => Tables.Request(name, new TableSpec(WithGlobalIndex: withIndex)) with
    {
        BillingMode = BillingMode.Provisioned,
        ProvisionedThroughput = new ProvisionedThroughput { ReadCapacityUnits = 3, WriteCapacityUnits = 4 },
        GlobalSecondaryIndexes = withIndex
            ? [Tables.Request(name, new TableSpec(WithGlobalIndex: true)).GlobalSecondaryIndexes![0] with { ProvisionedThroughput = new ProvisionedThroughput { ReadCapacityUnits = 1, WriteCapacityUnits = 1 } }]
            : null,
    };

    private static async Task<List<string>> ListAllAsync(DynamoDbClient client, int? limit)
    {
        var names = new List<string>();
        var request = new ListTablesRequest { Limit = limit };
        do
        {
            var page = await client.ListTablesAsync(request);
            names.AddRange(page.TableNames);
            request = request with { ExclusiveStartTableName = page.LastEvaluatedTableName };
        }
        while (request.ExclusiveStartTableName is not null);

        return names;
    }
}
