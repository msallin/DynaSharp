using System.Buffers.Text;
using System.Text;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

// A token comes from a client. Whatever the text holds, it ends in an ArgumentException before a request goes out, or it reaches a request as values of a key and nothing else.
public sealed class FanOutTokenTests
{
    private static readonly AttributeValue[] TwoPartitions = ["p0", "p1"];

    [Test]
    public async Task Next_APartitionPartlyGiven_HoldsItsLastItemGivenAndNotTheEndOfThePage()
    {
        // Arrange
        var wire = new FanOutWire().Put("p0", "1", "3", "4").Put("p1", "2", "5");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).Limit(10).PageAsync(1);

        // Assert (p0 continues after 1 and has 3 next; nothing was given from p1, so it starts over and has 2 next)
        await Assert.That(Text(page.Next)).IsEqualTo("""{"fanOut":{"L":[{"L":[{"M":{"pk":{"S":"p0"},"sk":{"S":"1"}}},{"M":{"pk":{"S":"p0"},"sk":{"S":"3"}}}]},{"L":[{"M":{}},{"M":{"pk":{"S":"p1"},"sk":{"S":"2"}}}]}]}}""");
    }

    [Test]
    public async Task Next_APartitionThatIsDone_IsMarkedDone()
    {
        // Arrange
        var wire = new FanOutWire().Put("p0", "1").Put("p1", "2", "3");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).Limit(10).PageAsync(2);

        // Assert
        await Assert.That(Text(page.Next)).IsEqualTo("""{"fanOut":{"L":[{"NULL":true},{"L":[{"M":{"pk":{"S":"p1"},"sk":{"S":"2"}}},{"M":{"pk":{"S":"p1"},"sk":{"S":"3"}}}]}]}}""");
    }

    [Test]
    public async Task Next_APageGivenOutWhole_HoldsTheKeyItReadUpTo()
    {
        // Arrange: everything of the page went out, and the filter dropped what stands between that item and the key
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{"pk":{"S":"p0"},"sk":{"S":"1"}}],"LastEvaluatedKey":{"pk":{"S":"p0"},"sk":{"S":"9"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query(["p0"]).PageAsync(1);

        // Assert
        await Assert.That(Text(page.Next)).IsEqualTo("""{"fanOut":{"L":[{"L":[{"M":{"pk":{"S":"p0"},"sk":{"S":"9"}}},{"M":{}}]}]}}""");
    }

    [Test]
    public async Task Next_APartitionTheFollowingPageDidNotRead_KeepsItsEntry()
    {
        // Arrange
        var wire = new FanOutWire().Put("p0", "1", "2", "3", "4").Put("p1", "8", "9");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        var first = await shop.Query(TwoPartitions).Limit(1).PageAsync(2);

        // Act: 1 and 2 were given, and p1 was read for its first item. Now 3 comes from p0, and nothing goes to p1, which passes its entry on.
        var reads = handler.Requests.Count;
        var second = await shop.Query(TwoPartitions).Limit(1).PageAsync(1, first.Next);
        var entries = Text(second.Next);

        // Assert
        await Assert.That((handler.Requests.Count - reads, entries)).IsEqualTo((1, """{"fanOut":{"L":[{"L":[{"M":{"pk":{"S":"p0"},"sk":{"S":"3"}}},{"M":{}}]},{"L":[{"M":{}},{"M":{"pk":{"S":"p1"},"sk":{"S":"8"}}}]}]}}"""));
    }

    [Test]
    public async Task Next_ThroughItsText_ReadsOn()
    {
        // Arrange
        var wire = new FanOutWire().Put("p0", "1", "3").Put("p1", "2", "4");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        var first = await shop.Query(TwoPartitions).PageAsync(2);

        // Act
        var second = await shop.Query(TwoPartitions).PageAsync(2, ContinuationToken.Parse(first.Next.ToString()));

        // Assert
        await Assert.That(second.Items.SortKeys()).IsEqualTo("3,4");
    }

    [Test]
    public async Task PageAsync_NumberAndBinarySortKeys_ReadOnAfterThem()
    {
        // Arrange
        var numbers = new FanOutWire().Put("p0", """{"N":"-2"}""", """{"N":"2"}""").Put("p1", """{"N":"1"}""");
        var binary = new FanOutWire().Put("p0", """{"B":"AA=="}""", """{"B":"+w=="}""").Put("p1", """{"B":"AQ=="}""");
        using var numberHandler = new StubHandler(numbers.Answer);
        using var binaryHandler = new StubHandler(binary.Answer);
        using var numberClient = TestClient.Create(numberHandler);
        using var binaryClient = TestClient.Create(binaryHandler);
        var numberTable = numberClient.Table("Shop", "pk", "sk");
        var binaryTable = binaryClient.Table("Shop", "pk", "sk");

        // Act
        var firstNumber = await numberTable.Query(TwoPartitions).PageAsync(1);
        var nextNumber = await numberTable.Query(TwoPartitions).PageAsync(5, firstNumber.Next);
        var firstBinary = await binaryTable.Query(TwoPartitions).PageAsync(1);
        var nextBinary = await binaryTable.Query(TwoPartitions).PageAsync(5, firstBinary.Next);

        // Assert
        await Assert.That((string.Join(",", nextNumber.Items.Select(static item => item["sk"].AsNumber())), nextBinary.Count)).IsEqualTo(("1,2", 2));
    }

    [Test]
    public async Task PageAsync_TokenOfAnotherNumberOfPartitions_ThrowsNamingTheNumbers()
    {
        // Arrange
        var wire = new FanOutWire().Put("p0", "1", "2").Put("p1", "3").Put("p2", "4");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        var token = (await shop.Query(TwoPartitions).PageAsync(1)).Next;
        var sent = handler.Requests.Count;

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(async () => await shop.Query(["p0", "p1", "p2"]).PageAsync(1, token));

        // Assert
        await Assert.That((failure.Message.Contains("holds 2 partitions and the query has 3", StringComparison.Ordinal), failure.ParamName, handler.Requests.Count)).IsEqualTo((true, "after", sent));
    }

    [Test]
    public async Task PageAsync_TokenOfAPlainQuery_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var token = ContinuationToken.FromKey(new Item { ["pk"] = "p0", ["sk"] = "1" });

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(async () => await client.Table("Shop", "pk", "sk").Query(TwoPartitions).PageAsync(1, token));

        // Assert
        await Assert.That((failure.ParamName, handler.Requests.Count)).IsEqualTo(("after", 0));
    }

    [Test]
    [Arguments("""{"fanOut":{"S":"x"}}""")]
    [Arguments("""{"other":{"L":[{"NULL":true},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"NULL":true},{"NULL":true}]},"TableName":{"S":"Other"}}""")]
    [Arguments("""{"fanOut":{"L":[{"S":"x"},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"BOOL":true},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"M":{"pk":{"S":"p0"},"sk":{"S":"1"}}},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"M":{}},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"M":{}}]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"M":{}},{"M":{}},{"M":{}}]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"S":"x"},{"M":{}}]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"M":{}},{"S":"x"}]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"NULL":true},{"M":{}}]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"M":{"pk":{"S":"p0"}}},{"M":{}}]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"M":{"pk":{"S":"p0"},"sk":{"S":"1"},"more":{"S":"x"}}},{"M":{}}]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"M":{"pk":{"S":"p0"},"other":{"S":"1"}}},{"M":{}}]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"M":{"pk":{"BOOL":true},"sk":{"S":"1"}}},{"M":{}}]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"M":{"pk":{"S":"p0"},"sk":{"M":{}}}},{"M":{}}]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"M":{"pk":{"S":"p0"},"sk":{"N":"abc"}}},{"M":{}}]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"M":{"pk":{"S":"p0"},"sk":{"N":"1E99999999999"}}},{"M":{}}]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"M":{}},{"M":{"pk":{"S":"p0"}}}]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"M":{}},{"M":{"pk":{"S":"p0"},"sk":{"S":"1"},"more":{"S":"x"}}}]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"M":{}},{"M":{"pk":{"S":"p0"},"other":{"S":"1"}}}]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"M":{}},{"M":{"pk":{"S":"p0"},"sk":{"BOOL":true}}}]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"M":{}},{"M":{"pk":{"S":"p0"},"sk":{"N":"abc"}}}]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"M":{"pk":{"S":"p0"},"sk":{"S":"1"}}},{"M":{"pk":{"S":"p0"},"sk":{"N":"abc"}}}]},{"NULL":true}]}}""")]
    [Arguments("""{"fanOut":{"L":[{"L":[{"M":{"pk":{"S":"p0"},"sk":{"N":"abc"}}},{"M":{"pk":{"S":"p0"},"sk":{"N":"1"}}}]},{"NULL":true}]}}""")]
    public async Task PageAsync_TokenWithTheWrongShape_ThrowsBeforeAnyRequest(string json)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var token = ContinuationToken.Parse(Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json)));

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(async () => await client.Table("Shop", "pk", "sk").Query(TwoPartitions).PageAsync(1, token));

        // Assert
        await Assert.That((failure.ParamName, failure.Message.Contains("p0", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo(("after", false, 0));
    }

    [Test]
    public async Task PageAsync_TokenOfADifferentShapeInOneEntry_LeavesNoArrayOutstanding()
    {
        // Arrange: the first entry is good and owns its buffers by the time the second one fails
        var pool = new Serialization.CountingArrayPool<byte>();
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler, pool: pool);
        var token = ContinuationToken.Parse(Base64Url.EncodeToString("""{"fanOut":{"L":[{"L":[{"M":{"pk":{"S":"p0"},"sk":{"S":"1"}}},{"M":{}}]},{"S":"x"}]}}"""u8));

        // Act
        await TestClient.CaptureAsync<ArgumentException>(async () => await client.Table("Shop", "pk", "sk").Query(TwoPartitions).PageAsync(1, token));
        client.Dispose();

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task PageAsync_TokenWithMembersInTheKeyThatLookLikeARequest_SendsThemOnlyAsValues()
    {
        // Arrange: text that tries to close the key and add a member of its own stays the string it is
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "No."));
        using var client = TestClient.Create(handler);
        var sortKey = """x"}},"TableName":"Other","a":{"b":{"S":"c""";
        var key = new Item { ["pk"] = "p0", ["sk"] = sortKey };
        var token = ContinuationToken.FromKey(new Item { ["fanOut"] = AttributeValue.FromList([AttributeValue.FromList([AttributeValue.FromMap(key), AttributeValue.FromMap([])])]) });

        // Act
        await TestClient.CaptureAsync<ValidationException>(async () => await client.Table("Shop", "pk", "sk").Query(["p0"]).PageAsync(1, token));
        var start = System.Text.Json.JsonElement.Parse(handler.Requests[0].BodyText);

        // Assert
        await Assert.That((start.GetProperty("TableName").GetString(), start.GetProperty("ExclusiveStartKey").GetProperty("sk").GetProperty("S").GetString())).IsEqualTo(("Shop", sortKey));
    }

    [Test]
    public async Task PageAsync_HintWithMembersThatLookLikeARequest_GoesIntoNoRequest()
    {
        // Arrange: only the start of an entry is sent
        var wire = new FanOutWire().Put("p0", "1", "2");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);
        var hint = new Item { ["pk"] = "secret-hint", ["sk"] = "0" };
        var token = ContinuationToken.FromKey(new Item { ["fanOut"] = AttributeValue.FromList([AttributeValue.FromList([AttributeValue.FromMap([]), AttributeValue.FromMap(hint)])]) });

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query(new AttributeValue[] { "p0" }).PageAsync(1, token);

        // Assert
        await Assert.That((page.Items.Single().GetString("sk"), handler.Requests[0].BodyText.Contains("secret-hint", StringComparison.Ordinal))).IsEqualTo(("1", false));
    }

    [Test]
    public async Task PageAsync_TokenWithoutHints_IsReadFromTheStartKeys()
    {
        // Arrange: an entry with an empty second key and one with both empty, which read like the entries of a token that knows nothing of what comes next
        var wire = new FanOutWire().Put("p0", "1", "3").Put("p1", "2");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);
        var token = ContinuationToken.Parse(Base64Url.EncodeToString("""{"fanOut":{"L":[{"L":[{"M":{"pk":{"S":"p0"},"sk":{"S":"1"}}},{"M":{}}]},{"L":[{"M":{}},{"M":{}}]}]}}"""u8));

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).PageAsync(5, token);

        // Assert
        await Assert.That((page.Items.Select(static item => item.GetString("sk")).Aggregate(static (a, b) => a + "," + b), handler.Requests.Count)).IsEqualTo(("2,3", 2));
    }

    [Test]
    public async Task PageAsync_FirstPartitionFromTheStartAndTheSecondAfterAKey_ReadsBoth()
    {
        // Arrange
        var wire = new FanOutWire().Put("p0", "3").Put("p1", "1", "2");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);
        var token = ContinuationToken.Parse(Base64Url.EncodeToString("""{"fanOut":{"L":[{"L":[{"M":{}},{"M":{}}]},{"L":[{"M":{"pk":{"S":"p1"},"sk":{"S":"1"}}},{"M":{}}]}]}}"""u8));

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).PageAsync(5, token);

        // Assert
        await Assert.That(string.Join(",", page.Items.Select(static item => item.GetString("sk")))).IsEqualTo("2,3");
    }

    [Test]
    public async Task PageAsync_TokenWithAllPartitionsDone_GivesAnEmptyLastPageAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var token = ContinuationToken.Parse(Base64Url.EncodeToString("""{"fanOut":{"L":[{"NULL":true},{"NULL":true}]}}"""u8));

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).PageAsync(1, token);

        // Assert
        await Assert.That((page.Count, page.HasMore, handler.Requests.Count)).IsEqualTo((0, false, 0));
    }

    [Test]
    public async Task Parse_FanOutToken_IsAToken()
    {
        // Arrange
        var wire = new FanOutWire().Put("p0", "1", "2").Put("p1", "3");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);
        var page = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).PageAsync(1);

        // Act
        var parsed = ContinuationToken.TryParse(page.Next.ToString(), out var token);

        // Assert
        await Assert.That((parsed, token)).IsEqualTo((true, page.Next));
    }

    private static string Text(ContinuationToken token) => Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token.ToString()));
}
