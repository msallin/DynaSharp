using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

public sealed class UpdateCommandTests
{
    private const string Start = """{"TableName":"Shop","Key":{"pk":{"S":"a"},"sk":{"S":"b"}}""";

    [Test]
    public async Task ToRequestJson_SetOfAString_WritesAPlaceholderForNameAndValue()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").Set("city", "Bern").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n0 = :v0","ExpressionAttributeNames":{"#n0":"city"},"ExpressionAttributeValues":{":v0":{"S":"Bern"}}}""");
    }

    [Test]
    public async Task ToRequestJson_SetOfANumber_WritesTheNumberAsText()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").Set("price", 12.5m).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n0 = :v0","ExpressionAttributeNames":{"#n0":"price"},"ExpressionAttributeValues":{":v0":{"N":"12.5"}}}""");
    }

    [Test]
    public async Task ToRequestJson_SetOfABoolean_WritesTheBoolean()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").Set("active", true).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n0 = :v0","ExpressionAttributeNames":{"#n0":"active"},"ExpressionAttributeValues":{":v0":{"BOOL":true}}}""");
    }

    [Test]
    public async Task ToRequestJson_SetThroughAHandle_WritesThroughItsConverter()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").Set(Customer.Attrs.OrderCount, 7).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n0 = :v0","ExpressionAttributeNames":{"#n0":"orderCount"},"ExpressionAttributeValues":{":v0":{"N":"7"}}}""");
    }

    [Test]
    public async Task ToRequestJson_SetOfNullThroughAHandleOfAnEntityTable_IsARemove()
    {
        // Arrange (the attribute feeds no key)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Purchase>("Shop").Update("a", 1).IgnoreVersion().Set(Purchase.Attrs.ShipTo, null!).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"a"},"sk":{"N":"1"}},"UpdateExpression":"SET #n1 = if_not_exists(#n1, :v0) REMOVE #n0","ExpressionAttributeNames":{"#n0":"ShipTo","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"S":"PURCHASE"}}}""");
    }

    [Test]
    public async Task ToRequestJson_SetOfNullThroughAHandleOfATableWithoutASchema_IsARemove()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").Set(Purchase.Attrs.ShipTo, null!).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"REMOVE #n0","ExpressionAttributeNames":{"#n0":"ShipTo"}}""");
    }

    [Test]
    public async Task ToRequestJson_SetOfNullBesideASet_KeepsBothClauses()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").Set(Purchase.Attrs.Total, 1m).Set(Purchase.Attrs.ShipTo, null!).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0 REMOVE #n1"
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfNullThroughANestedHandle_RemovesThePath()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").Set(Purchase.Attrs.ShipTo.At(Place.Attrs.City), null!).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"REMOVE #n0.#n1","ExpressionAttributeNames":{"#n0":"ShipTo","#n1":"City"}}""");
    }

    [Test]
    public async Task ToRequestJson_SetOfNullInATransaction_IsARemove()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Update(client.Table<Purchase>("Shop"), "a", 1).IgnoreVersion().Set(Purchase.Attrs.ShipTo, null!).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n1 = if_not_exists(#n1, :v0) REMOVE #n0","ExpressionAttributeNames":{"#n0":"ShipTo","#n1":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfNullWithoutTheForgiving_IsARemove()
    {
        // Arrange
        using var fixture = new Fixture();
        string? nobody = null;

        // Act
        var json = fixture.Shop.Update("a", "b").Set(Customer.Attrs.Email, nobody).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"REMOVE #n0","ExpressionAttributeNames":{"#n0":"email"}}""");
    }

    [Test]
    public async Task ToRequestJson_NullableValueWithAValue_WritesIt()
    {
        // Arrange
        using var fixture = new Fixture();
        int? count = 7;

        // Act
        var json = fixture.Shop.Update("a", "b").Set(Customer.Attrs.OrderCount, count).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n0 = :v0","ExpressionAttributeNames":{"#n0":"orderCount"},"ExpressionAttributeValues":{":v0":{"N":"7"}}}""");
    }

    [Test]
    public async Task ToRequestJson_NullableValueWithoutAValue_IsARemove()
    {
        // Arrange
        using var fixture = new Fixture();
        int? count = null;

        // Act
        var json = fixture.Shop.Update("a", "b").Set(Customer.Attrs.OrderCount, count).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"REMOVE #n0","ExpressionAttributeNames":{"#n0":"orderCount"}}""");
    }

    [Test]
    public async Task ToRequestJson_NullableValueWithAValueThroughTheInterface_WritesIt()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").Filled(Customer.Attrs.OrderCount, 7).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n0 = :v0","ExpressionAttributeNames":{"#n0":"orderCount"},"ExpressionAttributeValues":{":v0":{"N":"7"}}}""");
    }

    [Test]
    public async Task ToRequestJson_NullableValueWithoutAValueThroughTheInterface_IsARemove()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").Filled(Customer.Attrs.OrderCount, null).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"REMOVE #n0","ExpressionAttributeNames":{"#n0":"orderCount"}}""");
    }

    [Test]
    public async Task ToRequestJson_NullableValueInATransaction_WritesOrRemoves()
    {
        // Arrange
        using var fixture = new Fixture();
        int? count = 7;
        int? none = null;

        // Act
        var json = fixture.Client.TransactWrite().Update(fixture.Shop, "a", "b").Set(Customer.Attrs.OrderCount, count).Set(Customer.Attrs.Email, "x").Update(fixture.Shop, "c", "d").Set(Customer.Attrs.OrderCount, none).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TransactItems":[{"Update":{"TableName":"Shop","Key":{"pk":{"S":"a"},"sk":{"S":"b"}},"UpdateExpression":"SET #n0 = :v0, #n1 = :v1","ExpressionAttributeNames":{"#n0":"orderCount","#n1":"email"},"ExpressionAttributeValues":{":v0":{"N":"7"},":v1":{"S":"x"}}}},{"Update":{"TableName":"Shop","Key":{"pk":{"S":"c"},"sk":{"S":"d"}},"UpdateExpression":"REMOVE #n0","ExpressionAttributeNames":{"#n0":"orderCount"}}}],"ClientRequestToken":"t"}""");
    }

    [Test]
    public async Task ToRequestJson_NullableValueThroughTheInterfaceInATransaction_WritesOrRemoves()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Client.TransactWrite().Update(fixture.Shop, "a", "b").Filled(Customer.Attrs.OrderCount, 7).Update(fixture.Shop, "c", "d").Filled(Customer.Attrs.OrderCount, null).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"REMOVE #n0"
            """);
    }

    [Test]
    public async Task ToRequestJson_SetIfNotExistsOfNullThroughAHandle_Throws()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentNullException>(() => Task.FromResult(fixture.Shop.Update("a", "b").SetIfNotExists(Customer.Attrs.Email, null!).ToRequestJson()));

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task ToRequestJson_DeleteOfNullThroughAHandle_Throws()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentNullException>(() => Task.FromResult(fixture.Shop.Update("a", "b").Delete(Customer.Attrs.Email, null!).ToRequestJson()));

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task ToRequestJson_AddOfNullThroughAHandle_Throws()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentNullException>(() => Task.FromResult(fixture.Shop.Update("a", "b").Add(DynaSharp.UnitTests.Mapping.KeyEntities.Handles.Attrs.Tags, null!).ToRequestJson()));

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task ToRequestJson_SetWithAnInterpolatedAction_RendersTheGrammar()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").Set($"{Attr("total")} = {Attr("total")} + {5}").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n0 = #n0 + :v0","ExpressionAttributeNames":{"#n0":"total"},"ExpressionAttributeValues":{":v0":{"N":"5"}}}""");
    }

    [Test]
    public async Task ToRequestJson_SeveralActionsOfOneClause_JoinsThemWithACommaAndOneKeyword()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").Set("x", 1).Set($"{Attr("y")} = {2}").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"UpdateExpression\":\"SET #n0 = :v0, #n1 = :v1\"");
    }

    [Test]
    [Arguments("SetIfNotExists", "SET #n0 = if_not_exists(#n0, :v0)")]
    [Arguments("Add", "ADD #n0 :v0")]
    [Arguments("Delete", "DELETE #n0 :v0")]
    public async Task ToRequestJson_ActionWithAValue_RendersItsClause(string action, string expected)
    {
        // Arrange
        using var fixture = new Fixture();
        var command = fixture.Shop.Update("a", "b");

        // Act
        var json = action switch
        {
            "SetIfNotExists" => command.SetIfNotExists("n", "x").ToRequestJson(),
            "Add" => command.Add("n", AttributeValue.FromStringSet(["x"])).ToRequestJson(),
            _ => command.Delete("n", AttributeValue.FromStringSet(["x"])).ToRequestJson(),
        };

        // Assert
        await Assert.That(json).Contains($"\"UpdateExpression\":\"{expected}\"");
    }

    [Test]
    [Arguments("SetIfNotExists", "SET #n0 = if_not_exists(#n0, :v0)")]
    [Arguments("Add", "ADD #n0 :v0")]
    public async Task ToRequestJson_ActionWithANumber_WritesTheNumber(string action, string expected)
    {
        // Arrange
        using var fixture = new Fixture();
        var command = fixture.Shop.Update("a", "b");

        // Act
        var json = action == "Add" ? command.Add("n", 1).ToRequestJson() : command.SetIfNotExists("n", 1).ToRequestJson();

        // Assert
        await Assert.That(json).Contains($"\"UpdateExpression\":\"{expected}\"");
    }

    [Test]
    [Arguments("SetIfNotExists", "SET #n0 = if_not_exists(#n0, :v0)")]
    [Arguments("Add", "ADD #n0 :v0")]
    [Arguments("Delete", "DELETE #n0 :v0")]
    public async Task ToRequestJson_ActionThroughAHandle_RendersItsClause(string action, string expected)
    {
        // Arrange
        using var fixture = new Fixture();
        var command = fixture.Shop.Update("a", "b");
        var attribute = Attr<int>("n");

        // Act
        var json = action switch
        {
            "SetIfNotExists" => command.SetIfNotExists(attribute, 1).ToRequestJson(),
            "Add" => command.Add(attribute, 1).ToRequestJson(),
            _ => command.Delete(attribute, 1).ToRequestJson(),
        };

        // Assert
        await Assert.That(json).Contains($"\"UpdateExpression\":\"{expected}\"");
    }

    [Test]
    public async Task ToRequestJson_Remove_RendersThePathWithoutAValue()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").Remove("legacy").Remove("tags[0]").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"REMOVE #n0, #n1[0]","ExpressionAttributeNames":{"#n0":"legacy","#n1":"tags"}}""");
    }

    [Test]
    public async Task ToRequestJson_ClausesInAnyOrder_WritesThemInTheOrderOfTheExpression()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").Delete("d", AttributeValue.FromStringSet(["x"])).Add("c", 1).Remove("r").Set("s", 1).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"UpdateExpression\":\"SET #n3 = :v1 REMOVE #n2 ADD #n1 :v1 DELETE #n0 :v0\"");
    }

    [Test]
    public async Task ToRequestJson_ReservedWords_NeverReachTheExpression()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").Set("status", "x").Add("size", 1).Remove("order").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"UpdateExpression\":\"SET #n0 = :v0 REMOVE #n2 ADD #n1 :v1\"");
    }

    [Test]
    public async Task ToRequestJson_NoChange_WritesNoUpdateExpression()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + "}");
    }

    [Test]
    public async Task ToRequestJson_StructCondition_WritesTheCondition()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").Set("x", 1).If(Attr("version").Eq(3)).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n0 = :v0","ConditionExpression":"#n1 = :v1","ExpressionAttributeNames":{"#n0":"x","#n1":"version"},"ExpressionAttributeValues":{":v0":{"N":"1"},":v1":{"N":"3"}}}""");
    }

    [Test]
    public async Task ToRequestJson_InterpolatedCondition_WritesTheCondition()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").If($"attribute_exists({Attr("pk")}) AND size({Attr("tags")}) < {10}").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ConditionExpression\":\"attribute_exists(#n0) AND size(#n1) < :v0\"");
    }

    [Test]
    public async Task ToRequestJson_TwoConditions_RequiresBoth()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").If(Attr("a").Eq(1).Or(Attr("b").Eq(2))).If(Attr("c").Eq(3)).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ConditionExpression\":\"(#n0 = :v0 OR #n1 = :v1) AND (#n2 = :v2)\"");
    }

    [Test]
    public async Task ToRequestJson_ThreeConditions_RequiresAllOfThem()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").If(Attr("a").Exists()).If(Attr("b").Exists()).IfExists().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ConditionExpression\":\"(attribute_exists(#n0)) AND (attribute_exists(#n1)) AND (attribute_exists(#n2))\"");
    }

    [Test]
    [Arguments(true, "attribute_exists(#n0)")]
    [Arguments(false, "attribute_not_exists(#n0)")]
    public async Task ToRequestJson_ExistenceOfTheItem_ChecksThePartitionKey(bool exists, string expected)
    {
        // Arrange
        using var fixture = new Fixture();
        var command = fixture.Shop.Update("a", "b");

        // Act
        var json = exists ? command.IfExists().ToRequestJson() : command.IfNotExists().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ",\"ConditionExpression\":\"" + expected + "\",\"ExpressionAttributeNames\":{\"#n0\":\"pk\"}}");
    }

    [Test]
    [Arguments("ReturnNew", "ALL_NEW")]
    [Arguments("ReturnOld", "ALL_OLD")]
    public async Task ToRequestJson_ReturnValues_SendsTheWireName(string option, string expected)
    {
        // Arrange
        using var fixture = new Fixture();
        var command = fixture.Shop.Update("a", "b");

        // Act
        var json = option == "ReturnNew" ? command.ReturnNew().ToRequestJson() : command.ReturnOld().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ",\"ReturnValues\":\"" + expected + "\"}");
    }

    [Test]
    public async Task ToRequestJson_EveryOption_WritesEachMember()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").ReturnOldOnFailure().ReturnCapacity(ReturnConsumedCapacity.Indexes).ReturnMetrics().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","ReturnValuesOnConditionCheckFailure":"ALL_OLD","ReturnConsumedCapacity":"INDEXES","ReturnItemCollectionMetrics":"SIZE"}""");
    }

    [Test]
    public async Task ToRequestJson_HelpersOverTheInterfaces_WorkOnACommandWithoutABox()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").Audited("me").OnlyIfOwned("me").Once().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"UpdateExpression\":\"SET #n0 = :v0\",\"ConditionExpression\":\"#n1 = :v0\"");
    }

    [Test]
    public async Task ToRequestJson_HelperThroughEveryMemberOfTheInterface_RendersEachClause()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Shop.Update("a", "b").Touched(Attr<int>("t"), 1).AllUntyped().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"UpdateExpression\":\"SET #n0 = :v0, #n0 = if_not_exists(#n0, :v0), #n1 = if_not_exists(#n1, :v0) REMOVE #n4 ADD #n0 :v0, #n2 :v1 DELETE #n0 :v0, #n3 :v2\"");
    }

    [Test]
    public async Task MaxAttempts_ZeroAttempts_Throws()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var failure = Rejected(fixture.Shop);

        // Assert
        await Assert.That(failure).IsTrue();
    }

    [Test]
    public async Task SendAsync_MaxAttemptsOne_DoesNotRetryOnAClientThatDoes()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("ProvisionedThroughputExceededException", "Slow down."));
        using var client = DynaSharp.UnitTests.Http.RetryTestSupport.CreateClient(handler, maxAttempts: 3);

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(async () => await client.Table("Shop", "pk", "sk").Update("a", "b").Add("n", 1).MaxAttempts(1).SendAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_MaxAttemptsAboveTheClient_RetriesForThisCall()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("ProvisionedThroughputExceededException", "Slow down."));
        handler.Enqueue(StubResponse.DynamoDbError("ProvisionedThroughputExceededException", "Slow down."));
        handler.Enqueue(StubResponse.Json("{}"));
        var time = new ManualTimeProvider();
        using var client = DynaSharp.UnitTests.Http.RetryTestSupport.CreateClient(handler, time, maxAttempts: 1);

        // Act
        await DynaSharp.UnitTests.Http.RetryTestSupport.AdvanceThroughDelaysAsync(Send(client), time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(3);

        static async Task Send(DynamoDbClient client) => await client.Table("Shop", "pk", "sk").Update("a", "b").Set("n", 1).MaxAttempts(3).SendAsync();
    }

    [Test]
    public async Task SendAsync_ConditionFails_ReturnsTheItemOnTheException()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"__type":"com.amazonaws.dynamodb.v20120810#ConditionalCheckFailedException","message":"The conditional request failed","Item":{"pk":{"S":"a"}}}""", System.Net.HttpStatusCode.BadRequest));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await client.Table("Shop", "pk", "sk").Update("a", "b").Set("n", 1).IfExists().ReturnOldOnFailure().SendAsync());

        // Assert
        await Assert.That(failure.Item!["pk"].AsString()).IsEqualTo("a");
    }

    [Test]
    public async Task SendAsync_ReturnNew_ReadsTheItem()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"pk":{"S":"a"},"n":{"N":"2"}},"ConsumedCapacity":{"TableName":"Shop","CapacityUnits":1.0},"ItemCollectionMetrics":{"SizeEstimateRangeGB":[0.0,1.0]}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk", "sk").Update("a", "b").Set("n", 2).ReturnNew().SendAsync();

        // Assert
        await Assert.That((result.HasAttributes, result.Attributes!["n"].AsNumber<int>(), result.ConsumedCapacity!.CapacityUnits, result.ItemCollectionMetrics!.SizeEstimateRangeGB!.Count)).IsEqualTo((true, 2, (double?)1.0, 2));
    }

    [Test]
    public async Task SendAsync_ResponseWithAnUnknownMember_SkipsIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Unknown":{"x":[1]}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk", "sk").Update("a", "b").Set("n", 2).SendAsync();

        // Assert
        await Assert.That(result.HasAttributes).IsFalse();
    }

    [Test]
    public async Task Await_Command_SendsIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk", "sk").Update("a", "b").Set("n", 2);

        // Assert
        await Assert.That(handler.Requests.Single().Operation).IsEqualTo("UpdateItem");
    }

    [Test]
    public async Task SendAsync_TypedTable_ReadsTheEntity()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"pk":{"S":"CUSTOMER#alice"},"sk":{"S":"PROFILE"},"_type":{"S":"CUSTOMER"},"fullName":{"S":"Alice"},"orderCount":{"N":"3"}}}"""));
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var result = await customers.Update(Customer.Keys.Of("alice")).Add(Customer.Attrs.OrderCount, 1).ReturnNew().SendAsync();

        // Assert
        await Assert.That(result.Attributes).IsEqualTo(new Customer("alice", "Alice", 3));
    }

    private static bool Rejected(Table<Item> shop)
    {
        var command = shop.Update("a", "b");
        try
        {
            command.MaxAttempts(0);
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return command.Set("still", "usable").ToRequestJson().Length > 0;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly StubHandler _handler = new();

        public Fixture()
        {
            Client = TestClient.Create(_handler);
            Shop = Client.Table("Shop", "pk", "sk");
        }

        public DynamoDbClient Client { get; }

        public Table<Item> Shop { get; }

        public void Dispose()
        {
            Client.Dispose();
            _handler.Dispose();
        }
    }
}
