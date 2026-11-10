using DynaSharp.Serialization;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping.KeyEntities;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

/// <summary><c>Select(handle).And(handle)</c>: some attributes of a read as a tuple, without the entity. The bodies and the responses are written by hand from <c>api.md</c>.</summary>
public sealed class SelectAndCommandTests
{
    // Four attributes of an item of Handles: a number, a nullable number, a nullable string and an enum.
    private const string Four = """{"Count":{"N":"7"},"Total":{"N":"8"},"Nickname":{"S":"bob"},"Plan":{"S":"Pro"}}""";

    private const string FourItems = $$$"""{"Items":[{{{Four}}},{"Count":{"N":"1"},"Plan":{"S":"Free"}}],"Count":2,"ScannedCount":2}""";

    [Test]
    public async Task ToRequestJson_GetOfAnEntity_ProjectsTheAttributesAndTheDiscriminator()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Count).And(Handles.Attrs.Total).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"HANDLES#a"},"sk":{"S":"SLOT#001"}},"ProjectionExpression":"#n0, #n1, #n2","ExpressionAttributeNames":{"#n0":"Count","#n1":"_type","#n2":"Total"}}""");
    }

    [Test]
    public async Task ToRequestJson_EveryShapeAtEveryPosition_ProjectsEachAttributeOnce()
    {
        // Arrange (each chain takes another order of a plain, a nullable value and a nullable reference handle, so that every And is called)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");
        var key = Handles.Keys.Of("a", 1);
        var pk = Handles.Keys.Pk.Of("a");

        // Act
        string[] projections =
        [
            table.Get(key).Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).And(Handles.Attrs.Nickname).ToRequestJson(),
            table.Get(key).Select(Handles.Attrs.Count).And(Handles.Attrs.Total).And(Handles.Attrs.Nickname).And(Handles.Attrs.Plan).ToRequestJson(),
            table.Get(key).Select(Handles.Attrs.Count).And(Handles.Attrs.Nickname).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).ToRequestJson(),
            table.Query(pk).Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).And(Handles.Attrs.Nickname).ToRequestJson(),
            table.Query(pk).Select(Handles.Attrs.Count).And(Handles.Attrs.Total).And(Handles.Attrs.Nickname).And(Handles.Attrs.Plan).ToRequestJson(),
            table.Query(pk).Select(Handles.Attrs.Count).And(Handles.Attrs.Nickname).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).ToRequestJson(),
            table.Scan().Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).And(Handles.Attrs.Nickname).ToRequestJson(),
            table.Scan().Select(Handles.Attrs.Count).And(Handles.Attrs.Total).And(Handles.Attrs.Nickname).And(Handles.Attrs.Plan).ToRequestJson(),
            table.Scan().Select(Handles.Attrs.Count).And(Handles.Attrs.Nickname).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).ToRequestJson(),
        ];

        // Assert
        await Assert.That(projections.Select(static json => json.Split("\"ProjectionExpression\":\"")[1].Split('"')[0].Split(", ").Length)).IsEquivalentTo([5, 5, 5, 4, 4, 4, 4, 4, 4]);
    }

    [Test]
    public async Task ToRequestJson_TwoAndThreeAttributes_ProjectsEach()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var table = client.Table("Shop", "pk");

        // Act
        string[] projections =
        [
            table.Scan().Select(Attr<int>("n")).And(Attr<int>("m")).ToRequestJson(),
            table.Get("a").Select(Attr<int>("n")).And(Attr<int>("m")).And(Attr<int>("o")).ToRequestJson(),
            table.Query("a").Select(Attr<int>("n")).And(Attr<int>("m")).And(Attr<int>("o")).ToRequestJson(),
            table.Scan().Select(Attr<int>("n")).And(Attr<int>("m")).And(Attr<int>("o")).ToRequestJson(),
        ];

        // Assert
        await Assert.That(projections.Select(static json => json.Split("\"ExpressionAttributeNames\":")[1])).IsEquivalentTo(
        [
            """{"#n0":"n","#n1":"m"}}""",
            """{"#n0":"n","#n1":"m","#n2":"o"}}""",
            """{"#n0":"pk","#n1":"n","#n2":"m","#n3":"o"},"ExpressionAttributeValues":{":v0":{"S":"a"}}}""",
            """{"#n0":"n","#n1":"m","#n2":"o"}}""",
        ]);
    }

    [Test]
    public async Task ToRequestJson_QueryOfAnIndexKeyedByTheDiscriminator_ProjectsItOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Handles>("Shop").Index("ByType", "_type", "sk").QueryByDiscriminator().Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ProjectionExpression":"#n1, #n0, #n2"
            """);
    }

    [Test]
    public async Task SendAsync_FourAttributes_GivesThemInTheOrderOfTheCalls()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json($$$"""{"Item":{{{Four}}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Plan).And(Handles.Attrs.Nickname).And(Handles.Attrs.Total).And(Handles.Attrs.Count).SendAsync();

        // Assert
        await Assert.That(result.Value).IsEqualTo((Plan.Pro, (string?)"bob", (long?)8, 7));
    }

    [Test]
    public async Task SendAsync_MissingNullableAndSetAttributes_GiveNullAndAnEmptySet()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"_type":{"S":"HANDLES"},"Count":{"N":"7"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Total).And(Handles.Attrs.Nickname).And(Handles.Attrs.Tags).SendAsync();

        // Assert
        await Assert.That((result.Value.Item1, result.Value.Item2, result.Value.Item3.Count)).IsEqualTo(((long?)null, (string?)null, 0));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task SendAsync_AttributeMissingOnAHandleThatCannotBeNull_ThrowsWhereverItStands(int position)
    {
        // Arrange (Count is missing, at the given position among Total and Nickname)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"Total":{"N":"8"},"Nickname":{"S":"bob"}}}"""));
        using var client = TestClient.Create(handler);
        var get = client.Table<Handles>("Shop");
        var key = Handles.Keys.Of("a", 1);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => _ = position switch
        {
            0 => (await get.Get(key).Select(Handles.Attrs.Count).And(Handles.Attrs.Total).And(Handles.Attrs.Nickname).SendAsync()).Value.Item1,
            1 => (await get.Get(key).Select(Handles.Attrs.Total).And(Handles.Attrs.Count).And(Handles.Attrs.Nickname).SendAsync()).Value.Item2,
            _ => (await get.Get(key).Select(Handles.Attrs.Total).And(Handles.Attrs.Nickname).And(Handles.Attrs.Count).SendAsync()).Value.Item3,
        });

        // Assert
        await Assert.That((failure.AttributeName, failure.Key?["sk"].AsString())).IsEqualTo(("Count", "SLOT#001"));
    }

    [Test]
    public async Task SendAsync_ItemOfAnotherType_ThrowsAsTheTypedReadDoes()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"Count":{"N":"7"},"_type":{"S":"ORDER"},"Total":{"N":"8"}}}"""));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");

        // Act
        var selected = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await table.Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Count).And(Handles.Attrs.Total).SendAsync());
        var typed = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await table.Get(Handles.Keys.Of("a", 1)).SendAsync());

        // Assert
        await Assert.That((selected.Message, selected.AttributeName)).IsEqualTo((typed.Message, typed.AttributeName));
    }

    [Test]
    public async Task SendAsync_NestedPathAmongOthers_ReadsItThroughTheMap()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"Home":{"M":{"City":{"S":"Bern"}}},"Count":{"N":"7"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Home.At(PostalAddress.Attrs.City)).And(Handles.Attrs.Count).SendAsync();

        // Assert
        await Assert.That(result.Value).IsEqualTo(("Bern", 7));
    }

    [Test]
    public async Task SendAsync_NoItem_IsNotFound()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).SendAsync();

        // Assert
        await Assert.That(result.Found).IsFalse();
    }

    [Test]
    public async Task Await_CommandOfEachSize_SendsIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json($$$"""{"Item":{{{Four}}}}"""));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");
        var key = Handles.Keys.Of("a", 1);

        // Act
        var two = await table.Get(key).Select(Handles.Attrs.Count).And(Handles.Attrs.Total);
        var three = await table.Get(key).Select(Handles.Attrs.Count).And(Handles.Attrs.Total).And(Handles.Attrs.Nickname);
        var four = await table.Get(key).Select(Handles.Attrs.Count).And(Handles.Attrs.Total).And(Handles.Attrs.Nickname).And(Handles.Attrs.Plan);

        // Assert
        await Assert.That((two.Value.Item1, three.Value.Item3, four.Value.Item4)).IsEqualTo((7, (string?)"bob", Plan.Pro));
    }

    [Test]
    public async Task And_SameAttributeTwice_ThrowsAtTheCallAndLeavesTheCommand()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        var json = string.Empty;

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() =>
        {
            var command = shop.Get("a").Select(Attr<int>("n")).And(Attr<int>("m"));
            var copy = command;
            try
            {
                _ = command.And(Attr<int>("n"));
            }
            finally
            {
                json = copy.ToRequestJson();
            }

            return Task.CompletedTask;
        });

        // Assert
        await Assert.That((failure.ParamName, json.Contains("\"#n0, #n1\"", StringComparison.Ordinal))).IsEqualTo(("attribute", true));
    }

    [Test]
    public async Task And_PathWithAListIndex_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.Query("a").Select(Attr<int>("n")).And(new Attr<int, NumberConverter<int>>("lines[1].n")));

        // Assert
        await Assert.That((failure as ArgumentException)?.ParamName).IsEqualTo("attribute");
    }

    [Test]
    public async Task And_AfterTheCommandWasExtended_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() =>
        {
            var command = shop.Scan().Select(Attr<int>("n"));
            var copy = command;
            _ = copy.And(Attr<int>("m"));
            _ = command.And(Attr<int>("o"));
        });

        // Assert
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task ValuesAsync_QueryOfEachSize_YieldsTheTuplesOfAllItems()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(FourItems));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");
        var pk = Handles.Keys.Pk.Of("a");

        // Act
        var two = await table.Query(pk).Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).ValuesAsync().ToListAsync();
        var three = await table.Query(pk).Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).ValuesAsync().ToListAsync();
        var four = await table.Query(pk).Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).And(Handles.Attrs.Nickname).ValuesAsync().ToListAsync();

        // Assert
        await Assert.That((two[1], three[1].Item3, four[0].Item4, four[1].Item4)).IsEqualTo(((1, Plan.Free), (long?)null, (string?)"bob", (string?)null));
    }

    [Test]
    public async Task ValueAsync_QueryOfEachSize_GivesTheFirstItem()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(FourItems));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");
        var pk = Handles.Keys.Pk.Of("a");

        // Act
        var two = await table.Query(pk).Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).ValueAsync();
        var three = await table.Query(pk).Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).ValueAsync();
        var four = await table.Query(pk).Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).And(Handles.Attrs.Nickname).ValueAsync();

        // Assert
        await Assert.That((two.Value, three.Value.Item3, four.Value.Item4)).IsEqualTo(((7, Plan.Pro), (long?)8, (string?)"bob"));
    }

    [Test]
    public async Task PageAsync_QueryOfEachSize_GivesTheTuplesAndTheCounts()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(FourItems));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");
        var pk = Handles.Keys.Pk.Of("a");
        var after = ContinuationToken.FromKey(new Dictionary<string, AttributeValue> { ["pk"] = "a" });

        // Act
        var pages = new[]
        {
            (await table.Query(pk).Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).PageAsync(after)).Count,
            (await table.Query(pk).Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).PageAsync(CancellationToken.None)).Count,
            (await table.Query(pk).Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).PageAsync(after)).Count,
            (await table.Query(pk).Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).PageAsync(CancellationToken.None)).Count,
            (await table.Query(pk).Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).And(Handles.Attrs.Nickname).PageAsync(after)).Count,
            (await table.Query(pk).Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).And(Handles.Attrs.Nickname).PageAsync(CancellationToken.None)).Count,
        };

        // Assert
        await Assert.That((string.Join(",", pages), handler.Requests.Count(static request => request.BodyText.Contains("ExclusiveStartKey", StringComparison.Ordinal)))).IsEqualTo(("2,2,2,2,2,2", 3));
    }

    [Test]
    public async Task PagesAsync_QueryOfEachSize_GivesEveryResponse()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(FourItems));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");
        var pk = Handles.Keys.Pk.Of("a");

        // Act
        int[] counts =
        [
            (await table.Query(pk).Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).PagesAsync().ToListAsync()).Single().Count,
            (await table.Query(pk).Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).PagesAsync().ToListAsync()).Single().Count,
            (await table.Query(pk).Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).And(Handles.Attrs.Nickname).PagesAsync().ToListAsync()).Single().Count,
        ];

        // Assert
        await Assert.That(counts).IsEquivalentTo([2, 2, 2]);
    }

    [Test]
    public async Task ValuesAsync_ScanOfEachSize_YieldsTheTuplesOfAllItems()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(FourItems));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");

        // Act
        var two = await table.Scan().Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).ValuesAsync().ToListAsync();
        var three = await table.Scan().Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).ValuesAsync().ToListAsync();
        var four = await table.Scan().Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).And(Handles.Attrs.Nickname).ValuesAsync().ToListAsync();

        // Assert
        await Assert.That((two[0], three[0].Item3, four[1].Item4)).IsEqualTo(((7, Plan.Pro), (long?)8, (string?)null));
    }

    [Test]
    public async Task PageAsync_ScanOfEachSize_GivesTheTuplesAndTheCounts()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(FourItems));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");
        var after = ContinuationToken.FromKey(new Dictionary<string, AttributeValue> { ["pk"] = "a", ["sk"] = "b" });

        // Act
        var pages = new[]
        {
            (await table.Scan().Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).PageAsync(after)).Count,
            (await table.Scan().Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).PageAsync(CancellationToken.None)).Count,
            (await table.Scan().Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).PageAsync(after)).Count,
            (await table.Scan().Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).PageAsync(CancellationToken.None)).Count,
            (await table.Scan().Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).And(Handles.Attrs.Nickname).PageAsync(after)).Count,
            (await table.Scan().Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).And(Handles.Attrs.Nickname).PageAsync(CancellationToken.None)).Count,
        };

        // Assert
        await Assert.That((string.Join(",", pages), handler.Requests.Count(static request => request.BodyText.Contains("ExclusiveStartKey", StringComparison.Ordinal)))).IsEqualTo(("2,2,2,2,2,2", 3));
    }

    [Test]
    public async Task PagesAsync_ScanOfEachSize_GivesEveryResponse()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(FourItems));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");

        // Act
        int[] counts =
        [
            (await table.Scan().Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).PagesAsync().ToListAsync()).Single().Count,
            (await table.Scan().Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).PagesAsync().ToListAsync()).Single().Count,
            (await table.Scan().Select(Handles.Attrs.Count).And(Handles.Attrs.Plan).And(Handles.Attrs.Total).And(Handles.Attrs.Nickname).PagesAsync().ToListAsync()).Single().Count,
        ];

        // Assert
        await Assert.That(counts).IsEquivalentTo([2, 2, 2]);
    }

    [Test]
    public async Task And_EveryCommandKeepsItsReadForTheDebugger()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var table = client.Table("Shop", "pk");

        // Act
        string[] texts =
        [
            table.Get("a").Select(Attr<int>("n")).And(Attr<int>("m")).DebuggerText,
            table.Get("a").Select(Attr<int>("n")).And(Attr<int>("m")).And(Attr<int>("o")).DebuggerText,
            table.Get("a").Select(Attr<int>("n")).And(Attr<int>("m")).And(Attr<int>("o")).And(Attr<int>("p")).DebuggerText,
            table.Query("a").Select(Attr<int>("n")).And(Attr<int>("m")).DebuggerText,
            table.Query("a").Select(Attr<int>("n")).And(Attr<int>("m")).And(Attr<int>("o")).DebuggerText,
            table.Query("a").Select(Attr<int>("n")).And(Attr<int>("m")).And(Attr<int>("o")).And(Attr<int>("p")).DebuggerText,
            table.Scan().Select(Attr<int>("n")).And(Attr<int>("m")).DebuggerText,
            table.Scan().Select(Attr<int>("n")).And(Attr<int>("m")).And(Attr<int>("o")).DebuggerText,
            table.Scan().Select(Attr<int>("n")).And(Attr<int>("m")).And(Attr<int>("o")).And(Attr<int>("p")).DebuggerText,
        ];

        // Assert
        await Assert.That(texts.Select(static text => text.Split(':')[0]).Distinct()).IsEquivalentTo(["GetItem on Shop", "Query on Shop", "Scan on Shop"]);
    }
}
