using DynaSharp.Testing;
using DynaSharp.UnitTests.Operations;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.Expr;
using static DynaSharp.UnitTests.Fluent.PageJson;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

// These tests name attributes outside the entity on purpose, to see what the request holds.
#pragma warning disable DYNA213

namespace DynaSharp.UnitTests.Fluent;

// Query and Scan share what follows pages, so QueryCommandTests pins that. What is here is what a scan has for itself, and the proof that each of its terminals
// runs on that path.
public sealed class ScanCommandTests
{
    [Test]
    public async Task ToRequestJson_Options_AreSent()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Scan().Filter(Attr("x").Exists()).Filter($"{Attr("y")} > {1}").Project("z").Limit(5).Consistent().ReturnCapacity().MaxAttempts(2).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","ConsistentRead":true,"Limit":5,"FilterExpression":"(attribute_exists(#n0)) AND (#n1 > :v0)","ProjectionExpression":"#n2","ExpressionAttributeNames":{"#n0":"x","#n1":"y","#n2":"z"},"ExpressionAttributeValues":{":v0":{"N":"1"}},"ReturnConsumedCapacity":"TOTAL"}""");
    }

    [Test]
    public async Task ToRequestJson_Entity_FiltersOnTheDiscriminatorAndProjectsWhatItNeeds()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Tenant>("Tenants").Scan().Project(Attr("x")).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Tenants","FilterExpression":"#n0 = :v0","ProjectionExpression":"#n1, #n0, #n2","ExpressionAttributeNames":{"#n0":"kind","#n1":"x","#n2":"pk"},"ExpressionAttributeValues":{":v0":{"S":"TENANT"}}}""");
    }

