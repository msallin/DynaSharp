using DynaSharp.Requests;
using DynaSharp.Testing;
using Microsoft.Extensions.Logging;

namespace DynaSharp.UnitTests.Fluent;

// What IfNotExists() checks on a table it finds: a key that differs throws, and drift of indexes, time to live and the stream is reported.
public sealed partial class CreateTableCommandTests
{
    private const string OrderKey = """
        "KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"sk","KeyType":"RANGE"}]
        """;

    private const string OrderAttributes = """
        "AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"sk","AttributeType":"S"},{"AttributeName":"gsi1pk","AttributeType":"S"},{"AttributeName":"gsi1sk","AttributeType":"S"},{"AttributeName":"bytrackingpk","AttributeType":"S"},{"AttributeName":"lsi1sk","AttributeType":"S"}]
        """;

    private const string Gsi1 = """
        {"IndexName":"GSI1","IndexStatus":"ACTIVE","KeySchema":[{"AttributeName":"gsi1pk","KeyType":"HASH"},{"AttributeName":"gsi1sk","KeyType":"RANGE"}]}
        """;

    private const string ByTracking = """
        {"IndexName":"ByTracking","IndexStatus":"ACTIVE","KeySchema":[{"AttributeName":"bytrackingpk","KeyType":"HASH"}]}
        """;

    private const string Lsi1 = """
        {"IndexName":"LSI1","KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"lsi1sk","KeyType":"RANGE"}]}
        """;

