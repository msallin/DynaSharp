using System.Net;
using DynaSharp.Requests;
using DynaSharp.Streams;
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
        await Assert.That(exception.Message).IsEqualTo("Requested resource not found");
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
    public async Task CreateTable_KeyNames_DeclaresStringKeysOnDemand()
    {
        // Arrange
        var fake = new FakeDynamoDb();

        // Act
        var description = fake.CreateTable("Locks", "pk", "sk");

        // Assert
        await Assert.That((
            string.Join(",", description.KeySchema.Select(static key => $"{key.AttributeName}:{key.KeyType}")),
            string.Join(",", description.AttributeDefinitions.Select(static attribute => $"{attribute.AttributeName}:{attribute.AttributeType}")),
            description.BillingModeSummary?.BillingMode))
            .IsEqualTo(("pk:Hash,sk:Range", "pk:String,sk:String", (BillingMode?)BillingMode.PayPerRequest));
    }

    [Test]
    public async Task CreateTable_PartitionKeyNameAlone_HasNoSortKey()
    {
        // Arrange
        var fake = new FakeDynamoDb();

        // Act
        var description = fake.CreateTable("Locks", "pk");

        // Assert
        await Assert.That(string.Join(",", description.KeySchema.Select(static key => key.AttributeName))).IsEqualTo("pk");
    }

    [Test]
    [Arguments(null, "pk", "tableName")]
    [Arguments("Locks", null, "partitionKey")]
    public async Task CreateTable_KeyNamesWithANull_ThrowsArgumentNullException(string? tableName, string? partitionKey, string parameter)
    {
        // Arrange
        var fake = new FakeDynamoDb();

        // Act
        var act = () => fake.CreateTable(tableName!, partitionKey!, null);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName(parameter);
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
    public async Task CreateTable_SseEnabledWithAKeyId_DescribesTheKeyOfTheFakesAccount()
    {
        // Arrange
        var fake = new FakeDynamoDb();

        // Act
        var description = fake.CreateTable(Users with { SSESpecification = new SSESpecification { Enabled = true, KMSMasterKeyId = "1234abcd" } });

        // Assert
        await Assert.That(description.SSEDescription).IsEqualTo(new SSEDescription
        {
            Status = SSEStatus.Enabled,
            SSEType = SSEType.Kms,
            KMSMasterKeyArn = "arn:aws:kms:us-east-1:123456789012:key/1234abcd",
        });
    }

    [Test]
    public async Task CreateTable_SseDisabled_DescribesNoEncryption()
    {
        // Arrange
        var fake = new FakeDynamoDb();

        // Act
        var description = fake.CreateTable(Users with { SSESpecification = new SSESpecification { Enabled = false } });

        // Assert
        await Assert.That(description.SSEDescription).IsNull();
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
    public async Task CreateTableOfT_EntityWithTimeToLive_TurnsItOnSoThatTheSweepDeletes()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        var description = fake.CreateTable<SchemaSession>("Shop");
        var past = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
        fake.Put("Shop", new Item { ["pk"] = "SESSION#a", ["sk"] = "SESSION", ["expires"] = past });

        // Act
        var expired = fake.ExpireItems();

        // Assert
        await Assert.That((description.TableName, expired, fake.Items("Shop").Count)).IsEqualTo(("Shop", 1, 0));
    }

    [Test]
    public async Task CreateTableOfT_EntityWithTimeToLive_DescribesItAsEnabled()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable<SchemaSession>("Shop");
        using var client = fake.CreateClient();

        // Act
        var answer = await client.DescribeTimeToLiveAsync(new DescribeTimeToLiveRequest { TableName = "Shop" });

        // Assert
        await Assert.That((answer.TimeToLiveDescription!.TimeToLiveStatus, answer.TimeToLiveDescription.AttributeName)).IsEqualTo((TimeToLiveStatus.Enabled, (string?)"expires"));
    }

    [Test]
    public async Task CreateTableOfT_TableThatExists_ThrowsResourceInUse()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable<SchemaSession>("Shop");

        // Act
        var act = () => fake.CreateTable<SchemaSession>("Shop");

        // Assert
        await Assert.That(act).ThrowsExactly<ResourceInUseException>();
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
    public async Task CreateTableOfT_WithoutAName_CreatesTheTableTheEntityDeclares()
    {
        // Arrange
        var fake = new FakeDynamoDb();

        // Act
        var table = fake.CreateTable<Patron>();

        // Assert
        await Assert.That((table.TableName, fake.TableNames.Single())).IsEqualTo(("patrons", "patrons"));
    }

    [Test]
    public async Task PutOfT_WithoutAName_GetOfTAndItemsOfTReadItBackFromTheDeclaredTable()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable<Patron>();
        var patron = new Patron("p-1", "Ada");

        // Act
        fake.Put(patron);

        // Assert
        await Assert.That((fake.Get<Patron>(new Item { ["pk"] = "p-1" }), fake.Items<Patron>().Single(), fake.Items("patrons").Count)).IsEqualTo((patron, patron, 1));
    }

    [Test]
    public async Task DirectApiWithoutAName_EntityWithoutATable_ThrowsInvalidOperationException()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        var subscriber = new Subscriber("s-1", "a@example.com");

        // Act (what DYNA238 reports at compile time is what this test checks at run time)
