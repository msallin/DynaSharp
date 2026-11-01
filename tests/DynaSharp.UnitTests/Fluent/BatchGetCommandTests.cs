using System.Text.Json;
using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Requests;
using DynaSharp.Serialization;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.UnitTests.Fluent.BatchStubs;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Fluent;

public sealed class BatchGetCommandTests
{
    private const string Throttle = "ProvisionedThroughputExceededException";

    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(5);

    [Test]
    [Arguments(1, 1)]
    [Arguments(99, 1)]
    [Arguments(100, 1)]
    [Arguments(101, 2)]
    [Arguments(200, 2)]
    [Arguments(201, 3)]
    public async Task SendAsync_NumberOfKeys_CutsThemInto100PerRequest(int keys, int requests)
    {
        // Arrange
        using var handler = new StubHandler(AnswerKeys);
        using var client = TestClient.Create(handler);
        var ids = Enumerable.Range(0, keys).Select(static i => new TextKey("id-" + i));

        // Act
        var result = await client.Table("Shop", "pk").BatchGet().Keys(ids).SendAsync();

        // Assert
        await Assert.That((handler.Requests.Count, result.Requests, result.Items.Count, handler.Requests.Sum(CountKeys))).IsEqualTo((requests, requests, keys, keys));
    }

    [Test]
    public async Task SendAsync_NoKeys_SendsNothingAndReturnsAnEmptyResult()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").BatchGet().Keys(Array.Empty<TextKey>()).SendAsync();

