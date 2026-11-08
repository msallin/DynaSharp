using DynaSharp.Testing;
using DynaSharp.UnitTests.Fluent.Mix;
using static DynaSharp.Expr;
using static DynaSharp.UnitTests.Fluent.Mix.MixJson;
using static DynaSharp.UnitTests.Fluent.PageJson;

namespace DynaSharp.UnitTests.Fluent;

public sealed class MixedQueryCommandTests
{
    private static string OutsideTheEntity => "x";

    [Test]
    public async Task Take_Items_LimitsTheFirstPageToThem()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Query("a").Of<Alpha, Beta>().Take(3).ToRequestJson();

        // Assert
        await Assert.That(json).EndsWith(",\"Limit\":3}");
    }

    [Test]
    public async Task Descending_True_ReadsDownwards()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Query("a").Of<Alpha, Beta>().Descending(true).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ScanIndexForward\":false");
    }

    [Test]
    public async Task Of_TwoTypes_FiltersOnBothDiscriminators()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Query("a").Of<Alpha, Beta>().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","KeyConditionExpression":"#n0 = :v0","FilterExpression":"#n1 IN (:v1, :v2)","ExpressionAttributeNames":{"#n0":"pk","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"S":"a"},":v1":{"S":"ALPHA"},":v2":{"S":"BETA"}}}""");
    }

    [Test]
    public async Task Of_ThreeAndFourTypes_FilterOnAllDiscriminators()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var three = shop.Query("a").Of<Alpha, Beta, Gamma>().ToRequestJson();
        var four = shop.Query("a").Of<Alpha, Beta, Gamma, Delta>().ToRequestJson();

        // Assert
        await Assert.That((three.Contains("#n1 IN (:v1, :v2, :v3)", StringComparison.Ordinal), four.Contains("#n1 IN (:v1, :v2, :v3, :v4)", StringComparison.Ordinal), four.Contains(":v4\":{\"S\":\"DELTA\"}", StringComparison.Ordinal))).IsEqualTo((true, true, true));
    }

    [Test]
    public async Task Of_AfterAFilter_BothMustHold()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Query("a").Filter(Attr("x").Exists()).Of<Alpha, Beta>().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"FilterExpression\":\"(attribute_exists(#n1)) AND (#n2 IN (:v1, :v2))\"");
    }

    [Test]
    public async Task Of_DiscriminatorInAnotherAttribute_FiltersOnThatAttribute()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Query("a").Of<Kinded, KindedToo>().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"#n1\":\"kind\"");
    }

    [Test]
    public async Task Project_TypesWithoutASortKey_AddsTheDiscriminatorAndThePartitionKeyOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Query("a").Of<Kinded, KindedToo>().Project(OutsideTheEntity).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ProjectionExpression\":\"#n2, #n1, #n0\"");
    }

    [Test]
    public async Task Of_Index_ReadsTheIndex()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Index("ByStatus", "status", "placedAt").Query("open").Of<Alpha, Beta>().ToRequestJson();

        // Assert
        await Assert.That(json).StartsWith("""{"TableName":"Shop","IndexName":"ByStatus","KeyConditionExpression":"#n0 = :v0","FilterExpression":"#n1 IN (:v1, :v2)",""");
    }

    [Test]
    public async Task Of_IndexWithTheDiscriminatorAsPartitionKey_HasNoFilter()
    {
        // Arrange (DynamoDB refuses a filter on a key attribute of what a query reads)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Index("ByType", "_type", "sk").Query("ALPHA").Of<Alpha, Beta>().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","IndexName":"ByType","KeyConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"_type"},"ExpressionAttributeValues":{":v0":{"S":"ALPHA"}}}""");
    }

    [Test]
    public async Task Of_IndexWithTheDiscriminatorAsSortKey_HasNoFilter()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Index("ByPartitionAndType", "pk", "_type").Query("a").Of<Alpha, Beta>().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","IndexName":"ByPartitionAndType","KeyConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"pk"},"ExpressionAttributeValues":{":v0":{"S":"a"}}}""");
    }

    [Test]
    public async Task Of_TableKeyedByTheDiscriminator_HasNoFilter()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "_type", "sk").Query("ALPHA").Of<Alpha, Beta>().ToRequestJson();

        // Assert
        await Assert.That(json.Contains("FilterExpression", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Of_IndexKeyedByTheDiscriminatorWithAFilter_KeepsThatFilter()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Index("ByType", "_type", "sk").Query("ALPHA").Filter(Attr("x").Exists()).Of<Alpha, Beta>().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"FilterExpression\":\"attribute_exists(#n1)\"");
    }

    [Test]
    public async Task Of_TwoTypesWithOneDiscriminator_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(shop.Query("a").Of<Alpha, AlphaTwin>().ToRequestJson()));

        // Assert
        await Assert.That(failure.Message).Contains("ALPHA occurs twice");
    }

    [Test]
    public async Task Of_DiscriminatorsInDifferentAttributes_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(shop.Query("a").Of<Alpha, Beta, Kinded>().ToRequestJson()));

        // Assert
        await Assert.That(failure.Message).Contains("_type and kind");
    }

    [Test]
    public async Task Of_TypeThatClaimsADiscriminatorAndHasNone_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(shop.Query("a").Of<Alpha, Undeclared>().ToRequestJson()));

        // Assert
        await Assert.That(failure.Message).Contains("position 1");
    }

    [Test]
    public async Task Of_CommandThatWasExtended_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(() =>
        {
            var query = client.Table("Shop", "pk", "sk").Query("a");
            _ = query.Limit(1);
            var again = query;
            _ = again.Of<Alpha, Beta>();
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.Message).Contains("already sent, extended or rendered");
    }

    [Test]
    public async Task Terminal_CommandThatWasExtended_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(() =>
        {
            var query = client.Table("Shop", "pk", "sk").Query("a").Of<Alpha, Beta>();
            _ = query.Limit(1);
            var again = query;
            again.ToRequestJson();
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.Message).Contains("already sent, extended or rendered");
    }

    [Test]
    public async Task Clauses_TwoTypes_ReachTheRequest()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Query("a").Of<Alpha, Beta>()
            .Filter($"{Attr(OutsideTheEntity)} > {1}").Filter(Attr("y").Exists()).NotDeleted().Project(OutsideTheEntity).Project(Attr("y")).Limit(5).Descending().Consistent().ReturnCapacity().MaxAttempts(1).Once().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","ConsistentRead":true,"Limit":5,"ScanIndexForward":false,"KeyConditionExpression":"#n0 = :v0","FilterExpression":"(#n1 IN (:v1, :v2)) AND (#n2 > :v3) AND (attribute_exists(#n3)) AND (attribute_not_exists(#n4))","ProjectionExpression":"#n2, #n3, #n1, #n0, #n5","ExpressionAttributeNames":{"#n0":"pk","#n1":"_type","#n2":"x","#n3":"y","#n4":"deletedAt","#n5":"sk"},"ExpressionAttributeValues":{":v0":{"S":"a"},":v1":{"S":"ALPHA"},":v2":{"S":"BETA"},":v3":{"N":"1"}},"ReturnConsumedCapacity":"TOTAL"}""");
    }

    [Test]
    public async Task Clauses_ThreeTypes_ReachTheRequest()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Query("a").Of<Alpha, Beta, Gamma>()
            .Filter($"{Attr(OutsideTheEntity)} > {1}").Filter(Attr("y").Exists()).Project(OutsideTheEntity).Project(Attr("y")).Limit(5).Descending().Consistent().ReturnCapacity().MaxAttempts(1).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"Limit\":5,\"ScanIndexForward\":false");
    }

    [Test]
    public async Task Clauses_FourTypes_ReachTheRequest()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Query("a").Of<Alpha, Beta, Gamma, Delta>()
            .Filter($"{Attr("x")} > {1}").Filter(Attr("y").Exists()).Project("x").Project(Attr("y")).Limit(5).Descending().Consistent().ReturnCapacity().MaxAttempts(1).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"Limit\":5,\"ScanIndexForward\":false");
    }

    [Test]
    public async Task Project_DiscriminatorAndKeysAreThere_AreNotAddedTwice()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Query("a").Of<Alpha, Beta>().Project("_type", "sk", "pk").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ProjectionExpression\":\"#n1, #n2, #n0\"");
    }

    [Test]
    public async Task ItemsAsync_DiscriminatorInAnyPlace_ReadsEachItemAsItsType()
    {
        // Arrange
        using var handler = new StubHandler(_ => Page(AlphaItem, BetaItem, AlphaItem));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query("a").Of<Alpha, Beta>().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", items.Select(Show))).IsEqualTo("A1,B2,A1");
    }

    [Test]
    public async Task ItemsAsync_ThreeAndFourTypes_ReadEachItemAsItsType()
    {
        // Arrange
        using var handler = new StubHandler(_ => Page(GammaItem, BetaItem, AlphaItem));
        using var four = new StubHandler(_ => Page(DeltaItem, GammaItem, BetaItem, AlphaItem));
        using var client = TestClient.Create(handler);
        using var fourClient = TestClient.Create(four);

        // Act
        var threeItems = await client.Table("Shop", "pk", "sk").Query("a").Of<Alpha, Beta, Gamma>().ItemsAsync().ToListAsync();
        var fourItems = await fourClient.Table("Shop", "pk", "sk").Query("a").Of<Alpha, Beta, Gamma, Delta>().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((string.Join(",", threeItems.Select(Show)), string.Join(",", fourItems.Select(Show)))).IsEqualTo(("G3,B2,A1", "D4,G3,B2,A1"));
    }

    [Test]
    public async Task ItemsAsync_EscapedNameAndValueOfTheDiscriminator_FindsIt()
    {
        // Arrange: a JSON writer is free to escape anything
        const string Escaped = """{"pk":{"S":"a"},"sk":{"S":"1"},"_type":{"S":"ALPHA"}}""";
        using var handler = new StubHandler(_ => Page(Escaped));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query("a").Of<Alpha, Beta>().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(items.Single().Is(out Alpha? _)).IsTrue();
    }

    [Test]
    public async Task ItemsAsync_DiscriminatorInANestedMap_IsNotTakenForIt()
    {
        // Arrange: the only _type before the real one is inside a map, and the item has only the real one at the top level
        using var handler = new StubHandler(_ => Page(DeltaItem));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query("a").Of<Alpha, Beta, Gamma, Delta>().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(items.Single().Is(out Delta _)).IsTrue();
    }

    [Test]
    public async Task ItemsAsync_ItemOfAnotherType_ThrowsNamingTheTypeFound()
    {
        // Arrange
        using var handler = new StubHandler(_ => Page(GammaItem));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => shop.Query("a").Of<Alpha, Beta>().ItemsAsync().ToListAsync());

        // Assert
        await Assert.That((failure.Message, failure.AttributeName, failure.EntityType)).IsEqualTo(("The item is of type \"GAMMA\", so it cannot be read as Mixed<ALPHA, BETA>.", "_type", "Mixed<ALPHA, BETA>"));
    }

    [Test]
    public async Task ItemsAsync_ItemWithoutDiscriminator_ThrowsNamingTheAttribute()
    {
        // Arrange
        using var handler = new StubHandler(_ => Page("""{"pk":{"S":"a"},"sk":{"S":"1"}}"""));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => shop.Query("a").Of<Alpha, Beta>().ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo("The attribute \"_type\" is missing, so the item cannot be read as Mixed<ALPHA, BETA>.");
    }

    [Test]
    public async Task ItemsAsync_DiscriminatorThatIsNoString_ThrowsNamingTheAttribute()
    {
        // Arrange
        using var handler = new StubHandler(_ => Page("""{"_type":{"N":"1"}}"""));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => shop.Query("a").Of<Alpha, Beta>().ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("_type");
    }

    [Test]
    public async Task ItemsAsync_ItemThatItsTypeCannotRead_ThrowsForThatType()
    {
        // Arrange: a BETA needs Amount
        using var handler = new StubHandler(_ => Page("""{"_type":{"S":"BETA"},"pk":{"S":"a"},"sk":{"S":"1"},"Amount":{"S":"none"}}"""));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => shop.Query("a").Of<Alpha, Beta>().ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure.EntityType).IsEqualTo("Beta");
    }

    [Test]
    public async Task PagesAsync_TwoTypes_GivesThePagesWithWhatTheFilterDropped()
    {
        // Arrange
        using var handler = new StubHandler(_ => Page(AlphaItem, BetaItem));
        using var client = TestClient.Create(handler);

        // Act
        var page = (await client.Table("Shop", "pk", "sk").Query("a").Of<Alpha, Beta>().PagesAsync().ToListAsync()).Single();

        // Assert
        await Assert.That((page.Count, page.ScannedCount, string.Join(",", page.Items.Select(Show)))).IsEqualTo((2, 3, "A1,B2"));
    }

    [Test]
    public async Task PagesAsync_ThreeAndFourTypes_GiveThePages()
    {
        // Arrange
        using var handler = new StubHandler(_ => Page(AlphaItem, BetaItem));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var three = await shop.Query("a").Of<Alpha, Beta, Gamma>().PagesAsync().ToListAsync();
        var four = await shop.Query("a").Of<Alpha, Beta, Gamma, Delta>().PagesAsync().ToListAsync();

        // Assert
        await Assert.That((three.Single().Count, four.Single().Count)).IsEqualTo((2, 2));
    }

    [Test]
    public async Task PageAsync_Token_ContinuesAtTheKey()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{\"Items\":[" + AlphaItem + "],\"Count\":1,\"ScannedCount\":1,\"LastEvaluatedKey\":" + Key("1") + "}"));
        handler.Enqueue(Page(BetaItem));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var first = await shop.Query("a").Of<Alpha, Beta>().PageAsync();
        var second = await shop.Query("a").Of<Alpha, Beta>().PageAsync(ContinuationToken.Parse(first.Next.ToString()));

        // Assert
        await Assert.That((Show(second.Items.Single()), handler.Requests[1].BodyText.EndsWith(",\"ExclusiveStartKey\":" + Key("1") + "}", StringComparison.Ordinal))).IsEqualTo(("B2", true));
    }

    [Test]
    public async Task PageAsync_ThreeAndFourTypes_GiveAPage()
    {
        // Arrange
        using var handler = new StubHandler(_ => Page(AlphaItem));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var three = await shop.Query("a").Of<Alpha, Beta, Gamma>().PageAsync();
        var four = await shop.Query("a").Of<Alpha, Beta, Gamma, Delta>().PageAsync();

        // Assert
        await Assert.That((three.Count, four.Count)).IsEqualTo((1, 1));
    }

    [Test]
    public async Task DebuggerText_EveryArity_ShowsTheRequest()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var texts = new[] { shop.Query("a").Of<Alpha, Beta>().DebuggerText, shop.Query("a").Of<Alpha, Beta, Gamma>().DebuggerText, shop.Query("a").Of<Alpha, Beta, Gamma, Delta>().DebuggerText };

        // Assert
        await Assert.That(texts.All(static text => text.StartsWith("Query on Shop: {\"TableName\":\"Shop\"", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    [Arguments("ItemsAsync")]
    [Arguments("PagesAsync")]
    [Arguments("PageAsync")]
    public async Task Terminal_Projection_SendsItWithWhatTheTypesNeed(string terminal)
    {
        // Arrange
        using var handler = new StubHandler(_ => Page());
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        var expected = shop.Query("a").Of<Alpha, Beta>().Project(OutsideTheEntity).ToRequestJson();
        var read = shop.Query("a").Of<Alpha, Beta>().Project(OutsideTheEntity);

        // Act
        await (terminal switch
        {
            "ItemsAsync" => (Task)read.ItemsAsync().ToListAsync(),
            "PagesAsync" => read.PagesAsync().ToListAsync(),
            _ => read.PageAsync().AsTask(),
        });

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).IsEqualTo(expected);
    }
}
