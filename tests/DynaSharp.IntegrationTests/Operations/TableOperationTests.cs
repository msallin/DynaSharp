using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Operations;

/// <summary>CreateTable, DeleteTable and UpdateTimeToLive against the emulators.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class TableOperationTests(Backend backend)
{
    [Test]
    public async Task CreateTable_PartitionKeyOnDemand_ReturnsTheDescriptionOfANewTable()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var name = Tables.UniqueName();

        // Act
        var response = await sandbox.CreateTableAsync(Tables.Request(name, TableSpec.PartitionOnly));

        // Assert
        var description = response.TableDescription!;
        await Assert.That(description.TableName).IsEqualTo(name);
        await Assert.That(description.TableStatus is TableStatus.Creating or TableStatus.Active).IsTrue();
        await Assert.That(description.TableArn).Contains($":table/{name}", StringComparison.Ordinal);
        await Assert.That(description.KeySchema).IsEquivalentTo([new KeySchemaElement("pk", KeyType.Hash)]);
        await Assert.That(description.BillingModeSummary!.BillingMode).IsEqualTo(BillingMode.PayPerRequest);
    }

    [Test]
    public async Task CreateTable_ProvisionedWithLocalAndGlobalIndexesAndTags_CreatesTheTableAndItsIndexes()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.Tags);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var name = Tables.UniqueName();
        var request = new CreateTableRequest
        {
            TableName = name,
            AttributeDefinitions =
            [
                new AttributeDefinition("pk", ScalarAttributeType.String),
                new AttributeDefinition("sk", ScalarAttributeType.String),
                new AttributeDefinition("lsk", ScalarAttributeType.Number),
                new AttributeDefinition("gp", ScalarAttributeType.String),
            ],
            KeySchema = [new KeySchemaElement("pk", KeyType.Hash), new KeySchemaElement("sk", KeyType.Range)],
            BillingMode = BillingMode.Provisioned,
            ProvisionedThroughput = new ProvisionedThroughput { ReadCapacityUnits = 5, WriteCapacityUnits = 6 },
            LocalSecondaryIndexes =
            [
                new LocalSecondaryIndex
                {
                    IndexName = "by-lsk",
                    KeySchema = [new KeySchemaElement("pk", KeyType.Hash), new KeySchemaElement("lsk", KeyType.Range)],
                    Projection = new IndexProjection { ProjectionType = ProjectionType.Include, NonKeyAttributes = ["extra"] },
                },
            ],
            GlobalSecondaryIndexes =
            [
                new GlobalSecondaryIndex
                {
                    IndexName = "by-gp",
                    KeySchema = [new KeySchemaElement("gp", KeyType.Hash)],
                    Projection = new IndexProjection { ProjectionType = ProjectionType.KeysOnly },
                    ProvisionedThroughput = new ProvisionedThroughput { ReadCapacityUnits = 2, WriteCapacityUnits = 3 },
                },
            ],
            Tags = [new TableTag { Key = "env", Value = "integration-test" }],
        };

        // Act
        await sandbox.CreateTableAsync(request);
        var description = (await sandbox.Client.DescribeTableAsync(new DescribeTableRequest { TableName = name })).Table!;

        // Assert
        var local = description.LocalSecondaryIndexes.Single();
        await Assert.That((local.IndexName, local.Projection!.ProjectionType)).IsEqualTo(("by-lsk", ProjectionType.Include));
        await Assert.That(local.Projection.NonKeyAttributes).IsEquivalentTo(["extra"]);
        await Assert.That(string.Join(",", local.KeySchema.Select(static key => $"{key.AttributeName}:{key.KeyType}"))).IsEqualTo("pk:Hash,lsk:Range");
        var global = description.GlobalSecondaryIndexes.Single();
        await Assert.That((global.IndexName, global.IndexStatus, global.Projection!.ProjectionType)).IsEqualTo(("by-gp", IndexStatus.Active, ProjectionType.KeysOnly));
        await Assert.That(global.KeySchema).IsEquivalentTo([new KeySchemaElement("gp", KeyType.Hash)]);
    }

    [Test]
    public async Task CreateTable_LocalIndex_AllowsQueriesByTheIndexSortKey()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var name = Tables.UniqueName();
        await sandbox.CreateTableAsync(new CreateTableRequest
        {
            TableName = name,
            AttributeDefinitions =
            [
                new AttributeDefinition("pk", ScalarAttributeType.String),
                new AttributeDefinition("sk", ScalarAttributeType.String),
                new AttributeDefinition("lsk", ScalarAttributeType.Number),
            ],
            KeySchema = [new KeySchemaElement("pk", KeyType.Hash), new KeySchemaElement("sk", KeyType.Range)],
            BillingMode = BillingMode.PayPerRequest,
            LocalSecondaryIndexes =
            [
                new LocalSecondaryIndex
                {
                    IndexName = "by-lsk",
                    KeySchema = [new KeySchemaElement("pk", KeyType.Hash), new KeySchemaElement("lsk", KeyType.Range)],
                    Projection = new IndexProjection { ProjectionType = ProjectionType.All },
                },
            ],
        });
        foreach (var i in new[] { 3, 1, 2 })
        {
            await sandbox.Client.PutItemAsync(new PutItemRequest
            {
                TableName = name,
                Item = new Dictionary<string, AttributeValue> { ["pk"] = "a", ["sk"] = $"sk-{i}", ["lsk"] = AttributeValue.FromNumber(i) },
            });
        }

        // Act
        var response = await sandbox.Client.QueryAsync(new QueryRequest
        {
            TableName = name,
            IndexName = "by-lsk",
            KeyConditionExpression = "pk = :pk",
            ExpressionAttributeValues = Items.Values((":pk", "a")),
            ConsistentRead = true,
        });

        // Assert
        await Assert.That(string.Join(",", response.Items.Select(static item => item["lsk"].AsNumber()))).IsEqualTo("1,2,3");
    }

    [Test]
    [Arguments(StreamViewType.KeysOnly)]
    [Arguments(StreamViewType.NewImage)]
    [Arguments(StreamViewType.OldImage)]
    [Arguments(StreamViewType.NewAndOldImages)]
    public async Task CreateTable_WithStream_ReturnsTheStreamSettings(StreamViewType viewType)
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.Streams);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var name = Tables.UniqueName();

        // Act
        var response = await sandbox.CreateTableAsync(Tables.Request(name, TableSpec.WithStream(viewType)));

        // Assert
        var description = response.TableDescription!;
        await Assert.That(description.StreamSpecification).IsEqualTo(new StreamSpecification { StreamEnabled = true, StreamViewType = viewType });
        await Assert.That(description.LatestStreamArn).Contains($"table/{name}/stream/", StringComparison.Ordinal);
    }

    [Test]
    public async Task CreateTable_NameAlreadyInUse_ThrowsResourceInUseException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var failure = await Items.FailureOfAsync<ResourceInUseException>(() => sandbox.Client.CreateTableAsync(Tables.Request(table, TableSpec.PartitionOnly)));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ResourceInUseException");
    }

    [Test]
    public async Task CreateTable_NameTooShort_ThrowsValidationException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => sandbox.Client.CreateTableAsync(Tables.Request("ab", TableSpec.PartitionOnly)));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ValidationException");
    }

    [Test]
    public async Task CreateTable_KeyAttributeWithoutDefinition_ThrowsValidationException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var request = Tables.Request(Tables.UniqueName(), TableSpec.PartitionOnly) with { AttributeDefinitions = [] };

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => sandbox.Client.CreateTableAsync(request));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ValidationException");
    }

    [Test]
    public async Task DeleteTable_ExistingTable_ReturnsItsDescriptionAndTheTableIsGone()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.TableGoneAtOnce);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Client.DeleteTableAsync(new DeleteTableRequest { TableName = table });
        var failure = await Items.FailureOfAsync<ResourceNotFoundException>(() => sandbox.Client.DescribeTableAsync(new DescribeTableRequest { TableName = table }));

        // Assert
        await Assert.That(response.TableDescription!.TableName).IsEqualTo(table);
        await Assert.That(failure.ErrorCode).IsEqualTo("ResourceNotFoundException");
    }

    [Test]
    public async Task DeleteTable_ThenCreateWithTheSameName_Succeeds()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.TableGoneAtOnce);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "from the first table") });
        await sandbox.Client.DeleteTableAsync(new DeleteTableRequest { TableName = table });

        // Act
        await Tables.CreateAsync(sandbox.Client, Tables.Request(table, TableSpec.PartitionOnly));
        var item = await sandbox.Client.GetItemAsync(new GetItemRequest { TableName = table, Key = Items.Key("a"), ConsistentRead = true });

        // Assert
        await Assert.That(item.Item).IsNull();
    }

    [Test]
    public async Task DeleteTable_MissingTable_ThrowsResourceNotFoundException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ResourceNotFoundException>(() => sandbox.Client.DeleteTableAsync(new DeleteTableRequest { TableName = Tables.Missing }));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ResourceNotFoundException");
    }

    [Test]
    public async Task UpdateTimeToLive_Enable_ReturnsTheSpecification()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Client.UpdateTimeToLiveAsync(new UpdateTimeToLiveRequest
        {
            TableName = table,
            TimeToLiveSpecification = new TimeToLiveSpecification { Enabled = true, AttributeName = "expires" },
        });

        // Assert
        await Assert.That(response.TimeToLiveSpecification).IsEqualTo(new TimeToLiveSpecification { Enabled = true, AttributeName = "expires" });
    }

    [Test]
    public async Task UpdateTimeToLive_EnableThenDisable_ReturnsTheDisabledSpecification()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.TimeToLiveChangesAgainAtOnce);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.UpdateTimeToLiveAsync(new UpdateTimeToLiveRequest { TableName = table, TimeToLiveSpecification = new TimeToLiveSpecification { Enabled = true, AttributeName = "expires" } });

        // Act
        var response = await sandbox.Client.UpdateTimeToLiveAsync(new UpdateTimeToLiveRequest
        {
            TableName = table,
            TimeToLiveSpecification = new TimeToLiveSpecification { Enabled = false, AttributeName = "expires" },
        });

        // Assert
        await Assert.That(response.TimeToLiveSpecification).IsEqualTo(new TimeToLiveSpecification { Enabled = false, AttributeName = "expires" });
    }

    [Test]
    public async Task UpdateTimeToLive_EnableTwice_ThrowsValidationException()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.TimeToLiveEnabledTwiceRejected);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var enable = new UpdateTimeToLiveRequest { TableName = table, TimeToLiveSpecification = new TimeToLiveSpecification { Enabled = true, AttributeName = "expires" } };
        await sandbox.Client.UpdateTimeToLiveAsync(enable);

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => sandbox.Client.UpdateTimeToLiveAsync(enable));

        // Assert
        await Assert.That(failure.Message).Contains("already enabled", StringComparison.Ordinal);
    }

    [Test]
    public async Task UpdateTimeToLive_MissingTable_ThrowsResourceNotFoundException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ResourceNotFoundException>(() => sandbox.Client.UpdateTimeToLiveAsync(new UpdateTimeToLiveRequest
        {
            TableName = Tables.Missing,
            TimeToLiveSpecification = new TimeToLiveSpecification { Enabled = true, AttributeName = "expires" },
        }));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ResourceNotFoundException");
    }
}