        // Assert
        await Assert.That((handler.Requests.Count, result.Items.Count, result.Requests, result.ConsumedCapacity.Count)).IsEqualTo((0, 0, 0, 0));
    }

    [Test]
    public async Task SendAsync_Await_SendsIt()
    {
        // Arrange
        using var handler = new StubHandler(AnswerKeys);
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").BatchGet().Key("a");

        // Assert
        await Assert.That(result.Items.Single().GetString("pk")).IsEqualTo("a");
    }

    [Test]
    public async Task SendAsync_Typed_ReadsTheEntities()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":{"Shop":[{"pk":{"S":"CUSTOMER#a"},"fullName":{"S":"Alice"}},{"pk":{"S":"CUSTOMER#b"},"fullName":{"S":"Bob"}}]},"UnprocessedKeys":{}}"""));
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var result = await customers.BatchGet().Key(Customer.Keys.Of("a")).Key(Customer.Keys.Of("b")).SendAsync();

        // Assert
        await Assert.That(result.Items).IsEquivalentTo([new Customer("a", "Alice"), new Customer("b", "Bob")]);
    }

    [Test]
    public async Task SendAsync_EveryOption_WritesThemIntoTheBody()
    {
        // Arrange
        using var handler = new StubHandler(AnswerKeys);
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk", "sk").BatchGet()
            .Key("a", "b")
            .Consistent()
            .Project("status", "address.city")
            .Project(Customer.Attrs.FullName)
            .ReturnCapacity(ReturnConsumedCapacity.Indexes)
            .SendAsync();

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).IsEqualTo(
            """{"RequestItems":{"Shop":{"Keys":[{"pk":{"S":"a"},"sk":{"S":"b"}}],"ConsistentRead":true,"ProjectionExpression":"#n0, #n1.#n2, #n3","ExpressionAttributeNames":{"#n0":"status","#n1":"address","#n2":"city","#n3":"fullName"}}},"ReturnConsumedCapacity":"INDEXES"}""");
    }

    [Test]
    public async Task ToRequestJson_TypedProjection_AddsTheDiscriminatorAndTheKeys()
    {
        // Arrange
        using var handler = new StubHandler(AnswerKeys);
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").BatchGet().Key("a", "b").Project(Customer.Attrs.FullName).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"RequestItems":{"Shop":{"Keys":[{"pk":{"S":"a"},"sk":{"S":"b"}}],"ProjectionExpression":"#n0, #n1, #n2, #n3","ExpressionAttributeNames":{"#n0":"fullName","#n1":"_type","#n2":"pk","#n3":"sk"}}}}""");
    }

    [Test]
    public async Task SendAsync_TypedProjection_AddsTheDiscriminatorAndTheKeys()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Responses":{"Shop":[]}}"""));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table<Customer>("Shop").BatchGet().Key("a", "b").Project(Customer.Attrs.FullName).SendAsync();

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).Contains("""#n0, #n1, #n2, #n3""");
    }

    [Test]
    public async Task SendAsync_OptionsAfterTheKeys_GoIntoEveryRequest()
    {
        // Arrange
        using var handler = new StubHandler(AnswerKeys);
        using var client = TestClient.Create(handler);
        var ids = Enumerable.Range(0, 101).Select(static i => new TextKey("id-" + i));

        // Act
        await client.Table("Shop", "pk").BatchGet().Keys(ids).Consistent().Project("a").SendAsync();

        // Assert
        await Assert.That(handler.Requests.Count(static request => request.BodyText.Contains("\"ConsistentRead\":true,\"ProjectionExpression\":\"#n0\"", StringComparison.Ordinal))).IsEqualTo(2);
    }

    [Test]
    public async Task SendAsync_Capacity_HoldsTheEntriesOfAllRequests()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":{"Shop":[]},"ConsumedCapacity":[{"TableName":"Shop","CapacityUnits":50.0}]}"""));
        using var client = TestClient.Create(handler);
        var ids = Enumerable.Range(0, 101).Select(static i => new TextKey("id-" + i));

        // Act
        var result = await client.Table("Shop", "pk").BatchGet().Keys(ids).ReturnCapacity().SendAsync();

        // Assert
        await Assert.That(result.ConsumedCapacity.Sum(static capacity => capacity.CapacityUnits)).IsEqualTo(100.0);
    }

    [Test]
    public async Task SendAsync_UnknownMember_IsSkipped()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"New":{"a":1},"Responses":{"Shop":[{"pk":{"S":"a"}}]}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").BatchGet().Key("a").SendAsync();

        // Assert
        await Assert.That(result.Items.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_Unprocessed_SendsItBackAsItCame()
    {
        // Arrange (a response at its 16 MB leaves keys; the spaces and the unknown member show that nothing was parsed and written anew)
        const string Left = """{ "Shop" : { "Keys" : [ {"pk":{"S":"b"}} ], "ConsistentRead":true, "ProjectionExpression":"#n0", "ExpressionAttributeNames":{"#n0":"v"}, "Unknown":1 } }""";
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Responses":{"Shop":[{"pk":{"S":"a"}}]},"UnprocessedKeys":""" + Left + "}"));
        handler.Enqueue(StubResponse.Json("""{"Responses":{"Shop":[{"pk":{"S":"b"}}]}}"""));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);

        // Act
        var result = await client.Table("Shop", "pk").BatchGet().Key("a").Key("b").Consistent().Project("v").ReturnCapacity().SendAsync();

        // Assert
        await Assert.That((handler.Requests[1].BodyText, result.Items.Count, result.Requests)).IsEqualTo(("{\"RequestItems\":" + Left + ",\"ReturnConsumedCapacity\":\"TOTAL\"}", 2, 2));
    }

    [Test]
    public async Task SendAsync_Over16MegabytesAcrossRounds_DoesNotCountProgressAgainstTheBound()
    {
        // Arrange (each round answers one key, as a response does whose items reach the 16 MB, far beyond the bound of rounds without progress)
        const int Keys = BatchDriver.MaxStalledRounds + 5;
        using var handler = new StubHandler(request =>
        {
            var keys = JsonElement.Parse(RequestItems(request)).GetProperty("Shop").GetProperty("Keys").EnumerateArray().Select(static key => key.GetRawText()).ToList();
            var left = keys.Count == 1 ? "{}" : "{\"Shop\":{\"Keys\":[" + string.Join(',', keys.Skip(1)) + "]}}";
            return StubResponse.Json("{\"Responses\":{\"Shop\":[" + keys[0] + "]},\"UnprocessedKeys\":" + left + "}");
        });
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var ids = Enumerable.Range(0, Keys).Select(static i => new TextKey("id-" + i));

        // Act
        var result = await client.Table("Shop", "pk").BatchGet().Keys(ids).SendAsync();

        // Assert
        await Assert.That((result.Items.Count, result.Requests)).IsEqualTo((Keys, Keys));
    }

    [Test]
    public async Task SendAsync_NothingMoves_GivesUpWithTheKeysAndWhatWasRead()
    {
        // Arrange (of 150 keys, the first request is answered, and the second gets nothing done)
        using var handler = new StubHandler(LeaveAllKeys);
        handler.Enqueue(StubResponse.Json("""{"Responses":{"Shop":[{"pk":{"S":"id-0"}}]}}"""));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var ids = Enumerable.Range(0, 150).Select(static i => new TextKey("id-" + i));
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<BatchGetIncompleteException<Item>>(async () => await shop.BatchGet().Keys(ids).SendAsync());

        // Assert
        await Assert.That((handler.Requests.Count, failure.Items.Count, failure.UnprocessedKeys["Shop"].Keys.Count)).IsEqualTo((BatchDriver.MaxStalledRounds + 1, 1, 50));
    }

    [Test]
    public async Task SendAsync_ExhaustedInTheMiddle_HoldsWhatIsLeftAndWhatWasNeverSent()
    {
        // Arrange (of 250 keys the second request leaves one for good, and the third is never sent)
        const string Left = """{"UnprocessedKeys":{"Shop":{"Keys":[{"pk":{"S":"id-199"}}],"ConsistentRead":true}}}""";
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        handler.DefaultResponse = StubResponse.Json(Left);
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var ids = Enumerable.Range(0, 250).Select(static i => new TextKey("id-" + i));
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<BatchGetIncompleteException<Item>>(async () => await shop.BatchGet().Keys(ids).Consistent().SendAsync());

        // Assert
        var left = failure.UnprocessedKeys["Shop"];
        await Assert.That((left.Keys.Count, left.Keys[0]["pk"].AsString(), left.Keys[^1]["pk"].AsString(), left.ConsistentRead)).IsEqualTo((51, "id-199", "id-249", true));
    }

    [Test]
    public async Task SendAsync_ExhaustedOnTheLastRequest_HoldsWhatIsLeftOfIt()
    {
        // Arrange
        using var handler = new StubHandler(LeaveAllKeys);
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<BatchGetIncompleteException<Item>>(async () => await shop.BatchGet().Key("a").Project("v").SendAsync());

        // Assert
        await Assert.That((failure.UnprocessedKeys["Shop"].Keys.Count, failure.UnprocessedKeys["Shop"].ProjectionExpression, failure.OperationName, failure.TableName)).IsEqualTo((1, (string?)"#n0", "BatchGetItem", (string?)"Shop"));
    }

    [Test]
    public async Task SendAsync_Exhausted_TheUnprocessedKeysGoIntoTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler(LeaveAllKeys);
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");
        var failure = await TestClient.CaptureAsync<BatchGetIncompleteException<Item>>(async () => await shop.BatchGet().Key("a").Consistent().Project("v").SendAsync());
        handler.DefaultResponse = StubResponse.Json("{}");
        var sent = handler.Requests.Count;

        // Act
        await client.BatchGetItemAsync(new BatchGetItemRequest { RequestItems = failure.UnprocessedKeys });

        // Assert
        await Assert.That(RequestItems(handler.Requests[sent])).IsEqualTo(RequestItems(handler.Requests[0]));
    }

    [Test]
    public async Task SendAsync_ThrottledOnItsLastAttempt_HoldsTheRequestAndTheRest()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Responses":{"Shop":[{"pk":{"S":"id-0"}}]}}"""));
        handler.DefaultResponse = StubResponse.DynamoDbError(Throttle, "Throttled.");
        using var client = TestClient.Create(handler);
        var ids = Enumerable.Range(0, 150).Select(static i => new TextKey("id-" + i));
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<BatchGetIncompleteException<Item>>(async () => await shop.BatchGet().Keys(ids).SendAsync());

        // Assert
        await Assert.That((failure.UnprocessedKeys["Shop"].Keys.Count, failure.Items.Count, failure.InnerException is ProvisionedThroughputExceededException)).IsEqualTo((50, 1, true));
    }

    [Test]
    public async Task SendAsync_AnotherFailure_SurfacesAsItIs()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "No such table."));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<ResourceNotFoundException>(async () => await client.Table("Shop", "pk").BatchGet().Key("a").SendAsync());

        // Assert
        await Assert.That(failure.OperationName).IsEqualTo("BatchGetItem");
    }

    [Test]
    public async Task SendAsync_DuplicatesInOneRequest_SendsThemForDynamoDBToReject()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "Provided list of item keys contains duplicates"));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await client.Table("Shop", "pk").BatchGet().Key("a").Key("a").SendAsync());

        // Assert
        await Assert.That((CountKeys(handler.Requests.Single()), failure.Message)).IsEqualTo((2, "Provided list of item keys contains duplicates"));
    }

    [Test]
    public async Task SendAsync_DuplicatesAcrossRequests_ReadsThemTwice()
    {
        // Arrange (key 0 and key 100 are the same, which only two requests can hold)
        using var handler = new StubHandler(AnswerKeys);
        using var client = TestClient.Create(handler);
        var ids = Enumerable.Range(0, 101).Select(static i => new TextKey("id-" + (i % 100)));

        // Act
        var result = await client.Table("Shop", "pk").BatchGet().Keys(ids).SendAsync();

        // Assert
        await Assert.That(result.Items.Count(static item => item.GetString("pk") == "id-0")).IsEqualTo(2);
    }

    [Test]
    public async Task SendAsync_ItemOfAnotherType_ThrowsMappingException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":{"Shop":[{"pk":{"S":"CUSTOMER#a"},"_type":{"S":"ORDER"},"fullName":{"S":"Alice"}}]}}"""));
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await customers.BatchGet().Key(Customer.Keys.Of("a")).SendAsync());

        // Assert
        await Assert.That(failure.EntityType).IsEqualTo(nameof(Customer));
    }

    [Test]
    public async Task SendAsync_WaitBetweenRounds_FollowsTheJitterOfTheRetryPolicyOnTheClockOfTheClient()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(LeaveAllKeys);
        using var client = TestClient.Create(handler, time, attemptTimeout: AttemptTimeout, random: FixedRandom.One);

        // Act
        var call = Send(client.Table("Shop", "pk"), CancellationToken.None);
        var delays = await AdvanceThroughDelaysAsync(call, time, AttemptTimeout);

        // Assert
        await Assert.That(delays.Select(static delay => delay.TotalMilliseconds)).IsEquivalentTo([100.0, 200.0, 400.0, 800.0, 1600.0, 3200.0, 6400.0]);
    }

    [Test]
    public async Task SendAsync_CancelledDuringTheWait_ThrowsAndGivesTheStateBack()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(LeaveAllKeys);
        using var client = TestClient.Create(handler, time, pool, AttemptTimeout, random: FixedRandom.One);
        using var cancellation = new CancellationTokenSource();

        // Act
        var call = Send(client.Table("Shop", "pk"), cancellation.Token);
        while (time.NextDueIn is null || time.NextDueIn >= AttemptTimeout)
        {
            await Task.Yield();
        }

        await cancellation.CancelAsync();
        var failure = await TestClient.CaptureAsync<OperationCanceledException>(() => call);
        client.Dispose();

        // Assert
        await Assert.That((failure.CancellationToken == cancellation.Token, pool.Outstanding)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task Key_EmptyKey_ThrowsAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.BatchGet().Key(string.Empty));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentException>();
    }

    [Test]
    public async Task Key_NullKey_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.BatchGet().Key((TextKey)null!));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task Keys_NullElement_NamesTheCollection()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        var keys = new TextKey[] { null! };

        // Act
        var failure = TestClient.Catch(() => _ = shop.BatchGet().Keys(keys));

        // Assert
        await Assert.That((failure as ArgumentNullException)?.ParamName).IsEqualTo("keys");
    }

    [Test]
    public async Task Keys_Null_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.BatchGet().Keys((IEnumerable<TextKey>)null!));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task MaxAttempts_Zero_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.BatchGet().MaxAttempts(0));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(int.MinValue)]
    [Arguments(-1)]
    [Arguments(0)]
    public async Task MaxStalledRounds_BelowOne_Throws(int rounds)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.BatchGet().MaxStalledRounds(rounds));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task MaxStalledRounds_One_GivesUpOnTheFirstUnprocessed()
    {
        // Arrange
        using var handler = new StubHandler(LeaveAllKeys);
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        await TestClient.CaptureAsync<BatchGetIncompleteException<Item>>(async () => await shop.BatchGet().Key("a").MaxStalledRounds(1).SendAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task MaxStalledRounds_AboveTheDefault_SendsThatManyRequests()
    {
        // Arrange
        const int Rounds = BatchDriver.MaxStalledRounds + 2;
        using var handler = new StubHandler(LeaveAllKeys);
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");

        // Act
        await TestClient.CaptureAsync<BatchGetIncompleteException<Item>>(async () => await shop.BatchGet().Key("a").MaxStalledRounds(Rounds).SendAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(Rounds);
    }

    [Test]
    public async Task MaxAttempts_One_KeepsTheDefaultBoundOfRounds()
    {
        // Arrange
        using var handler = new StubHandler(LeaveAllKeys);
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");

        // Act
        await TestClient.CaptureAsync<BatchGetIncompleteException<Item>>(async () => await shop.BatchGet().Key("a").MaxAttempts(1).SendAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(BatchDriver.MaxStalledRounds);
    }

    [Test]
    public async Task ToRequestJson_MoreThanOneRequest_RendersTheFirst()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var ids = Enumerable.Range(0, 101).Select(static i => new TextKey("id-" + i));

        // Act
        var json = client.Table("Shop", "pk").BatchGet().Keys(ids).ToRequestJson();

        // Assert
        await Assert.That(JsonElement.Parse(json).GetProperty("RequestItems").GetProperty("Shop").GetProperty("Keys").GetArrayLength()).IsEqualTo(100);
    }

    [Test]
    public async Task ToRequestJson_NoKeys_RendersAnEmptyRequest()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").BatchGet().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"RequestItems":{}}""");
    }

    [Test]
    public async Task Default_Used_ThrowsAndSaysWhy()
    {
        // Arrange (nothing to arrange)

        // Act
        var failure = TestClient.Catch(static () => _ = default(BatchGetCommand<Item>).Consistent());

        // Assert
        await Assert.That(failure.Message).Contains("default value");
    }

    [Test]
    public async Task DebuggerText_WhileBuilding_ShowsTheKeysSoFar()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var text = client.Table("Shop", "pk").BatchGet().Key("a").DebuggerText;

        // Assert
        await Assert.That(text).IsEqualTo("""BatchGetItem on Shop: {"pk":{"S":"a"}}""");
    }

    private static Task<BatchGetResult<Item>> Send(Table<Item> table, CancellationToken cancellationToken) =>
        table.BatchGet().Key("a").SendAsync(cancellationToken).AsTask();

    // A key of an untyped table, which a caller writes by hand; a class, so that it can be null.
    private sealed class TextKey(string partitionKey) : IKey<Item>
    {
        public void WriteKey(DynamoDbWriter writer)
        {
            writer.WriteAttributeName("pk"u8);
            writer.WriteString(partitionKey);
        }
    }
}
