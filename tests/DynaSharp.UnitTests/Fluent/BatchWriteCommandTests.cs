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

public sealed class BatchWriteCommandTests
{
    private const string Throttle = "ProvisionedThroughputExceededException";

    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(5);

    [Test]
    [Arguments(1, 1)]
    [Arguments(24, 1)]
    [Arguments(25, 1)]
    [Arguments(26, 2)]
    [Arguments(50, 2)]
    [Arguments(51, 3)]
    public async Task SendAsync_NumberOfWrites_CutsThemInto25PerRequest(int writes, int requests)
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").BatchWrite();
        for (var i = 0; i < writes; i++)
        {
            command = command.Put(Numbered(i));
        }

        // Act
        var result = await command.SendAsync();

        // Assert
        await Assert.That((handler.Requests.Count, result.Requests, result.Written, handler.Requests.Sum(CountWrites))).IsEqualTo((requests, requests, writes, writes));
    }

    [Test]
    public async Task SendAsync_26Writes_KeepsTheirOrderAcrossRequests()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").BatchWrite();
        for (var i = 0; i < 26; i++)
        {
            command = command.Put(Numbered(i));
        }

        // Act
        await command.SendAsync();

        // Assert
        var sent = handler.Requests.SelectMany(static request => JsonElement.Parse(RequestItems(request)).GetProperty("Shop").EnumerateArray())
            .Select(static write => write.GetProperty("PutRequest").GetProperty("Item").GetProperty("pk").GetProperty("S").GetString()!);
        await Assert.That(sent).IsEquivalentTo(Enumerable.Range(0, 26).Select(static i => "item-" + i), TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task SendAsync_MoreWritesThanAnIdleListKeeps_LeavesTheStateFitForTheNextBatch()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        await SendMany(shop, 3000);

        // Act
        var result = await SendMany(shop, 30);

        // Assert
        await Assert.That((result.Written, CountWrites(handler.Requests[^1]))).IsEqualTo((30, 5));
    }

    [Test]
    public async Task SendAsync_NoWrites_SendsNothingAndReturnsAnEmptyResult()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").BatchWrite().SendAsync();

        // Assert
        await Assert.That((handler.Requests.Count, result.Written, result.Requests, result.ConsumedCapacity.Count, result.ItemCollectionMetrics.Count)).IsEqualTo((0, 0, 0, 0, 0));
    }

    [Test]
    public async Task SendAsync_Await_SendsIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").BatchWrite().Put(Numbered(1));

        // Assert
        await Assert.That(result.Written).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_PutAndDeleteOfEveryForm_WritesTheBodyOfThem()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        await customers.BatchWrite()
            .Put(new Customer("a", "Alice"))
            .Delete(Customer.Keys.Of("b"))
            .Delete(Customer.Keys.Of("c"))
            .SendAsync();

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).IsEqualTo(
            """{"RequestItems":{"Shop":[{"PutRequest":{"Item":{"pk":{"S":"CUSTOMER#a"},"sk":{"S":"PROFILE"},"_type":{"S":"CUSTOMER"},"fullName":{"S":"Alice"},"orderCount":{"N":"0"}}}},{"DeleteRequest":{"Key":{"pk":{"S":"CUSTOMER#b"},"sk":{"S":"PROFILE"}}}},{"DeleteRequest":{"Key":{"pk":{"S":"CUSTOMER#c"},"sk":{"S":"PROFILE"}}}}]}}""");
    }

    [Test]
    public async Task SendAsync_OtherTypeOnTheSameTable_GoesIntoOneList()
    {
        // Arrange (Of gives another view of the table, and DynamoDB allows a table once in RequestItems)
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        var customers = shop.Of<Customer>();

        // Act
        await shop.BatchWrite().Put(new Item { ["pk"] = "x", ["sk"] = "y" }).Put(customers, new Customer("a", "Alice")).Delete("z", "y").Delete(customers, Customer.Keys.Of("b")).SendAsync();

        // Assert
        using var document = JsonDocument.Parse(RequestItems(handler.Requests.Single()));
        await Assert.That((document.RootElement.EnumerateObject().Count(), document.RootElement.GetProperty("Shop").GetArrayLength())).IsEqualTo((1, 4));
    }

    [Test]
    public async Task SendAsync_OtherTables_GroupsTheWritesByTable()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var first = client.Table("First", "pk");
        var second = client.Table("Second", "pk");
        var third = client.Table("Third", "pk");
        var fourth = client.Table("Fourth", "pk");
        var fifth = client.Table("Fifth", "pk");

        // Act
        await first.BatchWrite().Put(Numbered(1)).Put(second, Numbered(2)).Delete(first, "a").Delete(third, "b").Delete(fourth, "c").Delete(fifth, "d").Put(second, Numbered(3)).SendAsync();

        // Assert
        using var document = JsonDocument.Parse(RequestItems(handler.Requests.Single()));
        var root = document.RootElement;
        await Assert.That((root.GetProperty("First").GetArrayLength(), root.GetProperty("Second").GetArrayLength(), root.EnumerateObject().Count())).IsEqualTo((2, 2, 5));
    }

    [Test]
    public async Task SendAsync_RequestThatHoldsOneOfTwoTables_NamesOnlyThatTable()
    {
        // Arrange (the second table is only in the writes of the second request)
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var first = client.Table("First", "pk");
        var second = client.Table("Second", "pk");
        var command = first.BatchWrite();
        for (var i = 0; i < 25; i++)
        {
            command = command.Put(Numbered(i));
        }

        // Act
        await command.Put(second, Numbered(25)).SendAsync();

        // Assert
        await Assert.That(RequestItems(handler.Requests[1])).DoesNotContain("First");
    }

    [Test]
    public async Task SendAsync_Options_GoIntoEveryRequest()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").BatchWrite().ReturnCapacity(ReturnConsumedCapacity.Indexes).ReturnMetrics();
        for (var i = 0; i < 26; i++)
        {
            command = command.Put(Numbered(i));
        }

        // Act
        await command.SendAsync();

        // Assert
        await Assert.That(handler.Requests.Count(static request => request.BodyText.EndsWith(""","ReturnConsumedCapacity":"INDEXES","ReturnItemCollectionMetrics":"SIZE"}""", StringComparison.Ordinal))).IsEqualTo(2);
    }

    [Test]
    public async Task SendAsync_Capacity_AddsUpTheEntriesOfAllRequests()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"ConsumedCapacity":[{"TableName":"Shop","CapacityUnits":25.0}]}"""));
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").BatchWrite().ReturnCapacity();
        for (var i = 0; i < 26; i++)
        {
            command = command.Put(Numbered(i));
        }

        // Act
        var result = await command.SendAsync();

        // Assert
        await Assert.That(result.ConsumedCapacity.Sum(static capacity => capacity.CapacityUnits)).IsEqualTo(50.0);
    }

    [Test]
    public async Task SendAsync_Metrics_MergesThemByTable()
    {
        // Arrange (the first request reports one table, the second that table and another)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"ItemCollectionMetrics":{"Shop":[{"ItemCollectionKey":{"pk":{"S":"a"}},"SizeEstimateRangeGB":[0.0,1.0]}]}}"""));
        handler.Enqueue(StubResponse.Json("""{"ItemCollectionMetrics":{"Shop":[{"ItemCollectionKey":{"pk":{"S":"b"}},"SizeEstimateRangeGB":[0.0,1.0]}],"Audit":[{"ItemCollectionKey":{"pk":{"S":"c"}},"SizeEstimateRangeGB":[0.0,1.0]}]}}"""));
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").BatchWrite().ReturnMetrics();
        for (var i = 0; i < 26; i++)
        {
            command = command.Put(Numbered(i));
        }

        // Act
        var result = await command.SendAsync();

        // Assert
        await Assert.That((result.ItemCollectionMetrics["Shop"].Count, result.ItemCollectionMetrics["Audit"].Count)).IsEqualTo((2, 1));
    }

    [Test]
    public async Task SendAsync_Unprocessed_SendsItBackAsItCame()
    {
        // Arrange (the extra spaces and the unknown member show that nothing was parsed and written anew)
        const string Left = """{ "Shop" : [ {"PutRequest":{"Item":{"pk":{"S":"item-1"}}, "Unknown":1}} ] }""";
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{\"UnprocessedItems\":" + Left + "}"));
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);

        // Act
        await client.Table("Shop", "pk").BatchWrite().Put(Numbered(0)).Put(Numbered(1)).ReturnCapacity().SendAsync();

        // Assert
        await Assert.That(handler.Requests[1].BodyText).IsEqualTo("{\"RequestItems\":" + Left + ",\"ReturnConsumedCapacity\":\"TOTAL\"}");
    }

    [Test]
    public async Task SendAsync_UnprocessedThatEnds_ReportsEveryWriteAndEveryRequest()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"UnprocessedItems":{"Shop":[{"PutRequest":{"Item":{"pk":{"S":"item-1"}}}}]}}"""));
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);

        // Act
        var result = await client.Table("Shop", "pk").BatchWrite().Put(Numbered(0)).Put(Numbered(1)).SendAsync();

        // Assert
        await Assert.That((result.Written, result.Requests)).IsEqualTo((2, 2));
    }

    [Test]
    public async Task SendAsync_UnprocessedOfTheFirstRequest_ComesBeforeTheNextRequest()
    {
        // Arrange (each request is done before the next starts)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"UnprocessedItems":{"Shop":[{"PutRequest":{"Item":{"pk":{"S":"item-0"}}}}]}}"""));
        handler.DefaultResponse = StubResponse.Json("{}");
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var command = client.Table("Shop", "pk").BatchWrite();
        for (var i = 0; i < 26; i++)
        {
            command = command.Put(Numbered(i));
        }

        // Act
        await command.SendAsync();

        // Assert
        await Assert.That(handler.Requests.Select(CountWrites)).IsEquivalentTo([25, 1, 1]);
    }

    [Test]
    public async Task SendAsync_EmptyUnprocessed_EndsTheRequest()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"UnprocessedItems":{},"Other":[1]}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").BatchWrite().Put(Numbered(0)).SendAsync();

        // Assert
        await Assert.That(result.Requests).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_TableWithoutWritesInUnprocessed_EndsTheRequest()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"UnprocessedItems":{"Shop":[]}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").BatchWrite().Put(Numbered(0)).SendAsync();

        // Assert
        await Assert.That(result.Requests).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_NothingMoves_GivesUpAfterTheBoundWithEverythingUnprocessed()
    {
        // Arrange
        using var handler = new StubHandler(LeaveAllWrites);
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);

        // Act
        var failure = await TestClient.CaptureAsync<BatchIncompleteException>(async () => await client.Table("Shop", "pk").BatchWrite().Put(Numbered(0)).Put(Numbered(1)).SendAsync());

        // Assert
        await Assert.That((handler.Requests.Count, failure.Written, failure.UnprocessedItems["Shop"].Count)).IsEqualTo((BatchDriver.MaxStalledRounds, 0, 2));
    }

    [Test]
    public async Task SendAsync_SlowProgress_DoesNotGiveUpWhileSomethingMoves()
    {
        // Arrange (one write per round, far beyond the bound of rounds without progress)
        const int Writes = BatchDriver.MaxStalledRounds + 5;
        var left = Writes;
        using var handler = new StubHandler(request =>
        {
            left--;
            var writes = JsonElement.Parse(RequestItems(request)).GetProperty("Shop").EnumerateArray().Skip(1).Select(static write => write.GetRawText());
            return StubResponse.Json(left == 0 ? "{}" : "{\"UnprocessedItems\":{\"Shop\":[" + string.Join(',', writes) + "]}}");
        });
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var command = client.Table("Shop", "pk").BatchWrite();
        for (var i = 0; i < Writes; i++)
        {
            command = command.Put(Numbered(i));
        }

        // Act
        var result = await command.SendAsync();

        // Assert
        await Assert.That((result.Written, result.Requests)).IsEqualTo((Writes, Writes));
    }

    [Test]
    public async Task SendAsync_ProgressThenNothingMoves_CountsTheBoundFromTheLastProgress()
    {
        // Arrange (the first request takes one of three, the rest nothing)
        using var handler = new StubHandler(LeaveAllWrites);
        handler.Enqueue(StubResponse.Json("""{"UnprocessedItems":{"Shop":[{"PutRequest":{"Item":{"pk":{"S":"item-1"}}}},{"PutRequest":{"Item":{"pk":{"S":"item-2"}}}}]}}"""));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);

        // Act
        var failure = await TestClient.CaptureAsync<BatchIncompleteException>(async () => await client.Table("Shop", "pk").BatchWrite().Put(Numbered(0)).Put(Numbered(1)).Put(Numbered(2)).SendAsync());

        // Assert
        await Assert.That((handler.Requests.Count, failure.Written, failure.UnprocessedItems["Shop"].Count)).IsEqualTo((BatchDriver.MaxStalledRounds + 1, 1, 2));
    }

    [Test]
    public async Task SendAsync_MaxStalledRoundsOne_GivesUpOnTheFirstUnprocessed()
    {
        // Arrange
        using var handler = new StubHandler(LeaveAllWrites);
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);

        // Act
        var failure = await TestClient.CaptureAsync<BatchIncompleteException>(async () => await client.Table("Shop", "pk").BatchWrite().Put(Numbered(0)).MaxStalledRounds(1).SendAsync());

        // Assert
        await Assert.That((handler.Requests.Count, failure.UnprocessedItems["Shop"].Count)).IsEqualTo((1, 1));
    }

    [Test]
    public async Task SendAsync_MaxStalledRoundsAboveTheDefault_SendsThatManyRequests()
    {
        // Arrange
        const int Rounds = BatchDriver.MaxStalledRounds + 2;
        using var handler = new StubHandler(LeaveAllWrites);
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);

        // Act
        await TestClient.CaptureAsync<BatchIncompleteException>(async () => await client.Table("Shop", "pk").BatchWrite().Put(Numbered(0)).MaxStalledRounds(Rounds).SendAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(Rounds);
    }

    [Test]
    public async Task SendAsync_MaxStalledRounds_LeavesTheAttemptsOfARequestAlone()
    {
        // Arrange (a server error is an attempt, not a round)
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);

        // Act
        await client.Table("Shop", "pk").BatchWrite().Put(Numbered(0)).MaxStalledRounds(1).MaxAttempts(2).SendAsync();

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SendAsync_MaxAttemptsOne_KeepsTheDefaultBoundOfRounds()
    {
        // Arrange
        using var handler = new StubHandler(LeaveAllWrites);
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);

        // Act
        await TestClient.CaptureAsync<BatchIncompleteException>(async () => await client.Table("Shop", "pk").BatchWrite().Put(Numbered(0)).MaxAttempts(1).SendAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(BatchDriver.MaxStalledRounds);
    }

    [Test]
    public async Task SendAsync_MaxAttempts_IsTheAttemptsOfEachRequest()
    {
        // Arrange (the client alone would make one attempt)
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);

        // Act
        await client.Table("Shop", "pk").BatchWrite().Put(Numbered(0)).MaxAttempts(2).SendAsync();

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SendAsync_AfterACommandWithItsOwnBound_UsesTheDefaultBound()
    {
        // Arrange (the state goes back to the pool, and the next batch takes it)
        using var handler = new StubHandler(LeaveAllWrites);
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");
        shop.BatchWrite().Put(Numbered(0)).MaxStalledRounds(1).ToRequestJson();

        // Act
        await TestClient.CaptureAsync<BatchIncompleteException>(async () => await shop.BatchWrite().Put(Numbered(0)).SendAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(BatchDriver.MaxStalledRounds);
    }

    [Test]
    public async Task SendAsync_ExhaustedInTheMiddle_HoldsWhatIsLeftOfTheRequestAndWhatWasNeverSent()
    {
        // Arrange (of 60 writes, the first request is taken, the second leaves its last write for good, and the third is never sent)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        handler.Enqueue(StubResponse.Json("""{"UnprocessedItems":{"Shop":[{"PutRequest":{"Item":{"pk":{"S":"item-49"},"index":{"N":"49"}}}}]}}"""));
        handler.DefaultResponse = new StubResponse { Body = """{"UnprocessedItems":{"Shop":[{"PutRequest":{"Item":{"pk":{"S":"item-49"},"index":{"N":"49"}}}}]}}""" };
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<BatchIncompleteException>(async () => await SendMany(shop, 60));

        // Assert
        var left = failure.UnprocessedItems["Shop"].Select(static write => write.PutRequest!.Item["pk"].AsString()).ToList();
        await Assert.That((failure.Written, left.Count, left[0], left[^1])).IsEqualTo((49, 11, "item-49", "item-59"));
    }

    [Test]
    public async Task SendAsync_ExhaustedOnOneTable_KeepsTheWritesOfTheOthersApart()
    {
        // Arrange
        using var handler = new StubHandler(LeaveAllWrites);
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var first = client.Table("First", "pk");
        var second = client.Table("Second", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<BatchIncompleteException>(async () => await SendMany(first, 29, second));

        // Assert
        await Assert.That((failure.UnprocessedItems["First"].Count, failure.UnprocessedItems["Second"].Count, failure.TableName)).IsEqualTo((29, 1, (string?)null));
    }

    [Test]
    public async Task SendAsync_Exhausted_TheUnprocessedItemsGoIntoTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler(LeaveAllWrites);
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var failure = await TestClient.CaptureAsync<BatchIncompleteException>(async () => await client.Table("Shop", "pk").BatchWrite().Put(Numbered(7)).Delete("gone").SendAsync());
        handler.DefaultResponse = StubResponse.Json("{}");
        var sent = handler.Requests.Count;

        // Act
        await client.BatchWriteItemAsync(new BatchWriteItemRequest { RequestItems = failure.UnprocessedItems });

        // Assert
        await Assert.That(RequestItems(handler.Requests[sent])).IsEqualTo(RequestItems(handler.Requests[0]));
    }

    [Test]
    public async Task SendAsync_Exhausted_NamesTheOperationAndTheTableAndNoItem()
    {
        // Arrange
        using var handler = new StubHandler(LeaveAllWrites);
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);

        // Act
        var failure = await TestClient.CaptureAsync<BatchIncompleteException>(async () => await client.Table("Shop", "pk").BatchWrite().Put(new Item { ["pk"] = "secret" }).SendAsync());

        // Assert
        await Assert.That((failure.OperationName, failure.TableName, failure.ErrorCode, failure.Message.Contains("secret", StringComparison.Ordinal))).IsEqualTo(("BatchWriteItem", (string?)"Shop", "BatchIncomplete", false));
    }

    [Test]
    public async Task SendAsync_ThrottledOnItsLastAttempt_HoldsTheRequestAndTheRest()
    {
        // Arrange (DynamoDB answers a request of which it can take nothing with this error, and the client has one attempt)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        handler.DefaultResponse = StubResponse.DynamoDbError(Throttle, "Throttled.");
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<BatchIncompleteException>(async () => await SendMany(shop, 30));

        // Assert
        await Assert.That((failure.Written, failure.UnprocessedItems["Shop"].Count, failure.InnerException is ProvisionedThroughputExceededException)).IsEqualTo((25, 5, true));
    }

    [Test]
    public async Task SendAsync_ThrottledAfterAPartOfTheRequest_HoldsWhatWasLeftAndTheRest()
    {
        // Arrange (the first response leaves one of 25, and the repeat of it is throttled)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"UnprocessedItems":{"Shop":[{"PutRequest":{"Item":{"pk":{"S":"item-24"}}}}]}}"""));
        handler.DefaultResponse = StubResponse.DynamoDbError(Throttle, "Throttled.");
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<BatchIncompleteException>(async () => await SendMany(shop, 30));

        // Assert
        await Assert.That((failure.Written, failure.UnprocessedItems["Shop"].Count)).IsEqualTo((24, 6));
    }

    [Test]
    public async Task Put_ValueWithALoneSurrogate_ThrowsArgumentExceptionAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var act = () => shop.BatchWrite().Put(new Item { ["pk"] = "a", ["note"] = "b\uD800" }).SendAsync();

        // Assert
        await Assert.That(() => act().AsTask()).ThrowsExactly<ArgumentException>();
        await Assert.That(handler.Requests.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_RejectedOnTheFirstRequest_SurfacesAsItIs()
    {
        // Arrange (nothing was applied, so there is nothing to report but the failure)
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "Provided list of item keys contains duplicates"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await SendMany(shop, 27));

        // Assert
        await Assert.That((handler.Requests.Count, failure.Expressions)).IsEqualTo((1, (IReadOnlyDictionary<string, string>?)null));
    }

    [Test]
    public async Task SendAsync_NetworkFailureOnTheFirstRequest_SurfacesAsItIs()
    {
        // Arrange
        using var handler = new StubHandler(_ => throw new HttpRequestException("No route."));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<HttpRequestException>(async () => await SendMany(shop, 27));

        // Assert
        await Assert.That(failure.Message).IsEqualTo("No route.");
    }

    [Test]
    public async Task SendAsync_RejectedAfterARequestWentThrough_HoldsTheProgressAndTheFailure()
    {
        // Arrange (one key twice in the second request: DynamoDB rejects it, and the first stays applied)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        handler.DefaultResponse = StubResponse.DynamoDbError("ValidationException", "Provided list of item keys contains duplicates");
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<BatchIncompleteException>(async () => await SendMany(shop, 27));

        // Assert
        await Assert.That((failure.Written, failure.UnprocessedItems["Shop"].Count, failure.InnerException is ValidationException, handler.Requests.Count)).IsEqualTo((25, 2, true, 2));
    }

    [Test]
    public async Task SendAsync_RejectedAfterAPartOfTheRequest_HoldsWhatWasLeftAndTheRest()
    {
        // Arrange (the first response leaves one of 25, and the repeat of it is rejected)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"UnprocessedItems":{"Shop":[{"PutRequest":{"Item":{"pk":{"S":"item-24"}}}}]}}"""));
        handler.DefaultResponse = StubResponse.DynamoDbError("ResourceNotFoundException", "Deleted meanwhile.");
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<BatchIncompleteException>(async () => await SendMany(shop, 30));

        // Assert
        await Assert.That((failure.Written, failure.UnprocessedItems["Shop"].Count, failure.InnerException is ResourceNotFoundException)).IsEqualTo((24, 6, true));
    }

    [Test]
    public async Task SendAsync_ResponseThatIsNoResponseAfterAPartOfTheRequest_HoldsWhatWasLeftAndTheRest()
    {
        // Arrange (the first response leaves one of 25, and the answer to its repeat cannot be read)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"UnprocessedItems":{"Shop":[{"PutRequest":{"Item":{"pk":{"S":"item-24"}}}}]}}"""));
        handler.DefaultResponse = StubResponse.Json("""{"UnprocessedItems":{"Shop":{}}}""");
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<BatchIncompleteException>(async () => await SendMany(shop, 30));

        // Assert
        await Assert.That((failure.Written, failure.UnprocessedItems["Shop"].Count, failure.InnerException is DynamoDbException { ErrorCode: "UnrecognizedResponse" }, failure.Message))
            .IsEqualTo((24, 6, true, "DynamoDB took 24 of 30 writes in 2 requests and left the rest unprocessed."));
    }

    [Test]
    public async Task SendAsync_NetworkFailureAfterARequestWentThrough_HoldsTheProgressAndTheFailure()
    {
        // Arrange
        var calls = 0;
        using var handler = new StubHandler(_ => ++calls > 1 ? throw new HttpRequestException("No route.") : StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<BatchIncompleteException>(async () => await SendMany(shop, 27));

        // Assert
        await Assert.That((failure.Written, failure.UnprocessedItems["Shop"].Count, failure.InnerException is HttpRequestException)).IsEqualTo((25, 2, true));
    }

    [Test]
    public async Task SendAsync_CancelledAfterARequestWentThrough_ThrowsWithTheTokenOfTheCaller()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var calls = 0;
        using var handler = new StubHandler(async (_, cancelled) =>
        {
            if (++calls == 1)
            {
                return StubResponse.Json("{}");
            }

            await cancellation.CancelAsync();
            cancelled.ThrowIfCancellationRequested();
            return StubResponse.Json("{}");
        });
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<OperationCanceledException>(async () => await SendMany(shop, 27, cancellationToken: token));

        // Assert
        await Assert.That((failure.CancellationToken == token, handler.Requests.Count)).IsEqualTo((true, 2));
    }

    [Test]
    public async Task SendAsync_DuplicatesInOneRequest_SendsThemAsTheyAre()
    {
        // Arrange (no key is looked at on the way: it would cost a read of every item)
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk").BatchWrite().Put(Numbered(1)).Put(Numbered(1)).Delete("item-1").SendAsync();

        // Assert
        await Assert.That(CountWrites(handler.Requests.Single())).IsEqualTo(3);
    }

    [Test]
    public async Task SendAsync_WaitBetweenRounds_FollowsTheJitterOfTheRetryPolicyOnTheClockOfTheClient()
    {
        // Arrange (full jitter at its largest, so the waits are the ceilings of throttling: 100 ms, 200 ms, and so on; the first request has no wait before it)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(LeaveAllWrites);
        using var client = TestClient.Create(handler, time, attemptTimeout: AttemptTimeout, random: FixedRandom.One);

        // Act
        var call = Send(client.Table("Shop", "pk"));
        var delays = await AdvanceThroughDelaysAsync(call, time, AttemptTimeout);

        // Assert
        await Assert.That(delays.Select(static delay => delay.TotalMilliseconds)).IsEquivalentTo([100.0, 200.0, 400.0, 800.0, 1600.0, 3200.0, 6400.0]);
    }

    [Test]
    public async Task SendAsync_Unprocessed_TakesNothingFromTheRetryQuota()
    {
        // Arrange (the quota lasts for a hundred retries, so these rounds would use it up, and the last request would not be retried)
        using var handler = new StubHandler(LeaveAllWrites);
        using var client = TestClient.Create(handler, random: FixedRandom.Zero, maxAttempts: 2);
        for (var i = 0; i < 20; i++)
        {
            await TestClient.CaptureAsync<BatchIncompleteException>(async () => await client.Table("Shop", "pk").BatchWrite().Put(Numbered(i)).SendAsync());
        }

        handler.Enqueue(ServerError());
        handler.Enqueue(StubResponse.Json("{}"));

        // Act
        var result = await client.Table("Shop", "pk").BatchWrite().Put(Numbered(0)).SendAsync();

        // Assert
        await Assert.That(result.Written).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_CancelledDuringTheWait_ThrowsWithTheTokenOfTheCaller()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(LeaveAllWrites);
        using var client = TestClient.Create(handler, time, attemptTimeout: AttemptTimeout, random: FixedRandom.One);
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        // Act
        var call = SendWith(client.Table("Shop", "pk"), token);
        await WaitForTimerAsync(time);
        await cancellation.CancelAsync();
        var failure = await TestClient.CaptureAsync<OperationCanceledException>(() => call);

        // Assert
        await Assert.That((failure.CancellationToken == token, handler.Requests.Count)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task SendAsync_CancelledDuringTheWait_GivesTheStateBack()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(LeaveAllWrites);
        using var client = TestClient.Create(handler, time, pool, AttemptTimeout, random: FixedRandom.One);
        using var cancellation = new CancellationTokenSource();

        // Act
        var call = SendWith(client.Table("Shop", "pk"), cancellation.Token);
        await WaitForTimerAsync(time);
        await cancellation.CancelAsync();
        await TestClient.CaptureAsync<OperationCanceledException>(() => call);
        client.Dispose();

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_CancelledBeforeTheStart_ThrowsAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var call = SendWith(client.Table("Shop", "pk"), cancellation.Token);
        await TestClient.CaptureAsync<OperationCanceledException>(() => call);

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_DisposedClient_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        client.Dispose();

        // Act
        var call = Send(shop);
        var failure = await TestClient.CaptureAsync<ObjectDisposedException>(() => call);

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task SendAsync_ResponseThatIsNoResponse_FailsAsUnrecognized()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"UnprocessedItems":{"Shop":{}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbException>(async () => await client.Table("Shop", "pk").BatchWrite().Put(Numbered(0)).SendAsync());

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("UnrecognizedResponse");
    }

    [Test]
    public async Task SendAsync_SecondTime_ThrowsWithoutAnotherRequest()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var misused = SendTwice(client.Table("Shop", "pk"));
        await misused.First;

        // Assert
        await Assert.That((misused.SecondThrew, handler.Requests.Count)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task Put_ChangedAfterwards_SendsWhatItWas()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var item = Numbered(1);
        var command = client.Table("Shop", "pk").BatchWrite().Put(item);

        // Act
        item["pk"] = "changed";
        await command.SendAsync();

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).DoesNotContain("changed");
    }

    [Test]
    public async Task PutAll_ItemsOfASequence_WritesWhatPutsInAChainWrite()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");
        Customer[] items = [new("a", "Alice"), new("b", "Bob")];
        var chain = customers.BatchWrite().Put(items[0]).Put(items[1]).ToRequestJson();

        // Act
        var json = customers.BatchWrite().PutAll(items).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(chain);
    }

    [Test]
    [Arguments(0, 0)]
    [Arguments(25, 1)]
    [Arguments(26, 2)]
    public async Task SendAsync_PutAllOfManyItems_CutsThemInto25PerRequest(int writes, int requests)
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var result = await shop.BatchWrite().PutAll(Enumerable.Range(0, writes).Select(Numbered)).SendAsync();

        // Assert
        await Assert.That((handler.Requests.Count, result.Written)).IsEqualTo((requests, writes));
    }

    [Test]
    public async Task PutAll_AfterOtherWrites_AddsToThem()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        var chain = shop.BatchWrite().Delete("gone").Put(Numbered(1)).Delete("too").ToRequestJson();

        // Act
        var json = shop.BatchWrite().Delete("gone").PutAll([Numbered(1)]).Delete("too").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(chain);
    }

    [Test]
    public async Task PutAll_SequenceWithSideEffects_EnumeratesItOnceAndAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        var reads = 0;
        var items = Enumerable.Range(0, 3).Select(i =>
        {
            reads++;
            return Numbered(i);
        });

        // Act
        var command = shop.BatchWrite().PutAll(items);
        var readByTheCall = reads;
        _ = command.ToRequestJson();

        // Assert
        await Assert.That((readByTheCall, reads)).IsEqualTo((3, 3));
    }

    [Test]
    public async Task PutAll_NullSequence_ThrowsAndLeavesTheCommandUsable()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        var command = shop.BatchWrite();
        NullSequence(command, out var failure);

        // Act
        var json = command.Delete("a").ToRequestJson();

        // Assert
        await Assert.That((failure is ArgumentNullException, json.Length > 0)).IsEqualTo((true, true));
    }

    [Test]
    public async Task PutAll_NullItemInTheSequence_ThrowsNamingTheSequence()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.BatchWrite().PutAll([Numbered(1), null!]));

        // Assert
        await Assert.That(((ArgumentNullException)failure).ParamName).IsEqualTo("items");
    }

    [Test]
    public async Task PutAll_KeyThatFailsItsTemplate_ThrowsBeforeAnythingIsSent()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        var day = new DateOnly(2026, 10, 31);
        Ticket[] items = [new() { Id = "t1", OpenedAt = day }, new() { Id = "a#b", OpenedAt = day }];

        // Act
        var failure = TestClient.Catch(() => _ = tickets.BatchWrite().PutAll(items));

        // Assert
        await Assert.That((failure is KeyFormatException, handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task PutAll_OfAnEntityWithAVersion_ThrowsAtTheTerminalWithoutIgnoreVersion()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        Ticket[] items = [new() { Id = "t1", OpenedAt = new DateOnly(2026, 10, 31), Revision = 1 }];

        // Act
#pragma warning disable DYNA236 // The batch throws on purpose.
        var failure = TestClient.Catch(() => tickets.BatchWrite().PutAll(items).ToRequestJson());
#pragma warning restore DYNA236

        // Assert
        await Assert.That(failure.Message).Contains(nameof(Ticket));
    }

    [Test]
    public async Task PutAll_OfAnEntityWithAVersionAndIgnoreVersion_WritesThemAsTheyAre()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        Ticket[] items = [new() { Id = "t1", OpenedAt = new DateOnly(2026, 10, 31), Revision = 1 }];

        // Act
        var json = tickets.BatchWrite().PutAll(items).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Revision":{"N":"1"}
            """);
    }

    [Test]
    public async Task DeleteAll_GeneratedKeys_WritesWhatDeletesInAChainWrite()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");
        string[] ids = ["a", "b"];
        var chain = customers.BatchWrite().Delete(Customer.Keys.Of("a")).Delete(Customer.Keys.Of("b")).ToRequestJson();

        // Act
        var json = customers.BatchWrite().DeleteAll(ids.Select(Customer.Keys.Of)).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(chain);
    }

    [Test]
    public async Task DeleteAll_SequenceOfTheKeyInterface_WritesTheSame()
    {
        // Arrange (the form history/api-design.md names: a struct key does not convert to it by variance, so each is boxed)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");
        IEnumerable<IKey<Customer>> keys = [Customer.Keys.Of("a"), new ClassKey()];

        // Act
        var json = customers.BatchWrite().DeleteAll(keys).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"RequestItems":{"Shop":[{"DeleteRequest":{"Key":{"pk":{"S":"CUSTOMER#a"},"sk":{"S":"PROFILE"}}}},{"DeleteRequest":{"Key":{}}}]}}""");
    }

    [Test]
    public async Task DeleteAll_SequenceWithSideEffects_EnumeratesItOnceAndAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");
        var reads = 0;
        var keys = Enumerable.Range(0, 3).Select(i =>
        {
            reads++;
            return Customer.Keys.Of("id");
        });

        // Act
        var command = customers.BatchWrite().DeleteAll(keys);
        var readByTheCall = reads;
        _ = command.ToRequestJson();

        // Assert
        await Assert.That((readByTheCall, reads)).IsEqualTo((3, 3));
    }

    [Test]
    public async Task DeleteAll_NullSequence_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = TestClient.Catch(() => _ = customers.BatchWrite().DeleteAll((IEnumerable<IKey<Customer>>)null!));

        // Assert
        await Assert.That(((ArgumentNullException)failure).ParamName).IsEqualTo("keys");
    }

    [Test]
    public async Task DeleteAll_NullKeyInTheSequence_ThrowsNamingTheSequence()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = TestClient.Catch(() => _ = customers.BatchWrite().DeleteAll(new ClassKey[] { null! }));

        // Assert
        await Assert.That(((ArgumentNullException)failure).ParamName).IsEqualTo("keys");
    }

    [Test]
    public async Task DeleteAll_StaleCommand_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = TestClient.Catch(() =>
        {
            var command = customers.BatchWrite();
            _ = command.DeleteAll([Customer.Keys.Of("a")]);
            var again = command;
            _ = again.DeleteAll([Customer.Keys.Of("b")]);
        });

        // Assert
        await Assert.That(failure.Message).Contains("already sent, extended or rendered");
    }

    [Test]
    public async Task PutAll_StaleCommand_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() =>
        {
            var command = shop.BatchWrite();
            _ = command.PutAll([Numbered(1)]);
            var again = command;
            _ = again.PutAll([Numbered(2)]);
        });

        // Assert
        await Assert.That(failure.Message).Contains("already sent, extended or rendered");
    }

    [Test]
    public async Task Put_NullItem_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.BatchWrite().Put(null!));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task Put_NullItemOnAnotherTable_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.BatchWrite().Put(shop, null!));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task Put_NullTable_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.BatchWrite().Put((Table<Item>)null!, Numbered(1)));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task Delete_NullTable_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.BatchWrite().Delete((Table<Item>)null!, "a"));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task Delete_NullTableWithAKey_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = TestClient.Catch(() => _ = customers.BatchWrite().Delete((Table<Customer>)null!, Customer.Keys.Of("a")));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task Delete_NullKey_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = TestClient.Catch(() => _ = customers.BatchWrite().Delete((ClassKey)null!));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task Delete_KeyOfAClass_WritesWhatItWrites()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var json = customers.BatchWrite().Delete(customers, new ClassKey()).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"RequestItems":{"Shop":[{"DeleteRequest":{"Key":{}}}]}}""");
    }

    [Test]
    public async Task Delete_NullKeyOnAnotherTable_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = TestClient.Catch(() => _ = customers.BatchWrite().Delete(customers, (ClassKey)null!));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task Delete_EmptyKey_ThrowsAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.BatchWrite().Delete(string.Empty));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentException>();
    }

    [Test]
    public async Task MaxAttempts_Zero_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.BatchWrite().MaxAttempts(0));

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
        var failure = TestClient.Catch(() => _ = shop.BatchWrite().MaxStalledRounds(rounds));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ToRequestJson_MoreThanOneRequest_RendersTheFirst()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").BatchWrite();
        for (var i = 0; i < 26; i++)
        {
            command = command.Delete("key-" + i);
        }

        // Act
        var json = command.ToRequestJson();

        // Assert
        await Assert.That(JsonElement.Parse(json).GetProperty("RequestItems").GetProperty("Shop").GetArrayLength()).IsEqualTo(25);
    }

    [Test]
    public async Task ToRequestJson_NoWrites_RendersAnEmptyRequest()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").BatchWrite().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"RequestItems":{}}""");
    }

    [Test]
    public async Task Default_Used_ThrowsAndSaysWhy()
    {
        // Arrange (nothing to arrange)

        // Act
        var failure = TestClient.Catch(static () => _ = default(BatchWriteCommand<Item>).Put(Numbered(1)));

        // Assert
        await Assert.That(failure.Message).Contains("default value");
    }

    [Test]
    public async Task DebuggerText_WhileBuilding_ShowsTheWritesSoFar()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var text = client.Table("Shop", "pk").BatchWrite().Delete("a").DebuggerText;

        // Assert
        await Assert.That(text).IsEqualTo("""BatchWriteItem on Shop: {"DeleteRequest":{"Key":{"pk":{"S":"a"}}}}""");
    }

    [Test]
    public async Task FindEnd_WritesThatFillTheRequestExactly_StayTogether()
    {
        // Arrange (two writes and the comma between them make the limit)
        var actions = Actions(1000, BatchWriteOperation.MaxWriteBytesPerRequest - 1000 - 1);

        // Act
        var end = BatchWriteOperation.FindEnd(actions);

        // Assert
        await Assert.That(end).IsEqualTo(2);
    }

    [Test]
    public async Task FindEnd_OneByteBeyondTheLimit_StartsANewRequest()
    {
        // Arrange
        var actions = Actions(1000, BatchWriteOperation.MaxWriteBytesPerRequest - 1000);

        // Act
        var end = BatchWriteOperation.FindEnd(actions);

        // Assert
        await Assert.That(end).IsEqualTo(1);
    }

    [Test]
    public async Task FindEnd_OneWriteAboveTheLimit_GoesAloneForDynamoDBToJudge()
    {
        // Arrange
        var actions = Actions(BatchWriteOperation.MaxWriteBytesPerRequest + 1, 10);

        // Act
        var end = BatchWriteOperation.FindEnd(actions);

        // Assert
        await Assert.That(end).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_Over16MegabytesOfWrites_CutsThemBelowTheLimit()
    {
        // Arrange (17 items of 1 MB are far below 25 writes and above 16 MB)
        var big = new string('x', 1024 * 1024);
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").BatchWrite();
        for (var i = 0; i < 17; i++)
        {
            command = command.Put(new Item { ["pk"] = "item-" + i, ["big"] = big });
        }

        // Act
        await command.SendAsync();

        // Assert
        await Assert.That((handler.Requests.Select(CountWrites).Sum(), handler.Requests.Count, handler.Requests.Max(static request => request.BodyText.Length) <= 16 * 1024 * 1024)).IsEqualTo((17, 2, true));
    }

    private static Task<BatchWriteResult> Send(Table<Item> table) => table.BatchWrite().Put(Numbered(0)).SendAsync().AsTask();

    // Numbered items, the last of them one on another table if there is one. The numbers repeat from 26 on, so 27 writes hold one key twice in a request of their own.
    private static ValueTask<BatchWriteResult> SendMany(Table<Item> table, int count, Table<Item>? other = null, CancellationToken cancellationToken = default)
    {
        var command = table.BatchWrite();
        for (var i = 0; i < count; i++)
        {
            command = command.Put(Numbered(i % 60));
        }

        return (other is null ? command : command.Put(other, Numbered(count))).SendAsync(cancellationToken);
    }

    private static Task<BatchWriteResult> SendWith(Table<Item> table, CancellationToken cancellationToken) =>
        table.BatchWrite().Put(Numbered(0)).SendAsync(cancellationToken).AsTask();

    private static (Task First, bool SecondThrew) SendTwice(Table<Item> table)
    {
        var command = table.BatchWrite().Put(Numbered(0));
        var first = command.SendAsync().AsTask();
        try
        {
            var again = command;
            _ = again.SendAsync().AsTask();
            return (first, false);
        }
        catch (InvalidOperationException)
        {
            return (first, true);
        }
    }

    private static void NullSequence(BatchWriteCommand<Item> command, out Exception? failure)
    {
        failure = null;
        try
        {
            _ = command.PutAll(null!);
        }
        catch (ArgumentNullException exception)
        {
            failure = exception;
        }
    }

    private static ActionList Actions(params int[] lengths)
    {
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var table = client.Table("Shop", "pk").Model;
        var actions = new ActionList(System.Buffers.ArrayPool<byte>.Shared);
        var offset = 0;
        foreach (var length in lengths)
        {
            actions.Add(table, offset);
            offset += length;
        }

        actions.Seal(offset);
        return actions;
    }

    // The call parks on the timer of the wait; a timer that is armed means the wait has started.
    private static async Task WaitForTimerAsync(ManualTimeProvider time)
    {
        while (time.NextDueIn is null || time.NextDueIn >= AttemptTimeout)
        {
            await Task.Yield();
        }
    }

    private sealed class ClassKey : IKey<Customer>
    {
        public void WriteKey(DynamoDbWriter writer)
        {
        }
    }
}
