using DynaSharp.Fluent;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Operations;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.Expr;
using static DynaSharp.UnitTests.Fluent.PageJson;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Fluent;

public sealed class QueryCommandTests
{
    [Test]
    public async Task Query_EmptyPartitionKey_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(shop.Query(string.Empty).ToRequestJson()));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("partitionKey");
    }

    [Test]
    public async Task Query_SortKeyConditionOnATableWithoutSortKey_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(shop.Query("a", SortKey.Eq("b")).ToRequestJson()));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("sortKey");
    }

    [Test]
    public async Task Query_SortKeyConditionOnAnIndexWithoutSortKey_NamesTheIndex()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var index = client.Table("Shop", "pk", "sk").Index("ByStatus", "status");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(index.Query("a", SortKey.Eq("b")).ToRequestJson()));

        // Assert
        await Assert.That(failure.Message).Contains("ByStatus");
    }

    [Test]
    public async Task Query_NoSortKeyConditionOnATableWithoutSortKey_ReadsThePartition()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Query("a").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","KeyConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"pk"},"ExpressionAttributeValues":{":v0":{"S":"a"}}}""");
    }

    [Test]
    public async Task Query_KeyNameWithADot_IsOneName()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "a.b", "c.d").Query(1, SortKey.Gt(2)).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""{"#n0":"a.b","#n1":"c.d"}""");
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(int.MinValue)]
    public async Task Limit_BelowOne_Throws(int limit)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => Task.FromResult(shop.Query("a").Limit(limit).ToRequestJson()));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("items");
    }

    [Test]
    [Arguments(1)]
    [Arguments(25)]
    [Arguments(int.MaxValue)]
    public async Task Limit_Valid_IsSent(int limit)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Query("a").Limit(limit).ToRequestJson();

        // Assert
        await Assert.That(json).Contains($"\"Limit\":{limit},");
    }

    [Test]
    public async Task MaxAttempts_BelowOne_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => Task.FromResult(shop.Query("a").MaxAttempts(0).ToRequestJson()));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("attempts");
    }

    [Test]
    public async Task MaxAttempts_One_SendsOneRequestPerPage()
    {
        // Arrange
        using var handler = new StubHandler(_ => Throttled());
        using var client = CreateClient(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => shop.Query("a").MaxAttempts(1).ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Filter_SecondOne_BothMustHold()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Query("a").Filter(Attr("x").Eq(1)).Filter($"{Attr("y")} > {2}").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"FilterExpression\":\"(#n1 = :v1) AND (#n2 > :v2)\"");
    }

    [Test]
    public async Task Filter_ThroughTheInterface_NeedsNoBox()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Query("a").NotDeleted().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("attribute_not_exists(#n1)");
    }

    [Test]
    public async Task Filter_NullCondition_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentNullException>(() => Task.FromResult(shop.Query("a").Filter<Condition>(null!).ToRequestJson()));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("condition");
    }

    [Test]
    public async Task Filter_StaleHandle_ThrowsBeforeItWrites()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var message = FilterOnStaleHandle(shop);

        // Assert
        await Assert.That(message).StartsWith("This Query on Shop was already sent, extended or rendered.");
    }

    [Test]
    public async Task Project_SecondCall_AddsToTheFirst()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Query("a").Project("x").Project("y.z").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ProjectionExpression\":\"#n1, #n2.#n3\"");
    }

    [Test]
    public async Task Project_Item_AddsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Query("a").Project("x").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ProjectionExpression\":\"#n1\"");
    }

    [Test]
    public async Task Project_EntityWithoutDiscriminator_AddsTheKeysOnly()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Session>("Sessions").Query("a").Project("x").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ExpressionAttributeNames\":{\"#n0\":\"id\",\"#n1\":\"x\"}").And.Contains("\"ProjectionExpression\":\"#n1, #n0\"");
    }

    [Test]
    public async Task Project_DiscriminatorOfAnotherName_AddsThatName()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Tenant>("Tenants").Query("a").Project("x").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ProjectionExpression\":\"#n2, #n1, #n0\"").And.Contains("\"#n1\":\"kind\"");
    }

    [Test]
    public async Task Project_EntityThatProjectsItsKeysItself_DoesNotRepeatThem()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Query("a").Project("pk", "_type", "fullName", "sk").ToRequestJson();

        // Assert (a path that is named twice is an error at DynamoDB)
        await Assert.That(json).Contains("\"ProjectionExpression\":\"#n0, #n1, #n2, #n3\"");
    }

    [Test]
    public async Task Project_EntityThatProjectsAPathBelowAKey_DoesNotOverlapIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Query("a").Project("pk.part", "_type[0]", "sk").ToRequestJson();

        // Assert (DynamoDB rejects two paths that overlap)
        await Assert.That(json).Contains("\"ProjectionExpression\":\"#n0.#n2, #n1[0], #n3\"");
    }

    [Test]
    public async Task Project_Filter_AndKeyConditionShareANameWithTheProjection()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Query("a").Filter(Attr("fullName").Eq("x")).Project("fullName").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ProjectionExpression\":\"#n2, #n1, #n0, #n3\"");
    }

    [Test]
    public async Task ToRequestJson_EntityWithoutProjection_AddsNoProjection()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Query("a").ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("ProjectionExpression");
    }

    [Test]
    public async Task ToRequestJson_EntityWithDiscriminator_FiltersOnIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Query("a").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"FilterExpression\":\"#n1 = :v1\"").And.Contains("\"#n1\":\"_type\"").And.Contains("\":v1\":{\"S\":\"CUSTOMER\"}");
    }

    [Test]
    public async Task ToRequestJson_EntityWithoutDiscriminator_HasNoFilter()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Session>("Sessions").Query("a").ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("FilterExpression");
    }

    [Test]
    public async Task ToRequestJson_DiscriminatorIsThePartitionKeyOfTheIndex_LeavesTheFilterOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Index("ByType", "_type", "sk").Query("CUSTOMER").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","IndexName":"ByType","KeyConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"_type"},"ExpressionAttributeValues":{":v0":{"S":"CUSTOMER"}}}""");
    }

    [Test]
    public async Task ToRequestJson_DiscriminatorIsTheSortKeyOfTheIndex_LeavesTheFilterOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Index("ByPartition", "pk", "_type").Query("a", SortKey.Eq("CUSTOMER")).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","IndexName":"ByPartition","KeyConditionExpression":"#n0 = :v0 AND #n1 = :v1","ExpressionAttributeNames":{"#n0":"pk","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"S":"a"},":v1":{"S":"CUSTOMER"}}}""");
    }

    [Test]
    public async Task ToRequestJson_ScanOfAnIndexOnTheDiscriminator_KeepsTheFilter()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Index("ByType", "_type", "sk").Scan().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","IndexName":"ByType","FilterExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"_type"},"ExpressionAttributeValues":{":v0":{"S":"CUSTOMER"}}}""");
    }

    [Test]
    public async Task ToRequestJson_IndexOfAnEntity_FiltersOnTheDiscriminatorToo()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Index("ByEmail").Query("a@b.ch").ToRequestJson();

        // Assert
        await Assert.That(json).StartsWith("""{"TableName":"Shop","IndexName":"ByEmail","KeyCondition""").And.Contains("\"FilterExpression\":\"#n1 = :v1\"");
    }

    [Test]
    public async Task ToRequestJson_SentTwice_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var message = RenderTwice(shop);

        // Assert
        await Assert.That(message).StartsWith("This Query on Shop was already sent, extended or rendered.");
    }

    [Test]
    public async Task DebuggerText_Command_NamesOperationAndTable()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var text = client.Table("Shop", "pk").Query("a").DebuggerText;

        // Assert
        await Assert.That(text).Contains("Query on Shop: {\"TableName\":\"Shop\"");
    }

    [Test]
    public async Task Query_GeneratedKey_IsTheQueryWithEqOnTheSortKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var byKey = customers.Query(Customer.Keys.Of("alice")).ToRequestJson();
        var byCondition = customers.Query("CUSTOMER#alice", SortKey.Eq("PROFILE")).ToRequestJson();

        // Assert
        await Assert.That(byKey).IsEqualTo(byCondition);
    }

    [Test]
    public async Task Query_KeyOfATableWithoutASortKey_QueriesThePartitionKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Session>("Sessions").Query(new SessionKey("s-1")).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Sessions","KeyConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"id"},"ExpressionAttributeValues":{":v0":{"S":"s-1"}}}""");
    }

    [Test]
    public async Task Query_KeyWithOneValueTwice_SharesItsPlaceholder()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var json = shop.Query(new TextKey("pk"u8.ToArray(), "a", "sk"u8.ToArray(), "a")).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","KeyConditionExpression":"#n0 = :v0 AND #n1 = :v0","ExpressionAttributeNames":{"#n0":"pk","#n1":"sk"},"ExpressionAttributeValues":{":v0":{"S":"a"}}}""");
    }

    [Test]
    public async Task Query_KeyNameThatJsonEscapes_NamesTheSameAttributeAsAConditionDoes()
    {
        // Arrange (the name comes out of the JSON of the key, escaped as a writer escapes it)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "p\"k", "s\\k");

        // Act
        var byKey = shop.Query(new TextKey("p\"k"u8.ToArray(), "a", "s\\k"u8.ToArray(), "b")).ToRequestJson();
        var byCondition = shop.Query("a", SortKey.Eq("b")).ToRequestJson();

        // Assert
        await Assert.That(byKey).IsEqualTo(byCondition);
    }

    [Test]
    public async Task Query_NullKey_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentNullException>(() => Task.FromResult(shop.Query((TextKey)null!).ToRequestJson()));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("key");
    }

    [Test]
    public async Task Query_ByKey_GoesOnWithTheFilterOfTheCommand()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Query(Customer.Keys.Of("alice")).Filter(Attr("x").Exists()).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "FilterExpression":"(#n2 = :v2) AND (attribute_exists(#n3))"
            """);
    }

    [Test]
    public async Task ItemAsync_OnePage_GivesTheFirstItem()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Last("1", "2"));
        using var client = TestClient.Create(handler);

        // Act
        var item = await client.Table("Shop", "pk", "sk").Query("a").ItemAsync();

        // Assert
        await Assert.That(item!.GetString("sk")).IsEqualTo("1");
    }

    [Test]
    public async Task ItemAsync_Nothing_GivesNull()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Last());
        using var client = TestClient.Create(handler);

        // Act
        var item = await client.Table("Shop", "pk", "sk").Query("a").ItemAsync();

        // Assert
        await Assert.That(item).IsNull();
    }

    [Test]
    public async Task ItemAsync_NoLimitAsked_SendsWhatToRequestJsonShows()
    {
        // Arrange (a limit works before the filter, so one of its own could lose the match)
        using var handler = new StubHandler();
        handler.Enqueue(Last("1"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var rendered = shop.Query("a").Filter(Attr("x").Exists()).ToRequestJson();
        await shop.Query("a").Filter(Attr("x").Exists()).ItemAsync();

        // Assert
        await Assert.That(handler.Requests[0].BodyText).IsEqualTo(rendered);
    }

    [Test]
    public async Task ItemAsync_LimitAsked_SendsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Last("1"));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk", "sk").Query("a").Limit(1).ItemAsync();

        // Assert
        await Assert.That(handler.Requests[0].BodyText).Contains("""
            "Limit":1
            """);
    }

    [Test]
    public async Task ItemAsync_FilteredEmptyPages_FollowsThemAndStopsAtTheFirstItem()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(FilteredEmpty("1"));
        handler.Enqueue(FilteredEmpty("2"));
        handler.Enqueue(More("3", "4"));
        handler.Enqueue(Last("5"));
        using var client = TestClient.Create(handler);

        // Act
        var item = await client.Table("Shop", "pk", "sk").Query("a").Filter(Attr("x").Exists()).ItemAsync();

        // Assert
        await Assert.That((item!.GetString("sk"), handler.Requests.Count)).IsEqualTo(("3", 3));
    }

    [Test]
    public async Task ItemAsync_OnlyFilteredEmptyPages_GivesNull()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(FilteredEmpty("1"));
        handler.Enqueue(Last());
        using var client = TestClient.Create(handler);

        // Act
        var item = await client.Table("Shop", "pk", "sk").Query("a").ItemAsync();

        // Assert
        await Assert.That((item is null, handler.Requests.Count)).IsEqualTo((true, 2));
    }

    [Test]
    public async Task ItemAsync_Index_GivesTheFirstItem()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Last("1"));
        using var client = TestClient.Create(handler);

        // Act
        var item = await client.Table("Shop", "pk", "sk").Index("ByStatus", "status").Query("open").ItemAsync();

        // Assert
        await Assert.That((item!.GetString("sk"), handler.Requests[0].BodyText.Contains("ByStatus", StringComparison.Ordinal))).IsEqualTo(("1", true));
    }

    [Test]
    public async Task ItemAsync_ItemOfTheEntity_ReadsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Items":[{"pk":{"S":"CUSTOMER#alice"},"sk":{"S":"PROFILE"},"_type":{"S":"CUSTOMER"},"fullName":{"S":"Alice"},"orderCount":{"N":"3"}}],"Count":1,"ScannedCount":1}"""));
        using var client = TestClient.Create(handler);

        // Act
        var customer = await client.Table<Customer>("Shop").Query(Customer.Keys.Of("alice")).ItemAsync();

        // Assert
        await Assert.That(customer).IsEqualTo(new Customer("alice", "Alice", 3));
    }

    [Test]
    public async Task ItemAsync_ProjectionOfAnEntity_AsksForWhatTheEntityNeeds()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Last());
        using var client = TestClient.Create(handler);

        // Act
        await client.Table<Customer>("Shop").Query("a").Project("email").ItemAsync();

        // Assert
        await Assert.That(handler.Requests[0].BodyText).Contains("""
            "ProjectionExpression":"#n2, #n1, #n0, #n3"
            """);
    }

    [Test]
    public async Task ItemAsync_StructAndNoItem_GivesItsDefault()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Items":[]}"""));
        using var client = TestClient.Create(handler);

        // Act
        var session = await client.Table<Session>("Sessions").Query("s").ItemAsync();

        // Assert
        await Assert.That(session).IsEqualTo(default(Session));
    }

    [Test]
    public async Task ItemAsync_Done_ReleasesTheStateOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Last("1"));
        using var client = TestClient.Create(handler);
        var (item, state, released) = StartItem(client.Table("Shop", "pk", "sk"));

        // Act
        await item;

        // Assert
        await Assert.That(state.Version).IsEqualTo(released);
    }

    [Test]
    public async Task ItemAsync_ServiceFails_ReleasesTheStateOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("ResourceNotFoundException", "Gone."));
        using var client = TestClient.Create(handler);
        var (item, state, released) = StartItem(client.Table("Shop", "pk", "sk"));

        // Act
        await TestClient.CaptureAsync<ResourceNotFoundException>(async () => await item);

        // Assert
        await Assert.That(state.Version).IsEqualTo(released);
    }

    [Test]
    public async Task ItemAsync_Cancelled_SendsNothingAndReleasesTheState()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var (item, state, released) = StartItem(client.Table("Shop", "pk", "sk"), cancellation.Token);

        // Act
        await TestClient.CaptureAsync<OperationCanceledException>(async () => await item);

        // Assert
        await Assert.That((state.Version, handler.Requests.Count)).IsEqualTo((released, 0));
    }

    [Test]
    public async Task ItemAsync_CommandUsedAlready_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(async () => await ItemAfterRendering(shop));

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task ItemsAsync_OnePage_GivesItsItems()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Last("1", "2", "3"));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query("a").ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.SortKeys(), handler.Requests.Count)).IsEqualTo(("1,2,3", 1));
    }

    [Test]
    public async Task ItemsAsync_Nothing_GivesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Last());
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query("a").ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(items).IsEmpty();
    }

    [Test]
    public async Task ItemsAsync_ResponseWithoutItems_GivesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query("a").ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(items).IsEmpty();
    }

    [Test]
    public async Task ItemsAsync_SeveralPages_FollowsThem()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1", "2"));
        handler.Enqueue(More("3"));
        handler.Enqueue(Last("4", "5"));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query("a").ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.SortKeys(), handler.Requests.Count)).IsEqualTo(("1,2,3,4,5", 3));
    }

    [Test]
    public async Task ItemsAsync_NextPage_SendsTheKeyAsItCame()
    {
        // Arrange: a key is passed on as bytes, so what a parser would normalize must survive: an escape, a number's spelling, spaces.
        const string Key = """{ "pk": {"S":"café"}, "sk":{"N":"1.50E+1"} }""";
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Items(Key, "1")));
        handler.Enqueue(Last());
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk", "sk").Query("a").ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(handler.Requests[1].BodyText).EndsWith(",\"ExclusiveStartKey\":" + Key + "}");
    }

    [Test]
    public async Task ItemsAsync_NextPage_DiffersFromTheFirstByTheStartKeyOnly()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        handler.Enqueue(More("2"));
        handler.Enqueue(Last());
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk", "sk").Query("a", SortKey.BeginsWith("O")).Filter(Attr("x").Exists()).Project("y").Limit(5).Descending().Consistent().ReturnCapacity().ItemsAsync().ToListAsync();
        var bodies = handler.Requests.Select(static request => request.BodyText).ToList();

        // Assert
        await Assert.That((bodies[1], bodies[2])).IsEqualTo((bodies[0][..^1] + ",\"ExclusiveStartKey\":" + Key("1") + "}", bodies[0][..^1] + ",\"ExclusiveStartKey\":" + Key("2") + "}"));
    }

    [Test]
    public async Task ItemsAsync_LongKey_GrowsTheBody()
    {
        // Arrange: longer than what a state keeps for a key and than the body has room for
        var sortKey = new string('k', 9000);
        using var handler = new StubHandler();
        handler.Enqueue(More(sortKey));
        handler.Enqueue(Last());
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk", "sk").Query("a").ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(handler.Requests[1].BodyText).EndsWith(",\"ExclusiveStartKey\":" + Key(sortKey) + "}");
    }

    [Test]
    public async Task ItemsAsync_FilteredEmptyPages_FollowsThemToTheItems()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(FilteredEmpty("1"));
        handler.Enqueue(FilteredEmpty("2"));
        handler.Enqueue(More("3"));
        handler.Enqueue(FilteredEmpty("4"));
        handler.Enqueue(Last("5"));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query("a").Filter(Attr("x").Exists()).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.SortKeys(), handler.Requests.Count)).IsEqualTo(("3,5", 5));
    }

    [Test]
    public async Task ItemsAsync_OnlyFilteredEmptyPages_EndsWithNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(FilteredEmpty("1"));
        handler.Enqueue(Last());
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query("a").ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.Count, handler.Requests.Count)).IsEqualTo((0, 2));
    }

    [Test]
    public async Task ItemsAsync_EmptyLastEvaluatedKey_Ends()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Items("{}", "1")));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query("a").ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.Count, handler.Requests.Count)).IsEqualTo((1, 1));
    }

    [Test]
    public async Task ItemsAsync_EmptyLastEvaluatedKeyWithSpaces_Ends()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Items("{ }", "1")));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk", "sk").Query("a").ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ItemsAsync_LastEvaluatedKeyThatIsNoObject_FailsTheRead()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Items("null", "1")));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbException>(() => shop.Query("a").ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("UnrecognizedResponse");
    }

    [Test]
    public async Task ItemsAsync_LimitPerPage_ReadsEverything()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1", "2"));
        handler.Enqueue(Last("3"));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query("a").Limit(2).ItemsAsync().ToListAsync();

        // Assert (a limit shapes pages, it does not end the read)
        await Assert.That((items.Count, handler.Requests.All(static request => request.BodyText.Contains("\"Limit\":2,", StringComparison.Ordinal)))).IsEqualTo((3, true));
    }

    [Test]
    public async Task ItemsAsync_NotEnumerated_SendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        _ = client.Table("Shop", "pk", "sk").Query("a").ItemsAsync();

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ItemsAsync_BreakOutEarly_SendsNoFurtherPage()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1", "2"));
        using var client = TestClient.Create(handler);
        var read = StartItems(client.Table("Shop", "pk", "sk"));

        // Act
        await foreach (var unused in read.Read)
        {
            break;
        }

        // Assert
        await Assert.That((read.ReleasedOnce, handler.Requests.Count)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task ItemsAsync_Completed_ReleasesTheStateOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        handler.Enqueue(Last("2"));
        using var client = TestClient.Create(handler);
        var read = StartItems(client.Table("Shop", "pk", "sk"));

        // Act
        await read.Read.ToListAsync();

        // Assert
        await Assert.That(read.ReleasedOnce).IsTrue();
    }

    [Test]
    public async Task ItemsAsync_DisposedOnAPageBoundary_ReleasesTheStateOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        using var client = TestClient.Create(handler);
        var read = StartItems(client.Table("Shop", "pk", "sk"));
        var enumerator = read.Read.GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        // Act
        await enumerator.DisposeAsync();

        // Assert
        await Assert.That(read.ReleasedOnce).IsTrue();
    }

    [Test]
    public async Task ItemsAsync_DisposedBeforeTheFirstMove_ReleasesTheStateAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var read = StartItems(client.Table("Shop", "pk", "sk"));
        var enumerator = read.Read.GetAsyncEnumerator();

        // Act
        await enumerator.DisposeAsync();

        // Assert
        await Assert.That((read.ReleasedOnce, handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task ItemsAsync_DisposedTwice_ReleasesTheStateOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Last("1"));
        using var client = TestClient.Create(handler);
        var read = StartItems(client.Table("Shop", "pk", "sk"));
        var enumerator = read.Read.GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        // Act
        await enumerator.DisposeAsync();
        await enumerator.DisposeAsync();

        // Assert
        await Assert.That(read.ReleasedOnce).IsTrue();
    }

    [Test]
    public async Task ItemsAsync_MovedAfterDispose_IsOver()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var enumerator = client.Table("Shop", "pk", "sk").Query("a").ItemsAsync().GetAsyncEnumerator();
        await enumerator.DisposeAsync();

        // Act
        var moved = await enumerator.MoveNextAsync();

        // Assert
        await Assert.That((moved, handler.Requests.Count)).IsEqualTo((false, 0));
    }

    [Test]
    public async Task ItemsAsync_DisposedWhileAPageIsInFlight_RefusesAndKeepsTheState()
    {
        // Arrange
        var answer = new TaskCompletionSource<StubResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler((_, _) => answer.Task);
        using var client = TestClient.Create(handler);
        var read = StartItems(client.Table("Shop", "pk", "sk"));
        var enumerator = read.Read.GetAsyncEnumerator();
        var moving = enumerator.MoveNextAsync();

        // Act
        await TestClient.CaptureAsync<NotSupportedException>(async () => await enumerator.DisposeAsync());
        var releasedEarly = read.ReleasedOnce;
        answer.SetResult(Last("1"));
        await moving;
        await enumerator.DisposeAsync();

        // Assert
        await Assert.That((releasedEarly, read.ReleasedOnce)).IsEqualTo((false, true));
    }

    [Test]
    public async Task ItemsAsync_EnumeratedTwice_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Last("1"));
        using var client = TestClient.Create(handler);
        var items = client.Table("Shop", "pk", "sk").Query("a").ItemsAsync();
        await items.ToListAsync();

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(() => items.ToListAsync());

        // Assert
        await Assert.That((failure.Message.StartsWith("This Query on Shop was enumerated already.", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task ItemsAsync_SecondEnumerationBeforeTheFirstRan_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var items = client.Table("Shop", "pk", "sk").Query("a").ItemsAsync();
        var first = items.GetAsyncEnumerator();

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(() => Task.FromResult(items.GetAsyncEnumerator()));
        await first.DisposeAsync();

        // Assert
        await Assert.That(failure.Message).Contains("enumerated already");
    }

    [Test]
    public async Task ItemsAsync_CommandUsedAgainAfterItStarted_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var message = EnumerateFromAStaleHandle(shop);

        // Assert
        await Assert.That(message).StartsWith("This Query on Shop was already sent, extended or rendered.");
    }

    [Test]
    public async Task ItemsAsync_CancelledMidEnumeration_ThrowsTheTokenOfTheCallerAtThePageBoundary()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        using var handler = new StubHandler();
        handler.Enqueue(More("1", "2"));
        using var client = TestClient.Create(handler);
        var read = StartItems(client.Table("Shop", "pk", "sk"), cancellation.Token);
        var seen = new List<Item>();

        // Act
        var failure = await TestClient.CaptureAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in read.Read)
            {
                seen.Add(item);
                await cancellation.CancelAsync();
            }
        });

        // Assert (the page in hand is delivered: nothing more is read, and what was read is not lost)
        await Assert.That((seen.Count, failure.CancellationToken == cancellation.Token, handler.Requests.Count, read.ReleasedOnce)).IsEqualTo((2, true, 1, true));
    }

    [Test]
    public async Task ItemsAsync_WithCancellation_StopsTheRead()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var read = StartItems(client.Table("Shop", "pk", "sk"));

        // Act
        await TestClient.CaptureAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in read.Read.WithCancellation(cancellation.Token))
            {
                // Nothing arrives.
            }
        });

        // Assert
        await Assert.That((handler.Requests.Count, read.ReleasedOnce)).IsEqualTo((0, true));
    }

    [Test]
    public async Task ItemsAsync_TokenOfTheCallAndOfWithCancellation_EitherStopsTheRead()
    {
        // Arrange
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        using var client = TestClient.Create(handler);
        var read = StartItems(client.Table("Shop", "pk", "sk"), first.Token);

        // Act
        await TestClient.CaptureAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in read.Read.WithCancellation(second.Token))
            {
                await second.CancelAsync();
            }
        });

        // Assert
        await Assert.That((handler.Requests.Count, read.ReleasedOnce)).IsEqualTo((1, true));
    }

    [Test]
    public async Task ItemsAsync_TwoTokensThatStayQuiet_ReadsToTheEnd()
    {
        // Arrange
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        using var handler = new StubHandler();
        handler.Enqueue(Last("1"));
        using var client = TestClient.Create(handler);
        var read = StartItems(client.Table("Shop", "pk", "sk"), first.Token);

        // Act
        var items = await read.Read.WithCancellation(second.Token).ToListAsync();

        // Assert
        await Assert.That((items.Count, read.ReleasedOnce)).IsEqualTo((1, true));
    }

    [Test]
    public async Task PagesAsync_TwoTokensThatStayQuiet_ReadsToTheEnd()
    {
        // Arrange
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        using var handler = new StubHandler();
        handler.Enqueue(Last("1"));
        using var client = TestClient.Create(handler);
        var read = StartPages(client.Table("Shop", "pk", "sk"), first.Token);

        // Act
        var pages = await read.Read.WithCancellation(second.Token).ToListAsync();

        // Assert
        await Assert.That((pages.Count, read.ReleasedOnce)).IsEqualTo((1, true));
    }

    [Test]
    public async Task ItemsAsync_CancelledWhileAPageIsInFlight_StopsIt()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, token) =>
        {
            reached.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Last();
        });
        using var client = TestClient.Create(handler);
        var read = StartItems(client.Table("Shop", "pk", "sk"), cancellation.Token);

        // Act
        var reading = read.Read.ToListAsync();
        await reached.Task;
        await cancellation.CancelAsync();
        var failure = await TestClient.CaptureAsync<OperationCanceledException>(() => reading);

        // Assert
        await Assert.That((failure.CancellationToken == cancellation.Token, read.ReleasedOnce)).IsEqualTo((true, true));
    }

    [Test]
    public async Task ItemsAsync_ServiceFails_SurfacesAndReleasesTheStateOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        handler.Enqueue(StubResponse.DynamoDbError("ResourceNotFoundException", "Gone."));
        using var client = TestClient.Create(handler);
        var read = StartItems(client.Table("Shop", "pk", "sk"));

        // Act
        await TestClient.CaptureAsync<ResourceNotFoundException>(() => read.Read.ToListAsync());

        // Assert
        await Assert.That(read.ReleasedOnce).IsTrue();
    }

    [Test]
    public async Task ItemsAsync_RejectedOnTheSecondPage_ExplainsTheExpressionsWithoutTheKey()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("key-secret"));
        handler.Enqueue(StubResponse.DynamoDbError("ValidationException", "Rejected."));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(() => shop.Query("a").Filter(Attr("status").Eq("open")).ItemsAsync().ToListAsync());

        // Assert
        await Assert.That((failure.ExpressionNames!["#n1"], failure.Expressions!.ContainsKey("KeyConditionExpression"), failure.Message.Contains("key-secret", StringComparison.Ordinal))).IsEqualTo(("status", true, false));
    }

    [Test]
    public async Task ItemsAsync_InvalidOption_FailsOnEnumerationAndGivesTheStateBack()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => shop.Query("a").ReturnCapacity((ReturnConsumedCapacity)99).ItemsAsync().ToListAsync());
        var json = shop.Query("b").ToRequestJson();

        // Assert
        await Assert.That((failure is not null, json.Contains("ReturnConsumedCapacity", StringComparison.Ordinal))).IsEqualTo((true, false));
    }

    [Test]
    public async Task ItemsAsync_ClientDisposed_ThrowsAndReleasesTheState()
    {
        // Arrange
        using var handler = new StubHandler();
        var client = TestClient.Create(handler);
        var read = StartItems(client.Table("Shop", "pk", "sk"));
        client.Dispose();

        // Act
        await TestClient.CaptureAsync<ObjectDisposedException>(() => read.Read.ToListAsync());

        // Assert
        await Assert.That(read.ReleasedOnce).IsTrue();
    }

    [Test]
    public async Task ItemsAsync_ThrottledOnTheFirstPage_SendsTheSameBodyAgain()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Last("1"));
        using var client = CreateClient(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query("a").ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.Count, handler.Requests[0].BodyText == handler.Requests[1].BodyText)).IsEqualTo((1, true));
    }

    [Test]
    public async Task ItemsAsync_ThrottledOnALaterPage_RepeatsThatPageOnly()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        handler.Enqueue(ServerError());
        handler.Enqueue(Throttled());
        handler.Enqueue(More("2"));
        handler.Enqueue(Last("3"));
        using var client = CreateClient(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query("a").ItemsAsync().ToListAsync();
        var bodies = handler.Requests.Select(static request => request.BodyText).ToList();

        // Assert
        await Assert.That((items.SortKeys(), bodies[1] == bodies[2], bodies[2] == bodies[3], bodies[3].Contains(Key("1"), StringComparison.Ordinal), bodies[4].Contains(Key("2"), StringComparison.Ordinal)))
            .IsEqualTo(("1,2,3", true, true, true, true));
    }

    [Test]
    public async Task ItemsAsync_ItemsBehindTheKeyCannotBeRead_FailsTheRead()
    {
        // Arrange: the key comes first in this response, and the items after it cannot be read
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{\"LastEvaluatedKey\":" + Key("9") + ",\"Items\":[{\"pk\":{\"N\":\"1\"}}]}"));
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbException>(() => customers.Query("a").ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("UnrecognizedResponse");
    }

    [Test]
    public async Task ItemsAsync_ItemOfTheEntity_ReadsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Items":[{"pk":{"S":"CUSTOMER#alice"},"sk":{"S":"PROFILE"},"_type":{"S":"CUSTOMER"},"fullName":{"S":"Alice"},"orderCount":{"N":"3"}}],"Count":1,"ScannedCount":4}"""));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table<Customer>("Shop").Query("CUSTOMER#alice").ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(items.Single()).IsEqualTo(new Customer("alice", "Alice", 3));
    }

    [Test]
    public async Task ItemsAsync_ItemOfAStruct_ReadsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Items":[{"id":{"S":"s-1"}},{"id":{"S":"s-2"}}]}"""));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table<Session>("Sessions").Query("s").ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", items.Select(static session => session.Id))).IsEqualTo("s-1,s-2");
    }

    [Test]
    public async Task ItemsAsync_ItemOfAnotherEntity_ThrowsTheMappingException()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Items":[{"pk":{"S":"a"},"fullName":{"S":"Alice"},"_type":{"S":"ORDER"}}]}"""));
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => customers.Query("a").ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("_type");
    }

    [Test]
    public async Task ItemsAsync_ManyItems_GrowsPastTheFirstRental()
    {
        // Arrange
        var sortKeys = Enumerable.Range(0, 1000).Select(static i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        using var handler = new StubHandler();
        handler.Enqueue(Last(sortKeys));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query("a").ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(items.SortKeys()).IsEqualTo(string.Join(",", sortKeys));
    }

    [Test]
    public async Task ItemsAsync_PoolAfterTheRead_HoldsWhatAnIdleStateHolds()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler();
        handler.Enqueue(More(new string('k', 40_000)));
        handler.Enqueue(Last());
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await client.Table("Shop", "pk", "sk").Query("a").ItemsAsync().ToListAsync();
        client.Dispose();

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task Release_NextCommand_StartsCleanAfterARead()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        handler.Enqueue(Last());
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        await shop.Query("a").Filter(Attr("x").Exists()).Project("y").Limit(5).Descending().Consistent().ReturnCapacity().MaxAttempts(2).ItemsAsync().ToListAsync();

        // Act
        var json = shop.Query("b").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","KeyConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"pk"},"ExpressionAttributeValues":{":v0":{"S":"b"}}}""");
    }

    [Test]
    public async Task PagesAsync_SeveralPages_GivesEachWithItsNumbers()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1", "2"));
        handler.Enqueue(FilteredEmpty("5"));
        handler.Enqueue(Last("6"));
        using var client = TestClient.Create(handler);

        // Act
        var pages = await client.Table("Shop", "pk", "sk").Query("a").PagesAsync().ToListAsync();

        // Assert (a page that a filter left empty is a response like any other)
        await Assert.That(string.Join(";", pages.Select(static page => $"{page.Items.Count},{page.Count},{page.ScannedCount},{page.HasMore}"))).IsEqualTo("2,2,2,True;0,0,7,True;1,1,1,False");
    }

    [Test]
    public async Task PagesAsync_Next_HoldsTheKeyOfThePage()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1", "2"));
        handler.Enqueue(Last("3"));
        using var client = TestClient.Create(handler);

        // Act
        var pages = await client.Table("Shop", "pk", "sk").Query("a").PagesAsync().ToListAsync();

        // Assert
        await Assert.That((pages[0].Next.ToKey()!.GetString("sk"), pages[1].Next.HasValue, pages[1].Next.ToKey())).IsEqualTo(("2", false, null));
    }

    [Test]
    public async Task PagesAsync_ConsumedCapacity_IsPerPage()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{\"Items\":[],\"ConsumedCapacity\":{\"TableName\":\"Shop\",\"CapacityUnits\":1.5},\"LastEvaluatedKey\":" + Key("1") + "}"));
        handler.Enqueue(StubResponse.Json("""{"Items":[]}"""));
        using var client = TestClient.Create(handler);

        // Act
        var pages = await client.Table("Shop", "pk", "sk").Query("a").ReturnCapacity().PagesAsync().ToListAsync();

        // Assert
        await Assert.That((pages[0].ConsumedCapacity!.CapacityUnits, pages[1].ConsumedCapacity)).IsEqualTo(((double?)1.5, null));
    }

    [Test]
    public async Task PagesAsync_EnumeratedTwice_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Last());
        using var client = TestClient.Create(handler);
        var pages = client.Table("Shop", "pk", "sk").Query("a").PagesAsync();
        await pages.ToListAsync();

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(() => pages.ToListAsync());

        // Assert
        await Assert.That(failure.Message).Contains("enumerated already");
    }

    [Test]
    public async Task PagesAsync_DisposedAfterAPage_ReleasesTheStateOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        using var client = TestClient.Create(handler);
        var read = StartPages(client.Table("Shop", "pk", "sk"));

        // Act
        await foreach (var unused in read.Read)
        {
            break;
        }

        // Assert
        await Assert.That(read.ReleasedOnce).IsTrue();
    }

    [Test]
    public async Task PagesAsync_Cancelled_ThrowsAtTheNextPage()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        using var client = TestClient.Create(handler);
        var read = StartPages(client.Table("Shop", "pk", "sk"), cancellation.Token);

        // Act
        await TestClient.CaptureAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in read.Read)
            {
                await cancellation.CancelAsync();
            }
        });

        // Assert
        await Assert.That((handler.Requests.Count, read.ReleasedOnce)).IsEqualTo((1, true));
    }

    [Test]
    public async Task PagesAsync_ServiceFails_ReleasesTheStateOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("ResourceNotFoundException", "Gone."));
        using var client = TestClient.Create(handler);
        var read = StartPages(client.Table("Shop", "pk", "sk"));

        // Act
        await TestClient.CaptureAsync<ResourceNotFoundException>(() => read.Read.ToListAsync());

        // Assert
        await Assert.That(read.ReleasedOnce).IsTrue();
    }

    [Test]
    public async Task PageAsync_NoPosition_ReadsTheFirstPage()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query("a").PageAsync();

        // Assert
        await Assert.That((page.Items.Count, page.HasMore, handler.Requests[0].BodyText.Contains("ExclusiveStartKey", StringComparison.Ordinal))).IsEqualTo((1, true, false));
    }

    [Test]
    public async Task PageAsync_Position_ContinuesFromIt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        handler.Enqueue(Last("2"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        var first = await shop.Query("a").PageAsync();

        // Act
        var second = await shop.Query("a").PageAsync(ContinuationToken.Parse(first.Next.ToString()));

        // Assert
        await Assert.That((second.HasMore, handler.Requests[1].BodyText.EndsWith(",\"ExclusiveStartKey\":" + Key("1") + "}", StringComparison.Ordinal))).IsEqualTo((false, true));
    }

    [Test]
    public async Task PageAsync_ConsumedCapacity_IsOnThePage()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Items":[],"ConsumedCapacity":{"TableName":"Shop","CapacityUnits":0.5}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query("a").ReturnCapacity().PageAsync();

        // Assert
        await Assert.That(page.ConsumedCapacity!.CapacityUnits).IsEqualTo(0.5);
    }

    [Test]
    public async Task PageAsync_Done_ReleasesTheStateOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Last());
        using var client = TestClient.Create(handler);
        var (page, state, released) = StartPage(client.Table("Shop", "pk", "sk"));

        // Act
        await page;

        // Assert
        await Assert.That(state.Version).IsEqualTo(released);
    }

    [Test]
    public async Task PageAsync_ServiceFails_ReleasesTheStateOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("ResourceNotFoundException", "Gone."));
        using var client = TestClient.Create(handler);
        var (page, state, released) = StartPage(client.Table("Shop", "pk", "sk"));

        // Act
        await TestClient.CaptureAsync<ResourceNotFoundException>(async () => await page);

        // Assert
        await Assert.That(state.Version).IsEqualTo(released);
    }

    [Test]
    public async Task PageAsync_Cancelled_SendsNothingAndReleasesTheState()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var (page, state, released) = StartPage(client.Table("Shop", "pk", "sk"), cancellation.Token);

        // Act
        await TestClient.CaptureAsync<OperationCanceledException>(async () => await page);

        // Assert
        await Assert.That((state.Version, handler.Requests.Count)).IsEqualTo((released, 0));
    }

    [Test]
    public async Task PageAsync_ClientDisposed_ThrowsAndReleasesTheState()
    {
        // Arrange
        using var handler = new StubHandler();
        var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        client.Dispose();

        // Act
        var failure = await TestClient.CaptureAsync<ObjectDisposedException>(async () => await shop.Query("a").PageAsync());

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task PageAsync_ResponseVector_ReadsWhatTheRequestApiReads()
    {
        // Arrange: botocore's responses, with the request API as the proven reader of them
        var failures = new List<string>();

        // Act
        foreach (var vector in OperationVectors.Responses.Where(static vector => vector.Operation == "Query"))
        {
            using var handler = new StubHandler(_ => StubResponse.Json(vector.Wire));
            using var client = TestClient.Create(handler);
            var request = await DynaSharp.Requests.DynamoDbRequests.QueryAsync(client, TestClient.CreateQuery());
            var page = await client.Table("Users", "pk").Query("a").PageAsync();
            if (!Equivalent(page, request.Items, request.Count, request.ScannedCount, request.LastEvaluatedKey, request.ConsumedCapacity))
            {
                failures.Add(vector.Name);
            }
        }

        // Assert
        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task CountAsync_SeveralPages_SumsThemWithSelectCount()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{\"Count\":3,\"ScannedCount\":9,\"LastEvaluatedKey\":" + Key("9") + "}"));
        handler.Enqueue(StubResponse.Json("""{"Count":0,"ScannedCount":9}"""));
        using var client = TestClient.Create(handler);

        // Act
        var count = await client.Table("Shop", "pk", "sk").Query("a").Limit(9).CountAsync();

        // Assert
        await Assert.That((count, handler.Requests.Count, handler.Requests[1].BodyText.Contains("\"Select\":\"COUNT\"", StringComparison.Ordinal))).IsEqualTo((3, 2, true));
    }

    [Test]
    public async Task CountAsync_Entity_CountsWhatPassesTheDiscriminator()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Count":1,"ScannedCount":4}"""));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table<Customer>("Shop").Query("a").CountAsync();

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).Contains("\"FilterExpression\":\"#n1 = :v1\"");
    }

    [Test]
    public async Task CountAsync_Projection_ThrowsAndGivesTheStateBack()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(async () => await shop.Query("a").Project("x").CountAsync());
        var json = shop.Query("b").ToRequestJson();

        // Assert
        await Assert.That((failure.Message.StartsWith("This Query on Shop counts items", StringComparison.Ordinal), json.Contains("Projection", StringComparison.Ordinal))).IsEqualTo((true, false));
    }

    [Test]
    public async Task CountAsync_MoreThanAnIntHolds_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{\"Count\":2147483647,\"LastEvaluatedKey\":" + Key("9") + "}"));
        handler.Enqueue(StubResponse.Json("""{"Count":1}"""));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<OverflowException>(async () => await shop.Query("a").CountAsync());

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task CountAsync_ServiceFails_GivesTheStateBack()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("ResourceNotFoundException", "Gone."));
        using var client = TestClient.Create(handler, pool: pool);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        await TestClient.CaptureAsync<ResourceNotFoundException>(async () => await shop.Query("a").CountAsync());
        client.Dispose();

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    /// <summary>Whether a page of a command says what the request API says of the same response.</summary>
    internal static bool Equivalent(Page<Item> page, IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>> items, int count, int scannedCount, IReadOnlyDictionary<string, AttributeValue>? key, ConsumedCapacity? capacity)
    {
        var sameItems = page.Items.Count == items.Count && page.Items.Zip(items).All(static pair => new Item(pair.Second).Equals(pair.First));
        var sameKey = key is null ? !page.HasMore : new Item(key).Equals(page.Next.ToKey());
        return sameItems && sameKey && page.Count == count && page.ScannedCount == scannedCount && Equals(page.ConsumedCapacity?.CapacityUnits, capacity?.CapacityUnits);
    }

    private static Tracked<IAsyncEnumerable<Item>> StartItems(Table<Item> shop, CancellationToken cancellationToken = default)
    {
        var command = shop.Query("a");
        var handle = command.Handle;
        return new(command.ItemsAsync(cancellationToken), handle.State!, handle.Version + 2);
    }

    private static Tracked<IAsyncEnumerable<Page<Item>>> StartPages(Table<Item> shop, CancellationToken cancellationToken = default)
    {
        var command = shop.Query("a");
        var handle = command.Handle;
        return new(command.PagesAsync(cancellationToken), handle.State!, handle.Version + 2);
    }

    private static (Task<Page<Item>> Page, CommandState State, int Released) StartPage(Table<Item> shop, CancellationToken cancellationToken = default)
    {
        var command = shop.Query("a");
        var handle = command.Handle;
        return (command.PageAsync(default, cancellationToken).AsTask(), handle.State!, handle.Version + 2);
    }

    private static (Task<Item?> Item, CommandState State, int Released) StartItem(Table<Item> shop, CancellationToken cancellationToken = default)
    {
        var command = shop.Query("a");
        var handle = command.Handle;
        return (command.ItemAsync(cancellationToken).AsTask(), handle.State!, handle.Version + 2);
    }

    private static ValueTask<Item?> ItemAfterRendering(Table<Item> shop)
    {
        var command = shop.Query("a");
        _ = command.ToRequestJson();
        var again = command;
        return again.ItemAsync();
    }

    private static string FilterOnStaleHandle(Table<Item> shop)
    {
        var stale = shop.Query("a");
        _ = stale.ToRequestJson();
        try
        {
            var again = stale;
            _ = again.Filter($"{Attr("a")} = {1}");
            return string.Empty;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    private static string RenderTwice(Table<Item> shop)
    {
        var command = shop.Query("a");
        _ = command.ToRequestJson();
        try
        {
            var again = command;
            _ = again.ToRequestJson();
            return string.Empty;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    private static string EnumerateFromAStaleHandle(Table<Item> shop)
    {
        var command = shop.Query("a");
        _ = command.ItemsAsync();
        try
        {
            var again = command;
            _ = again.ItemsAsync();
            return string.Empty;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    private readonly struct SessionKey(string id) : DynaSharp.Mapping.IKey<Session>
    {
        public void WriteKey(DynaSharp.Serialization.DynamoDbWriter writer)
        {
            writer.WriteAttributeName("id"u8);
            writer.WriteString(id);
        }
    }

    private sealed class TextKey(byte[] partitionKeyName, string partitionKey, byte[] sortKeyName, string sortKey) : DynaSharp.Mapping.IKey<Item>
    {
        public void WriteKey(DynaSharp.Serialization.DynamoDbWriter writer)
        {
            writer.WriteAttributeName(partitionKeyName);
            writer.WriteString(partitionKey);
            writer.WriteAttributeName(sortKeyName);
            writer.WriteString(sortKey);
        }
    }
}
