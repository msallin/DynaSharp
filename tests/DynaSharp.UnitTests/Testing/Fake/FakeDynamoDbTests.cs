using System.Net;
using DynaSharp.Requests;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Fluent;
using DynaSharp.UnitTests.Testing.Consumer;

namespace DynaSharp.UnitTests.Testing.Fake;

// The fake as a test uses it: a real client over it, the direct API to set up and read state, and the handler seam for faults.
public sealed class FakeDynamoDbTests
{
    private static readonly CreateTableRequest Users = new()
    {
        TableName = "Users",
        AttributeDefinitions = [new AttributeDefinition("pk", ScalarAttributeType.String)],
        KeySchema = [new KeySchemaElement("pk", KeyType.Hash)],
        BillingMode = BillingMode.PayPerRequest,
    };

    private static readonly Item AliceKey = new() { ["pk"] = "alice" };

    [Test]
    public async Task CreateClient_PutThenGet_ReadsWhatWasWritten()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        using var client = fake.CreateClient();
        await client.CreateTableAsync(Users);

        // Act
        await client.PutItemAsync(new PutItemRequest { TableName = "Users", Item = new Item { ["pk"] = "alice", ["age"] = 42 } });
        var response = await client.GetItemAsync(new GetItemRequest { TableName = "Users", Key = AliceKey });