    [Test]
    public async Task ToRequestJson_Index_NamesIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Index("ByStatus", "status").Scan().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","IndexName":"ByStatus"}""");
    }

    [Test]
    public async Task ToRequestJson_NameThatNeedsEscaping_IsEscaped()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Index("By\"Status", "status").Scan().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","IndexName":"By\"Status"}""");
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
        await Assert.That(message).StartsWith("This Scan on Shop was already sent, extended or rendered.");
    }

    [Test]
    public async Task Filter_ThroughTheInterface_NeedsNoBox()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Scan().NotDeleted().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("attribute_not_exists(#n0)");
    }

    [Test]
    public async Task Limit_BelowOne_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => Task.FromResult(shop.Scan().Limit(0).ToRequestJson()));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("items");
    }

    [Test]
    public async Task MaxAttempts_BelowOne_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => Task.FromResult(shop.Scan().MaxAttempts(0).ToRequestJson()));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("attempts");
    }

    [Test]
    public async Task DebuggerText_Command_NamesOperationAndTable()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var text = client.Table("Shop", "pk").Scan().DebuggerText;

        // Assert
        await Assert.That(text).Contains("Scan on Shop: {\"TableName\":\"Shop\"");
    }

    [Test]
    [Arguments(0, 1)]
    [Arguments(0, 2)]
    [Arguments(1, 2)]
    [Arguments(7, 8)]
    [Arguments(999_999, 1_000_000)]
    public async Task Segment_Valid_IsSent(int segment, int of)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Scan().Segment(segment, of).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo($"{{\"TableName\":\"Shop\",\"Segment\":{segment},\"TotalSegments\":{of}}}");
    }

    [Test]
    [Arguments(0, 0, "of")]
    [Arguments(0, -1, "of")]
    [Arguments(0, 1_000_001, "of")]
    [Arguments(-1, 2, "segment")]
    [Arguments(2, 2, "segment")]
    [Arguments(int.MaxValue, 2, "segment")]
    public async Task Segment_OutOfRange_Throws(int segment, int of, string parameter)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => Task.FromResult(shop.Scan().Segment(segment, of).ToRequestJson()));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo(parameter);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(1_000_001)]
    public async Task Parallel_OutOfRange_Throws(int segments)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => Task.FromResult(shop.Scan().Parallel(segments).DebuggerText));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("segments");
    }

    [Test]
    public async Task Parallel_AfterTake_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(() => Task.FromResult(shop.Scan().Take(10).Parallel(2).DebuggerText));

        // Assert
        await Assert.That(failure.Message).Contains("takes 10 items");
    }

    [Test]
    public async Task Take_BelowOne_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => Task.FromResult(shop.Scan().Take(0).ToRequestJson()));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("items");
    }

    [Test]
    public async Task ItemsAsync_Take_AsksEachPageForWhatIsLeft()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1", "2"));
        handler.Enqueue(More("3"));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Scan().Take(3).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.SortKeys(), handler.Requests[1].BodyText.Contains("\"Limit\":1,", StringComparison.Ordinal))).IsEqualTo(("1,2,3", true));
    }

    [Test]
    public async Task Parallel_AfterSegment_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(() => Task.FromResult(shop.Scan().Segment(1, of: 2).Parallel(2).DebuggerText));

        // Assert
        await Assert.That(failure.Message).Contains("reads one segment already");
    }

    [Test]
    public async Task ItemsAsync_FilteredEmptyPage_FollowsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(FilteredEmpty("1"));
        handler.Enqueue(More("2"));
        handler.Enqueue(Last("3"));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Scan().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.SortKeys(), handler.Requests[0].Operation, handler.Requests[2].BodyText)).IsEqualTo(("2,3", "Scan", "{\"TableName\":\"Shop\",\"ExclusiveStartKey\":" + Key("2") + "}"));
    }

    [Test]
    public async Task ItemsAsync_Done_ReleasesTheStateOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Last("1"));
        using var client = TestClient.Create(handler);
        var read = StartItems(client.Table("Shop", "pk", "sk"));

        // Act
        await read.Read.ToListAsync();

        // Assert
        await Assert.That(read.ReleasedOnce).IsTrue();
    }

    [Test]
    public async Task ItemsAsync_RetryOnALaterPage_RepeatsThatPage()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        handler.Enqueue(Throttled());
        handler.Enqueue(Last("2"));
        using var client = CreateClient(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Scan().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.SortKeys(), handler.Requests[1].BodyText == handler.Requests[2].BodyText)).IsEqualTo(("1,2", true));
    }

    [Test]
    public async Task PagesAsync_Pages_HoldTheirNumbers()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(FilteredEmpty("1"));
        handler.Enqueue(Last("2"));
        using var client = TestClient.Create(handler);

        // Act
        var pages = await client.Table("Shop", "pk", "sk").Scan().PagesAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(";", pages.Select(static page => $"{page.Count},{page.ScannedCount},{page.HasMore}"))).IsEqualTo("0,7,True;1,1,False");
    }

    [Test]
    public async Task PageAsync_SegmentAndPosition_ContinuesTheSegment()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Last("2"));
        using var client = TestClient.Create(handler);
        var position = ContinuationToken.FromKey(new Item { ["pk"] = "a" });

        // Act
        var page = await client.Table("Shop", "pk", "sk").Scan().Segment(2, of: 8).PageAsync(position);

        // Assert
        await Assert.That((page.Count, handler.Requests.Single().BodyText)).IsEqualTo((1, """{"TableName":"Shop","Segment":2,"TotalSegments":8,"ExclusiveStartKey":{"pk":{"S":"a"}}}"""));
    }

    [Test]
    public async Task PageAsync_ResponseVector_ReadsWhatTheRequestApiReads()
    {
        // Arrange: botocore's responses, with the request API as the proven reader of them
        var failures = new List<string>();

        // Act
        foreach (var vector in OperationVectors.Responses.Where(static vector => vector.Operation == "Scan"))
        {
            using var handler = new StubHandler(_ => StubResponse.Json(vector.Wire));
            using var client = TestClient.Create(handler);
            var request = await DynaSharp.Requests.DynamoDbRequests.ScanAsync(client, new DynaSharp.Requests.ScanRequest { TableName = "Users" });
            var page = await client.Table("Users", "pk").Scan().PageAsync();
            if (!QueryCommandTests.Equivalent(page, request.Items, request.Count, request.ScannedCount, request.LastEvaluatedKey, request.ConsumedCapacity))
            {
                failures.Add(vector.Name);
            }
        }

        // Assert
        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task CountAsync_SeveralPages_SumsThem()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{\"Count\":3,\"ScannedCount\":9,\"LastEvaluatedKey\":" + Key("9") + "}"));
        handler.Enqueue(StubResponse.Json("""{"Count":4,"ScannedCount":9}"""));
        using var client = TestClient.Create(handler);

        // Act
        var count = await client.Table("Shop", "pk", "sk").Scan().CountAsync();

        // Assert
        await Assert.That((count, handler.Requests[0].BodyText)).IsEqualTo((7, """{"TableName":"Shop","Select":"COUNT"}"""));
    }

    [Test]
    public async Task CountAsync_Projection_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(async () => await shop.Scan().Project("x").CountAsync());

        // Assert
        await Assert.That(failure.Message).StartsWith("This Scan on Shop counts items");
    }

    [Test]
    public async Task Release_NextCommand_StartsCleanAfterASegment()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Last());
        var pool = new CountingArrayPool<byte>();
        using var client = TestClient.Create(handler, pool: pool);
        var shop = client.Table("Shop", "pk", "sk");
        await shop.Scan().Segment(1, of: 2).Limit(5).ItemsAsync().ToListAsync();

        // Act
        var json = shop.Scan().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop"}""");
    }

    private static Tracked<IAsyncEnumerable<Item>> StartItems(Table<Item> shop)
    {
        var command = shop.Scan();
        var handle = command.Handle;
        return new(command.ItemsAsync(), handle.State!, handle.Version + 2);
    }

    private static string RenderTwice(Table<Item> shop)
    {
        var command = shop.Scan();
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
}
