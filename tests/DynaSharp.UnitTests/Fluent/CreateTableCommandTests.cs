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
    private const string Disabled = """{"TimeToLiveDescription":{"TimeToLiveStatus":"DISABLED"}}""";
    private const string Updated = """{"TimeToLiveSpecification":{"Enabled":true,"AttributeName":"expires"}}""";
    private const string AllOfTheOrderIndexes = """{"Table":{"TableName":"Shop","TableStatus":"ACTIVE","GlobalSecondaryIndexes":[{"IndexName":"GSI1","IndexStatus":"ACTIVE"},{"IndexName":"ByTracking","IndexStatus":"ACTIVE"}]}}""";

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
    public async Task ToRequestJson_ProvisionedWithAStream_RendersTheBodySendAsyncSends()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Created));
        using var client = TestClient.Create(handler);
        var create = client.Table<SchemaOrder>("Shop").CreateTable().Including<SchemaCustomer>().Provisioned(read: 5, write: 3).Stream(StreamViewType.NewImage);

        // Act (rendering leaves the command usable)
        var json = create.ToRequestJson();
        await create.SendAsync();

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).IsEqualTo(json);
    }

    [Test]
    public async Task ToRequestJson_IfNotExistsAndTimeToLive_RendersTheCreateAloneAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var create = client.Table("Shop", "pk", "sk").CreateTable().IfNotExists().TimeToLive("expires").WaitUntilActive();

        // Act
        var json = create.ToRequestJson();

        // Assert
        const string Expected = """
            {
              "AttributeDefinitions": [{"AttributeName": "pk", "AttributeType": "S"}, {"AttributeName": "sk", "AttributeType": "S"}],
              "TableName": "Shop",
              "KeySchema": [{"AttributeName": "pk", "KeyType": "HASH"}, {"AttributeName": "sk", "KeyType": "RANGE"}],
              "BillingMode": "PAY_PER_REQUEST"
            }
            """;
        await Assert.That((JsonComparer.Differences(Expected, json), handler.Requests.Count)).IsEqualTo((string.Empty, 0));
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
        var description = (await client.Table("Shop", "pk").CreateTable().SendAsync()).Table;

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
        var result = await client.Table("Shop", "pk").CreateTable();

        // Assert
        await Assert.That((result.Table.TableName, result.Created, handler.Requests.Single().Operation)).IsEqualTo(("Shop", true, "CreateTable"));
    }

    [Test]
    public async Task SendAsync_NewTable_SaysItCreatedItAndTurnedNoTimeToLiveOn()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Created));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").CreateTable().SendAsync();

        // Assert
        await Assert.That((result.Created, result.TimeToLiveEnabled)).IsEqualTo((true, false));
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndTheFirstLookFindsTheTable_SaysItCreatedNothing()
    {
        // Arrange (what a program that creates its table at every start sees from the second start on)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").CreateTable().IfNotExists().SendAsync();

        // Assert
        await Assert.That((result.Created, result.Table.TableStatus)).IsEqualTo((false, TableStatus.Active));
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndAnotherProcessCreatedTheTableFirst_SaysItCreatedNothing()
    {
        // Arrange (two instances start together: the first look of this one finds nothing, and its create loses)
        using var handler = new StubHandler();
        handler.Enqueue(NotFound());
        handler.Enqueue(StubResponse.DynamoDbError("ResourceInUseException", "Table already exists."));
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").CreateTable().IfNotExists().SendAsync();

        // Assert
        await Assert.That(result.Created).IsFalse();
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndNoTable_SaysItCreatedIt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(NotFound());
        handler.Enqueue(StubResponse.Json(Created));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").CreateTable().IfNotExists().SendAsync();

        // Assert
        await Assert.That(result.Created).IsTrue();
    }

    [Test]
    public async Task SendAsync_RetryOfACreateWhoseResponseWasLost_SaysItCreatedNothing()
    {
        // Arrange (the first attempt created the table, and the retry cannot tell it from one of another process, as the docs say)
        using var handler = new StubHandler();
        handler.Enqueue(NotFound());
        handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Lost.", System.Net.HttpStatusCode.InternalServerError));
        handler.Enqueue(StubResponse.DynamoDbError("ResourceInUseException", "Table already exists."));
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler, new ManualTimeProvider(), maxAttempts: 2, random: new FixedRandom(0));

        // Act
        var result = await client.Table("Shop", "pk").CreateTable().IfNotExists().SendAsync();

        // Assert
        await Assert.That(result.Created).IsFalse();
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
        await Assert.That(((await waiting).Table.TableStatus, string.Join(",", handler.Requests.Select(static request => request.Operation)), delays.Single())).IsEqualTo((TableStatus.Active, "CreateTable,DescribeTable,DescribeTable", TimeSpan.FromMilliseconds(100)));
    }

    [Test]
    public async Task SendAsync_WaitUntilActive_WaitsForTheIndexesToo()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Created));
        handler.Enqueue(StubResponse.Json("""{"Table":{"TableName":"Shop","TableStatus":"ACTIVE","GlobalSecondaryIndexes":[{"IndexName":"ByCity","IndexStatus":"CREATING"}]}}"""));
        handler.Enqueue(StubResponse.Json("""{"Table":{"TableName":"Shop","TableStatus":"ACTIVE","GlobalSecondaryIndexes":[{"IndexName":"ByCity","IndexStatus":"ACTIVE"}]}}"""));
        using var client = TestClient.Create(handler, time);

        // Act
        var waiting = client.Table("Shop", "pk").CreateTable().WaitUntilActive().SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(waiting, time, Forever);
        var description = (await waiting).Table;

        // Assert
        await Assert.That((description.GlobalSecondaryIndexes.Single().IndexStatus, handler.Requests.Count)).IsEqualTo((DynaSharp.Requests.IndexStatus.Active, 3));
    }

    [Test]
    public async Task SendAsync_WaitUntilActiveWithAnIndexThatStaysBehind_NamesIt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Created));
        handler.DefaultResponse = StubResponse.Json("""{"Table":{"TableName":"Shop","TableStatus":"ACTIVE","GlobalSecondaryIndexes":[{"IndexName":"ByCity","IndexStatus":"CREATING"}]}}""");
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var command = client.Table("Shop", "pk").CreateTable().WaitUntilActive(TimeSpan.Zero);

        // Act
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(async () => await command.SendAsync());

        // Assert
        await Assert.That((failure.LastStatus, failure.PendingIndex)).IsEqualTo(((TableStatus?)TableStatus.Active, (string?)"ByCity"));
    }

    [Test]
    public async Task SendAsync_WaitUntilActive_WaitsForTheIndexesOfTheEntityToBeListed()
    {
        // Arrange (a local index needs no wait, so LSI1 does not show)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Created));
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json("""{"Table":{"TableStatus":"ACTIVE","GlobalSecondaryIndexes":[{"IndexName":"GSI1","IndexStatus":"ACTIVE"},{"IndexName":"ByTracking","IndexStatus":"ACTIVE"}]}}"""));
        using var client = TestClient.Create(handler, time);

        // Act
        var waiting = client.Table<SchemaOrder>("Shop").CreateTable().WaitUntilActive().SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(waiting, time, Forever);
        var description = (await waiting).Table;

        // Assert
        await Assert.That((description.GlobalSecondaryIndexes.Count, handler.Requests.Count)).IsEqualTo((2, 3));
    }

    [Test]
    public async Task SendAsync_WaitUntilActiveWithAnIncludedEntity_WaitsForItsIndexesToo()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Created));
        handler.DefaultResponse = StubResponse.Json("""{"Table":{"TableStatus":"ACTIVE","GlobalSecondaryIndexes":[{"IndexName":"GSI1","IndexStatus":"ACTIVE"},{"IndexName":"ByTracking","IndexStatus":"ACTIVE"}]}}""");
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var command = client.Table<SchemaOrder>("Shop").CreateTable().Including<SchemaCustomer>().WaitUntilActive(TimeSpan.Zero);

        // Act
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(async () => await command.SendAsync());

        // Assert
        await Assert.That((failure.PendingIndex, failure.Message.Contains("not listed", StringComparison.Ordinal))).IsEqualTo(((string?)"ByEmail", true));
    }

    [Test]
    public async Task SendAsync_WaitUntilActiveOnATableWithoutAnEntityIncludingOne_WaitsForItsIndexes()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Created));
        handler.DefaultResponse = StubResponse.Json(Active);
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var command = client.Table("Shop", "pk", "sk").CreateTable().Including<SchemaCustomer>().WaitUntilActive(TimeSpan.Zero);

        // Act
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(async () => await command.SendAsync());

        // Assert
        await Assert.That(failure.PendingIndex).IsEqualTo("GSI1");
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
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(() => waiting);

        // Assert
        await Assert.That((time.GetUtcNow() - started, failure.TableName, failure.LastStatus)).IsEqualTo((TimeSpan.FromSeconds(1), "Shop", (TableStatus?)TableStatus.Creating));
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
        var description = (await client.Table("Shop", "pk").CreateTable().WaitUntilActive(TimeSpan.Zero).SendAsync()).Table;

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
    public async Task SendAsync_IfNotExistsAndTheTableExists_DescribesItAndCreatesNothing()
    {
        // Arrange (a role without dynamodb:CreateTable gets this far, and no failed call lands in CloudTrail)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler);

        // Act
        var description = (await client.Table("Shop", "pk").CreateTable().IfNotExists().SendAsync()).Table;

        // Assert
        await Assert.That((description.TableStatus, handler.Requests.Single().Operation, handler.Requests.Single().BodyText)).IsEqualTo((TableStatus.Active, "DescribeTable", """{"TableName":"Shop"}"""));
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndACreateFindsTheTableAfterAll_ReturnsItsDescription()
    {
        // Arrange (the first look lagged behind another process)
        using var handler = new StubHandler();
        handler.Enqueue(NotFound());
        handler.Enqueue(StubResponse.DynamoDbError("ResourceInUseException", "Table already exists."));
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler);

        // Act
        var description = (await client.Table("Shop", "pk").CreateTable().IfNotExists().SendAsync()).Table;

        // Assert
        await Assert.That((description.TableStatus, string.Join(",", handler.Requests.Select(static request => request.Operation)), handler.Requests[2].BodyText))
            .IsEqualTo((TableStatus.Active, "DescribeTable,CreateTable,DescribeTable", """{"TableName":"Shop"}"""));
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndAResponseWithoutADescription_CreatesNothing()
    {
        // Arrange (a call that succeeded found the table, whatever the response leaves out)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var description = (await client.Table("Shop", "pk").CreateTable().IfNotExists().SendAsync()).Table;

        // Assert
        await Assert.That((description.TableName, description.TableStatus, handler.Requests.Count)).IsEqualTo(("Shop", TableStatus.Unknown, 1));
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndARoleThatMayNotDescribe_SurfacesAndCreatesNothing()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("AccessDeniedException", "No."));
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").CreateTable().IfNotExists();

        // Act
        await TestClient.CaptureAsync<AccessDeniedException>(async () => await command.SendAsync());

        // Assert
        await Assert.That(handler.Requests.Single().Operation).IsEqualTo("DescribeTable");
    }

    [Test]
    public async Task SendAsync_ResponseWithoutADescription_DescribesTheTable()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler);

        // Act
        var description = (await client.Table("Shop", "pk").CreateTable().SendAsync()).Table;

        // Assert
        await Assert.That((description.TableStatus, string.Join(",", handler.Requests.Select(static request => request.Operation)))).IsEqualTo((TableStatus.Active, "CreateTable,DescribeTable"));
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndTheTableIsNew_LooksOnceAndCreates()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(NotFound());
        handler.Enqueue(StubResponse.Json(Created));
        using var client = TestClient.Create(handler);

        // Act
        var description = (await client.Table("Shop", "pk").CreateTable().IfNotExists().SendAsync()).Table;

        // Assert
        await Assert.That((description.TableStatus, string.Join(",", handler.Requests.Select(static request => request.Operation)))).IsEqualTo((TableStatus.Creating, "DescribeTable,CreateTable"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SendAsync_IfNotExistsAndTheTableIsDeleting_RethrowsResourceInUse(bool wait)
    {
        // Arrange (once the delete is through there is no table, so this is no table that exists)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Deleting));
        handler.Enqueue(StubResponse.DynamoDbError("ResourceInUseException", "Table is being deleted."));
        handler.Enqueue(StubResponse.Json(Deleting));
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").CreateTable().IfNotExists();
        command = wait ? command.WaitUntilActive() : command;

        // Act
        var failure = await TestClient.CaptureAsync<ResourceInUseException>(async () => await command.SendAsync());

        // Assert
        await Assert.That((failure.Message.Contains("being deleted", StringComparison.Ordinal), string.Join(",", handler.Requests.Select(static request => request.Operation)))).IsEqualTo((true, "DescribeTable,CreateTable,DescribeTable"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SendAsync_IfNotExistsAndTheDeleteEndedBetweenTheCalls_CreatesTheTable(bool wait)
    {
        // Arrange (CreateTable saw the table on its way out, and DescribeTable does not find it any more)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Deleting));
        handler.Enqueue(StubResponse.DynamoDbError("ResourceInUseException", "Table is being deleted."));
        handler.Enqueue(StubResponse.DynamoDbError("ResourceNotFoundException", "Gone."));
        handler.Enqueue(StubResponse.Json(Created));
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler, time);
        var command = client.Table("Shop", "pk").CreateTable().IfNotExists();
        command = wait ? command.WaitUntilActive() : command;

        // Act
        var sending = command.SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(sending, time, Forever);

        // Assert
        await Assert.That(((await sending).Table.TableStatus, string.Join(",", handler.Requests.Select(static request => request.Operation))))
            .IsEqualTo((wait ? TableStatus.Active : TableStatus.Creating, wait ? "DescribeTable,CreateTable,DescribeTable,CreateTable,DescribeTable" : "DescribeTable,CreateTable,DescribeTable,CreateTable"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SendAsync_IfNotExistsAndAnotherProcessIsCreatingTheTable_FindsItOnceDescribeTableDoes(bool wait)
    {
        // Arrange (two instances of a program start together, and DescribeTable lags behind the create of the other one)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(NotFound());
        for (var look = 0; look < 2; look++)
        {
            handler.Enqueue(StubResponse.DynamoDbError("ResourceInUseException", "Table is being created."));
            handler.Enqueue(StubResponse.DynamoDbError("ResourceNotFoundException", "Not yet."));
        }

        handler.Enqueue(StubResponse.DynamoDbError("ResourceInUseException", "Table is being created."));
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler, time);
        var command = client.Table("Shop", "pk").CreateTable().IfNotExists();
        command = wait ? command.WaitUntilActive() : command;

        // Act
        var sending = command.SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(sending, time, Forever);

        // Assert
        await Assert.That(((await sending).Table.TableStatus, handler.Requests.Count)).IsEqualTo((TableStatus.Active, wait ? 8 : 7));
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndDescribeTableNeverFindsTheTable_RethrowsAfterFiveCreatesAndBackedOffWaits()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(NotFound());
        for (var look = 0; look < 5; look++)
        {
            handler.Enqueue(StubResponse.DynamoDbError("ResourceInUseException", "Table is being created."));
            handler.Enqueue(StubResponse.DynamoDbError("ResourceNotFoundException", "Not yet."));
        }

        using var client = TestClient.Create(handler, time);
        var sending = client.Table("Shop", "pk").CreateTable().IfNotExists().SendAsync().AsTask();

        // Act
        var delays = await AdvanceThroughDelaysAsync(sending, time, Forever);
        var failure = await TestClient.CaptureAsync<ResourceInUseException>(async () => await sending);

        // Assert
        await Assert.That((failure.Message.Contains("being created", StringComparison.Ordinal), handler.Requests.Count, string.Join(",", delays.Select(static delay => delay.TotalMilliseconds))))
            .IsEqualTo((true, 11, "100,200,400,800"));
    }

    [Test]
    public async Task SendAsync_NotIfNotExistsAndTheTableExists_CreatesOnce()
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
    public async Task SendAsync_IfNotExistsAndTheTableIsCreating_ReturnsThatStatus()
    {
        // Arrange (the schema and the status of what exists are not compared, as the docs say)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Creating));
        using var client = TestClient.Create(handler);

        // Act
        var description = (await client.Table("Shop", "pk").CreateTable().IfNotExists().SendAsync()).Table;

        // Assert
        await Assert.That(description.TableStatus).IsEqualTo(TableStatus.Creating);
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndWaitUntilActive_WaitsForTheExistingTable()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Creating));
        handler.Enqueue(StubResponse.Json(Creating));
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler, time);

        // Act
        var waiting = client.Table("Shop", "pk").CreateTable().WaitUntilActive().IfNotExists().SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(waiting, time, Forever);

        // Assert
        await Assert.That(((await waiting).Table.TableStatus, string.Join(",", handler.Requests.Select(static request => request.Operation)))).IsEqualTo((TableStatus.Active, "DescribeTable,DescribeTable,DescribeTable"));
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndWaitUntilActiveOnAnActiveTable_DescribesOnce()
    {
        // Arrange (the docs tell the operator what this costs: one DescribeTable)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler, new ManualTimeProvider());

        // Act
        await client.Table("Shop", "pk").CreateTable().IfNotExists().WaitUntilActive().SendAsync();

        // Assert
        await Assert.That(handler.Requests.Single().Operation).IsEqualTo("DescribeTable");
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndWaitUntilActiveOnATableWithItsIndexesActive_DescribesOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Table":{"TableStatus":"ACTIVE","GlobalSecondaryIndexes":[{"IndexName":"GSI1","IndexStatus":"ACTIVE"},{"IndexName":"ByTracking","IndexStatus":"ACTIVE"}]}}"""));
        using var client = TestClient.Create(handler, new ManualTimeProvider());

        // Act
        await client.Table<SchemaOrder>("Shop").CreateTable().IfNotExists().WaitUntilActive().SendAsync();

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndWaitUntilActiveWithAnIndexBackfilling_GoesOnLooking()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Table":{"TableStatus":"ACTIVE","GlobalSecondaryIndexes":[{"IndexName":"ByCity","IndexStatus":"CREATING"}]}}"""));
        handler.Enqueue(StubResponse.Json("""{"Table":{"TableStatus":"ACTIVE","GlobalSecondaryIndexes":[{"IndexName":"ByCity","IndexStatus":"CREATING"}]}}"""));
        handler.Enqueue(StubResponse.Json("""{"Table":{"TableStatus":"ACTIVE","GlobalSecondaryIndexes":[{"IndexName":"ByCity","IndexStatus":"ACTIVE"}]}}"""));
        using var client = TestClient.Create(handler, time);

        // Act
        var waiting = client.Table("Shop", "pk").CreateTable().IfNotExists().WaitUntilActive().SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(waiting, time, Forever);

        // Assert
        await Assert.That(((await waiting).Table.GlobalSecondaryIndexes.Single().IndexStatus, handler.Requests.Count)).IsEqualTo((DynaSharp.Requests.IndexStatus.Active, 3));
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndWaitUntilActiveOnAnOldTableWithoutADeclaredIndex_TimesOutNamingIt()
    {
        // Arrange (nothing adds the index to a table that exists, which the docs say)
        using var handler = new StubHandler(_ => StubResponse.Json(Active));
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var command = client.Table<SchemaCustomer>("Shop").CreateTable().IfNotExists().WaitUntilActive(TimeSpan.Zero);

        // Act
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(async () => await command.SendAsync());

        // Assert
        await Assert.That((failure.PendingIndex, handler.Requests.Any(static request => request.Operation == "CreateTable"))).IsEqualTo(((string?)"GSI1", false));
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndWaitUntilActiveOnATableThatNeverListsADeclaredIndex_FailsAtTheFifthLookNamingIt()
    {
        // Arrange (an old table, which no wait gives the index: the five looks of 1.5 seconds and not the five minutes of the timeout)
        var time = new ManualTimeProvider();
        var started = time.GetUtcNow();
        using var handler = new StubHandler(_ => StubResponse.Json(Active));
        using var client = TestClient.Create(handler, time);

        // Act
        var sending = client.Table<SchemaCustomer>("Shop").CreateTable().IfNotExists().WaitUntilActive().SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(sending, time, Forever);
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(() => sending);

        // Assert
        await Assert.That((failure.PendingIndex, failure.LastStatus, handler.Requests.Count, time.GetUtcNow() - started))
            .IsEqualTo(((string?)"GSI1", (TableStatus?)TableStatus.Active, 1 + 5, TimeSpan.FromMilliseconds(1500)));
    }

    [Test]
    public async Task SendAsync_DeclaredIndexNeverListed_SaysSoAndWhatAddsIt()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json(Active));
        using var client = TestClient.Create(handler, time);

        // Act
        var sending = client.Table<SchemaCustomer>("Shop").CreateTable().IfNotExists().WaitUntilActive().SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(sending, time, Forever);
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(() => sending);

        // Assert
        await Assert.That(failure.Message)
            .IsEqualTo("The table Shop was Active, and the index GSI1 was not listed at 5 looks in a row. CreateTable adds no index to a table that exists: add it with UpdateTable first.");
    }

    [Test]
    public async Task SendAsync_DeclaredIndexListedAtTheFifthLook_Returns()
    {
        // Arrange (DescribeTable lags behind an UpdateTable that added the index, which the recipe sends before this command)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        for (var look = 0; look < 4; look++)
        {
            handler.Enqueue(StubResponse.Json(Active));
        }

        handler.Enqueue(StubResponse.Json(AllOfTheOrderIndexes));
        using var client = TestClient.Create(handler, time);

        // Act
        var sending = client.Table<SchemaOrder>("Shop").CreateTable().IfNotExists().WaitUntilActive().SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(sending, time, Forever);

        // Assert
        await Assert.That(((await sending).Table.GlobalSecondaryIndexes.Count, handler.Requests.Count)).IsEqualTo((2, 6));
    }

    [Test]
    public async Task SendAsync_LookThatListsEveryDeclaredIndex_StartsTheCountOver()
    {
        // Arrange (a look can reach a replica that lags, so only five looks in a row without the index end the wait)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        for (var look = 0; look < 4; look++)
        {
            handler.Enqueue(StubResponse.Json(Active));
        }

        handler.Enqueue(StubResponse.Json("""{"Table":{"TableStatus":"ACTIVE","GlobalSecondaryIndexes":[{"IndexName":"GSI1","IndexStatus":"CREATING"},{"IndexName":"ByTracking","IndexStatus":"CREATING"}]}}"""));
        for (var look = 0; look < 4; look++)
        {
            handler.Enqueue(StubResponse.Json(Active));
        }

        handler.Enqueue(StubResponse.Json(AllOfTheOrderIndexes));
        using var client = TestClient.Create(handler, time);

        // Act
        var sending = client.Table<SchemaOrder>("Shop").CreateTable().IfNotExists().WaitUntilActive().SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(sending, time, Forever);

        // Assert
        await Assert.That(((await sending).Table.TableStatus, handler.Requests.Count)).IsEqualTo((TableStatus.Active, 11));
    }

    [Test]
    public async Task SendAsync_LookThatDoesNotFindTheTable_NeitherCountsNorStartsOver()
    {
        // Arrange (such a look says nothing about the index)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Created));
        for (var look = 0; look < 4; look++)
        {
            handler.Enqueue(StubResponse.Json(Active));
        }

        handler.Enqueue(NotFound());
        handler.DefaultResponse = StubResponse.Json(Active);
        using var client = TestClient.Create(handler, time);

        // Act
        var sending = client.Table<SchemaOrder>("Shop").CreateTable().WaitUntilActive().SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(sending, time, Forever);
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(() => sending);

        // Assert
        await Assert.That((failure.PendingIndex, handler.Requests.Count)).IsEqualTo(((string?)"GSI1", 1 + 6));
    }

    [Test]
    public async Task SendAsync_IndexBackfillingAndAnotherNeverListed_FailsNamingTheOneNotListed()
    {
        // Arrange (a backfill can take hours, and the index that is not there does not wait for it)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Table":{"TableStatus":"ACTIVE","GlobalSecondaryIndexes":[{"IndexName":"GSI1","IndexStatus":"CREATING"}]}}"""));
        using var client = TestClient.Create(handler, time);

        // Act
        var sending = client.Table<SchemaOrder>("Shop").CreateTable().IfNotExists().WaitUntilActive(TimeSpan.FromHours(2)).SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(sending, time, Forever);
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(() => sending);

        // Assert
        await Assert.That((failure.PendingIndex, handler.Requests.Count)).IsEqualTo(((string?)"ByTracking", 6));
    }

    [Test]
    public async Task SendAsync_IfNotExistsAndAnotherError_Surfaces()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(NotFound());
        handler.Enqueue(StubResponse.DynamoDbError("LimitExceededException", "Too many."));
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").CreateTable().IfNotExists();

        // Act
        await TestClient.CaptureAsync<LimitExceededException>(async () => await command.SendAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SendAsync_RetryOfACreateWhoseResponseWasLost_IsSuccessWithIfNotExists()
    {
        // Arrange (the first attempt created the table, and the second finds it)
        using var handler = new StubHandler();
        handler.Enqueue(NotFound());
        handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Lost.", System.Net.HttpStatusCode.InternalServerError));
        handler.Enqueue(StubResponse.DynamoDbError("ResourceInUseException", "Table already exists."));
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler, new ManualTimeProvider(), maxAttempts: 2, random: new FixedRandom(0));

        // Act
        var description = (await client.Table("Shop", "pk").CreateTable().IfNotExists().SendAsync()).Table;

        // Assert
        await Assert.That((description.TableStatus, handler.Requests.Count)).IsEqualTo((TableStatus.Active, 4));
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
    [Arguments(StreamViewType.NewImage)]
    [Arguments(StreamViewType.OldImage)]
    [Arguments(StreamViewType.NewAndOldImages)]
    [Arguments(StreamViewType.KeysOnly)]
    public async Task ToRequest_Stream_ShowsIt(StreamViewType view)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var request = client.Table("Shop", "pk").CreateTable().Stream(view).ToRequest();

        // Assert
        await Assert.That(request.StreamSpecification).IsEqualTo(new StreamSpecification { StreamEnabled = true, StreamViewType = view });
    }

    [Test]
    public async Task ToRequest_NoStream_HasNone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var request = client.Table("Shop", "pk").CreateTable().ToRequest();

        // Assert
        await Assert.That(request.StreamSpecification).IsNull();
    }

    [Test]
    public async Task SendAsync_Stream_GoesIntoTheRequest()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Created));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk").CreateTable().Stream(StreamViewType.NewAndOldImages).SendAsync();

        // Assert
        const string Expected = """
            {
              "TableName": "Shop",
              "AttributeDefinitions": [{"AttributeName": "pk", "AttributeType": "S"}],
              "KeySchema": [{"AttributeName": "pk", "KeyType": "HASH"}],
              "BillingMode": "PAY_PER_REQUEST",
              "StreamSpecification": {"StreamEnabled": true, "StreamViewType": "NEW_AND_OLD_IMAGES"}
            }
            """;
        await Assert.That(JsonComparer.Differences(Expected, handler.Requests.Single().BodyText)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SendAsync_StreamAndIfNotExistsOnAnExistingTable_LeavesItsStreamAlone()
    {
        // Arrange (a stream applies to a table this command creates)
        using var handler = new StubHandler(_ => StubResponse.Json(Active));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk").CreateTable().Stream(StreamViewType.KeysOnly).IfNotExists().SendAsync();

        // Assert
        await Assert.That(handler.Requests.Single().Operation).IsEqualTo("DescribeTable");
    }

    [Test]
    [Arguments(StreamViewType.Unspecified)]
    [Arguments((StreamViewType)99)]
    public async Task Stream_NoView_ThrowsAtTheClauseBeforeAnyTableExists(StreamViewType view)
    {
        // Arrange (DynamoDB wants a view with a stream, and would tell at the end of a round trip)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").CreateTable();

        // Act
        var failure = TestClient.Catch(() => command.Stream(view));

        // Assert
        await Assert.That(((ArgumentOutOfRangeException)failure).ParamName).IsEqualTo("view");
    }

    [Test]
    public async Task SendAsync_TimeToLiveOnANewTable_WaitsForItThenDescribesAndTurnsItOn()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Created));
        handler.Enqueue(StubResponse.Json(Creating));
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json(Disabled));
        handler.Enqueue(StubResponse.Json(Updated));
        using var client = TestClient.Create(handler, time);

        // Act
        var sending = client.Table("Shop", "pk").CreateTable().TimeToLive("expires").SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(sending, time, Forever);
        var description = (await sending).Table;

        // Assert
        await Assert.That((description.TableStatus, Operations(handler), handler.Requests[3].BodyText, handler.Requests[4].BodyText)).IsEqualTo((
            TableStatus.Active,
            "CreateTable,DescribeTable,DescribeTable,DescribeTimeToLive,UpdateTimeToLive",
            """{"TableName":"Shop"}""",
            """{"TableName":"Shop","TimeToLiveSpecification":{"Enabled":true,"AttributeName":"expires"}}"""));
    }

    [Test]
    [Arguments("ENABLED")]
    [Arguments("ENABLING")]
    public async Task SendAsync_TimeToLiveOnOnThatAttribute_EndsThere(string status)
    {
        // Arrange (a second change within the hour is refused, so the look is what spares it)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json(TimeToLive(status, "expires")));
        using var client = TestClient.Create(handler, new ManualTimeProvider());

        // Act
        await client.Table("Shop", "pk").CreateTable().IfNotExists().TimeToLive("expires").SendAsync();

        // Assert
        await Assert.That(Operations(handler)).IsEqualTo("DescribeTable,DescribeTimeToLive");
    }

    [Test]
    [Arguments("""{"TimeToLiveDescription":{"TimeToLiveStatus":"DISABLED"}}""")]
    [Arguments("""{"TimeToLiveDescription":{"TimeToLiveStatus":"DISABLED","AttributeName":"expires"}}""")]
    [Arguments("""{"TimeToLiveDescription":{"TimeToLiveStatus":"DISABLING","AttributeName":"expires"}}""")]
    [Arguments("""{"TimeToLiveDescription":{"AttributeName":"expires"}}""")]
    [Arguments("{}")]
    public async Task SendAsync_TimeToLiveNotOnThatAttribute_TurnsItOn(string look)
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json(look));
        handler.Enqueue(StubResponse.Json(Updated));
        using var client = TestClient.Create(handler, new ManualTimeProvider());

        // Act
        await client.Table("Shop", "pk").CreateTable().IfNotExists().TimeToLive("expires").SendAsync();

        // Assert
        await Assert.That(Operations(handler)).IsEqualTo("DescribeTable,DescribeTimeToLive,UpdateTimeToLive");
    }

    [Test]
    [Arguments("ENABLED")]
    [Arguments("ENABLING")]
    public async Task SendAsync_TimeToLiveThatAnotherInstanceTurnedOnFirst_IsSuccess(string status)
    {
        // Arrange (instances of a program start together, and both find it off)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json(Disabled));
        handler.Enqueue(StubResponse.DynamoDbError("ValidationException", "TimeToLive is already enabled"));
        handler.Enqueue(StubResponse.Json(TimeToLive(status, "expires")));
        using var client = TestClient.Create(handler, new ManualTimeProvider());

        // Act
        var description = (await client.Table("Shop", "pk").CreateTable().IfNotExists().TimeToLive("expires").SendAsync()).Table;

        // Assert
        await Assert.That((description.TableStatus, Operations(handler))).IsEqualTo((TableStatus.Active, "DescribeTable,DescribeTimeToLive,UpdateTimeToLive,DescribeTimeToLive"));
    }

    [Test]
    public async Task SendAsync_TimeToLiveOnAnotherAttribute_SurfacesTheErrorOfDynamoDb()
    {
        // Arrange (only DynamoDB knows why, so it gets to say so)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json(TimeToLive("ENABLED", "other")));
        handler.Enqueue(StubResponse.DynamoDbError("ValidationException", "TimeToLive is active on a different AttributeName"));
        handler.Enqueue(StubResponse.Json(TimeToLive("ENABLED", "other")));
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var command = client.Table("Shop", "pk").CreateTable().IfNotExists().TimeToLive("expires");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await command.SendAsync());

        // Assert
        await Assert.That((failure.Message.Contains("different AttributeName", StringComparison.Ordinal), failure.OperationName)).IsEqualTo((true, (string?)"UpdateTimeToLive"));
    }

    [Test]
    public async Task SendAsync_TimeToLiveRejectedAndStillOff_SurfacesTheError()
    {
        // Arrange (turned off within the hour, for one, which is no race that was lost)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json(Disabled));
        handler.Enqueue(StubResponse.DynamoDbError("ValidationException", "Time to live has been modified multiple times within a fixed interval"));
        handler.Enqueue(StubResponse.Json(Disabled));
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var command = client.Table("Shop", "pk").CreateTable().IfNotExists().TimeToLive("expires");

        // Act
        await TestClient.CaptureAsync<ValidationException>(async () => await command.SendAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(4);
    }

    [Test]
    public async Task SendAsync_TimeToLiveOnANewTable_SaysItCreatedTheTableAndTurnedTimeToLiveOn()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Created));
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json(Disabled));
        handler.Enqueue(StubResponse.Json(Updated));
        using var client = TestClient.Create(handler, new ManualTimeProvider());

        // Act
        var result = await client.Table("Shop", "pk").CreateTable().TimeToLive("expires").SendAsync();

        // Assert
        await Assert.That((result.Created, result.TimeToLiveEnabled)).IsEqualTo((true, true));
    }

    [Test]
    public async Task SendAsync_TimeToLiveOnAFoundTableThatLacksIt_SaysItTurnedItOnAndCreatedNothing()
    {
        // Arrange (an older table, which the program now gives time to live)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json(Disabled));
        handler.Enqueue(StubResponse.Json(Updated));
        using var client = TestClient.Create(handler, new ManualTimeProvider());

        // Act
        var result = await client.Table("Shop", "pk").CreateTable().IfNotExists().TimeToLive("expires").SendAsync();

        // Assert
        await Assert.That((result.Created, result.TimeToLiveEnabled)).IsEqualTo((false, true));
    }

    [Test]
    [Arguments("ENABLED")]
    [Arguments("ENABLING")]
    public async Task SendAsync_TimeToLiveOnAlready_SaysItTurnedNothingOn(string status)
    {
        // Arrange (what a program that creates its table at every start sees from the second start on)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json(TimeToLive(status, "expires")));
        using var client = TestClient.Create(handler, new ManualTimeProvider());

        // Act
        var result = await client.Table("Shop", "pk").CreateTable().IfNotExists().TimeToLive("expires").SendAsync();

        // Assert
        await Assert.That(result.TimeToLiveEnabled).IsFalse();
    }

    [Test]
    public async Task SendAsync_TimeToLiveThatAnotherInstanceTurnedOnFirst_SaysThisCallDidNot()
    {
        // Arrange (only the instance whose update DynamoDB accepted turned it on)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json(Disabled));
        handler.Enqueue(StubResponse.DynamoDbError("ValidationException", "TimeToLive is already enabled"));
        handler.Enqueue(StubResponse.Json(TimeToLive("ENABLED", "expires")));
        using var client = TestClient.Create(handler, new ManualTimeProvider());

        // Act
        var result = await client.Table("Shop", "pk").CreateTable().IfNotExists().TimeToLive("expires").SendAsync();

        // Assert
        await Assert.That(result.TimeToLiveEnabled).IsFalse();
    }

    [Test]
    public async Task SendAsync_TimeToLiveOnAFoundTableThatNeverListsADeclaredIndex_FailsAtTheFifthLookAndLeavesItOff()
    {
        // Arrange (the implied wait of five minutes ends early the way a wait that was asked for does)
        var time = new ManualTimeProvider();
        var started = time.GetUtcNow();
        using var handler = new StubHandler(_ => StubResponse.Json(Active));
        using var client = TestClient.Create(handler, time);

        // Act
        var sending = client.Table<SchemaCustomer>("Shop").CreateTable().IfNotExists().TimeToLive("expires").SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(sending, time, Forever);
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(() => sending);

        // Assert
        await Assert.That((failure.PendingIndex, time.GetUtcNow() - started, Operations(handler).Contains("TimeToLive", StringComparison.Ordinal)))
            .IsEqualTo(((string?)"GSI1", TimeSpan.FromMilliseconds(1500), false));
    }

    [Test]
    public async Task SendAsync_TimeToLiveAndAnotherErrorOfTheUpdate_SurfacesWithoutALook()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json(Disabled));
        handler.Enqueue(StubResponse.DynamoDbError("AccessDeniedException", "No."));
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var command = client.Table("Shop", "pk").CreateTable().IfNotExists().TimeToLive("expires");

        // Act
        await TestClient.CaptureAsync<AccessDeniedException>(async () => await command.SendAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(3);
    }

    [Test]
    public async Task SendAsync_TimeToLiveOnATableThatStaysCreating_GivesUpAfterTheDefaultWaitAndLeavesItOff()
    {
        // Arrange (the implied wait is the one of WaitUntilActive())
        var time = new ManualTimeProvider();
        var started = time.GetUtcNow();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Created));
        handler.DefaultResponse = StubResponse.Json(Creating);
        using var client = TestClient.Create(handler, time);

        // Act
        var sending = client.Table("Shop", "pk").CreateTable().TimeToLive("expires").SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(sending, time, Forever);
        await TestClient.CaptureAsync<TableNotActiveException>(() => sending);

        // Assert
        await Assert.That((time.GetUtcNow() - started, Operations(handler).Contains("TimeToLive", StringComparison.Ordinal))).IsEqualTo((TimeSpan.FromMinutes(5), false));
    }

    [Test]
    public async Task SendAsync_TimeToLiveWithAWaitOfItsOwn_TakesThatTimeout()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var started = time.GetUtcNow();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Created));
        handler.DefaultResponse = StubResponse.Json(Creating);
        using var client = TestClient.Create(handler, time);

        // Act
        var sending = client.Table("Shop", "pk").CreateTable().WaitUntilActive(TimeSpan.FromSeconds(1)).TimeToLive("expires").SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(sending, time, Forever);
        await TestClient.CaptureAsync<TableNotActiveException>(() => sending);

        // Assert
        await Assert.That(time.GetUtcNow() - started).IsEqualTo(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task SendAsync_TimeToLiveOnAnExistingTableThatIsNotActive_WaitsFirst()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Creating));
        handler.Enqueue(StubResponse.Json(Creating));
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json(TimeToLive("ENABLED", "expires")));
        using var client = TestClient.Create(handler, time);

        // Act
        var sending = client.Table("Shop", "pk").CreateTable().IfNotExists().TimeToLive("expires").SendAsync().AsTask();
        await AdvanceThroughDelaysAsync(sending, time, Forever);

        // Assert
        await Assert.That(((await sending).Table.TableStatus, Operations(handler))).IsEqualTo((TableStatus.Active, "DescribeTable,DescribeTable,DescribeTable,DescribeTimeToLive"));
    }

    [Test]
    public async Task SendAsync_TimeToLiveOnAnExistingTableWithoutADeclaredIndex_TimesOutAndLeavesItOff()
    {
        // Arrange (the implied wait counts the way a wait that was asked for does)
        using var handler = new StubHandler(_ => StubResponse.Json(Active));
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var command = client.Table<SchemaCustomer>("Shop").CreateTable().IfNotExists().WaitUntilActive(TimeSpan.Zero).TimeToLive("expires");

        // Act
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(async () => await command.SendAsync());

        // Assert
        await Assert.That((failure.PendingIndex, Operations(handler).Contains("TimeToLive", StringComparison.Ordinal))).IsEqualTo(((string?)"GSI1", false));
    }

    [Test]
    public async Task SendAsync_TimeToLiveWithAResponseWithoutADescription_ReturnsWhatTheWaitFound()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json(TimeToLive("ENABLED", "expires")));
        using var client = TestClient.Create(handler, new ManualTimeProvider());

        // Act
        var description = (await client.Table("Shop", "pk").CreateTable().TimeToLive("expires").SendAsync()).Table;

        // Assert
        await Assert.That((description.TableStatus, Operations(handler))).IsEqualTo((TableStatus.Active, "CreateTable,DescribeTable,DescribeTimeToLive"));
    }

    [Test]
    public async Task SendAsync_TimeToLiveByAHandle_NamesItsAttribute()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json(Disabled));
        handler.Enqueue(StubResponse.Json(Updated));
        using var client = TestClient.Create(handler, new ManualTimeProvider());

        // Act
        await client.Table<SchemaReading>("Readings").CreateTable().IfNotExists().TimeToLive(SchemaReading.Attrs.ExpiresAt).SendAsync();

        // Assert
        await Assert.That(handler.Requests[2].BodyText).Contains("""
            "AttributeName":"ExpiresAt"
            """);
    }

    [Test]
    public async Task ToRequest_TimeToLive_IsNotInIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var plain = client.Table("Shop", "pk").CreateTable().ToRequest();

        // Act
        var request = client.Table("Shop", "pk").CreateTable().TimeToLive("expires").ToRequest();

        // Assert
        await Assert.That((request.TableName, request.BillingMode, request.StreamSpecification)).IsEqualTo((plain.TableName, plain.BillingMode, plain.StreamSpecification));
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task TimeToLive_NoName_ThrowsAtTheClause(string? attributeName)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").CreateTable();

        // Act
        var failure = TestClient.Catch(() => command.TimeToLive(attributeName!));

        // Assert
        await Assert.That(((ArgumentException)failure).ParamName).IsEqualTo("attributeName");
    }

    [Test]
    public async Task TimeToLive_DefaultHandle_ThrowsAtTheClause()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").CreateTable();

        // Act
        var failure = TestClient.Catch(() => command.TimeToLive(default(Attr)));

        // Assert
        await Assert.That(((ArgumentException)failure).ParamName).IsEqualTo("attribute");
    }

    [Test]
    [Arguments("place.expires")]
    [Arguments("lines[0]")]
    public async Task TimeToLive_NestedHandle_ThrowsNamingIt(string path)
    {
        // Arrange (DynamoDB would take that for the name of an attribute no item has, and refuse a correction for an hour)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").CreateTable();

        // Act
        var failure = TestClient.Catch(() => command.TimeToLive(new Attr(path)));

        // Assert
        await Assert.That((((ArgumentException)failure).ParamName, failure.Message.Contains(path, StringComparison.Ordinal))).IsEqualTo(("attribute", true));
    }

    [Test]
    public async Task SendAsync_TimeToLive_HasOneSpanPerRequest()
    {
        // Arrange
        using var capture = new ActivityCapture();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json(Disabled));
        handler.Enqueue(StubResponse.Json(Updated));
        using var client = TestClient.Create(handler, new ManualTimeProvider());

        // Act
        await client.Table("Shop", "pk").CreateTable().IfNotExists().TimeToLive("expires").SendAsync();

        // Assert
        await Assert.That(string.Join(",", capture.All.Select(static span => span.OperationName))).IsEqualTo("DynamoDB.DescribeTable,DynamoDB.DescribeTimeToLive,DynamoDB.UpdateTimeToLive");
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
    public async Task SendAsync_IfNotExistsAndACreateFindsTheTable_HasOneSpanPerRequest()
    {
        // Arrange
        using var capture = new ActivityCapture();
        using var handler = new StubHandler();
        handler.Enqueue(NotFound());
        handler.Enqueue(StubResponse.DynamoDbError("ResourceInUseException", "Table already exists."));
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk").CreateTable().IfNotExists().SendAsync();

        // Assert
        await Assert.That(string.Join(",", capture.All.Select(static span => span.OperationName))).IsEqualTo("DynamoDB.DescribeTable,DynamoDB.CreateTable,DynamoDB.DescribeTable");
    }

    private static string TimeToLive(string status, string attributeName) => $"{{\"TimeToLiveDescription\":{{\"TimeToLiveStatus\":\"{status}\",\"AttributeName\":\"{attributeName}\"}}}}";

    private static string Operations(StubHandler handler) => string.Join(",", handler.Requests.Select(static request => request.Operation));

    private static StubResponse NotFound() => StubResponse.DynamoDbError("ResourceNotFoundException", "No such table.");

    private static string Names(CreateTableRequest request)
    {
        var attributes = string.Join(" ", request.AttributeDefinitions.Select(static attribute => $"{attribute.AttributeName}:{attribute.AttributeType}").Order(StringComparer.Ordinal));
        var indexes = (request.GlobalSecondaryIndexes ?? []).Select(static index => index.IndexName).Concat((request.LocalSecondaryIndexes ?? []).Select(static index => index.IndexName));
        return $"{attributes} | {string.Join(" ", indexes.Order(StringComparer.Ordinal))}";
    }
}
