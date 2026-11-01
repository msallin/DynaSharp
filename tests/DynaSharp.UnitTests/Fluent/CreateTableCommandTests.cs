using DynaSharp.Requests;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Operations;
using DynaSharp.UnitTests.Telemetry;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>The expected bodies are written by hand from the entities in <c>SchemaEntities.cs</c>; <see cref="CommandVectorTests"/> holds them against botocore.</summary>
public sealed class CreateTableCommandTests
{
    private const string Created = """{"TableDescription":{"TableName":"Shop","TableStatus":"CREATING"}}""";
    private const string Active = """{"Table":{"TableName":"Shop","TableStatus":"ACTIVE"}}""";
    private const string Creating = """{"Table":{"TableName":"Shop","TableStatus":"CREATING"}}""";
    private const string Deleting = """{"Table":{"TableName":"Shop","TableStatus":"DELETING"}}""";

    private static readonly TimeSpan Forever = TimeSpan.FromDays(30);

    [Test]
    public async Task SendAsync_OneEntity_CreatesWhatItDeclaresOnDemandWithAllAttributesProjected()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Created));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table<SchemaOrder>("Shop").CreateTable().SendAsync();

        // Assert
        const string Expected = """
            {
              "TableName": "Shop",
              "AttributeDefinitions": [
                {"AttributeName": "pk", "AttributeType": "S"},
                {"AttributeName": "sk", "AttributeType": "S"},
                {"AttributeName": "gsi1pk", "AttributeType": "S"},
                {"AttributeName": "gsi1sk", "AttributeType": "S"},
                {"AttributeName": "bytrackingpk", "AttributeType": "S"},
                {"AttributeName": "lsi1sk", "AttributeType": "S"}
              ],
              "KeySchema": [{"AttributeName": "pk", "KeyType": "HASH"}, {"AttributeName": "sk", "KeyType": "RANGE"}],
              "GlobalSecondaryIndexes": [
                {
                  "IndexName": "GSI1",
                  "KeySchema": [{"AttributeName": "gsi1pk", "KeyType": "HASH"}, {"AttributeName": "gsi1sk", "KeyType": "RANGE"}],
                  "Projection": {"ProjectionType": "ALL"}
                },
                {
                  "IndexName": "ByTracking",
                  "KeySchema": [{"AttributeName": "bytrackingpk", "KeyType": "HASH"}],
                  "Projection": {"ProjectionType": "ALL"}
                }
              ],
              "LocalSecondaryIndexes": [
                {
                  "IndexName": "LSI1",
                  "KeySchema": [{"AttributeName": "pk", "KeyType": "HASH"}, {"AttributeName": "lsi1sk", "KeyType": "RANGE"}],
                  "Projection": {"ProjectionType": "ALL"}
                }
              ],
              "BillingMode": "PAY_PER_REQUEST"
            }
            """;
        await Assert.That((handler.Requests.Single().Operation, JsonComparer.Differences(Expected, handler.Requests.Single().BodyText))).IsEqualTo(("CreateTable", string.Empty));
    }

    [Test]
    public async Task SendAsync_ThreeEntitiesOverloadingAnIndex_CreatesTheUnionWithEachIndexOnce()
    {
        // Arrange (GSI1 has three templates over one pair of attributes, and each entity brings an index of its own)
        using var handler = new StubHandler(_ => StubResponse.Json(Created));
        using var client = TestClient.Create(handler);
        var orders = client.Table<SchemaOrder>("Shop");

        // Act
        await orders.CreateTable().Including<SchemaCustomer>().Including<SchemaProduct>().SendAsync();

        // Assert
        const string Expected = """
            {
              "TableName": "Shop",
              "AttributeDefinitions": [
                {"AttributeName": "pk", "AttributeType": "S"},
                {"AttributeName": "sk", "AttributeType": "S"},
                {"AttributeName": "gsi1pk", "AttributeType": "S"},
                {"AttributeName": "gsi1sk", "AttributeType": "S"},
                {"AttributeName": "bytrackingpk", "AttributeType": "S"},
                {"AttributeName": "lsi1sk", "AttributeType": "S"},
                {"AttributeName": "byemailpk", "AttributeType": "S"},
                {"AttributeName": "Category", "AttributeType": "S"},
                {"AttributeName": "Stock", "AttributeType": "N"}
              ],
              "KeySchema": [{"AttributeName": "pk", "KeyType": "HASH"}, {"AttributeName": "sk", "KeyType": "RANGE"}],
              "GlobalSecondaryIndexes": [
                {
                  "IndexName": "GSI1",
                  "KeySchema": [{"AttributeName": "gsi1pk", "KeyType": "HASH"}, {"AttributeName": "gsi1sk", "KeyType": "RANGE"}],
                  "Projection": {"ProjectionType": "ALL"}
                },
                {
                  "IndexName": "ByTracking",
                  "KeySchema": [{"AttributeName": "bytrackingpk", "KeyType": "HASH"}],
                  "Projection": {"ProjectionType": "ALL"}
                },
                {
                  "IndexName": "ByEmail",
                  "KeySchema": [{"AttributeName": "byemailpk", "KeyType": "HASH"}],
                  "Projection": {"ProjectionType": "ALL"}
                },
                {
                  "IndexName": "ByStock",
                  "KeySchema": [{"AttributeName": "Category", "KeyType": "HASH"}, {"AttributeName": "Stock", "KeyType": "RANGE"}],
                  "Projection": {"ProjectionType": "ALL"}
                }
              ],
              "LocalSecondaryIndexes": [
                {
                  "IndexName": "LSI1",
                  "KeySchema": [{"AttributeName": "pk", "KeyType": "HASH"}, {"AttributeName": "lsi1sk", "KeyType": "RANGE"}],
                  "Projection": {"ProjectionType": "ALL"}
                }
              ],
              "BillingMode": "PAY_PER_REQUEST"
            }
            """;
        await Assert.That(JsonComparer.Differences(Expected, handler.Requests.Single().BodyText)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SendAsync_Provisioned_SetsCapacityOnTheTableAndEachGlobalIndexButNotOnALocalOne()
    {
        // Arrange (DynamoDB refuses a global index without capacity of its own on such a table, and a local index with any)
        using var handler = new StubHandler(_ => StubResponse.Json(Created));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table<SchemaOrder>("Shop").CreateTable().Provisioned(read: 5, write: 7).SendAsync();

        // Assert
        const string Expected = """
            {
              "TableName": "Shop",
              "AttributeDefinitions": [
                {"AttributeName": "pk", "AttributeType": "S"},
                {"AttributeName": "sk", "AttributeType": "S"},
                {"AttributeName": "gsi1pk", "AttributeType": "S"},
                {"AttributeName": "gsi1sk", "AttributeType": "S"},
                {"AttributeName": "bytrackingpk", "AttributeType": "S"},
                {"AttributeName": "lsi1sk", "AttributeType": "S"}
              ],
              "KeySchema": [{"AttributeName": "pk", "KeyType": "HASH"}, {"AttributeName": "sk", "KeyType": "RANGE"}],
              "GlobalSecondaryIndexes": [
                {
                  "IndexName": "GSI1",
                  "KeySchema": [{"AttributeName": "gsi1pk", "KeyType": "HASH"}, {"AttributeName": "gsi1sk", "KeyType": "RANGE"}],
                  "Projection": {"ProjectionType": "ALL"},
                  "ProvisionedThroughput": {"ReadCapacityUnits": 5, "WriteCapacityUnits": 7}
                },
                {
                  "IndexName": "ByTracking",
                  "KeySchema": [{"AttributeName": "bytrackingpk", "KeyType": "HASH"}],
                  "Projection": {"ProjectionType": "ALL"},
                  "ProvisionedThroughput": {"ReadCapacityUnits": 5, "WriteCapacityUnits": 7}
                }
              ],
              "LocalSecondaryIndexes": [
                {
                  "IndexName": "LSI1",
                  "KeySchema": [{"AttributeName": "pk", "KeyType": "HASH"}, {"AttributeName": "lsi1sk", "KeyType": "RANGE"}],
                  "Projection": {"ProjectionType": "ALL"}
                }
              ],
              "BillingMode": "PROVISIONED",
              "ProvisionedThroughput": {"ReadCapacityUnits": 5, "WriteCapacityUnits": 7}
            }
            """;
        await Assert.That(JsonComparer.Differences(Expected, handler.Requests.Single().BodyText)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SendAsync_TableWithoutAnEntity_CreatesItsKeysAsStrings()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Created));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk", "sk").CreateTable().SendAsync();

        // Assert
        const string Expected = """
            {
              "TableName": "Shop",
              "AttributeDefinitions": [{"AttributeName": "pk", "AttributeType": "S"}, {"AttributeName": "sk", "AttributeType": "S"}],
              "KeySchema": [{"AttributeName": "pk", "KeyType": "HASH"}, {"AttributeName": "sk", "KeyType": "RANGE"}],
              "BillingMode": "PAY_PER_REQUEST"
            }
            """;
        await Assert.That(JsonComparer.Differences(Expected, handler.Requests.Single().BodyText)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SendAsync_TableWithoutASortKey_HasOneKeyAttribute()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Created));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "id").CreateTable().SendAsync();

        // Assert
        const string Expected = """
            {
              "TableName": "Shop",
              "AttributeDefinitions": [{"AttributeName": "id", "AttributeType": "S"}],
              "KeySchema": [{"AttributeName": "id", "KeyType": "HASH"}],
              "BillingMode": "PAY_PER_REQUEST"
            }
            """;
        await Assert.That(JsonComparer.Differences(Expected, handler.Requests.Single().BodyText)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SendAsync_TableWithoutAnEntityIncludingOne_TakesTheIndexesAndTheTypesOfTheEntity()
    {
        // Arrange (the strings of an untyped table are a default, so a number and a binary key are no conflict)
        using var handler = new StubHandler(_ => StubResponse.Json(Created));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Readings", "Sensor", "Stamp").CreateTable().Including<SchemaReading>().SendAsync();

        // Assert
        const string Expected = """
            {
              "TableName": "Readings",
              "AttributeDefinitions": [
                {"AttributeName": "Sensor", "AttributeType": "N"},
                {"AttributeName": "Stamp", "AttributeType": "B"},
                {"AttributeName": "ExpiresAt", "AttributeType": "N"}
              ],
              "KeySchema": [{"AttributeName": "Sensor", "KeyType": "HASH"}, {"AttributeName": "Stamp", "KeyType": "RANGE"}],
              "LocalSecondaryIndexes": [
                {
                  "IndexName": "ByExpiry",
                  "KeySchema": [{"AttributeName": "Sensor", "KeyType": "HASH"}, {"AttributeName": "ExpiresAt", "KeyType": "RANGE"}],
                  "Projection": {"ProjectionType": "ALL"}
                }
              ],
              "BillingMode": "PAY_PER_REQUEST"
            }
            """;
        await Assert.That(JsonComparer.Differences(Expected, handler.Requests.Single().BodyText)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ToRequest_ThreeEntitiesInAnyOrder_DeclaresTheSameSchema()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var first = client.Table<SchemaOrder>("Shop").CreateTable().Including<SchemaCustomer>().Including<SchemaProduct>().ToRequest();
        var second = client.Table<SchemaProduct>("Shop").CreateTable().Including<SchemaCustomer>().Including<SchemaOrder>().ToRequest();

        // Assert
        await Assert.That(Names(second)).IsEqualTo(Names(first));
    }

    [Test]
    public async Task Including_TheSameEntityAgain_ChangesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var orders = client.Table<SchemaOrder>("Shop");

        // Act
        var request = orders.CreateTable().Including<SchemaOrder>().Including<SchemaCustomer>().Including<SchemaCustomer>().ToRequest();

        // Assert
        await Assert.That(Names(request)).IsEqualTo(Names(orders.CreateTable().Including<SchemaCustomer>().ToRequest()));
    }

    [Test]
    public async Task Including_ViewOfAnUntypedTable_StartsFromTheEntityOfTheView()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var request = client.Table("Shop", "pk", "sk").Of<SchemaCustomer>().CreateTable().ToRequest();

        // Assert
        await Assert.That(string.Join(",", request.GlobalSecondaryIndexes!.Select(static index => index.IndexName))).IsEqualTo("GSI1,ByEmail");
    }

    [Test]
    public async Task Including_HandWrittenEntityWithoutTypes_CountsItsAttributesAsStringsAndItsIndexesAsGlobal()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var request = client.Table("Shop", "pk", "sk").CreateTable().Including<SchemaByHand>().ToRequest();

        // Assert
        await Assert.That((Names(request), request.LocalSecondaryIndexes!.Count)).IsEqualTo(("gsi1pk:String gsi1sk:String pk:String sk:String | ByHand GSI1", 0));
    }

    [Test]
    public async Task Including_HandWrittenEntityWithoutTypes_NeverConflictsWithAnEntityThatKnows()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var after = client.Table<SchemaReading>("Readings").CreateTable().Including<SchemaReadingByHand>().ToRequest();
        var before = client.Table("Readings", "Sensor", "Stamp").CreateTable().Including<SchemaReadingByHand>().Including<SchemaReading>().ToRequest();

        // Assert
        await Assert.That((Names(after), Names(before))).IsEqualTo(("ExpiresAt:Number Sensor:Number Stamp:Binary | ByExpiry", "ExpiresAt:Number Sensor:Number Stamp:Binary | ByExpiry"));
    }

    [Test]
    public async Task Including_AttributeOfAnIndexWithTwoTypes_ThrowsNamingBothAndTheAttribute()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table<SchemaOrder>("Shop").CreateTable();

        // Act
        var failure = TestClient.Catch(() => command.Including<SchemaRanked>());

        // Assert
        await Assert.That((failure.GetType(), failure.Message, handler.Requests.Count))
            .IsEqualTo((typeof(ArgumentException), $"{typeof(SchemaOrder)} stores the attribute gsi1sk as String, and {typeof(SchemaRanked)} stores it as Number.", 0));
    }

    [Test]
    public async Task Including_SortKeyOfTheTableWithAnotherType_ThrowsNamingBoth()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table<SchemaCustomer>("Shop").CreateTable();

        // Act
        var failure = TestClient.Catch(() => command.Including<SchemaNumberedSort>());

        // Assert
        await Assert.That((failure.GetType(), failure.Message)).IsEqualTo((typeof(ArgumentException), $"{typeof(SchemaCustomer)} stores the attribute sk as String, and {typeof(SchemaNumberedSort)} stores it as Number."));
    }

    [Test]
    public async Task Including_TheOtherWayRound_ThrowsNamingBoth()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table<SchemaRanked>("Shop").CreateTable();

        // Act
        var failure = TestClient.Catch(() => command.Including<SchemaOrder>());

        // Assert
        await Assert.That(failure.Message).IsEqualTo($"{typeof(SchemaRanked)} stores the attribute gsi1sk as Number, and {typeof(SchemaOrder)} stores it as String.");
    }

    [Test]
    public async Task Including_OtherNameForThePartitionKey_ThrowsNamingBoth()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table<SchemaOrder>("Shop").CreateTable();

        // Act
        var failure = TestClient.Catch(() => command.Including<SchemaRenamedKey>());

        // Assert
        await Assert.That((failure.GetType(), failure.Message)).IsEqualTo((typeof(ArgumentException), $"{typeof(SchemaOrder)} has the key (pk, sk), and {typeof(SchemaRenamedKey)} has (id, sk), so they cannot share a table."));
    }

    [Test]
    public async Task Including_EntityWithoutTheSortKeyOfTheTable_ThrowsNamingBoth()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table<SchemaOrder>("Shop").CreateTable();

        // Act
        var failure = TestClient.Catch(() => command.Including<SchemaFlat>());

        // Assert
        await Assert.That((failure.GetType(), failure.Message)).IsEqualTo((typeof(ArgumentException), $"{typeof(SchemaOrder)} has the key (pk, sk), and {typeof(SchemaFlat)} has (pk), so they cannot share a table."));
    }

    [Test]
    public async Task Including_TableOpenedWithOtherKeys_ThrowsNamingTheTable()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").CreateTable();

        // Act
        var failure = TestClient.Catch(() => command.Including<SchemaOrder>());

        // Assert
        await Assert.That((failure.GetType(), failure.Message)).IsEqualTo((typeof(ArgumentException), $"The table Shop has the key (pk), and {typeof(SchemaOrder)} has (pk, sk), so they cannot share a table."));
    }

    [Test]
    public async Task Including_IndexWithoutTheSortKeyOfTheOther_ThrowsNamingBothAndTheIndex()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table<SchemaOrder>("Shop").CreateTable();

        // Act
        var failure = TestClient.Catch(() => command.Including<SchemaHalfIndex>());

        // Assert
        await Assert.That((failure.GetType(), failure.Message))
            .IsEqualTo((typeof(ArgumentException), $"{typeof(SchemaOrder)} declares the index GSI1 as global over (gsi1pk, gsi1sk), and {typeof(SchemaHalfIndex)} as global over (gsi1pk)."));
    }

    [Test]
    public async Task Including_IndexOverAnotherAttribute_ThrowsNamingBothAndTheIndex()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table<SchemaOrder>("Shop").CreateTable();

        // Act
        var failure = TestClient.Catch(() => command.Including<SchemaOtherIndex>());

        // Assert
        await Assert.That((failure.GetType(), failure.Message))
            .IsEqualTo((typeof(ArgumentException), $"{typeof(SchemaOrder)} declares the index GSI1 as global over (gsi1pk, gsi1sk), and {typeof(SchemaOtherIndex)} as global over (gsi1pk, other)."));
    }

    [Test]
    public async Task Including_LocalIndexDeclaredAsGlobal_ThrowsNamingBoth()
    {
        // Arrange (the same attributes, and still two different indexes)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table<SchemaOrder>("Shop").CreateTable();

        // Act
        var failure = TestClient.Catch(() => command.Including<SchemaGlobalLocalIndex>());

        // Assert
        await Assert.That((failure.GetType(), failure.Message))
            .IsEqualTo((typeof(ArgumentException), $"{typeof(SchemaOrder)} declares the index LSI1 as local over (pk, lsi1sk), and {typeof(SchemaGlobalLocalIndex)} as global over (pk, lsi1sk)."));
    }

    [Test]
    public async Task SendAsync_NoWait_ReturnsTheNewDescriptionAfterOneRequest()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Created));
        using var client = TestClient.Create(handler);

        // Act
        var description = await client.Table("Shop", "pk").CreateTable().SendAsync();

        // Assert
        await Assert.That((description.TableStatus, handler.Requests.Count)).IsEqualTo((TableStatus.Creating, 1));
    }

    [Test]
    public async Task SendAsync_Twice_SendsTwice()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Created));
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").CreateTable();

        // Act
        await command.SendAsync();
        await command.SendAsync();

        // Assert
        await Assert.That(string.Join(",", handler.Requests.Select(static request => request.Operation))).IsEqualTo("CreateTable,CreateTable");
    }

    [Test]
    public async Task GetAwaiter_Awaited_SendsTheCommand()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Created));
        using var client = TestClient.Create(handler);

        // Act
        var description = await client.Table("Shop", "pk").CreateTable();

        // Assert
        await Assert.That((description.TableName, handler.Requests.Single().Operation)).IsEqualTo(("Shop", "CreateTable"));
    }

    [Test]
    public async Task SendAsync_WaitUntilActive_PollsOnTheClockOfTheClientAndReturnsTheActiveTable()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Created));
        handler.Enqueue(StubResponse.Json(Creating));
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler, time);

        // Act
        var waiting = client.Table("Shop", "pk").CreateTable().WaitUntilActive().SendAsync().AsTask();
        var delays = await AdvanceThroughDelaysAsync(waiting, time, Forever);

        // Assert
        await Assert.That(((await waiting).TableStatus, string.Join(",", handler.Requests.Select(static request => request.Operation)), delays.Single())).IsEqualTo((TableStatus.Active, "CreateTable,DescribeTable,DescribeTable", TimeSpan.FromMilliseconds(100)));
    }

    [Test]
    public async Task SendAsync_WaitUntilActiveWithoutATimeout_GivesUpAfterFiveMinutes()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var started = time.GetUtcNow();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Created));
        handler.DefaultResponse = StubResponse.Json(Creating);
        using var client = TestClient.Create(handler, time);

        // Act
        var waiting = client.Table("Shop", "pk").CreateTable().WaitUntilActive().SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(waiting, time, Forever);
        await TestClient.CaptureAsync<TimeoutException>(() => waiting);

        // Assert
        await Assert.That(time.GetUtcNow() - started).IsEqualTo(TimeSpan.FromMinutes(5));
    }

    [Test]
    public async Task SendAsync_WaitUntilActiveWithATimeout_GivesUpAtIt()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var started = time.GetUtcNow();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Created));
        handler.DefaultResponse = StubResponse.Json(Creating);
        using var client = TestClient.Create(handler, time);

        // Act
        var waiting = client.Table("Shop", "pk").CreateTable().WaitUntilActive(TimeSpan.FromSeconds(1)).SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(waiting, time, Forever);
        await TestClient.CaptureAsync<TimeoutException>(() => waiting);

        // Assert
        await Assert.That(time.GetUtcNow() - started).IsEqualTo(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task SendAsync_WaitUntilActiveWithZero_LooksOnceAfterCreating()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Created));
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler, new ManualTimeProvider());

        // Act
        var description = await client.Table("Shop", "pk").CreateTable().WaitUntilActive(TimeSpan.Zero).SendAsync();

        // Assert
        await Assert.That((description.TableStatus, handler.Requests.Count)).IsEqualTo((TableStatus.Active, 2));
    }

    [Test]
    public async Task WaitUntilActive_NegativeTimeout_ThrowsAtTheClauseBeforeAnyTableExists()
    {
        // Arrange (at SendAsync it would throw with the table already created)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").CreateTable();

        // Act
        var failure = TestClient.Catch(() => command.WaitUntilActive(Timeout.InfiniteTimeSpan));

        // Assert
        await Assert.That((((ArgumentOutOfRangeException)failure).ParamName, handler.Requests.Count)).IsEqualTo(("timeout", 0));
    }

    [Test]
    public async Task SendAsync_TableExists_ThrowsResourceInUse()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceInUseException", "Table already exists."));
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").CreateTable();

        // Act
        await TestClient.CaptureAsync<ResourceInUseException>(async () => await command.SendAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndTheTableExists_ReturnsItsDescription()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("ResourceInUseException", "Table already exists."));
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler);

        // Act
        var description = await client.Table("Shop", "pk").CreateTable().IfNotExists().SendAsync();

        // Assert
        await Assert.That((description.TableStatus, string.Join(",", handler.Requests.Select(static request => request.Operation)), handler.Requests[1].BodyText))
            .IsEqualTo((TableStatus.Active, "CreateTable,DescribeTable", """{"TableName":"Shop"}"""));
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndTheTableIsNew_DescribesNothing()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Created));
        using var client = TestClient.Create(handler);

        // Act
        var description = await client.Table("Shop", "pk").CreateTable().IfNotExists().SendAsync();

        // Assert
        await Assert.That((description.TableStatus, handler.Requests.Count)).IsEqualTo((TableStatus.Creating, 1));
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndTheTableIsDeleting_ReturnsThatStatus()
    {
        // Arrange (the schema and the status of what exists are not compared, as the docs say)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("ResourceInUseException", "Table is being deleted."));
        handler.Enqueue(StubResponse.Json(Deleting));
        using var client = TestClient.Create(handler);

        // Act
        var description = await client.Table("Shop", "pk").CreateTable().IfNotExists().SendAsync();

        // Assert
        await Assert.That(description.TableStatus).IsEqualTo(TableStatus.Deleting);
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndWaitUntilActive_WaitsForTheExistingTableWithoutAnExtraLook()
    {
        // Arrange (DescribeTable can miss a table for a moment, which a wait bears with and a plain describe would not)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("ResourceInUseException", "Table already exists."));
        handler.Enqueue(StubResponse.DynamoDbError("ResourceNotFoundException", "Not there yet."));
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler, time);

        // Act
        var waiting = client.Table("Shop", "pk").CreateTable().WaitUntilActive().IfNotExists().SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(waiting, time, Forever);

        // Assert
        await Assert.That(((await waiting).TableStatus, handler.Requests.Count)).IsEqualTo((TableStatus.Active, 3));
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndAnotherError_Surfaces()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("LimitExceededException", "Too many."));
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").CreateTable().IfNotExists();

        // Act
        await TestClient.CaptureAsync<LimitExceededException>(async () => await command.SendAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_RetryOfACreateWhoseResponseWasLost_IsSuccessWithIfNotExists()
    {
        // Arrange (the first attempt created the table, and the second finds it)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Lost.", System.Net.HttpStatusCode.InternalServerError));
        handler.Enqueue(StubResponse.DynamoDbError("ResourceInUseException", "Table already exists."));
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler, new ManualTimeProvider(), maxAttempts: 2, random: new FixedRandom(0));

        // Act
        var description = await client.Table("Shop", "pk").CreateTable().IfNotExists().SendAsync();

        // Assert
        await Assert.That((description.TableStatus, handler.Requests.Count)).IsEqualTo((TableStatus.Active, 3));
    }

    [Test]
    public async Task ToRequest_Clauses_SendsNothingAndCarriesOnlyWhatIsInTheRequest()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var request = client.Table("Shop", "pk").CreateTable().Provisioned(read: 1, write: 2).WaitUntilActive().IfNotExists().ToRequest();

        // Assert
        await Assert.That((request.TableName, request.BillingMode, request.ProvisionedThroughput?.ReadCapacityUnits, request.ProvisionedThroughput?.WriteCapacityUnits, handler.Requests.Count))
            .IsEqualTo(("Shop", BillingMode.Provisioned, (long?)1, (long?)2, 0));
    }

    [Test]
    public async Task ToRequest_ChangedWithWith_GoesThroughTheRequestApi()
    {
        // Arrange (the escape hatch: streams, tags and the rest of what the command does not model)
        using var handler = new StubHandler(_ => StubResponse.Json(Created));
        using var client = TestClient.Create(handler);
        var request = client.Table("Shop", "pk").CreateTable().ToRequest();

        // Act
        await client.CreateTableAsync(request with { Tags = [new TableTag { Key = "team", Value = "shop" }] });

        // Assert
        const string Expected = """
            {
              "TableName": "Shop",
              "AttributeDefinitions": [{"AttributeName": "pk", "AttributeType": "S"}],
              "KeySchema": [{"AttributeName": "pk", "KeyType": "HASH"}],
              "BillingMode": "PAY_PER_REQUEST",
              "Tags": [{"Key": "team", "Value": "shop"}]
            }
            """;
        await Assert.That(JsonComparer.Differences(Expected, handler.Requests.Single().BodyText)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SendAsync_DisposedClient_ThrowsNamingTheClient()
    {
        // Arrange
        using var handler = new StubHandler();
        var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").CreateTable();
        client.Dispose();

        // Act
        var failure = await TestClient.CaptureAsync<ObjectDisposedException>(async () => await command.SendAsync());

        // Assert
        await Assert.That((failure.ObjectName, handler.Requests.Count)).IsEqualTo((typeof(DynamoDbClient).FullName!, 0));
    }

    [Test]
    public async Task SendAsync_Cancelled_SendsNothing()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").CreateTable();

        // Act
        await TestClient.CaptureAsync<OperationCanceledException>(async () => await command.SendAsync(cancellation.Token));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_AnyTable_HasTheSpanOfTheOperation()
    {
        // Arrange
        var table = TestClient.UniqueTable();
        using var capture = new ActivityCapture();
        using var handler = new StubHandler(_ => StubResponse.Json(Created));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table(table, "pk").CreateTable().SendAsync();

        // Assert
        var span = capture.All.Single();
        await Assert.That((span.OperationName, span.GetTagItem("aws.dynamodb.table_names") is string[] names && names.SequenceEqual([table]))).IsEqualTo(("DynamoDB.CreateTable", true));
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndTheTableExists_HasOneSpanPerRequest()
    {
        // Arrange
        using var capture = new ActivityCapture();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("ResourceInUseException", "Table already exists."));
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk").CreateTable().IfNotExists().SendAsync();

        // Assert
        await Assert.That(string.Join(",", capture.All.Select(static span => span.OperationName))).IsEqualTo("DynamoDB.CreateTable,DynamoDB.DescribeTable");
    }

    private static string Names(CreateTableRequest request)
    {
        var attributes = string.Join(" ", request.AttributeDefinitions.Select(static attribute => $"{attribute.AttributeName}:{attribute.AttributeType}").Order(StringComparer.Ordinal));
        var indexes = (request.GlobalSecondaryIndexes ?? []).Select(static index => index.IndexName).Concat((request.LocalSecondaryIndexes ?? []).Select(static index => index.IndexName));
        return $"{attributes} | {string.Join(" ", indexes.Order(StringComparer.Ordinal))}";
    }
}