    [Test]
    public async Task SendAsync_IfNotExistsOverATableWithAnotherPartitionKey_ThrowsNamingBothKeysBeforeAnyWaitOrTimeToLive()
    {
        // Arrange (a lock table created with the partition key Resource, which code that writes pk meets after a deploy)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described("""
            "KeySchema":[{"AttributeName":"Resource","KeyType":"HASH"}],"AttributeDefinitions":[{"AttributeName":"Resource","AttributeType":"S"}]
            """)));
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var command = client.Table<SchemaOrder>("Shop").CreateTable().IfNotExists().WaitUntilActive().TimeToLive("expires");

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(async () => await command.SendAsync());

        // Assert
        await Assert.That((failure.Message, Operations(handler))).IsEqualTo((
            "The table Shop exists with the partition key Resource (String) and no sort key, and this command expects the partition key pk (String) and sort key sk (String). "
            + "Every read and write of these entities would fail with a ValidationException, and DynamoDB does not change the key of a table: give them a table of their own, or create a new one and move the items.",
            "DescribeTable"));
    }

    [Test]
    [Arguments("""[{"AttributeName":"pk","KeyType":"HASH"}]""", "partition key pk (String) and no sort key")]
    [Arguments("""[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"other","KeyType":"RANGE"}]""", "partition key pk (String) and sort key other")]
    public async Task SendAsync_IfNotExistsOverATableWithoutThatSortKey_Throws(string keySchema, string found)
    {
        // Arrange (a table without an entity knows its key names)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described($$"""
            "KeySchema":{{keySchema}},"AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}]
            """)));
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk", "sk").CreateTable().IfNotExists();

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(async () => await command.SendAsync());

        // Assert
        await Assert.That(failure.Message).StartsWith($"The table Shop exists with the {found}, and this command expects the partition key pk and sort key sk. ");
    }

    [Test]
    public async Task SendAsync_IfNotExistsOverATableWithASortKeyTheCommandLacks_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described(OrderKey)));
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").CreateTable().IfNotExists();

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(async () => await command.SendAsync());

        // Assert
        await Assert.That(failure.Message).StartsWith("The table Shop exists with the partition key pk and sort key sk, and this command expects the partition key pk and no sort key. ");
    }

    [Test]
    [Arguments("S", "B", "partition key Sensor (String) and sort key Stamp (Binary)")]
    [Arguments("N", "S", "partition key Sensor (Number) and sort key Stamp (String)")]
    public async Task SendAsync_IfNotExistsOverAKeyOfAnotherType_Throws(string partitionKeyType, string sortKeyType, string found)
    {
        // Arrange (SchemaReading stores a number and a binary value)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described($$"""
            "KeySchema":[{"AttributeName":"Sensor","KeyType":"HASH"},{"AttributeName":"Stamp","KeyType":"RANGE"}],
            "AttributeDefinitions":[{"AttributeName":"Sensor","AttributeType":"{{partitionKeyType}}"},{"AttributeName":"Stamp","AttributeType":"{{sortKeyType}}"}]
            """)));
        using var client = TestClient.Create(handler);
        var command = client.Table<SchemaReading>("Shop").CreateTable().IfNotExists();

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(async () => await command.SendAsync());

        // Assert
        await Assert.That(failure.Message).StartsWith($"The table Shop exists with the {found}, and this command expects the partition key Sensor (Number) and sort key Stamp (Binary). ");
    }

    [Test]
    [Arguments("""[{"AttributeName":"pk","AttributeType":"N"},{"AttributeName":"sk","AttributeType":"B"}]""")]
    [Arguments("[]")]
    public async Task SendAsync_IfNotExistsWhereOneSideKnowsNoType_ComparesTheNamesAlone(string definitions)
    {
        // Arrange (a table opened without an entity says nothing of the types, and a description without definitions neither)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described($$"""{{OrderKey}},"AttributeDefinitions":{{definitions}}""")));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk", "sk").CreateTable().IfNotExists().SendAsync();

        // Assert
        await Assert.That((result.Created, result.Drift.Count)).IsEqualTo((false, 0));
    }

    [Test]
    public async Task SendAsync_IfNotExistsOverAReadingTableWithoutDefinitions_ComparesTheNamesAlone()
    {
        // Arrange (the entity knows the types, the description does not)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described("""
            "KeySchema":[{"AttributeName":"Sensor","KeyType":"HASH"},{"AttributeName":"Stamp","KeyType":"RANGE"}],
            "LocalSecondaryIndexes":[{"IndexName":"ByExpiry","KeySchema":[{"AttributeName":"Sensor","KeyType":"HASH"},{"AttributeName":"ExpiresAt","KeyType":"RANGE"}]}]
            """)));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<SchemaReading>("Shop").CreateTable().IfNotExists().SendAsync();

        // Assert
        await Assert.That(result.Drift.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_IfNotExistsOverATableAsDeclared_ReportsNoDriftAndLogsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described($$"""{{OrderKey}},{{OrderAttributes}},"GlobalSecondaryIndexes":[{{Gsi1}},{{ByTracking}}],"LocalSecondaryIndexes":[{{Lsi1}}]""")));
        using var logs = new CapturingLoggerFactory();
        using var client = TestClient.Create(handler, loggerFactory: logs);

        // Act
        var result = await client.Table<SchemaOrder>("Shop").CreateTable().IfNotExists().SendAsync();

        // Assert
        await Assert.That((result.Drift.Count, logs.Entries.Count(static entry => entry.EventId.Id == 14))).IsEqualTo((0, 0));
    }

    [Test]
    public async Task SendAsync_IfNotExistsOverATableWithMoreThanDeclared_ReportsNoDrift()
    {
        // Arrange (another index, another attribute and a stream are other code's)
        const string Extra = """
            {"IndexName":"ByOther","IndexStatus":"ACTIVE","KeySchema":[{"AttributeName":"other","KeyType":"HASH"}]}
            """;
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described($$"""
            {{OrderKey}},"AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"other","AttributeType":"N"}],
            "GlobalSecondaryIndexes":[{{Gsi1}},{{ByTracking}},{{Extra}}],"LocalSecondaryIndexes":[{{Lsi1}}],
            "StreamSpecification":{"StreamEnabled":true,"StreamViewType":"KEYS_ONLY"}
            """)));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<SchemaOrder>("Shop").CreateTable().IfNotExists().SendAsync();

        // Assert
        await Assert.That(result.Drift.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_IfNotExistsOverATableThatLacksIndexes_ReportsEachAndSaysHowToAddIt()
    {
        // Arrange (an older table, which the entity gave a global and a local index since)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described($$"""{{OrderKey}},{{OrderAttributes}},"GlobalSecondaryIndexes":[{{ByTracking}}]""")));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<SchemaOrder>("Shop").CreateTable().IfNotExists().SendAsync();

        // Assert
        TableDrift[] expected =
        [
            new TableDrift(
                TableDriftKind.IndexMissing,
                "GSI1",
                "The table Shop lacks the global index GSI1 over the partition key gsi1pk (String) and sort key gsi1sk (String). Queries of it fail until UpdateTable adds it."),
            new TableDrift(
                TableDriftKind.IndexMissing,
                "LSI1",
                "The table Shop lacks the local index LSI1 over the partition key pk (String) and sort key lsi1sk (String). A local index exists only if the table was created with it, so queries of it fail until the items move to a new table that has it."),
        ];
        await Assert.That(result.Drift).IsEquivalentTo(expected);
    }

    [Test]
    public async Task SendAsync_IfNotExistsOverIndexesWithOtherKeys_ReportsEach()
    {
        // Arrange (GSI1 with another sort key, ByTracking as a local index, LSI1 over a number)
        const string OtherGsi1 = """
            {"IndexName":"GSI1","IndexStatus":"ACTIVE","KeySchema":[{"AttributeName":"gsi1pk","KeyType":"HASH"},{"AttributeName":"placed","KeyType":"RANGE"}]}
            """;
        const string LocalTracking = """
            {"IndexName":"ByTracking","KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"bytrackingpk","KeyType":"RANGE"}]}
            """;
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described($$"""
            {{OrderKey}},"AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"lsi1sk","AttributeType":"N"}],
            "GlobalSecondaryIndexes":[{{OtherGsi1}}],"LocalSecondaryIndexes":[{{Lsi1}},{{LocalTracking}}]
            """)));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<SchemaOrder>("Shop").CreateTable().IfNotExists().SendAsync();

        // Assert
        await Assert.That((string.Join(",", result.Drift.Select(static drift => $"{drift.Kind}:{drift.IndexName}")), result.Drift[0].Description)).IsEqualTo((
            "IndexDiffers:GSI1,IndexDiffers:ByTracking,IndexDiffers:LSI1",
            "The table Shop has the index GSI1 as a global index over the partition key gsi1pk and sort key placed, and the entities declare a global index GSI1 over the partition key gsi1pk (String) and sort key gsi1sk (String). "
            + "Queries of it read other attributes than they expect, or fail."));
    }

    [Test]
    public async Task SendAsync_IfNotExistsOverAnIndexThatDoesNotProjectTheDiscriminator_ReportsIt()
    {
        // Arrange (GSI1 of keys only, ByTracking including _type, LSI1 of every attribute: only GSI1 lacks the discriminator a typed read filters on)
        const string KeysOnlyGsi1 = """
            {"IndexName":"GSI1","IndexStatus":"ACTIVE","KeySchema":[{"AttributeName":"gsi1pk","KeyType":"HASH"},{"AttributeName":"gsi1sk","KeyType":"RANGE"}],"Projection":{"ProjectionType":"KEYS_ONLY"}}
            """;
        const string TrackingWithType = """
            {"IndexName":"ByTracking","IndexStatus":"ACTIVE","KeySchema":[{"AttributeName":"bytrackingpk","KeyType":"HASH"}],"Projection":{"ProjectionType":"INCLUDE","NonKeyAttributes":["_type"]}}
            """;
        const string AllLsi1 = """
            {"IndexName":"LSI1","KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"lsi1sk","KeyType":"RANGE"}],"Projection":{"ProjectionType":"ALL"}}
            """;
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described($$"""{{OrderKey}},{{OrderAttributes}},"GlobalSecondaryIndexes":[{{KeysOnlyGsi1}},{{TrackingWithType}}],"LocalSecondaryIndexes":[{{AllLsi1}}]""")));
        using var client = TestClient.Create(handler);
        const string Lacks = "The table Shop has the index GSI1 with a projection of KeysOnly, which lacks _type, the discriminator of the entities. A typed read of it filters on _type, so it finds nothing or fails. "
            + "An index keeps the projection it was created with: delete it and create it again with _type in the projection, or with ALL.";

        // Act
        var result = await client.Table<SchemaOrder>("Shop").CreateTable().IfNotExists().SendAsync();

        // Assert
        await Assert.That(result.Drift.Single()).IsEqualTo(new TableDrift(TableDriftKind.IndexDiffers, "GSI1", Lacks));
    }

    [Test]
    public async Task SendAsync_IfNotExistsOverALocalIndexWhereAGlobalIsDeclared_SaysBoth()
    {
        // Arrange
        const string LocalTracking = """
            {"IndexName":"ByTracking","KeySchema":[{"AttributeName":"bytrackingpk","KeyType":"HASH"}]}
            """;
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described($$"""{{OrderKey}},{{OrderAttributes}},"GlobalSecondaryIndexes":[{{Gsi1}}],"LocalSecondaryIndexes":[{{Lsi1}},{{LocalTracking}}]""")));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<SchemaOrder>("Shop").CreateTable().IfNotExists().SendAsync();

        // Assert
        await Assert.That(result.Drift.Single().Description).StartsWith("The table Shop has the index ByTracking as a local index over the partition key bytrackingpk (String) and no sort key, and the entities declare a global index ByTracking");
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndDrift_LogsItOnceAsAWarningOfTheClient()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described($$"""{{OrderKey}},{{OrderAttributes}},"LocalSecondaryIndexes":[{{Lsi1}}]""")));
        using var logs = new CapturingLoggerFactory();
        using var client = TestClient.Create(handler, loggerFactory: logs);

        // Act
        await client.Table<SchemaOrder>("Shop").CreateTable().IfNotExists().SendAsync();

        // Assert
        var entry = logs.Entries.Single(static entry => entry.EventId.Id == 14);
        await Assert.That((entry.Category, entry.Level, entry.Message)).IsEqualTo((
            "DynaSharp.DynamoDbClient",
            LogLevel.Warning,
            "DynamoDB table Shop does not match what CreateTable() declares (2 in CreateTableResult.Drift), and is left as it is: "
            + "The table Shop lacks the global index GSI1 over the partition key gsi1pk (String) and sort key gsi1sk (String). Queries of it fail until UpdateTable adds it. "
            + "The table Shop lacks the global index ByTracking over the partition key bytrackingpk (String) and no sort key. Queries of it fail until UpdateTable adds it."));
    }

    [Test]
    [Arguments("", "no stream")]
    [Arguments(""","StreamSpecification":{"StreamEnabled":false}""", "no stream")]
    [Arguments(""","StreamSpecification":{"StreamEnabled":true,"StreamViewType":"KEYS_ONLY"}""", "a stream of KeysOnly")]
    public async Task SendAsync_StreamAndIfNotExistsOverAnotherStream_ReportsIt(string stream, string has)
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described($$"""{{OrderKey}}{{stream}}""")));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk", "sk").CreateTable().Stream(StreamViewType.NewAndOldImages).IfNotExists().SendAsync();

        // Assert
        await Assert.That(result.Drift.Single()).IsEqualTo(new TableDrift(
            TableDriftKind.StreamDiffers,
            null,
            $"The table Shop has {has}, and the command asks for one of NewAndOldImages. UpdateTable with a StreamSpecification changes it."));
    }

    [Test]
    public async Task SendAsync_StreamAndIfNotExistsOverThatStream_ReportsNoDrift()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described($$"""{{OrderKey}},"StreamSpecification":{"StreamEnabled":true,"StreamViewType":"NEW_AND_OLD_IMAGES"}""")));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk", "sk").CreateTable().Stream(StreamViewType.NewAndOldImages).IfNotExists().SendAsync();

        // Assert
        await Assert.That(result.Drift.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_StreamAndIfNotExistsOverADescriptionWithoutAKeySchema_ComparesNothing()
    {
        // Arrange (DynamoDB always sends a key schema; a description without one says nothing of the table)
        using var handler = new StubHandler(_ => StubResponse.Json(Active));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<SchemaOrder>("Shop").CreateTable().Stream(StreamViewType.KeysOnly).IfNotExists().SendAsync();

        // Assert
        await Assert.That(result.Drift.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndAnotherProcessCreatedATableOfAnotherKeyFirst_Throws()
    {
        // Arrange (the table the create loop finds is checked as the first look would have)
        using var handler = new StubHandler();
        handler.Enqueue(NotFound());
        handler.Enqueue(StubResponse.DynamoDbError("ResourceInUseException", "Table already exists."));
        handler.Enqueue(StubResponse.Json(Described("""
            "KeySchema":[{"AttributeName":"id","KeyType":"HASH"}]
            """)));
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").CreateTable().IfNotExists();

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(async () => await command.SendAsync());

        // Assert
        await Assert.That(failure.Message).StartsWith("The table Shop exists with the partition key id and no sort key, and this command expects the partition key pk and no sort key. ");
    }

    [Test]
    public async Task SendAsync_ATableThisCallCreated_ReportsNoDrift()
    {
        // Arrange (CreateTable answers with what it was asked for, and nothing compares it)
        using var handler = new StubHandler(_ => StubResponse.Json(Created));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<SchemaOrder>("Shop").CreateTable().Stream(StreamViewType.KeysOnly).SendAsync();

        // Assert
        await Assert.That((result.Created, result.Drift.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task SendAsync_IfNotExistsOverAFakeTableOfTheEntities_ReportsNoDrift()
    {
        // Arrange (the description of the fake holds the key schema, the types, the indexes and the stream that the check reads)
        var fake = new FakeDynamoDb();
        using var client = fake.CreateClient();
        var command = client.Table<SchemaOrder>("Shop").CreateTable().Including<SchemaCustomer>().Stream(StreamViewType.NewImage);
        fake.CreateTable(command.ToRequest());

        // Act
        var result = await command.IfNotExists().SendAsync();

        // Assert
        await Assert.That((result.Created, result.Drift.Count)).IsEqualTo((false, 0));
    }

    [Test]
    public async Task SendAsync_IfNotExistsOverAFakeTableWithAnotherKey_ThrowsAndChangesNothing()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable("Locks", "Resource");
        using var client = fake.CreateClient();
        var command = client.Table<SchemaOrder>("Locks").CreateTable().IfNotExists().TimeToLive("expires");

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(async () => await command.SendAsync());

        // Assert
        await Assert.That((failure.Message.StartsWith("The table Locks exists with the partition key Resource (String) and no sort key, and this command expects the partition key pk (String) and sort key sk (String).", StringComparison.Ordinal), fake.DescribeTable("Locks").KeySchema.Single().AttributeName))
            .IsEqualTo((true, "Resource"));
    }

    [Test]
    public async Task SendAsync_IfNotExistsOverAFakeTableOfAnOlderEntity_ReportsTheIndexesItLacks()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable("Shop", "pk", "sk");
        using var client = fake.CreateClient();

        // Act
        var result = await client.Table("Shop", "pk", "sk").CreateTable().Including<SchemaCustomer>().IfNotExists().SendAsync();

        // Assert
        await Assert.That(string.Join(",", result.Drift.Select(static drift => $"{drift.Kind}:{drift.IndexName}"))).IsEqualTo("IndexMissing:GSI1,IndexMissing:ByEmail");
    }

    [Test]
    public async Task SendAsync_IfNotExistsOverAFakeTableWithTimeToLiveOnAnotherAttribute_ReportsItAndLeavesItOn()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable("Shop", "pk", "sk");
        using var client = fake.CreateClient();
        var shop = client.Table("Shop", "pk", "sk");
        await shop.CreateTable().IfNotExists().TimeToLive("other").SendAsync();

        // Act
        var result = await shop.CreateTable().IfNotExists().TimeToLive("expires").SendAsync();
        var timeToLive = await client.DescribeTimeToLiveAsync(new DescribeTimeToLiveRequest { TableName = "Shop" });

        // Assert
        await Assert.That((result.Drift.Single().Kind, timeToLive.TimeToLiveDescription?.AttributeName)).IsEqualTo((TableDriftKind.TimeToLiveDiffers, (string?)"other"));
    }

    private static string Described(string members) => """{"Table":{"TableName":"Shop","TableStatus":"ACTIVE",""" + members + "}}";
}
