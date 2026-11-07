using System.Text.Json;
using DynaSharp.Requests;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Fluent;

namespace DynaSharp.UnitTests.Testing;

/// <summary>
/// The answers to table administration, read through the client: what a test said is what the client reports. The bodies that are spelled out come from the service model of botocore and the examples next to it,
/// with the members a table on demand has, not from the writers of this library.
/// </summary>
public sealed class StubResponseTableTests
{
    private const string Key = """{"AttributeName":"pk","KeyType":"HASH"}""";
    private const string NoCapacity = """{"NumberOfDecreasesToday":0,"ReadCapacityUnits":0,"WriteCapacityUnits":0}""";
    private const string OrdersArn = "arn:aws:dynamodb:us-east-1:123456789012:table/Orders";
    private const string TableId = "00000000-0000-4000-8000-000000000000";

    [Test]
    public async Task Table_Name_IsTheBodyOfADescribeTable()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.Table("Orders");

        // Assert
        await Assert.That(response.Body).IsEqualTo(
            $$$"""{"Table":{"AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"TableName":"Orders","KeySchema":[{{{Key}}}],"TableStatus":"ACTIVE","CreationDateTime":1767225600,"ProvisionedThroughput":{{{NoCapacity}}},"TableSizeBytes":0,"ItemCount":0,"TableArn":"{{{OrdersArn}}}","TableId":"{{{TableId}}}","BillingModeSummary":{"BillingMode":"PAY_PER_REQUEST"},"DeletionProtectionEnabled":false}}""");
    }

    [Test]
    public async Task Table_Name_ReadsBackAsThatTable()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.Table("Orders"));

        // Act
        var table = (await client.DescribeTableAsync(TestClient.DescribeTable("Orders"))).Table!;

        // Assert
        await Assert.That((table.TableName, table.TableArn, table.TableStatus, table.BillingModeSummary!.BillingMode, table.KeySchema.Single().AttributeName))
            .IsEqualTo(("Orders", OrdersArn, TableStatus.Active, BillingMode.PayPerRequest, "pk"));
    }

    [Test]
    [Arguments(TableStatus.Creating, "CREATING")]
    [Arguments(TableStatus.Updating, "UPDATING")]
    [Arguments(TableStatus.Deleting, "DELETING")]
    [Arguments(TableStatus.Active, "ACTIVE")]
    [Arguments(TableStatus.InaccessibleEncryptionCredentials, "INACCESSIBLE_ENCRYPTION_CREDENTIALS")]
    [Arguments(TableStatus.Archiving, "ARCHIVING")]
    [Arguments(TableStatus.Archived, "ARCHIVED")]
    [Arguments(TableStatus.ReplicationNotAuthorized, "REPLICATION_NOT_AUTHORIZED")]
    public async Task Table_Status_SendsTheNameOfTheServiceModel(TableStatus status, string wireName)
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.Table("Orders", status);

        // Assert
        await Assert.That(JsonElement.Parse(response.Body).GetProperty("Table").GetProperty("TableStatus").GetString()).IsEqualTo(wireName);
    }

    [Test]
    public async Task Table_UnknownStatus_ThrowsBecauseItHasNoName()
    {
        // Arrange (nothing to arrange)

        // Act
        var act = () => StubResponse.Table("Orders", TableStatus.Unknown);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Table_NullName_Throws()
    {
        // Arrange
        string name = null!;

        // Act
        var act = () => StubResponse.Table(name);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Table_NoIndexes_LeavesTheListsOut()
    {
        // Arrange (nothing to arrange)

        // Act
        var table = JsonElement.Parse(StubResponse.Table("Orders").Body).GetProperty("Table");

        // Assert
        await Assert.That((table.TryGetProperty("GlobalSecondaryIndexes", out _), table.TryGetProperty("LocalSecondaryIndexes", out _))).IsEqualTo((false, false));
    }

    [Test]
    public async Task Table_GlobalIndex_IsTheBodyOfAnIndexInADescribeTable()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.Table("Orders", TableStatus.Active, StubResponse.GlobalIndex("ByCustomer", IndexStatus.Creating));

        // Assert
        await Assert.That(JsonElement.Parse(response.Body).GetProperty("Table").GetProperty("GlobalSecondaryIndexes")[0].GetRawText()).IsEqualTo(
            $$$"""{"IndexName":"ByCustomer","KeySchema":[{"AttributeName":"ByCustomer_pk","KeyType":"HASH"}],"Projection":{"ProjectionType":"ALL"},"IndexStatus":"CREATING","ProvisionedThroughput":{{{NoCapacity}}},"IndexSizeBytes":0,"ItemCount":0,"IndexArn":"{{{OrdersArn}}}/index/ByCustomer"}""");
    }

    [Test]
    public async Task Table_GlobalIndexes_ReadBackWithTheirStatus()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.Table("Orders", TableStatus.Updating, StubResponse.GlobalIndex("ByCustomer"), StubResponse.GlobalIndex("ByDate", IndexStatus.Deleting)));

        // Act
        var table = (await client.DescribeTableAsync(TestClient.DescribeTable("Orders"))).Table!;

        // Assert
        await Assert.That(table.GlobalSecondaryIndexes.Select(index => (index.IndexName!, index.IndexStatus)))
            .IsEquivalentTo([("ByCustomer", IndexStatus.Active), ("ByDate", IndexStatus.Deleting)]);
    }

    [Test]
    [Arguments(IndexStatus.Creating, "CREATING")]
    [Arguments(IndexStatus.Updating, "UPDATING")]
    [Arguments(IndexStatus.Deleting, "DELETING")]
    [Arguments(IndexStatus.Active, "ACTIVE")]
    public async Task GlobalIndex_Status_SendsTheNameOfTheServiceModel(IndexStatus status, string wireName)
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.Table("Orders", TableStatus.Active, StubResponse.GlobalIndex("ByCustomer", status));

        // Assert
        await Assert.That(JsonElement.Parse(response.Body).GetProperty("Table").GetProperty("GlobalSecondaryIndexes")[0].GetProperty("IndexStatus").GetString()).IsEqualTo(wireName);
    }

    [Test]
    public async Task GlobalIndex_NullName_Throws()
    {
        // Arrange
        string name = null!;

        // Act
        var act = () => StubResponse.GlobalIndex(name);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Table_GlobalIndex_DefinesTheKeyAttributeOfTheIndex()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.Table("Orders", TableStatus.Active, StubResponse.GlobalIndex("ByCustomer")));

        // Act
        var table = (await client.DescribeTableAsync(TestClient.DescribeTable("Orders"))).Table!;

        // Assert
        await Assert.That(table.AttributeDefinitions.Select(definition => definition.AttributeName)).IsEquivalentTo(["pk", "ByCustomer_pk"]);
    }

    [Test]
    public async Task Table_TwoIndexesOverOneAttribute_DefinesItOnce()
    {
        // Arrange
        var shared = new GlobalSecondaryIndexDescription
        {
            IndexName = "A",
            IndexStatus = IndexStatus.Active,
            KeySchema = [new KeySchemaElement("pk", KeyType.Hash)],
        };

        // Act
        var table = JsonElement.Parse(StubResponse.Table("Orders", TableStatus.Active, shared, shared with { IndexName = "B" }).Body).GetProperty("Table");

        // Assert
        await Assert.That(table.GetProperty("AttributeDefinitions").GetArrayLength()).IsEqualTo(1);
    }

    [Test]
    public async Task Table_IndexOfYourOwnWithoutAProjection_LeavesItOut()
    {
        // Arrange
        var index = new GlobalSecondaryIndexDescription { IndexName = "A", IndexStatus = IndexStatus.Active };

        // Act
        var written = JsonElement.Parse(StubResponse.Table("Orders", TableStatus.Active, index).Body).GetProperty("Table").GetProperty("GlobalSecondaryIndexes")[0];

        // Assert
        await Assert.That(written.TryGetProperty("Projection", out _)).IsFalse();
    }

    [Test]
    public async Task Table_BackfillingIndex_SendsTheFlag()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.Table("Orders", TableStatus.Active, StubResponse.GlobalIndex("ByCustomer", IndexStatus.Creating) with { Backfilling = true }));

        // Act
        var table = (await client.DescribeTableAsync(TestClient.DescribeTable("Orders"))).Table!;

        // Assert
        await Assert.That(table.GlobalSecondaryIndexes.Single().Backfilling).IsTrue();
    }

    [Test]
    public async Task Table_IndexNotBackfilling_LeavesTheFlagOut()
    {
        // Arrange (nothing to arrange)

        // Act
        var written = JsonElement.Parse(StubResponse.Table("Orders", TableStatus.Active, StubResponse.GlobalIndex("A")).Body).GetProperty("Table").GetProperty("GlobalSecondaryIndexes")[0];

        // Assert
        await Assert.That(written.TryGetProperty("Backfilling", out _)).IsFalse();
    }

    // What a wait reads: a table reads ACTIVE while an index it was given later is still being built.
    [Test]
    public async Task Table_IndexStillCreating_HoldsUpAWait()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.Table("Orders", TableStatus.Active, StubResponse.GlobalIndex("ByCustomer", IndexStatus.Creating)));

        // Act
        var exception = await TestClient.CaptureAsync<TableNotActiveException>(() => client.Table("Orders", "pk").WaitUntilActiveAsync(TimeSpan.Zero).AsTask());

        // Assert
        await Assert.That(exception.PendingIndex).IsEqualTo("ByCustomer");
    }

    [Test]
    public async Task Table_Entity_ListsTheGlobalIndexesItDeclaresAsActive()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.Table<SchemaOrder>("Orders"));

        // Act
        var table = (await client.DescribeTableAsync(TestClient.DescribeTable("Orders"))).Table!;

        // Assert
        await Assert.That(table.GlobalSecondaryIndexes.Select(index => (index.IndexName!, index.IndexStatus)))
            .IsEquivalentTo([("GSI1", IndexStatus.Active), ("ByTracking", IndexStatus.Active)]);
    }

    [Test]
    public async Task Table_Entity_ListsItsLocalIndexesApart()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.Table<SchemaOrder>("Orders"));

        // Act
        var table = (await client.DescribeTableAsync(TestClient.DescribeTable("Orders"))).Table!;

        // Assert
        await Assert.That(table.LocalSecondaryIndexes.Select(index => (index.IndexName!, index.KeySchema[1].AttributeName, index.Projection!.ProjectionType)))
            .IsEquivalentTo([("LSI1", "lsi1sk", ProjectionType.All)]);
    }

    [Test]
    public async Task Table_Entity_NamesTheKeysOfTheEntity()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.Table<SchemaOrder>("Orders"));

        // Act
        var table = (await client.DescribeTableAsync(TestClient.DescribeTable("Orders"))).Table!;

        // Assert
        await Assert.That(table.KeySchema).IsEquivalentTo([new KeySchemaElement("pk", KeyType.Hash), new KeySchemaElement("sk", KeyType.Range)]);
    }

    [Test]
    public async Task Table_Entity_GivesAnIndexTheKeysOfItsDeclaration()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.Table<SchemaOrder>("Orders"));

        // Act
        var table = (await client.DescribeTableAsync(TestClient.DescribeTable("Orders"))).Table!;

        // Assert
        await Assert.That(table.GlobalSecondaryIndexes.Single(index => index.IndexName == "GSI1").KeySchema)
            .IsEquivalentTo([new KeySchemaElement("gsi1pk", KeyType.Hash), new KeySchemaElement("gsi1sk", KeyType.Range)]);
    }

    [Test]
    public async Task Table_Entity_DefinesTheKeysWithTheirTypes()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.Table<SchemaReading>("Readings"));

        // Act
        var table = (await client.DescribeTableAsync(TestClient.DescribeTable("Readings"))).Table!;

        // Assert
        await Assert.That(table.AttributeDefinitions).IsEquivalentTo(
            [
                new AttributeDefinition("Sensor", ScalarAttributeType.Number),
                new AttributeDefinition("Stamp", ScalarAttributeType.Binary),
                new AttributeDefinition("ExpiresAt", ScalarAttributeType.Number),
            ]);
    }

    [Test]
    public async Task Table_Entity_TakesAStatus()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.Table<SchemaOrder>("Orders", TableStatus.Updating));

        // Act
        var table = (await client.DescribeTableAsync(TestClient.DescribeTable("Orders"))).Table!;

        // Assert
        await Assert.That(table.TableStatus).IsEqualTo(TableStatus.Updating);
    }

    [Test]
    public async Task Table_Entity_EndsAWait()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.Table<SchemaOrder>("Orders"));

        // Act
        var table = await client.Table<SchemaOrder>("Orders").WaitUntilActiveAsync(TimeSpan.Zero);

        // Assert
        await Assert.That(table.TableName).IsEqualTo("Orders");
    }

    [Test]
    public async Task Table_EntityWithoutAName_DescribesTheDeclaredTableWithTheStatus()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.Table<Consumer.Patron>(TableStatus.Creating));

        // Act
        var table = (await client.DescribeTableAsync(TestClient.DescribeTable("patrons"))).Table!;

        // Assert
        await Assert.That((table.TableName, table.TableStatus)).IsEqualTo(("patrons", TableStatus.Creating));
    }

    [Test]
    public async Task Table_EntityNullName_Throws()
    {
        // Arrange
        string name = null!;

        // Act
        var act = () => StubResponse.Table<SchemaOrder>(name);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task TableCreated_Name_IsTheBodyOfACreateTable()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.TableCreated("Orders");

        // Assert
        await Assert.That(response.Body).IsEqualTo(
            $$$"""{"TableDescription":{"AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"TableName":"Orders","KeySchema":[{{{Key}}}],"TableStatus":"CREATING","CreationDateTime":1767225600,"ProvisionedThroughput":{{{NoCapacity}}},"TableSizeBytes":0,"ItemCount":0,"TableArn":"{{{OrdersArn}}}","TableId":"{{{TableId}}}","BillingModeSummary":{"BillingMode":"PAY_PER_REQUEST"},"DeletionProtectionEnabled":false}}""");
    }

    [Test]
    public async Task TableCreated_Name_ReadsBackFromACreateTable()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.TableCreated("Orders"));

        // Act
        var table = (await client.CreateTableAsync(TestClient.CreateTable("Orders"))).TableDescription!;

        // Assert
        await Assert.That((table.TableName, table.TableStatus)).IsEqualTo(("Orders", TableStatus.Creating));
    }

    // An emulator answers CreateTable with a table that is active already.
    [Test]
    public async Task TableCreated_Status_ReadsBackThroughACommand()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.TableCreated("Orders", TableStatus.Active));

        // Act
        var result = await client.Table<SchemaOrder>("Orders").CreateTable().SendAsync();

        // Assert
        await Assert.That(result.Table.TableStatus).IsEqualTo(TableStatus.Active);
    }

    [Test]
    public async Task TableCreated_GlobalIndex_ReadsBackWithItsStatus()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.TableCreated("Orders", TableStatus.Creating, StubResponse.GlobalIndex("ByCustomer", IndexStatus.Creating)));

        // Act
        var table = (await client.CreateTableAsync(TestClient.CreateTable("Orders"))).TableDescription!;

        // Assert
        await Assert.That(table.GlobalSecondaryIndexes.Single().IndexStatus).IsEqualTo(IndexStatus.Creating);
    }

    [Test]
    public async Task TableCreated_NullName_Throws()
    {
        // Arrange
        string name = null!;

        // Act
        var act = () => StubResponse.TableCreated(name);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task TableUpdated_Name_IsTheBodyOfAnUpdateTable()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.TableUpdated("Orders");

        // Assert
        await Assert.That(response.Body).IsEqualTo(
            $$$"""{"TableDescription":{"AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"TableName":"Orders","KeySchema":[{{{Key}}}],"TableStatus":"UPDATING","CreationDateTime":1767225600,"ProvisionedThroughput":{{{NoCapacity}}},"TableSizeBytes":0,"ItemCount":0,"TableArn":"{{{OrdersArn}}}","TableId":"{{{TableId}}}","BillingModeSummary":{"BillingMode":"PAY_PER_REQUEST"},"DeletionProtectionEnabled":false}}""");
    }

    // An update that adds an index: DynamoDB answers while it builds the index, and the wait on DescribeTable comes after.
    [Test]
    public async Task TableUpdated_GlobalIndexThatTheUpdateAdds_ReadsBackWithItsStatus()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.TableUpdated("Orders", TableStatus.Updating, StubResponse.GlobalIndex("ByCustomer", IndexStatus.Creating)));

        // Act
        var table = (await client.UpdateTableAsync(TestClient.UpdateTable("Orders"))).TableDescription!;

        // Assert
        await Assert.That((table.TableName, table.TableStatus, table.GlobalSecondaryIndexes.Single().IndexStatus)).IsEqualTo(("Orders", TableStatus.Updating, IndexStatus.Creating));
    }

    [Test]
    public async Task TableUpdated_NullName_Throws()
    {
        // Arrange
        string name = null!;

        // Act
        var act = () => StubResponse.TableUpdated(name);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task TablesListed_Names_IsTheBodyOfTheLastPageOfAListTables()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.TablesListed("Orders", "Users");

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"TableNames":["Orders","Users"]}""");
    }

    [Test]
    public async Task TablesListed_NoNames_IsTheBodyOfAnAccountWithoutTables()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.TablesListed();

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"TableNames":[]}""");
    }

    [Test]
    public async Task TablesListed_LastEvaluatedTableName_IsTheBodyOfAPageThatMorePagesFollow()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.TablesListed(["Orders", "Users"], "Users");

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"TableNames":["Orders","Users"],"LastEvaluatedTableName":"Users"}""");
    }

    // A loop over the pages of an account, as a cleanup of test tables does it.
    [Test]
    public async Task TablesListed_TwoPages_ReadBackThroughListTables()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.TablesListed(["Orders"], "Orders"));
        stub.Enqueue(StubResponse.TablesListed("Users"));

        // Act
        var first = await client.ListTablesAsync(TestClient.ListTables());
        var second = await client.ListTablesAsync(new ListTablesRequest { ExclusiveStartTableName = first.LastEvaluatedTableName });

        // Assert
        await Assert.That((string.Join(",", first.TableNames.Concat(second.TableNames)), first.LastEvaluatedTableName, second.LastEvaluatedTableName)).IsEqualTo(("Orders,Users", (string?)"Orders", (string?)null));
    }

    [Test]
    public async Task TablesListed_NullName_Throws()
    {
        // Arrange
        string name = null!;

        // Act
        var act = () => StubResponse.TablesListed("Orders", name);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>().WithParameterName("tableNames");
    }

    [Test]
    public async Task TablesListed_NullLastEvaluatedTableName_Throws()
    {
        // Arrange
        string name = null!;

        // Act
        var act = () => StubResponse.TablesListed(["Orders"], name);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>().WithParameterName("lastEvaluatedTableName");
    }

    // The members of the example of DeleteTable, and the two names real tables carry: the keys and the indexes are no longer part of the answer.
    [Test]
    public async Task TableDeleted_Name_IsTheBodyOfADeleteTable()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.TableDeleted("Orders");

        // Assert
        await Assert.That(response.Body).IsEqualTo(
            $$$"""{"TableDescription":{"TableName":"Orders","TableStatus":"DELETING","ProvisionedThroughput":{{{NoCapacity}}},"TableSizeBytes":0,"ItemCount":0,"TableArn":"{{{OrdersArn}}}","TableId":"{{{TableId}}}"}}""");
    }

    [Test]
    public async Task TableDeleted_Name_ReadsBackThroughATable()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.TableDeleted("Orders"));

        // Act
        var table = await client.Table("Orders", "pk").DeleteTableAsync();

        // Assert
        await Assert.That((table.TableName, table.TableStatus)).IsEqualTo(("Orders", TableStatus.Deleting));
    }

    [Test]
    public async Task TableDeleted_NullName_Throws()
    {
        // Arrange
        string name = null!;

        // Act
        var act = () => StubResponse.TableDeleted(name);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task TimeToLive_EnabledWithAnAttribute_IsTheBodyOfADescribeTimeToLive()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.TimeToLive(TimeToLiveStatus.Enabled, "expires");

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"TimeToLiveDescription":{"TimeToLiveStatus":"ENABLED","AttributeName":"expires"}}""");
    }

    [Test]
    public async Task TimeToLive_DisabledWithoutAnAttribute_LeavesItOut()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.TimeToLive(TimeToLiveStatus.Disabled);

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"TimeToLiveDescription":{"TimeToLiveStatus":"DISABLED"}}""");
    }

    [Test]
    [Arguments(TimeToLiveStatus.Enabling)]
    [Arguments(TimeToLiveStatus.Disabling)]
    [Arguments(TimeToLiveStatus.Enabled)]
    [Arguments(TimeToLiveStatus.Disabled)]
    public async Task TimeToLive_Status_ReadsBackFromADescribeTimeToLive(TimeToLiveStatus status)
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.TimeToLive(status, "expires"));

        // Act
        var description = (await client.DescribeTimeToLiveAsync(TestClient.DescribeTimeToLive("Orders"))).TimeToLiveDescription!;

        // Assert
        await Assert.That((description.TimeToLiveStatus, description.AttributeName)).IsEqualTo((status, "expires"));
    }

    [Test]
    public async Task TimeToLive_UnknownStatus_ThrowsBecauseItHasNoName()
    {
        // Arrange (nothing to arrange)

        // Act
        var act = () => StubResponse.TimeToLive(TimeToLiveStatus.Unknown);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task TimeToLiveUpdated_Enabled_IsTheBodyOfAnUpdateTimeToLive()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.TimeToLiveUpdated(enabled: true, "expires");

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"TimeToLiveSpecification":{"AttributeName":"expires","Enabled":true}}""");
    }

    [Test]
    public async Task TimeToLiveUpdated_Disabled_ReadsBackFromAnUpdateTimeToLive()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.TimeToLiveUpdated(enabled: false, "expires"));

        // Act
        var specification = (await client.UpdateTimeToLiveAsync(TestClient.UpdateTimeToLive("Orders"))).TimeToLiveSpecification!;

        // Assert
        await Assert.That((specification.Enabled, specification.AttributeName)).IsEqualTo((false, "expires"));
    }

    [Test]
    public async Task TimeToLiveUpdated_NullAttribute_Throws()
    {
        // Arrange
        string attributeName = null!;

        // Act
        var act = () => StubResponse.TimeToLiveUpdated(enabled: true, attributeName);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ResourceNotFound_NoArguments_SurfacesAsItsException()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.ResourceNotFound());

        // Act
        var exception = await TestClient.CaptureAsync<ResourceNotFoundException>(() => client.DescribeTableAsync(TestClient.DescribeTable("Orders")));

        // Assert
        await Assert.That(exception.Message).Contains("Requested resource not found");
    }

    [Test]
    public async Task ResourceInUse_NoArguments_SurfacesAsItsException()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.ResourceInUse());

        // Act
        var exception = await TestClient.CaptureAsync<ResourceInUseException>(() => client.CreateTableAsync(TestClient.CreateTable("Orders")));

        // Assert
        await Assert.That(exception.Message).Contains("in use");
    }
}
