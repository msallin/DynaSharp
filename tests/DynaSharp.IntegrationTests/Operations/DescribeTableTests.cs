using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Operations;

/// <summary>DescribeTable against the emulators.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class DescribeTableTests(Backend backend)
{
    [Test]
    public async Task DescribeTable_PartitionKeyTable_ReportsNameStatusAndKey()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Client.DescribeTableAsync(new DescribeTableRequest { TableName = table });

        // Assert
        var description = response.Table!;
        await Assert.That((description.TableName, description.TableStatus)).IsEqualTo((table, TableStatus.Active));
        await Assert.That(description.KeySchema).IsEquivalentTo([new KeySchemaElement("pk", KeyType.Hash)]);
        await Assert.That(description.AttributeDefinitions).IsEquivalentTo([new AttributeDefinition("pk", ScalarAttributeType.String)]);
    }

    [Test]
    public async Task DescribeTable_PartitionAndSortKeyTable_ReportsBothKeysInOrder()
    {
        // Arrange: the sort key is a number and the index adds two string attributes
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);

        // Act
        var description = (await sandbox.Client.DescribeTableAsync(new DescribeTableRequest { TableName = table })).Table!;

        // Assert
        await Assert.That(string.Join(",", description.KeySchema.Select(static element => $"{element.AttributeName}:{element.KeyType}"))).IsEqualTo("pk:Hash,sk:Range");
        await Assert.That(description.AttributeDefinitions.Select(static definition => (definition.AttributeName, definition.AttributeType)).Order()).IsEquivalentTo(
            [
                ("gsi_pk", ScalarAttributeType.String),
                ("gsi_sk", ScalarAttributeType.String),
                ("pk", ScalarAttributeType.String),
                ("sk", ScalarAttributeType.Number),
            ]);
    }

    [Test]
    public async Task DescribeTable_BinaryKeyTable_ReportsTheBinaryType()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(new TableSpec(PartitionKeyType: KeyAttributeType.Binary));

        // Act
        var description = (await sandbox.Client.DescribeTableAsync(new DescribeTableRequest { TableName = table })).Table!;

        // Assert
        await Assert.That(description.AttributeDefinitions[0].AttributeType).IsEqualTo(ScalarAttributeType.Binary);
    }

    [Test]
    public async Task DescribeTable_NewTable_ReportsArnAndCreationTime()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var before = DateTimeOffset.UtcNow.AddMinutes(-5);
        var table = await sandbox.CreateTableAsync();

        // Act
        var description = (await sandbox.Client.DescribeTableAsync(new DescribeTableRequest { TableName = table })).Table!;

        // Assert
        await Assert.That(description.TableArn).Contains($":table/{table}");
        await Assert.That(description.CreationDateTime > before && description.CreationDateTime < DateTimeOffset.UtcNow.AddMinutes(5)).IsTrue();
    }

    [Test]
    public async Task DescribeTable_MissingTable_ThrowsResourceNotFoundException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ResourceNotFoundException>(
            () => sandbox.Client.DescribeTableAsync(new DescribeTableRequest { TableName = Tables.Missing }));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ResourceNotFoundException");
    }
}
