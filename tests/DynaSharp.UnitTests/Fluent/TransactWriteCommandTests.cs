using System.Text.Json;
using DynaSharp.Mapping;
using DynaSharp.Serialization;
using DynaSharp.Testing;
using static DynaSharp.Expr;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Fluent;

public sealed class TransactWriteCommandTests
{
    private const string Canceled = """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","Message":"Transaction cancelled","CancellationReasons":[{"Code":"None"},{"Code":"ConditionalCheckFailed","Message":"The conditional request failed","Item":{"customerId":{"S":"bob"}}},{"Code":"None"}]}""";

    [Test]
    public async Task SendAsync_EveryKindOfAction_WritesThemInOrder()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        await client.TransactWrite()
            .Put(shop, new Item { ["pk"] = "a", ["sk"] = "s" }).IfNotExists()
            .Update(shop, "b", "s").Set("city", "Bern").Add("n", 1).If(Attr("v").Eq(3)).ReturnOldOnFailure()
            .Delete(shop, "c", "s").IfExists()
            .Check(shop, "d", "s").If($"{Attr("n")} > {0}")
            .Token("token-1")
            .SendAsync();

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).IsEqualTo(
            """{"TransactItems":[{"Put":{"TableName":"Shop","Item":{"pk":{"S":"a"},"sk":{"S":"s"}},"ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"}}},""" +
            """{"Update":{"TableName":"Shop","Key":{"pk":{"S":"b"},"sk":{"S":"s"}},"UpdateExpression":"SET #n0 = :v0 ADD #n1 :v1","ConditionExpression":"#n2 = :v2","ExpressionAttributeNames":{"#n0":"city","#n1":"n","#n2":"v"},"ExpressionAttributeValues":{":v0":{"S":"Bern"},":v1":{"N":"1"},":v2":{"N":"3"}},"ReturnValuesOnConditionCheckFailure":"ALL_OLD"}},""" +
            """{"Delete":{"TableName":"Shop","Key":{"pk":{"S":"c"},"sk":{"S":"s"}},"ConditionExpression":"attribute_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"}}},""" +
            """{"ConditionCheck":{"TableName":"Shop","Key":{"pk":{"S":"d"},"sk":{"S":"s"}},"ConditionExpression":"#n0 > :v0","ExpressionAttributeNames":{"#n0":"n"},"ExpressionAttributeValues":{":v0":{"N":"0"}}}}],""" +
            "\"ClientRequestToken\":\"token-1\"}");
    }

    [Test]
    public async Task SendAsync_TypedActions_TakeTheKeyAndTheItemFromTheEntity()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        await client.TransactWrite()
            .Put(customers, new Customer("a", "Alice"))
            .Update(customers, Customer.Keys.Of("b")).Add(Customer.Attrs.OrderCount, 1).If(Customer.Attrs.OrderCount.Lt(100))
            .Delete(customers, Customer.Keys.Of("c"))
            .Check(customers, Customer.Keys.Of("d")).IfExists()
            .SendAsync();

        // Assert
        using var document = JsonDocument.Parse(handler.Requests.Single().BodyText);
        var actions = document.RootElement.GetProperty("TransactItems");
        await Assert.That(actions[3].GetProperty("ConditionCheck").GetProperty("Key").GetProperty("pk").GetProperty("S").GetString()).IsEqualTo("CUSTOMER#d");
    }

    [Test]
    public async Task SendAsync_ActionsOnTheirOwnPlaceholders_RestartForEachAction()
    {
        // Arrange (a name that means something else in the next action)
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        await client.TransactWrite().Update(shop, "a").Set("first", 1).Update(shop, "b").Set("second", 2).SendAsync();

        // Assert
        using var document = JsonDocument.Parse(handler.Requests.Single().BodyText);
        var second = document.RootElement.GetProperty("TransactItems")[1].GetProperty("Update");
        await Assert.That((second.GetProperty("ExpressionAttributeNames").GetProperty("#n0").GetString(), second.GetProperty("ExpressionAttributeValues").GetProperty(":v0").GetProperty("N").GetString())).IsEqualTo(("second", "2"));
    }

    [Test]
    public async Task SendAsync_IfNotExists_UsesThePartitionKeyOfTheTableOfTheAction()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var first = client.Table("First", "firstKey");
        var second = client.Table("Second", "secondKey");

        // Act
        await client.TransactWrite().Put(first, new Item { ["firstKey"] = "a" }).IfNotExists().Delete(second, "b").IfNotExists().SendAsync();

        // Assert
        using var document = JsonDocument.Parse(handler.Requests.Single().BodyText);
        var actions = document.RootElement.GetProperty("TransactItems");
        await Assert.That((actions[0].GetProperty("Put").GetProperty("ExpressionAttributeNames").GetProperty("#n0").GetString(), actions[1].GetProperty("Delete").GetProperty("ExpressionAttributeNames").GetProperty("#n0").GetString())).IsEqualTo(("firstKey", "secondKey"));
    }

    [Test]
    public async Task ToRequestJson_UniqueOfAKey_PutsTheKeyAloneIfNoItemHasIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var json = client.TransactWrite().Unique(customers, Customer.Keys.Of("alice"), out var taken).ReturnOldOnFailure().Token("t").ToRequestJson();

        // Assert
        await Assert.That((json, taken.Index)).IsEqualTo(("""{"TransactItems":[{"Put":{"TableName":"Shop","Item":{"pk":{"S":"CUSTOMER#alice"},"sk":{"S":"PROFILE"}},"ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"},"ReturnValuesOnConditionCheckFailure":"ALL_OLD"}}],"ClientRequestToken":"t"}""", 0));
    }

    [Test]
    public async Task ToRequestJson_UniqueOfAKeyAfterAnotherAction_HasTheMarkerOfItsPosition()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        _ = client.TransactWrite().Delete(customers, "a", "b").Unique(customers, Customer.Keys.Of("alice"), out var taken).Token("t").ToRequestJson();

        // Assert
        await Assert.That(taken.Index).IsEqualTo(1);
    }

    [Test]
    public async Task ToRequestJson_UniqueOfAKeyOfAVersionedEntity_NeedsNoVerb()
    {
        // Arrange (the item is a key and nothing else, so it has no version)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var json = client.TransactWrite().Unique(tickets, Ticket.Keys.Of("t1", new DateOnly(2026, 10, 31)), out _).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TransactItems":[{"Put":{"TableName":"Shop","Item":{"pk":{"S":"TICKET#t1"},"sk":{"S":"AT#2026-10-31"}},"ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"}}}],"ClientRequestToken":"t"}""");
    }

    [Test]
    public async Task ToRequestJson_UniqueOfAKeyThatIsAClass_WritesIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var json = client.TransactWrite().Unique(customers, new ClassKey(), out _).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Item":{}
            """);
    }

    [Test]
    public async Task Unique_NullTable_ThrowsArgumentNullException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = Assert.Throws<ArgumentNullException>(() => client.TransactWrite().Unique((Table<Customer>)null!, Customer.Keys.Of("alice"), out _).ToRequestJson());

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("table");
    }

    [Test]
    public async Task Unique_NullKey_ThrowsArgumentNullException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = Assert.Throws<ArgumentNullException>(() => client.TransactWrite().Unique(customers, (ClassKey)null!, out _).ToRequestJson());

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("key");
    }

    [Test]
    public async Task SendAsync_Unique_IsAPutWithNotExists()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var markers = client.Table("Markers", "pk", "sk");

        // Act
        await client.TransactWrite().Unique(markers, new Item { ["pk"] = "EMAIL#a@b.ch", ["sk"] = "MARKER" }, out _).ReturnOldOnFailure().Token("t").SendAsync();

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).IsEqualTo(
            """{"TransactItems":[{"Put":{"TableName":"Markers","Item":{"pk":{"S":"EMAIL#a@b.ch"},"sk":{"S":"MARKER"}},"ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"},"ReturnValuesOnConditionCheckFailure":"ALL_OLD"}}],"ClientRequestToken":"t"}""");
    }

    [Test]
    public async Task SendAsync_ReturnOldOnFailure_BelongsToItsAction()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        await client.TransactWrite().Delete(shop, "a").ReturnOldOnFailure().Delete(shop, "b").SendAsync();

        // Assert
        using var document = JsonDocument.Parse(handler.Requests.Single().BodyText);
        var actions = document.RootElement.GetProperty("TransactItems");
        await Assert.That((actions[0].GetProperty("Delete").TryGetProperty("ReturnValuesOnConditionCheckFailure", out _), actions[1].GetProperty("Delete").TryGetProperty("ReturnValuesOnConditionCheckFailure", out _))).IsEqualTo((true, false));
    }

    [Test]
    public async Task SendAsync_Options_GoToTheRequestOnce()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        await client.TransactWrite().ReturnCapacity(ReturnConsumedCapacity.Indexes).Delete(shop, "a").Delete(shop, "b").ReturnMetrics().Token("t").SendAsync();

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).EndsWith("""}}],"ClientRequestToken":"t","ReturnConsumedCapacity":"INDEXES","ReturnItemCollectionMetrics":"SIZE"}""");
    }

    [Test]
    public async Task SendAsync_NoToken_GeneratesOnePerSend()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        await client.TransactWrite().Delete(shop, "a").SendAsync();
        await client.TransactWrite().Delete(shop, "a").SendAsync();

        // Assert
        var tokens = handler.Requests.Select(static request => TestClient.TokenOf(request.BodyText)).ToList();
        await Assert.That((tokens[0] != tokens[1], Guid.TryParseExact(tokens[0], "D", out _))).IsEqualTo((true, true));
    }

    [Test]
    public async Task SendAsync_Retried_CarriesTheSameTokenOnEveryAttempt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(ServerError());
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = CreateClient(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        await client.TransactWrite().Delete(shop, "a").SendAsync();

        // Assert
        await Assert.That(handler.Requests.Select(static request => TestClient.TokenOf(request.BodyText)).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_Retried_MakesEveryAttempt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = CreateClient(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        await client.TransactWrite().Delete(shop, "a").SendAsync();

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SendAsync_ExplicitToken_IsEncoded()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        await client.TransactWrite().Token("a\"b").Delete(shop, "a").SendAsync();

        // Assert
        await Assert.That(TestClient.TokenOf(handler.Requests.Single().BodyText)).IsEqualTo("a\"b");
    }

    [Test]
    public async Task SendAsync_MaxAttemptsOnAStep_LimitsTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.DefaultResponse = Throttled();
        using var client = CreateClient(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(async () => await client.TransactWrite().MaxAttempts(2).Delete(shop, "a").MaxAttempts(1).SendAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_ResponseWithCapacityAndMetrics_ReturnsThem()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"ConsumedCapacity":[{"TableName":"Shop","CapacityUnits":4.0}],"ItemCollectionMetrics":{"Shop":[{"ItemCollectionKey":{"pk":{"S":"a"}},"SizeEstimateRangeGB":[0.0,1.0]}]},"New":1}"""));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var result = await client.TransactWrite().Delete(shop, "a").ReturnCapacity().ReturnMetrics().SendAsync();

        // Assert
        await Assert.That((result.ConsumedCapacity.Single().CapacityUnits, result.ItemCollectionMetrics["Shop"].Count)).IsEqualTo(((double?)4.0, 1));
    }

    [Test]
    public async Task SendAsync_EmptyResponse_HasEmptyLists()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var result = await client.TransactWrite().Delete(shop, "a");

        // Assert
        await Assert.That((result.ConsumedCapacity.Count, result.ItemCollectionMetrics.Count)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task SendAsync_Cancelled_MapsMarkersToReasonsInOrder()
    {
        // Arrange
        using var handler = new StubHandler(_ => new StubResponse { StatusCode = System.Net.HttpStatusCode.BadRequest, Body = Canceled });
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        var markers = new TransactMarker[3];

        // Act
        var failure = await TestClient.CaptureAsync<TransactionCanceledException>(() => SendThree(client, shop, markers));

        // Assert
        await Assert.That((failure.ReasonFor(markers[0]), failure.ReasonFor(markers[1]), failure.ReasonFor(markers[2]))).IsEqualTo((TransactionFailure.None, TransactionFailure.ConditionalCheckFailed, TransactionFailure.None));
    }

    [Test]
    public async Task SendAsync_Cancelled_FindsTheItemOfTheMarker()
    {
        // Arrange
        using var handler = new StubHandler(_ => new StubResponse { StatusCode = System.Net.HttpStatusCode.BadRequest, Body = Canceled });
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        var markers = new TransactMarker[3];

        // Act
        var failure = await TestClient.CaptureAsync<TransactionCanceledException>(() => SendThree(client, shop, markers));

        // Assert
        await Assert.That((failure.ItemFor(markers[0]), failure.ItemFor(markers[1])?.GetString("customerId"))).IsEqualTo(((Item?)null, (string?)"bob"));
    }

    [Test]
    public async Task SendAsync_Cancelled_IsNotRetried()
    {
        // Arrange
        using var handler = new StubHandler(_ => new StubResponse { StatusCode = System.Net.HttpStatusCode.BadRequest, Body = Canceled });
        using var client = CreateClient(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        await TestClient.CaptureAsync<TransactionCanceledException>(async () => await client.TransactWrite().Delete(shop, "a").SendAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Verbs_WithAMarker_CountTheActions()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        var customers = client.Table<Customer>("Shop");

        // Act
        _ = client.TransactWrite()
            .Put(shop, new Item { ["pk"] = "a" }, out var put)
            .Update(shop, "b", "s", out var update)
            .Set("n", 1)
            .Update(customers, Customer.Keys.Of("c"), out var typedUpdate)
            .Set("n", 1)
            .Delete(shop, "d", "s", out var delete)
            .Delete(customers, Customer.Keys.Of("e"), out var typedDelete)
            .Check(shop, "f", "s", out var check)
            .If(Attr("a").Exists())
            .Check(customers, Customer.Keys.Of("g"), out var typedCheck)
            .If(Attr("a").Exists())
            .Unique(shop, new Item { ["pk"] = "h" }, out var unique)
            .ToRequestJson();

        // Assert
        await Assert.That((put.Index, update.Index, typedUpdate.Index, delete.Index, typedDelete.Index, check.Index, typedCheck.Index, unique.Index)).IsEqualTo((0, 1, 2, 3, 4, 5, 6, 7));
    }

    [Test]
    public async Task Verbs_100Actions_Fit()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        await Actions(client.TransactWrite().Delete(shop, "first"), shop, 99).SendAsync();

        // Assert
        using var document = JsonDocument.Parse(handler.Requests.Single().BodyText);
        await Assert.That(document.RootElement.GetProperty("TransactItems").GetArrayLength()).IsEqualTo(100);
    }

    [Test]
    public async Task Verbs_101stAction_ThrowsAClearError()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = Actions(client.TransactWrite().Delete(shop, "first"), shop, 100));

        // Assert
        await Assert.That((failure is InvalidOperationException, failure.Message.Contains("at most 100 actions", StringComparison.Ordinal))).IsEqualTo((true, true));
    }

    [Test]
    public async Task IfExists_WithAnotherCondition_MeansBoth()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var json = client.TransactWrite().Delete(shop, "a").IfExists().If(Attr("n").Gt(1)).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ConditionExpression":"(attribute_exists(#n0)) AND (#n1 > :v0)"
            """);
    }

    [Test]
    public async Task If_OnEveryStep_TakesBothForms()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var json = client.TransactWrite()
            .Put(shop, new Item { ["pk"] = "a" }).If(Attr("x").Exists()).If($"{Attr("y")} = {1}")
            .Update(shop, "b").Set("x", 1).If(Attr("x").Exists()).If($"{Attr("y")} = {1}")
            .Delete(shop, "c").If(Attr("x").Exists()).If($"{Attr("y")} = {1}")
            .ToRequestJson();

        // Assert
        using var document = JsonDocument.Parse(json);
        var conditions = document.RootElement.GetProperty("TransactItems").EnumerateArray().Select(static action => action.EnumerateObject().Single().Value.GetProperty("ConditionExpression").GetString());
        await Assert.That(conditions.Distinct().Single()).IsEqualTo("(attribute_exists(#n0)) AND (#n1 = :v0)");
    }

    [Test]
    public async Task UpdateStep_EveryChange_GoesIntoTheExpression()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        var customers = client.Table<Customer>("Shop");

        // Act
        var json = client.TransactWrite()
            .Update(shop, "a")
            .Set("a", "x")
            .Set("b", 1)
            .Set(Customer.Attrs.Email, "e")
            .Set($"{Attr("c")} = {Attr("c")} + {1}")
            .SetIfNotExists("d", "x")
            .SetIfNotExists("e", 2)
            .SetIfNotExists(Customer.Attrs.FullName, "f")
            .Add("f", AttributeValue.FromStringSet(["x"]))
            .Add("g", 3)
            .Add(Customer.Attrs.OrderCount, 4)
            .Delete("h", AttributeValue.FromStringSet(["x"]))
            .Delete(new Attr<string, StringConverter>("i"), "y")
            .Remove("j")
            .Delete(customers, Customer.Keys.Of("b"))
            .ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = :v2, #n3 = #n3 + :v1, #n4 = if_not_exists(#n4, :v0), #n5 = if_not_exists(#n5, :v3), #n6 = if_not_exists(#n6, :v4) REMOVE #n12 ADD #n7 :v5, #n8 :v6, #n9 :v7 DELETE #n10 :v5, #n11 :v8"
            """);
    }

    [Test]
    public async Task Helpers_OverTheInterfaces_WorkOnEveryStep()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var json = client.TransactWrite()
            .Once()
            .Put(shop, new Item { ["pk"] = "a" }).OnlyIfOwned("me").Once()
            .Update(shop, "b").Audited("me").OnlyIfOwned("me").Once()
            .Check(shop, "c").OnlyIfOwned("me").Once()
            .ToRequestJson();

        // Assert
        await Assert.That(json).Contains("updatedBy");
    }

    [Test]
    public async Task SendAsync_Twice_Throws()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => SendTwice(client, shop));

        // Assert
        await Assert.That(failure.Message).Contains("TransactWriteItems on Shop was already sent, extended or rendered");
    }

    [Test]
    public async Task Verb_OnAStaleCommand_ThrowsAndNamesTheTransaction()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() =>
        {
            var command = client.TransactWrite();
            _ = command.Delete(shop, "a");
            var again = command;
            _ = again.Delete(shop, "b");
        });

        // Assert
        await Assert.That(failure.Message).Contains("TransactWriteItems on several tables was already sent, extended or rendered");
    }

    [Test]
    public async Task Conversion_LoopOverEveryKindOfAction_WritesWhatAChainWrites()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        var chain = client.TransactWrite()
            .Put(shop, new Item { ["pk"] = "a", ["sk"] = "s" }).IfNotExists()
            .Update(shop, "b", "s").Add("n", 1).ReturnOldOnFailure()
            .Delete(shop, "c", "s").IfExists()
            .Check(shop, "d", "s").If(Attr("n").Gt(0))
            .Token("t")
            .ToRequestJson();

        // Act
        var command = client.TransactWrite();
        command = command.Put(shop, new Item { ["pk"] = "a", ["sk"] = "s" }).IfNotExists();
        command = command.Update(shop, "b", "s").Add("n", 1).ReturnOldOnFailure();
        command = command.Delete(shop, "c", "s").IfExists();
        command = command.Check(shop, "d", "s").If(Attr("n").Gt(0));
        var json = command.Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(chain);
    }

    [Test]
    public async Task SendAsync_CommandOfALoop_SendsEveryActionWithItsMarker()
    {
        // Arrange (the marker of each round is the index of its action, which the second one fails)
        using var handler = new StubHandler(_ => new StubResponse { StatusCode = System.Net.HttpStatusCode.BadRequest, Body = Canceled });
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        string[] keys = ["a", "b", "c"];
        var markers = new TransactMarker[keys.Length];

        // Act
        var failure = await TestClient.CaptureAsync<TransactionCanceledException>(async () =>
        {
            var transaction = client.TransactWrite();
            for (var i = 0; i < keys.Length; i++)
            {
                transaction = transaction.Put(shop, new Item { ["pk"] = keys[i] }, out markers[i]).IfNotExists();
            }

            await transaction.SendAsync();
        });

        // Assert
        await Assert.That((failure.ReasonFor(markers[0]), failure.ReasonFor(markers[1]), failure.ReasonFor(markers[2])))
            .IsEqualTo((TransactionFailure.None, TransactionFailure.ConditionalCheckFailed, TransactionFailure.None));
    }

    [Test]
    public async Task Await_Command_SendsIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        TransactWriteCommand command = client.TransactWrite().Delete(shop, "a");

        // Act
        await command;

        // Assert
        await Assert.That(handler.Requests.Single().Operation).IsEqualTo("TransactWriteItems");
    }

    [Test]
    public async Task MaxAttempts_OnACommandOfALoop_Applies()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("ProvisionedThroughputExceededException", "Slow down."));
        using var client = CreateClient(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(async () =>
        {
            TransactWriteCommand command = client.TransactWrite().Delete(shop, "a");
            await command.MaxAttempts(1).SendAsync();
        });

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Verb_101stActionOfALoop_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() =>
        {
            var command = client.TransactWrite();
            for (var i = 0; i < 101; i++)
            {
                command = command.Delete(shop, "key");
            }
        });

        // Assert
        await Assert.That(failure.Message).Contains("at most 100 actions");
    }

    [Test]
    public async Task SendAsync_NoAction_ThrowsAndSendsNothing()
    {
        // Arrange (a loop over nothing)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(async () =>
        {
            var command = client.TransactWrite();
            await command.SendAsync();
        });

        // Assert
        await Assert.That((failure.Message.Contains("no action", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task ToRequestJson_NoAction_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = TestClient.Catch(() => client.TransactWrite().Token("t").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("no action");
    }

    [Test]
    public async Task Clause_OnAPutStepThatWasConverted_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() =>
        {
            var step = client.TransactWrite().Put(shop, new Item { ["pk"] = "a" });
            TransactWriteCommand command = step;
            _ = command;
            _ = step.IfNotExists();
        });

        // Assert
        await Assert.That(failure.Message).Contains("TransactWriteItems on Shop was already sent, extended or rendered");
    }

    [Test]
    public async Task Clause_OnAnUpdateStepThatWasConverted_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() =>
        {
            var step = client.TransactWrite().Update(shop, "a");
            TransactWriteCommand command = step;
            _ = command;
            _ = step.Add("n", 1);
        });

        // Assert
        await Assert.That(failure.Message).Contains("TransactWriteItems on Shop was already sent, extended or rendered");
    }

    [Test]
    public async Task Clause_OnAConditionStepThatWasConverted_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() =>
        {
            var step = client.TransactWrite().Delete(shop, "a");
            TransactWriteCommand command = step;
            _ = command;
            _ = step.IfExists();
        });

        // Assert
        await Assert.That(failure.Message).Contains("TransactWriteItems on Shop was already sent, extended or rendered");
    }

    [Test]
    public async Task Verb_OnAStepThatWasConverted_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() =>
        {
            var step = client.TransactWrite().Put(shop, new Item { ["pk"] = "a" });
            TransactWriteCommand command = step;
            _ = command;
            _ = step.Put(shop, new Item { ["pk"] = "b" });
        });

        // Assert
        await Assert.That(failure.Message).Contains("already sent, extended or rendered");
    }

    [Test]
    public async Task SendAsync_OnAStepThatWasConverted_ThrowsAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        await TestClient.CaptureAsync<InvalidOperationException>(async () =>
        {
            var step = client.TransactWrite().Delete(shop, "a");
            TransactWriteCommand command = step;
            _ = command;
            await step.SendAsync();
        });

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Conversion_OfAStepConvertedBefore_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() =>
        {
            var step = client.TransactWrite().Check(shop, "a");
            TransactWriteCommand first = step;
            TransactWriteCommand second = step;
            _ = first;
            _ = second;
        });

        // Assert
        await Assert.That(failure.Message).Contains("already sent, extended or rendered");
    }

    [Test]
    public async Task Conversion_OfAStepThatWasExtended_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() =>
        {
            var step = client.TransactWrite().Put(shop, new Item { ["pk"] = "a" });
            _ = step.IfNotExists();
            TransactWriteCommand command = step;
            _ = command;
        });

        // Assert
        await Assert.That(failure.Message).Contains("already sent, extended or rendered");
    }

    [Test]
    public async Task Conversion_OfADefaultPutStep_Throws()
    {
        // Arrange, Act
        var failure = TestClient.Catch(() =>
        {
            TransactWriteCommand command = default(PutStep);
            _ = command;
        });

        // Assert
        await Assert.That(failure.Message).Contains("default value");
    }

    [Test]
    public async Task Conversion_OfADefaultUpdateStep_Throws()
    {
        // Arrange, Act
        var failure = TestClient.Catch(() =>
        {
            TransactWriteCommand command = default(UpdateStep);
            _ = command;
        });

        // Assert
        await Assert.That(failure.Message).Contains("default value");
    }

    [Test]
    public async Task Conversion_OfADefaultConditionStep_Throws()
    {
        // Arrange, Act
        var failure = TestClient.Catch(() =>
        {
            TransactWriteCommand command = default(ConditionStep);
            _ = command;
        });

        // Assert
        await Assert.That(failure.Message).Contains("default value");
    }

    [Test]
    public async Task Verb_OnACommandThatWasExtendedAfterTheConversion_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() =>
        {
            TransactWriteCommand command = client.TransactWrite().Delete(shop, "a");
            _ = command.Delete(shop, "b");
            var again = command;
            _ = again.Delete(shop, "c");
        });

        // Assert
        await Assert.That(failure.Message).Contains("already sent, extended or rendered");
    }

    [Test]
    public async Task SendAsync_AfterAStaleStepFailed_TheCommandKeepsWhatItHolds()
    {
        // Arrange (the stale handle throws and leaves the one that owns the state as it is)
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var (failed, json) = CommandAfterAStaleStep(client, shop);

        // Assert
        await Assert.That((failed, json)).IsEqualTo((true, """{"TransactItems":[{"Delete":{"TableName":"Shop","Key":{"pk":{"S":"a"}}}}],"ClientRequestToken":"t"}"""));
    }

    [Test]
    public async Task Clause_OnAStepAfterItsCommandEnded_ThrowsAndLeavesTheNextCommandAlone()
    {
        // Arrange (the state went back to the pool, and the next command has it)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var (failed, json) = NextCommandAfterAStaleStep(client, shop);

        // Assert
        await Assert.That((failed, json)).IsEqualTo((true, """{"TableName":"Shop","Key":{"pk":{"S":"next"}}}"""));
    }

    [Test]
    public async Task Put_NullItem_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = client.TransactWrite().Put(shop, null!));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task Put_NullTable_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = TestClient.Catch(() => _ = client.TransactWrite().Put((Table<Item>)null!, new Item { ["pk"] = "a" }));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task Update_NullTable_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = TestClient.Catch(() => _ = client.TransactWrite().Update((Table<Item>)null!, "a"));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task Update_NullTableWithAKey_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = TestClient.Catch(() => _ = client.TransactWrite().Update((Table<Customer>)null!, Customer.Keys.Of("a")));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task Update_NullKey_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = TestClient.Catch(() => _ = client.TransactWrite().Update(customers, (ClassKey)null!));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task Check_KeyOfAClass_WritesWhatItWrites()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var json = client.TransactWrite().Check(customers, new ClassKey()).IfExists().Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Key":{}
            """);
    }

    [Test]
    public async Task Delete_EmptyKey_ThrowsAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = client.TransactWrite().Delete(shop, string.Empty));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentException>();
    }

    [Test]
    public async Task Token_Empty_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = TestClient.Catch(() => _ = client.TransactWrite().Token(string.Empty));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task MaxAttempts_Zero_ThrowsOnEveryLink(int link)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() =>
        {
            if (link == 0)
            {
                _ = client.TransactWrite().MaxAttempts(0);
            }
            else if (link == 1)
            {
                _ = client.TransactWrite().Delete(shop, "a").MaxAttempts(0);
            }
            else if (link == 2)
            {
                _ = client.TransactWrite().Put(shop, new Item { ["pk"] = "a" }).MaxAttempts(0);
            }
            else
            {
                _ = client.TransactWrite().Update(shop, "a").MaxAttempts(0);
            }
        });

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Default_Used_ThrowsAndSaysWhy()
    {
        // Arrange (nothing to arrange)

        // Act
        var failure = TestClient.Catch(static () => _ = default(TransactWriteCommand).Token("t"));

        // Assert
        await Assert.That(failure.Message).Contains("default value");
    }

    [Test]
    public async Task DebuggerText_OfEveryLink_ShowsTheTransactionSoFar()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var texts = Texts(client, shop);

        // Assert
        await Assert.That(texts).IsEquivalentTo(
        [
            """TransactWriteItems on several tables: {"TransactItems":[""",
            """TransactWriteItems on Shop: {"TransactItems":[{"Delete":{"TableName":"Shop","Key":{"pk":{"S":"a"}}""",
            """TransactWriteItems on Shop: {"TransactItems":[{"Put":{"TableName":"Shop","Item":{"pk":{"S":"a"}""",
            """TransactWriteItems on Shop: {"TransactItems":[{"Update":{"TableName":"Shop","Key":{"pk":{"S":"a"}}""",
        ]);
    }

    [Test]
    public async Task Handle_OfEveryStep_ReachesTheHandlerOfAnExpression()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        var step = client.TransactWrite().Put(shop, new Item { ["pk"] = "a" });

        // Act
        var length = CommandHelpers.FinishAndRelease(step);

        // Assert
        await Assert.That(length).IsGreaterThan(0);
    }

    [Test]
    public async Task SendAsync_DisposedClient_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        var pool = new Serialization.CountingArrayPool<byte>();
        using var client = TestClient.Create(handler, pool: pool);
        var shop = client.Table("Shop", "pk");
        client.Dispose();

        // Act
        var failure = TestClient.Catch(() => client.TransactWrite().Delete(shop, "a").SendAsync().AsTask());

        // Assert
        await Assert.That((failure is ObjectDisposedException, pool.Outstanding)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task SendAsync_InvalidOption_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(async () => await client.TransactWrite().Delete(shop, "a").ReturnCapacity((ReturnConsumedCapacity)99).SendAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(failure is null ? 1 : 0);
    }

    private static (bool Failed, string Json) CommandAfterAStaleStep(DynamoDbClient client, Table<Item> shop)
    {
        var step = client.TransactWrite().Delete(shop, "a");
        TransactWriteCommand command = step;
        var failed = FailsOnAClause(step);
        return (failed, command.Token("t").ToRequestJson());
    }

    private static (bool Failed, string Json) NextCommandAfterAStaleStep(DynamoDbClient client, Table<Item> shop)
    {
        var step = client.TransactWrite().Delete(shop, "a");
        TransactWriteCommand command = step;
        _ = command.Token("t").ToRequestJson();
        var next = shop.Get("next");
        var failed = FailsOnAClause(step);
        return (failed, next.ToRequestJson());
    }

    private static bool FailsOnAClause(ConditionStep step)
    {
        try
        {
            _ = step.IfExists();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static async Task SendThree(DynamoDbClient client, Table<Item> shop, TransactMarker[] markers)
    {
        await Three(client, shop, markers).SendAsync();
    }

    private static ConditionStep Three(DynamoDbClient client, Table<Item> shop, TransactMarker[] markers) => client.TransactWrite()
        .Put(shop, new Item { ["pk"] = "a" }, out markers[0])
        .Unique(shop, new Item { ["pk"] = "b" }, out markers[1])
        .Delete(shop, "c", default, out markers[2]);

    private static ConditionStep Actions(ConditionStep step, Table<Item> shop, int count)
    {
        for (var i = 0; i < count; i++)
        {
            step = step.Delete(shop, "key-" + i);
        }

        return step;
    }

    private static void SendTwice(DynamoDbClient client, Table<Item> shop)
    {
        var step = client.TransactWrite().Delete(shop, "a");
        _ = step.SendAsync().AsTask();
        var again = step;
        _ = again.SendAsync().AsTask();
    }

    private static List<string> Texts(DynamoDbClient client, Table<Item> shop) =>
    [
        client.TransactWrite().DebuggerText,
        client.TransactWrite().Delete(shop, "a").DebuggerText,
        client.TransactWrite().Put(shop, new Item { ["pk"] = "a" }).DebuggerText,
        client.TransactWrite().Update(shop, "a").DebuggerText,
    ];

    private sealed class ClassKey : IKey<Customer>
    {
        public void WriteKey(DynamoDbWriter writer)
        {
        }
    }
}