#pragma warning disable DYNA238
        Action[] calls =
        [
            () => fake.CreateTable<Subscriber>(),
            () => fake.Put(subscriber),
            () => fake.Get<Subscriber>(new Item { ["pk"] = "s-1" }),
            () => fake.Items<Subscriber>(),
            () => fake.TableItems<Subscriber>(),
            () => fake.DescribeTable<Subscriber>(),
            () => fake.EnableStream<Subscriber>(StreamViewType.NewImage),
            () => fake.DisableStream<Subscriber>(),
            () => fake.Delete(subscriber),
            () => fake.Delete(new SubscriberKey("s-1")),
            () => fake.Get(new SubscriberKey("s-1")),
            () => fake.GetItem(new SubscriberKey("s-1")),
            () => fake.PutItem<Subscriber>(new Item { ["pk"] = "s-1" }),
        ];
#pragma warning restore DYNA238

        // Assert
        await Assert.That(calls.Count(call => Throws<InvalidOperationException>(call))).IsEqualTo(calls.Length);
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
    public async Task ItemsOfT_TableOfSeveralEntities_GivesThoseOfTheEntityAndSkipsTheOthers()
    {
        // Arrange: an item of another entity whose email no subscriber could read, one without a type and one whose type is no string, each before the subscriber in key order.
        var fake = new FakeDynamoDb();
        fake.CreateTable<Subscriber>("Shop");
        var subscriber = new Subscriber("s-1", "a@example.com", "pro");
        fake.Put("Shop", subscriber);
        fake.Put("Shop", new Item { ["pk"] = "c-1", ["_type"] = "COST_CENTER", ["email"] = 5 });
        fake.Put("Shop", new Item { ["pk"] = "c-2", ["email"] = "b@example.com" });
        fake.Put("Shop", new Item { ["pk"] = "c-3", ["_type"] = 1, ["email"] = "c@example.com" });

        // Act
        var subscribers = fake.Items<Subscriber>("Shop");

        // Assert
        await Assert.That(subscribers).IsEquivalentTo([subscriber]);
    }

    [Test]
    public async Task ItemsOfT_ItemOfTheEntityItCannotRead_Throws()
    {
        // Arrange: a subscriber without its required email.
        var fake = new FakeDynamoDb();
        fake.CreateTable<Subscriber>("Shop");
        fake.Put("Shop", new Item { ["pk"] = "s-1", ["_type"] = "SUBSCRIBER" });

        // Act
        var read = () => fake.Items<Subscriber>("Shop");

        // Assert
        await Assert.That(read).Throws<DynamoDbMappingException>();
    }

    [Test]
    public async Task ItemsOfT_DiscriminatorUnderAnotherName_ComparesThatAttribute()
    {
        // Arrange: Account keeps its type in "kind", and "_type" means nothing to it.
        var fake = new FakeDynamoDb();
        fake.CreateTable<Mapping.Account>("Accounts");
        var account = new Mapping.Account { Id = "a-1" };
        fake.Put("Accounts", account);
        fake.Put("Accounts", new Item(Item.From(new Mapping.Account { Id = "a-0" })) { ["kind"] = "LEDGER", ["_type"] = "ACCOUNT" });

        // Act
        var accounts = fake.Items<Mapping.Account>("Accounts");

        // Assert
        await Assert.That(accounts.Select(read => read.Id)).IsEquivalentTo(["a-1"]);
    }

    [Test]
    public async Task ItemsOfT_EntityWithoutADiscriminator_ReadsEveryItemAndThrowsForOneItCannotRead()
    {
        // Arrange: nothing tells the item of another writer apart, so it is read as the entity, and its key does not fit the template.
        var fake = new FakeDynamoDb();
        fake.CreateTable<Mapping.KeyEntities.OwnedThing>("Things");
        fake.Put("Things", new Mapping.KeyEntities.OwnedThing { Id = "t-1" });
        fake.Put("Things", new Item { ["pk"] = "COST#1", ["_type"] = "COST_CENTER" });

        // Act
        var read = () => fake.Items<Mapping.KeyEntities.OwnedThing>("Things");

        // Assert
        await Assert.That(read).Throws<DynamoDbMappingException>();
    }

    [Test]
    public async Task ItemsOfT_Document_ReadsEveryItem()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable<Subscriber>("Shop");
        fake.Put("Shop", new Subscriber("s-1", "a@example.com"));
        fake.Put("Shop", new Item { ["pk"] = "c-1", ["_type"] = "COST_CENTER" });

        // Act
        var items = fake.Items<Item>("Shop");

        // Assert
        await Assert.That(items.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetOfT_ItemOfAnotherEntity_Throws()
    {
        // Arrange: a key names one item, so another entity on it is a mistake, as Get(key).ItemAsync() of a client says.
        var fake = new FakeDynamoDb();
        fake.CreateTable<Subscriber>("Shop");
        fake.Put("Shop", new Item { ["pk"] = "s-1", ["_type"] = "COST_CENTER", ["email"] = "a@example.com" });

        // Act
        var read = () => fake.Get<Subscriber>("Shop", new Item { ["pk"] = "s-1" });

        // Assert
        await Assert.That(read).Throws<DynamoDbMappingException>().WithMessageContaining("COST_CENTER", StringComparison.Ordinal);
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
    public async Task CreateStreamsClient_ReadShardOfADisabledStream_ReturnsEveryChangeAndEnds()
    {
        // Arrange: a table with a stream, three writes through a client, and the stream switched off, so the shard is closed and the read ends.
        var fake = new FakeDynamoDb();
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient(options => options with { MaxAttempts = 2 });
        await client.CreateTableAsync(Users with { StreamSpecification = new StreamSpecification { StreamEnabled = true, StreamViewType = StreamViewType.NewAndOldImages } });
        await client.PutItemAsync(new PutItemRequest { TableName = "Users", Item = new Item { ["pk"] = "alice", ["age"] = 41 } });
        await client.UpdateItemAsync(new UpdateItemRequest { TableName = "Users", Key = AliceKey, UpdateExpression = "SET age = :age", ExpressionAttributeValues = new Item { [":age"] = 42 } });
        await client.DeleteItemAsync(new DeleteItemRequest { TableName = "Users", Key = AliceKey });
        var arn = (await client.UpdateTableAsync(new UpdateTableRequest { TableName = "Users", StreamSpecification = new StreamSpecification { StreamEnabled = false } })).TableDescription!.LatestStreamArn!;
        var shard = (await streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = arn })).StreamDescription!.Shards.Single();

        // Act
        var records = await streams.ReadShardAsync(new ReadShardRequest { StreamArn = arn, ShardId = shard.ShardId!, ShardIteratorType = ShardIteratorType.TrimHorizon }).ToListAsync();

        // Assert
        await Assert.That(records.Select(static record => (record.EventName, record.DynamoDb!.NewImage?["age"].AsNumber(), record.DynamoDb.OldImage?["age"].AsNumber())))
            .IsEquivalentTo([(OperationType.Insert, "41", null), (OperationType.Modify, "42", "41"), (OperationType.Remove, null, "42")]);
    }

    [Test]
    public async Task CreateStreamsClient_ReadShardAfterASequenceNumber_ResumesBehindIt()
    {
        // Arrange: a reader that stored the sequence number of the first record starts again after it, on an open shard; the direct API records its writes too.
        var fake = new FakeDynamoDb();
        fake.CreateTable(Users with { StreamSpecification = new StreamSpecification { StreamEnabled = true, StreamViewType = StreamViewType.KeysOnly } });
        fake.Put("Users", new Item { ["pk"] = "a" });
        fake.Put("Users", new Item { ["pk"] = "b" });
        using var streams = fake.CreateStreamsClient();
        var arn = fake.DescribeTable("Users").LatestStreamArn!;
        var shard = (await streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = arn })).StreamDescription!.Shards.Single().ShardId!;
        var first = await streams.ReadShardAsync(new ReadShardRequest { StreamArn = arn, ShardId = shard, ShardIteratorType = ShardIteratorType.TrimHorizon }).FirstAsync();

        // Act
        var next = await streams.ReadShardAsync(new ReadShardRequest
        {
            StreamArn = arn,
            ShardId = shard,
            ShardIteratorType = ShardIteratorType.AfterSequenceNumber,
            SequenceNumber = first.DynamoDb!.SequenceNumber,
        }).FirstAsync();

        // Assert
        await Assert.That(next.DynamoDb!.Keys!["pk"].AsString()).IsEqualTo("b");
    }

    [Test]
    public async Task ExpireItems_TableWithStream_RecordsTheDeleteAsOneOfTheService()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var fake = new FakeDynamoDb(clock);
        fake.CreateTable(Users with { StreamSpecification = new StreamSpecification { StreamEnabled = true, StreamViewType = StreamViewType.OldImage } });
        using var client = fake.CreateClient();
        await client.UpdateTimeToLiveAsync(new UpdateTimeToLiveRequest { TableName = "Users", TimeToLiveSpecification = new TimeToLiveSpecification { Enabled = true, AttributeName = "expires" } });
        fake.Put("Users", new Item { ["pk"] = "alice", ["expires"] = clock.GetUtcNow().ToUnixTimeSeconds() });
        fake.ExpireItems();
        using var streams = fake.CreateStreamsClient();
        var arn = fake.DescribeTable("Users").LatestStreamArn!;
        var shard = (await streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = arn })).StreamDescription!.Shards.Single().ShardId!;

        // Act
        var records = await streams.ReadShardAsync(new ReadShardRequest { StreamArn = arn, ShardId = shard, ShardIteratorType = ShardIteratorType.TrimHorizon }).Take(2).ToListAsync();

        // Assert
        await Assert.That((records[1].EventName, records[1].UserIdentity?.PrincipalId, records[1].UserIdentity?.Type, records[0].UserIdentity))
            .IsEqualTo((OperationType.Remove, "dynamodb.amazonaws.com", "Service", (RecordIdentity?)null));
    }

    [Test]
    public async Task Put_ItemTheTableRejects_RecordsNothing()
    {
        // Arrange: a direct call that fails drops what it staged, as a request does.
        var fake = FakeStreams.UsersOn(new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)), "KEYS_ONLY");
        fake.Put("Users", new Item { ["pk"] = "a" });

        // Act
        var rejected = Throws<ValidationException>(() => fake.Put("Users", new Item { ["pk"] = "b", ["text"] = FakeWrites.Text(410_000) }));

        // Assert
        await Assert.That((rejected, FakeStreams.Records(fake).Count)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task DirectApi_NullArguments_ThrowArgumentNullException()
    {
        // Arrange
        var fake = new FakeDynamoDb();

        // Act
        Action[] calls =
        [
            () => fake.CreateTable((CreateTableRequest)null!),
            () => fake.CreateTable((CreateTableCommand)null!),
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
            () => fake.Get((DynaSharp.Mapping.IKey<Patron>)null!),
            () => fake.Delete((DynaSharp.Mapping.IKey<Patron>)null!),
            () => fake.Delete((Patron)null!),
        ];

        // Assert
        await Assert.That(calls.Count(call => Throws<ArgumentNullException>(call))).IsEqualTo(calls.Length);
    }

    [Test]
    public async Task Requests_TwoClientsOfTheFake_RecordsWhatBothSentInOrder()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable(Users);
        using var first = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();

        // Act
        await first.PutItemAsync(new PutItemRequest { TableName = "Users", Item = AliceKey });
        await streams.ListStreamsAsync(new ListStreamsRequest());

        // Assert
        await Assert.That(string.Join(",", fake.Requests.Select(static request => request.Operation))).IsEqualTo("PutItem,ListStreams");
    }

    [Test]
    public async Task Requests_DirectApiAndAHandlerOfItsOwn_RecordsNothing()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable(Users);
        using var handler = fake.CreateHandler();
        using var client = StubClient.Create(handler);

        // Act
        fake.Put("Users", AliceKey);
        await client.GetItemAsync(new GetItemRequest { TableName = "Users", Key = AliceKey });

        // Assert
        await Assert.That((fake.Requests.Count, handler.Requests.Count)).IsEqualTo((0, 1));
    }

    [Test]
    public async Task Enqueue_Throttle_AnswersTheNextRequestInsteadOfTheFakeAndChangesNothing()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable(Users);
        using var client = fake.CreateClient();
        fake.Enqueue(StubResponse.DynamoDbError("ProvisionedThroughputExceededException", "slow down"));

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.PutItemAsync(new PutItemRequest { TableName = "Users", Item = AliceKey }));
        await client.PutItemAsync(new PutItemRequest { TableName = "Users", Item = AliceKey });

        // Assert
        await Assert.That((fake.Items("Users").Count, fake.Requests.Count)).IsEqualTo((1, 2));
    }

    [Test]
    public async Task Enqueue_Null_ThrowsArgumentNullException()
    {
        // Arrange
        var fake = new FakeDynamoDb();

        // Act
        var act = () => fake.Enqueue(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("response");
    }

    [Test]
    public async Task Reset_RequestsAndAQueuedResponse_EmptiesBoth()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable(Users);
        using var client = fake.CreateClient();
        await client.ListTablesAsync(new ListTablesRequest());
        fake.Enqueue(StubResponse.DynamoDbError("ThrottlingException", "Rate exceeded"));

        // Act
        fake.Reset();
        var tables = await client.ListTablesAsync(new ListTablesRequest());

        // Assert
        await Assert.That((tables.TableNames.Count, fake.Requests.Count)).IsEqualTo((0, 1));
    }

    [Test]
    public async Task CreateTable_Command_AppliesKeysIndexesOfEveryEntityStreamAndTimeToLiveWithoutARequest()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        using var client = fake.CreateClient();
        var command = client.Table<SchemaSession>("Shop").CreateTable().Including<SchemaCustomer>().Stream(StreamViewType.NewImage);

        // Act
        var description = fake.CreateTable(command);

        // Assert
        await Assert.That((
                string.Join(",", description.KeySchema.Select(static key => key.AttributeName)),
                string.Join(",", description.GlobalSecondaryIndexes.Select(static index => index.IndexName).Order(StringComparer.Ordinal)),
                description.StreamSpecification?.StreamViewType,
                fake.Items("Shop").Count,
                fake.Requests.Count))
            .IsEqualTo(("pk,sk", "ByEmail,GSI1", (StreamViewType?)StreamViewType.NewImage, 0, 0));
    }

    [Test]
    public async Task CreateTable_CommandWithTimeToLive_TurnsItOnSoThatTheSweepDeletes()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        using var client = fake.CreateClient();
        fake.CreateTable(client.Table("Users", "pk").CreateTable().TimeToLive("expires"));
        fake.Put("Users", new Item { ["pk"] = "alice", ["expires"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds() });

        // Act
        var expired = fake.ExpireItems();

        // Assert
        await Assert.That(expired).IsEqualTo(1);
    }

    [Test]
    public async Task CreateTable_CommandIfNotExistsOnATableWithoutTimeToLive_KeepsTheItemsAndTurnsTimeToLiveOn()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable("Shop", "pk", "sk");
        fake.Put("Shop", new Item { ["pk"] = "SESSION#a", ["sk"] = "SESSION", ["expires"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds() });
        using var client = fake.CreateClient();

        // Act
        fake.CreateTable(client.Table<SchemaSession>("Shop").CreateTable().IfNotExists());

        // Assert
        await Assert.That(fake.ExpireItems()).IsEqualTo(1);
    }

    [Test]
    public async Task CreateTable_CommandIfNotExistsOnATableWithTimeToLiveElsewhere_LeavesItThere()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        using var client = fake.CreateClient();
        fake.CreateTable(client.Table("Shop", "pk", "sk").CreateTable().TimeToLive("gone"));

        // Act
        fake.CreateTable(client.Table<SchemaSession>("Shop").CreateTable().IfNotExists());
        var answer = await client.DescribeTimeToLiveAsync(new DescribeTimeToLiveRequest { TableName = "Shop" });

        // Assert
        await Assert.That(answer.TimeToLiveDescription!.AttributeName).IsEqualTo("gone");
    }

    [Test]
    public async Task CreateTable_CommandWithoutIfNotExistsOnATableThatExists_ThrowsResourceInUse()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable(Users);
        using var client = fake.CreateClient();
        var command = client.Table("Users", "pk").CreateTable();

        // Act
        var act = () => fake.CreateTable(command);

        // Assert
        await Assert.That(act).ThrowsExactly<ResourceInUseException>();
    }

    [Test]
    public async Task GetOfAKey_StoredEntity_ReadsItFromTheDeclaredTable()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable<Patron>();
        var patron = new Patron("p-1", "Ada");
        fake.Put(patron);

        // Act
        var read = fake.Get(Patron.Keys.Of("p-1"));

        // Assert
        await Assert.That(read).IsEqualTo(patron);
    }

    [Test]
    public async Task DeleteOfAKeyAndOfAnEntity_StoredAndMissing_SayWhetherThereWasOne()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable<Patron>();
        var ada = new Patron("p-1", "Ada");
        var grace = new Patron("p-2", "Grace");
        fake.Put(ada);
        fake.Put(grace);

        // Act
        var deleted = (fake.Delete(Patron.Keys.Of("p-1")), fake.Delete(grace), fake.Delete(grace));

        // Assert
        await Assert.That((deleted, fake.Items("patrons").Count)).IsEqualTo(((true, true, false), 0));
    }

    [Test]
    public async Task TableItems_TableOfSeveralEntities_GivesEveryItemRaw()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable<Patron>();
        fake.Put(new Patron("p-1", "Ada"));
        fake.Put("patrons", new Item { ["pk"] = "other", ["_type"] = "OTHER" });

        // Act
        var items = fake.TableItems<Patron>();

        // Assert
        await Assert.That(items.Select(static item => item["pk"].AsString())).IsEquivalentTo(["other", "p-1"]);
    }

    [Test]
    public async Task GetItem_GeneratedKey_GivesTheStoredItemWithEveryAttribute()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable<Patron>();
        fake.Put("patrons", new Item { ["pk"] = "p-1", ["_type"] = "PATRON", ["Name"] = "Ada", ["legacy"] = 7 });

        // Act
        var item = fake.GetItem(Patron.Keys.Of("p-1"));

        // Assert
        await Assert.That(item).IsEqualTo(new Item { ["pk"] = "p-1", ["_type"] = "PATRON", ["Name"] = "Ada", ["legacy"] = 7 });
    }

    [Test]
    public async Task GetItem_NoItemOnTheKey_ReturnsNull()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable<Patron>();

        // Act
        var item = fake.GetItem(Patron.Keys.Of("p-1"));

        // Assert
        await Assert.That(item).IsNull();
    }

    [Test]
    public async Task PutItem_ItemTheEntityCannotRead_StoresItInTheDeclaredTable()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable<Patron>();
        var item = new Item { ["pk"] = "p-1", ["_type"] = "PATRON", ["Name"] = 42 };

        // Act
        fake.PutItem<Patron>(item);

        // Assert
        await Assert.That(fake.Get("patrons", new Item { ["pk"] = "p-1" })).IsEqualTo(item);
    }

    [Test]
    public async Task DescribeTableOfT_DeclaredTable_DescribesIt()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable<Patron>();

        // Act
        var description = fake.DescribeTable<Patron>();

        // Assert
        await Assert.That(description.TableName).IsEqualTo("patrons");
    }

    [Test]
    public async Task EnableStreamOfTThenDisableStreamOfT_DeclaredTable_TurnsTheStreamOnAndOff()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable<Patron>();

        // Act
        var enabled = fake.EnableStream<Patron>(StreamViewType.KeysOnly);
        var disabled = fake.DisableStream<Patron>();

        // Assert
        await Assert.That((enabled.StreamSpecification?.StreamViewType, disabled.StreamSpecification)).IsEqualTo(((StreamViewType?)StreamViewType.KeysOnly, (StreamSpecification?)null));
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