        // Assert
        await Assert.That(response.Item!["age"].AsNumber()).IsEqualTo("42");
    }

    [Test]
    public async Task CreateClient_DeleteThroughTheClient_LeavesNoItem()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable(Users);
        fake.Put("Users", new Item { ["pk"] = "alice" });
        using var client = fake.CreateClient(options => options with { MaxAttempts = 2 });

        // Act
        await client.DeleteItemAsync(new DeleteItemRequest { TableName = "Users", Key = AliceKey });

        // Assert
        await Assert.That(fake.Get("Users", AliceKey)).IsNull();
    }

    [Test]
    public async Task CreateClient_MissingTable_ThrowsTheExceptionOfDynamoDb()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        using var client = fake.CreateClient();

        // Act
        var exception = await TestClient.CaptureAsync<ResourceNotFoundException>(() => client.GetItemAsync(new GetItemRequest { TableName = "Users", Key = AliceKey }));

        // Assert
        await Assert.That(exception.Message).IsEqualTo("Cannot do operations on a non-existent table");
    }

    [Test]
    public async Task CreateClient_UnsupportedMember_ThrowsWithTheCodeOfTheFake()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        using var client = fake.CreateClient();

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.CreateTableAsync(Users with { Tags = [new TableTag { Key = "team", Value = "shop" }] }));

        // Assert
        await Assert.That((exception.ErrorCode, exception.StatusCode)).IsEqualTo(("UnsupportedByFakeException", HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateHandler_QueuedThrottle_AnswersInsteadOfTheFakeAndChangesNothing()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable(Users);
        using var handler = fake.CreateHandler();
        handler.Enqueue(StubResponse.DynamoDbError("ProvisionedThroughputExceededException", "slow down"));
        using var client = StubClient.Create(handler);

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.PutItemAsync(new PutItemRequest { TableName = "Users", Item = AliceKey }));

        // Assert
        await Assert.That((fake.Items("Users").Count, handler.Requests.Count)).IsEqualTo((0, 1));
    }

    [Test]
    public async Task CreateHandler_TwoHandlers_ShareTheState()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        using var first = StubClient.Create(fake.CreateHandler());
        using var second = StubClient.Create(fake.CreateHandler());
        await first.CreateTableAsync(Users);

        // Act
        var tables = await second.ListTablesAsync(new ListTablesRequest());

        // Assert
        await Assert.That(tables.TableNames).IsEquivalentTo(["Users"]);
    }

    [Test]
    public async Task Answer_RecordedRequest_TakesTheOperationFromTheTarget()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        RecordedRequest? recorded = null;
        using var stub = new StubHandler(request =>
        {
            recorded = request;
            return new StubResponse();
        });
        using var client = StubClient.Create(stub);
        await client.SendRawAsync("DescribeLimits", "{}"u8.ToArray());

        // Act
        var response = fake.Answer(recorded!);

        // Assert
        await Assert.That((response.StatusCode, response.Body.Contains("AccountMaxReadCapacityUnits", StringComparison.Ordinal), response.Headers!.ContainsKey("x-amzn-RequestId")))
            .IsEqualTo((HttpStatusCode.OK, true, true));
    }

    [Test]
    public async Task Answer_NullArguments_ThrowArgumentNullException()
    {
        // Arrange
        var fake = new FakeDynamoDb();

        // Act
        Action[] calls = [() => fake.Answer(null!), () => fake.Answer(null!, "{}"), () => fake.Answer("GetItem", null!)];

        // Assert
        await Assert.That(calls.Count(call => Throws<ArgumentNullException>(call))).IsEqualTo(3);
    }

    [Test]
    public async Task CreateTable_Request_IsActiveAtOnceWithTheClockOfTheFake()
    {
        // Arrange
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var fake = new FakeDynamoDb(clock);

        // Act
        var description = fake.CreateTable(Users);

        // Assert
        await Assert.That((description.TableStatus, description.CreationDateTime, description.TableArn)).IsEqualTo((TableStatus.Active, clock.GetUtcNow(), "arn:aws:dynamodb:us-east-1:123456789012:table/Users"));
    }

    [Test]
    public async Task CreateTable_Twice_ThrowsResourceInUse()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable(Users);

        // Act
        var act = () => fake.CreateTable(Users);

        // Assert
        await Assert.That(act).Throws<ResourceInUseException>().WithMessage("Table already exists: Users", StringComparison.Ordinal);
    }

    [Test]
    public async Task CreateTable_WithTags_ThrowsWithTheCodeOfTheFake()
    {
        // Arrange
        var fake = new FakeDynamoDb();

        // Act
        var exception = Capture<DynamoDbException>(() => fake.CreateTable(Users with { Tags = [new TableTag { Key = "team", Value = "shop" }] }));

        // Assert
        await Assert.That(exception.ErrorCode).IsEqualTo("UnsupportedByFakeException");
    }

    [Test]
    public async Task CreateTableOfT_EntityWithAnIndex_DeclaresKeysAndIndexOnDemand()
    {
        // Arrange
        var fake = new FakeDynamoDb();

        // Act
        var description = fake.CreateTable<PlainIndexed>("Shop");

        // Assert
        await Assert.That((description.BillingModeSummary!.BillingMode, description.GlobalSecondaryIndexes.Single().IndexName, description.GlobalSecondaryIndexes.Single().IndexStatus))
            .IsEqualTo((BillingMode.PayPerRequest, "ByCode", IndexStatus.Active));
    }

    [Test]
    public async Task DescribeTable_Items_CountsThem()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable(Users);
        fake.Put("Users", new Item { ["pk"] = "a" });
        fake.Put("Users", new Item { ["pk"] = "b" });

        // Act
        var description = fake.DescribeTable("Users");

        // Assert
        await Assert.That((description.ItemCount, description.TableSizeBytes)).IsEqualTo((2L, 6L));
    }

    [Test]
    public async Task DescribeTable_Missing_ThrowsResourceNotFound()
    {
        // Arrange
        var fake = new FakeDynamoDb();

        // Act
        var exception = Capture<ResourceNotFoundException>(() => fake.DescribeTable("Users"));

        // Assert
        await Assert.That((exception.OperationName, exception.TableName)).IsEqualTo(("DescribeTable", "Users"));
    }

    [Test]
    public async Task DeleteTable_Table_FreesTheNameAtOnce()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable(Users);

        // Act
        fake.DeleteTable("Users");
        fake.CreateTable(Users);

        // Assert
        await Assert.That(fake.TableNames).IsEquivalentTo(["Users"]);
    }

    [Test]
    public async Task Reset_TablesAndAClient_DropsTheTablesAndKeepsTheClientWorking()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable(Users);
        using var client = fake.CreateClient();

        // Act
        fake.Reset();
        var tables = await client.ListTablesAsync(new ListTablesRequest());

        // Assert
        await Assert.That(tables.TableNames.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Put_ItemWithoutTheKey_ThrowsValidation()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable(Users);

        // Act
        var act = () => fake.Put("Users", new Item { ["name"] = "Alice" });

        // Assert
        await Assert.That(act).Throws<ValidationException>().WithMessage("One or more parameter values were invalid: Missing the key pk in the item", StringComparison.Ordinal);
    }

    [Test]
    public async Task Get_StoredItem_IsACopy()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable(Users);
        fake.Put("Users", new Item { ["pk"] = "alice", ["age"] = 1 });

        // Act
        var copy = fake.Get("Users", AliceKey)!;
        copy["age"] = 2;

        // Assert
        await Assert.That(fake.Get("Users", AliceKey)!["age"].AsNumber()).IsEqualTo("1");
    }

    [Test]
    public async Task Delete_StoredAndMissing_SaysWhetherThereWasOne()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable(Users);
        fake.Put("Users", new Item { ["pk"] = "alice" });

        // Act
        var deleted = (fake.Delete("Users", AliceKey), fake.Delete("Users", AliceKey));

        // Assert
        await Assert.That(deleted).IsEqualTo((true, false));
    }

    [Test]
    public async Task Delete_KeyOfAnotherSchema_ThrowsValidation()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable(Users);

        // Act
        var act = () => fake.Delete("Users", new Item { ["id"] = "alice" });

        // Assert
        await Assert.That(act).Throws<ValidationException>();
    }

    [Test]
    public async Task Items_Table_GivesCopiesInKeyOrder()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable(Users);
        fake.Put("Users", new Item { ["pk"] = "b" });
        fake.Put("Users", new Item { ["pk"] = "a" });

        // Act
        var items = fake.Items("Users");

        // Assert
        await Assert.That(string.Join(",", items.Select(item => item["pk"].AsString()))).IsEqualTo("a,b");
    }

    [Test]
    public async Task PutOfT_Entity_GetOfTReadsItBack()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable<Subscriber>("Shop");
        var subscriber = new Subscriber("s-1", "a@example.com", "pro");

        // Act
        fake.Put("Shop", subscriber);

        // Assert
        await Assert.That((fake.Get<Subscriber>("Shop", new Item { ["pk"] = "s-1" }), fake.Items<Subscriber>("Shop").Single())).IsEqualTo((subscriber, subscriber));
    }

    [Test]
    public async Task CreateClient_CommandsOfATable_CreateWaitPutAndGetThroughTheFake()
    {
        // Arrange: what a service does at start-up and on a request, through the commands of a table.
        var fake = new FakeDynamoDb();
        using var client = fake.CreateClient();
        var shop = client.Table<Subscriber>("Shop");
        await shop.CreateTable().IfNotExists().WaitUntilActive().SendAsync();
        var subscriber = new Subscriber("s-1", "a@example.com", "pro");

        // Act
        await shop.Put(subscriber).SendAsync();
        var read = await shop.Get(new SubscriberKey("s-1")).ItemAsync();

        // Assert
        await Assert.That((read, fake.Get<Subscriber>("Shop", new Item { ["pk"] = "s-1" }))).IsEqualTo((subscriber, subscriber));
    }

    [Test]
    public async Task GetOfT_NoItem_IsNull()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable<Subscriber>("Shop");

        // Act
        var found = fake.Get<Subscriber>("Shop", new Item { ["pk"] = "s-1" });

        // Assert
        await Assert.That(found).IsNull();
    }

    [Test]
    public async Task ExpireItems_ClockMovesPastTheExpiry_DeletesTheItem()
    {
        // Arrange: time to live as a service turns it on, and an item that expires an hour from now.
        var clock = new ManualTimeProvider();
        var fake = new FakeDynamoDb(clock);
        fake.CreateTable(Users);
        using var client = fake.CreateClient();
        await client.UpdateTimeToLiveAsync(new UpdateTimeToLiveRequest { TableName = "Users", TimeToLiveSpecification = new TimeToLiveSpecification { Enabled = true, AttributeName = "expires" } });
        fake.Put("Users", new Item { ["pk"] = "alice", ["expires"] = clock.GetUtcNow().AddHours(1).ToUnixTimeSeconds() });
        var before = fake.ExpireItems();

        // Act
        clock.Advance(TimeSpan.FromHours(1));
        var after = fake.ExpireItems();

        // Assert
        await Assert.That((before, after, fake.Items("Users").Count)).IsEqualTo((0, 1, 0));
    }

    [Test]
    public async Task DirectApi_NullArguments_ThrowArgumentNullException()
    {
        // Arrange
        var fake = new FakeDynamoDb();

        // Act
        Action[] calls =
        [
            () => fake.CreateTable(null!),
            () => fake.CreateTable<Subscriber>(null!),
            () => fake.DescribeTable(null!),
            () => fake.DeleteTable(null!),
            () => fake.Put(null!, []),
            () => fake.Put("Users", (Item)null!),
            () => fake.Get(null!, []),
            () => fake.Get("Users", null!),
            () => fake.Delete(null!, []),
            () => fake.Delete("Users", null!),
            () => fake.Items(null!),
        ];

        // Assert
        await Assert.That(calls.Count(call => Throws<ArgumentNullException>(call))).IsEqualTo(calls.Length);
    }

    private static T Capture<T>(Action call)
        where T : Exception
    {
        try
        {
            call();
        }
        catch (T exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"No {typeof(T).Name} was thrown.");
    }

    private static bool Throws<T>(Action call)
        where T : Exception
    {
        try
        {
            call();
            return false;
        }
        catch (T)
        {
            return true;
        }
    }
}
