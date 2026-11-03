using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Operations;

/// <summary>
/// ConsumedCapacity as DynamoDB and the emulators report it. All items here are far below 1 KB, 4 KB and 8 KB, so one write unit and half a read
/// unit (a full one for a strongly consistent read) are the documented costs. A scan pays that per partition it reads.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class ConsumedCapacityTests(Backend backend)
{
    [Test]
    public async Task GetItem_NotRequested_ReturnsNoConsumedCapacity()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Client.GetItemAsync(new GetItemRequest { TableName = table, Key = Items.Key("a") });

        // Assert
        await Assert.That(response.ConsumedCapacity).IsNull();
    }

    [Test]
    public async Task GetItem_Total_ReportsHalfAReadUnitForAnEventuallyConsistentRead()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "x") });

        // Act
        var response = await sandbox.Client.GetItemAsync(new GetItemRequest { TableName = table, Key = Items.Key("a"), ReturnConsumedCapacity = ReturnConsumedCapacity.Total });

        // Assert
        await Assert.That((response.ConsumedCapacity!.TableName, response.ConsumedCapacity.CapacityUnits)).IsEqualTo((table, 0.5));
    }

    [Test]
    public async Task GetItem_ConsistentRead_ReportsAFullReadUnit()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "x") });

        // Act
        var response = await sandbox.Client.GetItemAsync(new GetItemRequest
        {
            TableName = table,
            Key = Items.Key("a"),
            ConsistentRead = true,
            ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
        });

        // Assert
        await Assert.That(response.ConsumedCapacity!.CapacityUnits).IsEqualTo(1.0);
    }

    [Test]
    public async Task GetItem_Indexes_ReportsTheTableShare()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Client.GetItemAsync(new GetItemRequest
        {
            TableName = table,
            Key = Items.Key("a"),
            ConsistentRead = true,
            ReturnConsumedCapacity = ReturnConsumedCapacity.Indexes,
        });

        // Assert
        await Assert.That(response.ConsumedCapacity!.Table!.CapacityUnits).IsEqualTo(response.ConsumedCapacity.CapacityUnits);
    }

    [Test]
    public async Task PutItem_Total_ReportsOneWriteUnit()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "x"), ReturnConsumedCapacity = ReturnConsumedCapacity.Total });

        // Assert
        await Assert.That((response.ConsumedCapacity!.TableName, response.ConsumedCapacity.CapacityUnits)).IsEqualTo((table, 1.0));
    }

    [Test]
    public async Task UpdateItem_Total_ReportsOneWriteUnit()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = table,
            Key = Items.Key("a"),
            UpdateExpression = "SET v = :v",
            ExpressionAttributeValues = Items.Values((":v", "x")),
            ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
        });

        // Assert
        await Assert.That(response.ConsumedCapacity!.CapacityUnits).IsEqualTo(1.0);
    }

    [Test]
    public async Task DeleteItem_Total_ReportsOneWriteUnit()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "x") });

        // Act
        var response = await sandbox.Client.DeleteItemAsync(new DeleteItemRequest { TableName = table, Key = Items.Key("a"), ReturnConsumedCapacity = ReturnConsumedCapacity.Total });

        // Assert
        await Assert.That(response.ConsumedCapacity!.CapacityUnits).IsEqualTo(1.0);
    }

    [Test]
    public async Task Query_Total_ReportsTheCapacityOfTheItemsRead()
    {
        // Arrange: five items of about 100 bytes are read together: 500 bytes round up to one 4 KB unit, half for eventual consistency
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 5);

        // Act
        var response = await sandbox.Client.QueryAsync(new QueryRequest
        {
            TableName = table,
            KeyConditionExpression = "pk = :pk",
            ExpressionAttributeValues = Items.Values((":pk", "a")),
            ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
        });

        // Assert
        await Assert.That(response.ConsumedCapacity!.CapacityUnits).IsEqualTo(0.5);
    }

    [Test]
    public async Task Query_IndexesOnGlobalSecondaryIndex_ReportsTheIndexShare()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 3);

        // Act
        var response = await sandbox.Client.QueryAsync(new QueryRequest
        {
            TableName = table,
            IndexName = Tables.GlobalIndexName,
            KeyConditionExpression = "gsi_pk = :g",
            ExpressionAttributeValues = Items.Values((":g", "g")),
            ReturnConsumedCapacity = ReturnConsumedCapacity.Indexes,
        });

        // Assert
        var capacity = response.ConsumedCapacity!;
        await Assert.That(capacity.GlobalSecondaryIndexes!.Keys).IsEquivalentTo([Tables.GlobalIndexName]);
        await Assert.That(capacity.GlobalSecondaryIndexes[Tables.GlobalIndexName].CapacityUnits).IsEqualTo(capacity.CapacityUnits);
    }

    [Test]
    public async Task Scan_Total_ReportsHalfUnitsAndTwiceThatForAConsistentScan()
    {
        // Arrange (a scan reads every partition of the table and rounds what it read in each up to 4 KB: an emulator has one partition and
        // reports 0.5, a new on-demand table of real DynamoDB several, and 0.5 for each, also for an empty one)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.Sequence);
        await Items.PutSequenceAsync(sandbox.Client, table, "a", 3);

        // Act
        var eventual = await sandbox.Client.ScanAsync(new ScanRequest { TableName = table, ReturnConsumedCapacity = ReturnConsumedCapacity.Total });
        var consistent = await sandbox.Client.ScanAsync(new ScanRequest { TableName = table, ConsistentRead = true, ReturnConsumedCapacity = ReturnConsumedCapacity.Total });

        // Assert
        var units = eventual.ConsumedCapacity!.CapacityUnits!.Value;
        await Assert.That((eventual.ConsumedCapacity.TableName, units > 0 && units % 0.5 == 0, consistent.ConsumedCapacity!.CapacityUnits)).IsEqualTo((table, true, units * 2));
    }

    [Test]
    public async Task PutItem_LargeItem_ReportsOneWriteUnitPerKilobyte()
    {
        // Arrange: an item of about 2.5 KB costs 3 write units
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var text = new string('x', 2500);

        // Act
        var response = await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", text), ReturnConsumedCapacity = ReturnConsumedCapacity.Total });

        // Assert
        await Assert.That(response.ConsumedCapacity!.CapacityUnits).IsEqualTo(3.0);
    }
}
