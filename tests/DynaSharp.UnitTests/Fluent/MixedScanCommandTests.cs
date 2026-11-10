using DynaSharp.Testing;
using DynaSharp.UnitTests.Fluent.Mix;
using static DynaSharp.Expr;
using static DynaSharp.UnitTests.Fluent.Mix.MixJson;

// These tests name attributes outside the entity on purpose, to see what the request holds.
#pragma warning disable DYNA213

namespace DynaSharp.UnitTests.Fluent;

public sealed class MixedScanCommandTests
{
    private static string OutsideTheEntity => "x";

    [Test]
    public async Task Take_Items_LimitsTheFirstPageToThem()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Scan().Of<Alpha, Beta>().Take(3).ToRequestJson();

        // Assert
        await Assert.That(json).EndsWith(",\"Limit\":3}");
    }

    [Test]
    public async Task Of_TwoTypes_FiltersOnBothDiscriminators()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Scan().Of<Alpha, Beta>().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","FilterExpression":"#n0 IN (:v0, :v1)","ExpressionAttributeNames":{"#n0":"_type"},"ExpressionAttributeValues":{":v0":{"S":"ALPHA"},":v1":{"S":"BETA"}}}""");
    }

    [Test]
    public async Task Of_IndexKeyedByTheDiscriminator_KeepsTheFilter()
    {
        // Arrange (a scan takes a filter on any attribute, and has no key condition to say which items come back)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Index("ByType", "_type", "sk").Scan().Of<Alpha, Beta>().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"FilterExpression\":\"#n0 IN (:v0, :v1)\"");
    }

    [Test]
    public async Task Clauses_TwoTypes_ReachTheRequest()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Scan().Of<Alpha, Beta>()
            .Filter($"{Attr(OutsideTheEntity)} > {1}").Filter(Attr("y").Exists()).NotDeleted().Project(OutsideTheEntity).Project(Attr("y")).Limit(5).Consistent().ReturnCapacity().MaxAttempts(1).Once().Segment(1, of: 4).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","ConsistentRead":true,"Limit":5,"Segment":1,"TotalSegments":4,"FilterExpression":"(#n0 IN (:v0, :v1)) AND (#n1 > :v2) AND (attribute_exists(#n2)) AND (attribute_not_exists(#n3))","ProjectionExpression":"#n1, #n2, #n0, #n4, #n5","ExpressionAttributeNames":{"#n0":"_type","#n1":"x","#n2":"y","#n3":"deletedAt","#n4":"pk","#n5":"sk"},"ExpressionAttributeValues":{":v0":{"S":"ALPHA"},":v1":{"S":"BETA"},":v2":{"N":"1"}},"ReturnConsumedCapacity":"TOTAL"}""");
    }

    [Test]
    public async Task Clauses_ThreeTypes_ReachTheRequest()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Scan().Of<Alpha, Beta, Gamma>()
            .Filter($"{Attr(OutsideTheEntity)} > {1}").Filter(Attr("y").Exists()).Project(OutsideTheEntity).Project(Attr("y")).Limit(5).Consistent().ReturnCapacity().MaxAttempts(1).Segment(1, of: 4).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"Limit\":5,\"Segment\":1,\"TotalSegments\":4");
    }

    [Test]
    public async Task Clauses_FourTypes_ReachTheRequest()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Scan().Of<Alpha, Beta, Gamma, Delta>()
            .Filter($"{Attr("x")} > {1}").Filter(Attr("y").Exists()).Project("x").Project(Attr("y")).Limit(5).Consistent().ReturnCapacity().MaxAttempts(1).Segment(1, of: 4).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"Limit\":5,\"Segment\":1,\"TotalSegments\":4");
    }

    [Test]
    public async Task ItemsAsync_EveryArity_ReadsEachItemAsItsType()
    {
        // Arrange
        using var handler = new StubHandler(_ => Page(BetaItem, AlphaItem));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var two = await shop.Scan().Of<Alpha, Beta>().ItemsAsync().ToListAsync();
        var three = await shop.Scan().Of<Alpha, Beta, Gamma>().ItemsAsync().ToListAsync();
        var four = await shop.Scan().Of<Alpha, Beta, Gamma, Delta>().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((string.Join(",", two.Select(Show)), string.Join(",", three.Select(Show)), string.Join(",", four.Select(Show)))).IsEqualTo(("B2,A1", "B2,A1", "B2,A1"));
    }

    [Test]
    public async Task PagesAsync_EveryArity_GivesThePages()
    {
        // Arrange
        using var handler = new StubHandler(_ => Page(BetaItem, AlphaItem));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var two = await shop.Scan().Of<Alpha, Beta>().PagesAsync().ToListAsync();
        var three = await shop.Scan().Of<Alpha, Beta, Gamma>().PagesAsync().ToListAsync();
        var four = await shop.Scan().Of<Alpha, Beta, Gamma, Delta>().PagesAsync().ToListAsync();

        // Assert
        await Assert.That((two.Single().ScannedCount, three.Single().ScannedCount, four.Single().ScannedCount)).IsEqualTo((3, 3, 3));
    }

    [Test]
    public async Task PageAsync_EveryArity_GivesAPage()
    {
        // Arrange
        using var handler = new StubHandler(_ => Page(BetaItem, AlphaItem));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var two = await shop.Scan().Of<Alpha, Beta>().PageAsync();
        var three = await shop.Scan().Of<Alpha, Beta, Gamma>().PageAsync();
        var four = await shop.Scan().Of<Alpha, Beta, Gamma, Delta>().PageAsync();

        // Assert
        await Assert.That((two.Count, three.Count, four.Count)).IsEqualTo((2, 2, 2));
    }

    [Test]
    public async Task Parallel_EveryArity_ReadsAllSegmentsAsTheMix()
    {
        // Arrange
        using var handler = new StubHandler(_ => Page(AlphaItem));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var two = await shop.Scan().Of<Alpha, Beta>().Parallel(2).ItemsAsync().ToListAsync();
        var three = await shop.Scan().Of<Alpha, Beta, Gamma>().Parallel(2).ItemsAsync().ToListAsync();
        var four = await shop.Scan().Of<Alpha, Beta, Gamma, Delta>().Parallel(2).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((two.Count, three.Count, four.Count, handler.Requests.Count)).IsEqualTo((2, 2, 2, 6));
    }

    [Test]
    public async Task Parallel_Projection_ReachesEverySegmentWithWhatTheTypesNeed()
    {
        // Arrange
        using var handler = new StubHandler(_ => Page(AlphaItem));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk", "sk").Scan().Of<Alpha, Beta>().Project(OutsideTheEntity).Parallel(2).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(handler.Requests.All(static request => request.BodyText.Contains("\"ProjectionExpression\":\"#n1, #n0, #n2, #n3\"", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task DebuggerText_EveryArity_ShowsTheRequest()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var texts = new[] { shop.Scan().Of<Alpha, Beta>().DebuggerText, shop.Scan().Of<Alpha, Beta, Gamma>().DebuggerText, shop.Scan().Of<Alpha, Beta, Gamma, Delta>().DebuggerText };

        // Assert
        await Assert.That(texts.All(static text => text.StartsWith("Scan on Shop: {\"TableName\":\"Shop\"", StringComparison.Ordinal))).IsTrue();
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
        var expected = shop.Scan().Of<Alpha, Beta>().Project(OutsideTheEntity).ToRequestJson();
        var read = shop.Scan().Of<Alpha, Beta>().Project(OutsideTheEntity);

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
