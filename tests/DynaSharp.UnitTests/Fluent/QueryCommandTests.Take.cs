using System.Text.RegularExpressions;
using DynaSharp.Serialization;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Fluent.PageJson;

namespace DynaSharp.UnitTests.Fluent;

/// <summary><c>Take</c> of a query: a cap on the items of the read that sizes the limit of each page. <see cref="ScanCommandTests"/> has what only a scan does.</summary>
public sealed partial class QueryCommandTests
{
    [Test]
    public async Task Take_BelowOne_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => Task.FromResult(shop.Query("a").Take(0).ToRequestJson()));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("items");
    }

    [Test]
    [Arguments(0, 10, 10)]
    [Arguments(5, 10, 5)]
    [Arguments(10, 10, 10)]
    [Arguments(20, 10, 10)]
    public async Task ToRequestJson_Take_LimitsThePageToTheSmallerOfLimitAndTake(int limit, int take, int expected)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var query = client.Table("Shop", "pk", "sk").Query("a");

        // Act
        var json = (limit > 0 ? query.Limit(limit) : query).Take(take).ToRequestJson();

        // Assert
        await Assert.That(json).EndsWith($",\"Limit\":{expected}}}");
    }

    [Test]
    public async Task ItemsAsync_Take_AsksEachPageForWhatIsLeft()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1", "2"));
        handler.Enqueue(Last("3"));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query("a").Take(3).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.SortKeys(), Limits(handler))).IsEqualTo(("1,2,3", "3,1"));
    }

    [Test]
    public async Task ItemsAsync_TakeReached_SendsNoFurtherPage()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1", "2"));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query("a").Take(2).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.SortKeys(), handler.Requests.Count)).IsEqualTo(("1,2", 1));
    }

    [Test]
    public async Task ItemsAsync_TakeAfterAFilteredEmptyPage_ReadsOn()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(FilteredEmpty("5"));
        handler.Enqueue(More("6"));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query("a").Take(1).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.SortKeys(), Limits(handler))).IsEqualTo(("6", "1,1"));
    }

    [Test]
    public async Task PagesAsync_Take_StopsAfterTheItems()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        handler.Enqueue(More("2"));
        using var client = TestClient.Create(handler);

        // Act
        var pages = await client.Table("Shop", "pk", "sk").Query("a").Take(2).PagesAsync().ToListAsync();

        // Assert
        await Assert.That((pages.Count, Limits(handler), pages[1].Next.ToKey()!.GetString("sk"))).IsEqualTo((2, "2,1", "2"));
    }

    [Test]
    public async Task PageAsync_Take_GathersTheResponsesIntoOnePage()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1", "2"));
        handler.Enqueue(FilteredEmpty("5"));
        handler.Enqueue(More("6"));
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query("a").Take(3).PageAsync();

        // Assert
        await Assert.That((page.Items.SortKeys(), page.Count, page.ScannedCount, page.Next.ToKey()!.GetString("sk"), Limits(handler))).IsEqualTo(("1,2,6", 3, 10, "6", "3,1,1"));
    }

    [Test]
    public async Task PageAsync_TakeAtTheEndOfTheRead_HasNoNext()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        handler.Enqueue(Last("2"));
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query("a").Take(5).PageAsync();

        // Assert
        await Assert.That((page.Items.SortKeys(), page.HasMore)).IsEqualTo(("1,2", false));
    }

    [Test]
    [Arguments(1, true)]
    [Arguments(2, false)]
    public async Task PageAsync_TakeFromOneResponse_IsThatResponse(int take, bool hasMore)
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Items(take == 1 ? Key("1") : null, "1")));
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query("a").Take(take).PageAsync();

        // Assert
        await Assert.That((page.Items.SortKeys(), page.HasMore, handler.Requests.Count)).IsEqualTo(("1", hasMore, 1));
    }

    [Test]
    public async Task PageAsync_TakeOverSeveralResponses_SumsTheCapacity()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{\"Items\":[" + Key("1") + "],\"Count\":1,\"ScannedCount\":1,\"ConsumedCapacity\":{\"TableName\":\"Shop\",\"CapacityUnits\":1.5},\"LastEvaluatedKey\":" + Key("1") + "}"));
        handler.Enqueue(StubResponse.Json("{\"Items\":[" + Key("2") + "],\"Count\":1,\"ScannedCount\":1,\"ConsumedCapacity\":{\"TableName\":\"Shop\",\"CapacityUnits\":2}}"));
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query("a").ReturnCapacity().Take(2).PageAsync();

        // Assert
        await Assert.That(page.ConsumedCapacity!.CapacityUnits).IsEqualTo(3.5);
    }

    [Test]
    public async Task PageAsync_TakeWithAPosition_ContinuesFromIt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Last("3"));
        using var client = TestClient.Create(handler);
        var after = ContinuationToken.FromKey(new Dictionary<string, AttributeValue> { ["pk"] = "a", ["sk"] = "2" });

        // Act
        await client.Table("Shop", "pk", "sk").Query("a").Take(2).PageAsync(after);

        // Assert
        await Assert.That(handler.Requests[0].BodyText).Contains("\"ExclusiveStartKey\":" + Key("2"));
    }

    [Test]
    public async Task CountAsync_Take_CountsUpToIt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{\"Count\":2,\"ScannedCount\":2,\"LastEvaluatedKey\":" + Key("2") + "}"));
        handler.Enqueue(StubResponse.Json("{\"Count\":1,\"ScannedCount\":1,\"LastEvaluatedKey\":" + Key("3") + "}"));
        using var client = TestClient.Create(handler);

        // Act
        var count = await client.Table("Shop", "pk", "sk").Query("a").Take(3).CountAsync();

        // Assert
        await Assert.That((count, Limits(handler))).IsEqualTo((3, "3,1"));
    }

    [Test]
    public async Task ValuesAsync_TakeBeforeSelect_StopsAfterTheValues()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1", "2"));
        using var client = TestClient.Create(handler);

        // Act
        var values = await client.Table("Shop", "pk", "sk").Query("a").Take(2).Select(new Attr<string, StringConverter>("sk")).ValuesAsync().ToListAsync();

        // Assert
        await Assert.That((string.Join(",", values), handler.Requests.Count)).IsEqualTo(("1,2", 1));
    }

    private static string Limits(StubHandler handler) =>
        string.Join(",", handler.Requests.Select(static request => Regex.Match(request.BodyText, "\"Limit\":(\\d+)").Groups[1].Value));
}
