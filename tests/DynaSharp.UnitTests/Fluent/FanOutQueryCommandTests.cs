using System.Text.Json;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.Expr;
using static DynaSharp.UnitTests.Fluent.PageJson;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Fluent;

// The partitions are told apart by :pk in the request, so that nothing depends on how requests interleave. What must happen at once is arranged with semaphores that the handler and
// the test hand to each other, and nothing waits for time.
public sealed class FanOutQueryCommandTests
{
    private static readonly AttributeValue[] TwoPartitions = ["p0", "p1"];

    private static readonly PartitionKey<Customer>[] TwoCustomers = [Customer.Keys.Pk.Of("p0"), Customer.Keys.Pk.Of("p1")];

    private static readonly AttributeValue[] NoPartitions = [];

    [Test]
    public async Task Query_NullList_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentNullException>(() => Task.FromResult(shop.Query((IEnumerable<AttributeValue>)null!).ToRequestJson()));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("partitionKeys");
    }

    [Test]
    public async Task Query_RepeatedPartitionKey_ThrowsNamingThePositions()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(shop.Query(["secret-a", "secret-b", "secret-a"]).ToRequestJson()));

        // Assert
        await Assert.That((failure.Message.Contains("position 2 repeats the one at position 0", StringComparison.Ordinal), failure.Message.Contains("secret", StringComparison.Ordinal), failure.ParamName)).IsEqualTo((true, false, "partitionKeys"));
    }

    [Test]
    [Arguments("1", "1.0")]
    [Arguments("100", "1E+2")]
    [Arguments("-0.5", "-5E-1")]
    public async Task Query_RepeatedPartitionKeyInAnotherSpellingOfANumber_Throws(string first, string second)
    {
        // Arrange: DynamoDB takes these for one partition
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(shop.Query([AttributeValue.FromNumber(first), AttributeValue.FromNumber(second)]).ToRequestJson()));

        // Assert
        await Assert.That(failure.Message).Contains("position 1 repeats the one at position 0");
    }

    [Test]
    public async Task Query_DifferentKeysThatLookAlike_AreDifferentPartitions()
    {
        // Arrange: the text of a number is not a string of that text, and bytes are no string either
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var json = shop.Query([AttributeValue.FromNumber("1"), "1", AttributeValue.FromBinary("1"u8)]).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\":pk\":{\"N\":\"1\"}");
    }

    [Test]
    public async Task Query_NumberThatIsNoNumber_IsLeftToDynamoDb()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var json = shop.Query([AttributeValue.FromNumber("abc"), AttributeValue.FromNumber("def")]).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\":pk\":{\"N\":\"abc\"}");
    }

    [Test]
    public async Task Query_EmptyPartitionKey_ThrowsNamingThePosition()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failures = new[]
        {
            await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(shop.Query(["a", string.Empty]).ToRequestJson())),
            await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(shop.Query(["a", AttributeValue.FromBinary([])]).ToRequestJson())),
            await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(shop.Query(["a", default(AttributeValue)]).ToRequestJson())),
        };

        // Assert
        await Assert.That(failures.All(static failure => failure.Message.Contains("position 1 is empty", StringComparison.Ordinal) && failure.ParamName == "partitionKeys")).IsTrue();
    }

    [Test]
    public async Task Query_SortKeyConditionOnATableWithoutSortKey_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(shop.Query(TwoPartitions, SortKeyCondition.Eq("b")).ToRequestJson()));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("sortKey");
    }

    [Test]
    public async Task Query_ListThatYieldsOnce_IsEnumeratedOnceAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler(new FanOutWire().Put("p0", "1").Answer);
        using var client = TestClient.Create(handler);
        var enumerated = 0;
        IEnumerable<AttributeValue> Keys()
        {
            enumerated++;
            yield return "p0";
        }

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query(Keys()).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.Count, enumerated)).IsEqualTo((1, 1));
    }

    [Test]
    public async Task ToRequestJson_Options_ShowTheRequestOfTheFirstPartition()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Query(TwoCustomers, SortKeyCondition.Ge("x"))
            .Filter($"{Attr("a")} > {1}").Filter(Attr("b").Exists()).NotDeleted().Project(Attr("c")).Project(Attr("d")).Limit(5).Descending().Consistent().MaxConcurrency(2).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","ConsistentRead":true,"Limit":5,"ScanIndexForward":false,"KeyConditionExpression":"#n0 = :pk AND #n1 >= :v0","FilterExpression":"(#n2 = :v1) AND (#n3 > :v2) AND (attribute_exists(#n4)) AND (attribute_not_exists(#n5))","ProjectionExpression":"#n6, #n7, #n2, #n0, #n1","ExpressionAttributeNames":{"#n0":"pk","#n1":"sk","#n2":"_type","#n3":"a","#n4":"b","#n5":"deletedAt","#n6":"c","#n7":"d"},"ExpressionAttributeValues":{":v0":{"S":"x"},":v1":{"S":"CUSTOMER"},":v2":{"N":"1"},":pk":{"S":"CUSTOMER#p0"}}}""");
    }

    [Test]
    public async Task ToRequestJson_NoValueBesidesThePartition_IsTheOnlyValue()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Query(TwoPartitions).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","KeyConditionExpression":"#n0 = :pk","ExpressionAttributeNames":{"#n0":"pk"},"ExpressionAttributeValues":{":pk":{"S":"p0"}}}""");
    }

    [Test]
    public async Task ToRequestJson_NoPartitions_HasNoValueForThePartition()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Query(NoPartitions).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","KeyConditionExpression":"#n0 = :pk","ExpressionAttributeNames":{"#n0":"pk"}}""");
    }

    [Test]
    public async Task Project_Item_AddsTheKeysThatTheMergeNeeds()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Query(TwoPartitions).Project("x").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ProjectionExpression\":\"#n1, #n0, #n2\"");
    }

    [Test]
    public async Task Project_Index_AddsTheKeysOfIndexAndTable()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Index("ByStatus", "status", "placedAt").Query(TwoPartitions).Project("x").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ExpressionAttributeNames\":{\"#n0\":\"status\",\"#n1\":\"x\",\"#n2\":\"placedAt\",\"#n3\":\"pk\",\"#n4\":\"sk\"}");
    }

    [Test]
    public async Task Limit_BelowOne_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => Task.FromResult(client.Table("Shop", "pk", "sk").Query(TwoPartitions).Limit(0).ToRequestJson()));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("items");
    }

    [Test]
    public async Task MaxConcurrency_BelowOne_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => Task.FromResult(client.Table("Shop", "pk", "sk").Query(TwoPartitions).MaxConcurrency(0).ToRequestJson()));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("requests");
    }

    [Test]
    public async Task Clause_CommandThatWasExtended_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(() =>
        {
            var query = client.Table("Shop", "pk", "sk").Query(TwoPartitions);
            _ = query.Limit(1);
            var again = query;
            _ = again.Limit(2);
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.Message).Contains("already sent, extended or rendered");
    }

    [Test]
    public async Task Clause_DefaultCommand_Throws()
    {
        // Arrange and Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(() =>
        {
            _ = default(FanOutQueryCommand<Item>).MaxConcurrency(1);
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.Message).Contains("default value");
    }

    [Test]
    public async Task DebuggerText_Command_NamesOperationAndTable()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var text = client.Table("Shop", "pk", "sk").Query(TwoPartitions).DebuggerText;

        // Assert
        await Assert.That(text).StartsWith("Query on Shop: {\"TableName\":\"Shop\"");
    }

    [Test]
    public async Task ItemsAsync_NoPartitions_GivesNothingAndSendsNothing()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query(NoPartitions).ItemsAsync().ToListAsync();
        client.Dispose();

        // Assert
        await Assert.That((items.Count, handler.Requests.Count, pool.Outstanding)).IsEqualTo((0, 0, 0));
    }

    [Test]
    public async Task PageAsync_NoPartitions_GivesAnEmptyLastPageAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query(NoPartitions).PageAsync(10);

        // Assert
        await Assert.That((page.Count, page.HasMore, handler.Requests.Count)).IsEqualTo((0, false, 0));
    }

    [Test]
    public async Task CountAsync_NoPartitions_IsZeroAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var count = await client.Table("Shop", "pk", "sk").Query(NoPartitions).CountAsync();

        // Assert
        await Assert.That((count, handler.Requests.Count)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task ItemsAsync_Strings_MergeByTheirUtf8Bytes()
    {
        // Arrange: in UTF-16 the surrogates of U+1D11E sort before U+FFFD, and in UTF-8 after it; culture would put a before Z
        var wire = new FanOutWire().Put("p0", "Z", "é", "𝄞").Put("p1", "a", "�");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(items.SortKeys()).IsEqualTo("Z,a,é,�,𝄞");
    }

    [Test]
    public async Task ItemsAsync_EscapedStrings_MergeByWhatTheyStandFor()
    {
        // Arrange: a JSON writer is free to escape anything, and é sorts after z only as what it stands for
        using var handler = new StubHandler(request => StubResponse.Json(PartitionOf(request) == "p0"
            ? """{"Items":[{"pk":{"S":"p0"},"sk":{"S":"é"}}]}"""
            : """{"Items":[{"pk":{"S":"p1"},"sk":{"S":"z"}}]}"""));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(items.SortKeys()).IsEqualTo("z,é");
    }

    [Test]
    public async Task ItemsAsync_Numbers_MergeByValueAtFullPrecision()
    {
        // Arrange: as text 10 sorts before 9, and as doubles the last two are equal
        var wire = new FanOutWire()
            .Put("p0", N("-100"), N("-9.5"), N("10"), N("12345678901234567890123456789012345678"))
            .Put("p1", N("-10"), N("0"), N("9"), N("1E+2"), N("12345678901234567890123456789012345679"));
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", items.Select(static item => item["sk"].AsNumber()))).IsEqualTo("-100,-10,-9.5,0,9,10,1E+2,12345678901234567890123456789012345678,12345678901234567890123456789012345679");
    }

    [Test]
    public async Task ItemsAsync_Binary_MergesByTheBytesAndNotByTheirBase64()
    {
        // Arrange: Base64 of 0x00 is AA==, of 0xFB is +w==, and + sorts before A
        var wire = new FanOutWire().Put("p0", """{"B":"AA=="}""").Put("p1", """{"B":"+w=="}""");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", items.Select(static item => item["sk"].AsBinary().Span[0]))).IsEqualTo("0,251");
    }

    [Test]
    public async Task ItemsAsync_EqualSortKeys_ComeByThePositionOfThePartition()
    {
        // Arrange: the list is in another order than the names
        var wire = new FanOutWire().Put("p0", "a", "b").Put("p1", "a", "b").Put("p2", "a", "b");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query(["p2", "p0", "p1"]).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", items.Select(static item => item.GetString("sk") + item.GetString("from")))).IsEqualTo("ap2,ap0,ap1,bp2,bp0,bp1");
    }

    [Test]
    public async Task ItemsAsync_Descending_ReversesBothOrders()
    {
        // Arrange
        var wire = new FanOutWire().Put("p0", "a", "b").Put("p1", "a", "c");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).Descending().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", items.Select(static item => item.GetString("sk") + item.GetString("from")))).IsEqualTo("cp1,bp0,ap1,ap0");
    }

    [Test]
    public async Task ItemsAsync_Pages_AreFollowed()
    {
        // Arrange
        var wire = new FanOutWire { ItemsPerResponse = 2 }.Put("p0", "1", "3", "5", "7", "9").Put("p1", "2", "4", "6");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(items.SortKeys()).IsEqualTo("1,2,3,4,5,6,7,9");
    }

    [Test]
    public async Task ItemsAsync_FilteredEmptyPages_AreFollowed()
    {
        // Arrange: pages of one, and of the first partition only the last item passes
        var wire = new FanOutWire { ItemsPerResponse = 1 };
        wire.Put("p0", "1", passes: false).Put("p0", "2", passes: false).Put("p0", "3", passes: false).Put("p0", "4", passes: true).Put("p1", "5", passes: true);
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).Filter(Attr("x").Exists()).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(items.SortKeys()).IsEqualTo("4,5");
    }

    [Test]
    public async Task ItemsAsync_TableWithoutSortKey_GivesThePartitionsInTheirOrder()
    {
        // Arrange
        using var handler = new StubHandler(request => StubResponse.Json("{\"Items\":[{\"pk\":{\"S\":\"" + PartitionOf(request) + "\"}}]}"));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk").Query(["p1", "p2", "p0"]).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", items.Select(static item => item.GetString("pk")))).IsEqualTo("p1,p2,p0");
    }

    [Test]
    public async Task ItemsAsync_Entity_ReadsIt()
    {
        // Arrange
        using var handler = new StubHandler(request => StubResponse.Json("{\"Items\":[{\"pk\":{\"S\":\"" + PartitionOf(request) + "\"},\"sk\":{\"S\":\"PROFILE\"},\"fullName\":{\"S\":\"Alice\"}}]}"));
        using var client = TestClient.Create(handler);

        // Act
        var customers = await client.Table<Customer>("Shop").Query(TwoCustomers).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", customers.Select(static customer => customer.Id))).IsEqualTo("p0,p1");
    }

    [Test]
    public async Task ItemsAsync_Index_MergesByTheSortKeyOfTheIndexWithTheKeyOfTheTableInTheToken()
    {
        // Arrange: pages of two items end in a key of four attributes, which is what the next page of the index needs
        var bodies = new List<string>();
        using var handler = new StubHandler(request =>
        {
            lock (bodies)
            {
                bodies.Add(request.BodyText);
            }

            const string Item1 = """{"pk":{"S":"a"},"sk":{"S":"1"},"status":{"S":"p0"},"at":{"S":"x"}}""";
            return StubResponse.Json(request.BodyText.Contains("ExclusiveStartKey", StringComparison.Ordinal)
                ? """{"Items":[]}"""
                : "{\"Items\":[" + Item1 + "],\"LastEvaluatedKey\":" + Item1 + "}");
        });
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table("Shop", "pk", "sk").Index("ByStatus", "status", "at").Query(["p0"]).PageAsync(1);
        var next = await client.Table("Shop", "pk", "sk").Index("ByStatus", "status", "at").Query(["p0"]).PageAsync(1, page.Next);

        // Assert
        await Assert.That((next.Count, next.HasMore, bodies[1])).IsEqualTo((0, false, """{"TableName":"Shop","IndexName":"ByStatus","Limit":1,"KeyConditionExpression":"#n0 = :pk","ExpressionAttributeNames":{"#n0":"status"},"ExpressionAttributeValues":{":pk":{"S":"p0"}},"ExclusiveStartKey":{"pk":{"S":"a"},"sk":{"S":"1"},"status":{"S":"p0"},"at":{"S":"x"}}}"""));
    }

    [Test]
    public async Task ItemsAsync_RetryOfAPage_RepeatsIt()
    {
        // Arrange
        var attempts = 0;
        var wire = new FanOutWire().Put("p0", "1").Put("p1", "2");
        using var handler = new StubHandler(request => Interlocked.Increment(ref attempts) == 1 ? Throttled() : wire.Answer(request));
        using var client = CreateClient(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.SortKeys(), handler.Requests.Count)).IsEqualTo(("1,2", 3));
    }

    [Test]
    public async Task ItemsAsync_ItemWithoutItsSortKey_ThrowsNamingTheAttribute()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{"pk":{"S":"p0"}}]}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("sk");
    }

    [Test]
    public async Task ItemsAsync_KeyOfATypeThatNoKeyHas_ThrowsNamingTheAttribute()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{"pk":{"S":"p0"},"sk":{"BOOL":true}}]}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("sk");
    }

    [Test]
    public async Task ItemsAsync_SortKeyThatIsNoNumber_ThrowsNamingTheAttribute()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{"pk":{"S":"p0"},"sk":{"N":"abc"}}]}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("sk");
    }

    [Test]
    public async Task ItemsAsync_NextKeyWithoutTheSortKey_ThrowsNamingTheAttribute()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[],"LastEvaluatedKey":{"pk":{"S":"p0"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => client.Table("Shop", "pk", "sk").Query(["p0"]).ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("sk");
    }

    [Test]
    public async Task ItemsAsync_ManyItemsInAPage_KeepsAllTheirKeys()
    {
        // Arrange: more items than the arrays of keys start with
        var keys = Enumerable.Range(1000, 100).Select(static i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var wire = new FanOutWire().Put("p0", [.. keys.Where(static (_, i) => i % 2 == 0)]).Put("p1", [.. keys.Where(static (_, i) => i % 2 == 1)]);
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(items.SortKeys()).IsEqualTo(string.Join(",", keys));
    }

    [Test]
    public async Task ItemsAsync_LeftEarly_OnlyTheFirstPageOfEachPartitionWasRead()
    {
        // Arrange
        var wire = new FanOutWire { ItemsPerResponse = 2 }.Put("p0", "1", "2", "3", "4").Put("p1", "5", "6", "7", "8").Put("p2", "9");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        await foreach (var unused in client.Table("Shop", "pk", "sk").Query(["p0", "p1", "p2"]).ItemsAsync())
        {
            break;
        }

        // Assert
        await Assert.That(string.Join(",", wire.Reads.Order(StringComparer.Ordinal))).IsEqualTo("p0,p1,p2");
    }

    [Test]
    public async Task ItemsAsync_APartitionThatIsNotNeededYet_IsNotRead()
    {
        // Arrange: p1 sorts after everything p0 holds, so p0 is read to its end before p1 sees a second request
        var wire = new FanOutWire { ItemsPerResponse = 2 }.Put("p0", "1", "2", "3", "4").Put("p1", "5", "6", "7", "8");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);
        var seen = new List<int>();

        // Act
        await foreach (var unused in client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync())
        {
            seen.Add(wire.Reads.Count);
        }

        // Assert (the two first pages give 1 and 2, the third request 3 and 4, the fourth is the empty end of p0, then 5 and 6 are there already, and the rest of p1 comes after them)
        await Assert.That((string.Join(",", seen), string.Join(",", wire.Reads.Skip(2)))).IsEqualTo(("2,2,3,3,4,4,5,5", "p0,p0,p1,p1"));
    }

    [Test]
    public async Task ItemsAsync_KeyOfAnEmptyPageSortsAfterTheOtherItems_DoesNotFollowItYet()
    {
        // Arrange: of p0 a filter left nothing up to key 8, so nothing it can still give comes before 5, 6 or 7 of p1
        using var handler = new StubHandler(request =>
        {
            if (PartitionOf(request) == "p0")
            {
                return request.BodyText.Contains("ExclusiveStartKey", StringComparison.Ordinal) ? Partition("p0", "9") : FilteredEmpty("p0", "8");
            }

            return Partition("p1", "5", "6", "7");
        });
        using var client = TestClient.Create(handler);
        var sent = new List<int>();

        // Act
        await foreach (var unused in client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync())
        {
            sent.Add(handler.Requests.Count);
        }

        // Assert
        await Assert.That(string.Join(",", sent)).IsEqualTo("2,2,2,3");
    }

    [Test]
    public async Task ItemsAsync_EqualKeyInAPartitionOfAnEarlierPosition_ReadsThatPartitionFirst()
    {
        // Arrange: an index can hold more items with key 5 in p0, and those come before 5 of p1
        using var handler = new StubHandler(request =>
        {
            if (PartitionOf(request) == "p0")
            {
                return request.BodyText.Contains("ExclusiveStartKey", StringComparison.Ordinal) ? Partition("p0", "5") : More("p0", "5");
            }

            return Partition("p1", "5");
        });
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", items.Select(static item => item.GetString("pk")))).IsEqualTo("p0,p0,p1");
    }

    [Test]
    public async Task ItemsAsync_Descending_APartitionThatIsNotNeededYet_IsNotRead()
    {
        // Arrange: from the top, p1 holds everything down to 5, so p0 sees one request until p1 is through
        var wire = new FanOutWire { ItemsPerResponse = 2 }.Put("p0", "1", "2").Put("p1", "5", "6", "7", "8");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).Descending().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.SortKeys(), string.Join(",", wire.Reads.Skip(2)))).IsEqualTo(("8,7,6,5,2,1", "p1,p1,p0"));
    }

    [Test]
    public async Task ItemsAsync_EnumeratedTwice_Throws()
    {
        // Arrange
        using var handler = new StubHandler(new FanOutWire().Answer);
        using var client = TestClient.Create(handler);
        var items = client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync();
        await items.ToListAsync();

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(() => items.ToListAsync());

        // Assert
        await Assert.That(failure.Message).StartsWith("This Query on Shop was enumerated already.");
    }

    [Test]
    public async Task ItemsAsync_DisposedBeforeTheFirstMove_SendsNothingAndLeavesNothing()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler, pool: pool);
        var enumerator = client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync().GetAsyncEnumerator();

        // Act
        await enumerator.DisposeAsync();
        client.Dispose();

        // Assert
        await Assert.That((handler.Requests.Count, pool.Outstanding)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task ItemsAsync_BreakOutEarly_LeavesNoArrayOutstanding()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        var wire = new FanOutWire().Put("p0", "1", "2").Put("p1", "3");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await foreach (var unused in client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync())
        {
            break;
        }

        client.Dispose();

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task ItemsAsync_ToTheEnd_LeavesNoArrayOutstanding()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        var wire = new FanOutWire { ItemsPerResponse = 1 }.Put("p0", "1", "2").Put("p1", "3");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync().ToListAsync();
        client.Dispose();

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task ItemsAsync_MaxConcurrency_BoundsTheRequestsInFlight()
    {
        // Arrange: nothing answers until the test lets it, and the test lets it only when two requests wait, so the run proves both that two run at once and that no third does
        const int Partitions = 5;
        using var entered = new SemaphoreSlim(0);
        using var release = new SemaphoreSlim(0);
        var inFlight = 0;
        var most = 0;
        using var handler = new StubHandler(async (request, token) =>
        {
            InterlockedMax(ref most, Interlocked.Increment(ref inFlight));
            entered.Release();
            await release.WaitAsync(token);
            Interlocked.Decrement(ref inFlight);
            return Partition(PartitionOf(request), "1");
        });
        using var client = TestClient.Create(handler);
        var keys = Enumerable.Range(0, Partitions).Select(static i => (AttributeValue)("p" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        // Act
        var reading = client.Table("Shop", "pk", "sk").Query(keys).MaxConcurrency(2).ItemsAsync().ToListAsync();
        foreach (var round in new[] { 2, 2, 1 })
        {
            for (var i = 0; i < round; i++)
            {
                await entered.WaitAsync();
            }

            release.Release(round);
        }

        var items = await reading;

        // Assert
        await Assert.That((items.Count, most)).IsEqualTo((Partitions, 2));
    }

    [Test]
    public async Task ItemsAsync_DefaultConcurrency_IsEight()
    {
        // Arrange
        const int Partitions = 12;
        using var entered = new SemaphoreSlim(0);
        using var release = new SemaphoreSlim(0);
        var inFlight = 0;
        var most = 0;
        using var handler = new StubHandler(async (request, token) =>
        {
            InterlockedMax(ref most, Interlocked.Increment(ref inFlight));
            entered.Release();
            await release.WaitAsync(token);
            Interlocked.Decrement(ref inFlight);
            return Partition(PartitionOf(request), "1");
        });
        using var client = TestClient.Create(handler);
        var keys = Enumerable.Range(0, Partitions).Select(static i => (AttributeValue)("p" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        // Act
        var reading = client.Table("Shop", "pk", "sk").Query(keys).ItemsAsync().ToListAsync();
        foreach (var round in new[] { 8, 4 })
        {
            for (var i = 0; i < round; i++)
            {
                await entered.WaitAsync();
            }

            release.Release(round);
        }

        await reading;

        // Assert
        await Assert.That(most).IsEqualTo(8);
    }

    [Test]
    public async Task ItemsAsync_ResumedPartitionsBeforeTheFirstItem_AreReadAtOnce()
    {
        // Arrange: a token of start keys that says nothing of what comes next. p0 is first and is read alone, and then p1 and p2 sort before its next item, so both have to be
        // read, and no answer comes until both requests are out.
        var wire = new FanOutWire().Put("p0", "a", "x").Put("p1", "a", "b", "c").Put("p2", "a", "b", "d");
        var keys = new AttributeValue[] { "p0", "p1", "p2" };
        static AttributeValue After(AttributeValue partition) => AttributeValue.FromList([AttributeValue.FromMap(new Item { ["pk"] = partition, ["sk"] = "a" }), AttributeValue.FromMap([])]);
        var token = ContinuationToken.FromKey(new Item { ["fanOut"] = AttributeValue.FromList(keys.Select(After)) });
        using var entered = new SemaphoreSlim(0);
        using var release = new SemaphoreSlim(0);
        using var handler = new StubHandler(async (request, cancellation) =>
        {
            var answer = wire.Answer(request);
            if (PartitionOf(request) != "p0")
            {
                entered.Release();
                await release.WaitAsync(cancellation);
            }

            return answer;
        });
        using var client = TestClient.Create(handler);

        // Act
        var reading = client.Table("Shop", "pk", "sk").Query(keys).PageAsync(2, token).AsTask();
        await entered.WaitAsync();
        await entered.WaitAsync();
        release.Release(2);
        var page = await reading;

        // Assert
        await Assert.That(page.Items.SortKeys()).IsEqualTo("b,b");
    }

    [Test]
    public async Task ItemsAsync_Cancelled_StopsTheRequestsInFlightAndThrowsTheTokenOfTheCaller()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var pool = new CountingArrayPool<byte>();
        var counter = new AttemptCounter(2);
        var stopped = 0;
        using var handler = new StubHandler(async (_, token) =>
        {
            counter.Reached();
            await WaitUntilCancelledAsync(() => Interlocked.Increment(ref stopped), token);
            return Last();
        });
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        var reading = client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync(cancellation.Token).ToListAsync();
        await counter.WhenReachedAsync(2);
        await cancellation.CancelAsync();
        var failure = await TestClient.CaptureAsync<OperationCanceledException>(() => reading);
        client.Dispose();

        // Assert
        await Assert.That((failure.CancellationToken == cancellation.Token, stopped, pool.Outstanding)).IsEqualTo((true, 2, 0));
    }

    [Test]
    public async Task ItemsAsync_CancelledBeforeItStarted_ThrowsAndSendsNothing()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<OperationCanceledException>(() => client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync(cancellation.Token).ToListAsync());

        // Assert
        await Assert.That((failure.CancellationToken == cancellation.Token, handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task ItemsAsync_CancelledBetweenItemsOfAPage_StopsAtTheNextItem()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var wire = new FanOutWire().Put("p0", "1", "2", "3").Put("p1", "4");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);
        var seen = 0;

        // Act
        var failure = await TestClient.CaptureAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync(cancellation.Token))
            {
                seen++;
                await cancellation.CancelAsync();
            }
        });

        // Assert
        await Assert.That((seen, failure.CancellationToken == cancellation.Token)).IsEqualTo((1, true));
    }

    [Test]
    public async Task ItemsAsync_TwoTokensThatStayQuiet_ReadsToTheEnd()
    {
        // Arrange
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        var wire = new FanOutWire().Put("p0", "1").Put("p1", "2");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync(first.Token).WithCancellation(second.Token).ToListAsync();

        // Assert
        await Assert.That(items.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ItemsAsync_WithCancellation_StopsTheRead()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        await TestClient.CaptureAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync().WithCancellation(cancellation.Token))
            {
                // Nothing arrives.
            }
        });

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ItemsAsync_OnePartitionFails_SurfacesThatFailureOnceAndStopsTheOthers()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        var waiting = new AttemptCounter(2);
        var stopped = 0;
        using var handler = new StubHandler(async (request, token) =>
        {
            if (PartitionOf(request) == "p2")
            {
                await waiting.WhenReachedAsync(2);
                return StubResponse.DynamoDbError("ResourceNotFoundException", "Gone.");
            }

            waiting.Reached();
            await WaitUntilCancelledAsync(() => Interlocked.Increment(ref stopped), token);
            return Last();
        });
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        var failure = await TestClient.CaptureAsync<ResourceNotFoundException>(() => client.Table("Shop", "pk", "sk").Query(["p0", "p1", "p2"]).ItemsAsync().ToListAsync());
        client.Dispose();

        // Assert
        await Assert.That((failure.Message.StartsWith("Gone.", StringComparison.Ordinal), stopped, pool.Outstanding)).IsEqualTo((true, 2, 0));
    }

    [Test]
    public async Task ItemsAsync_SeveralPartitionsFail_SurfacesOneFailureAsItIs()
    {
        // Arrange
        var counter = new AttemptCounter(2);
        using var handler = new StubHandler(async (request, _) =>
        {
            counter.Reached();
            await counter.WhenReachedAsync(2);
            return StubResponse.DynamoDbError("ResourceNotFoundException", "Gone " + PartitionOf(request));
        });
        using var client = TestClient.Create(handler);

        // Act (the exception of the type it is, never an aggregate)
        var failure = await TestClient.CaptureAsync<ResourceNotFoundException>(() => client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure.Message).StartsWith("Gone ");
    }

    [Test]
    public async Task ItemsAsync_FailureOfALaterPage_SurfacesAndLeavesNoArrayOutstanding()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        var wire = new FanOutWire { ItemsPerResponse = 1 }.Put("p0", "1", "2").Put("p1", "3");
        using var handler = new StubHandler(request => request.BodyText.Contains("ExclusiveStartKey", StringComparison.Ordinal) ? StubResponse.DynamoDbError("ValidationException", "No.") : wire.Answer(request));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(() => client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync().ToListAsync());
        client.Dispose();

        // Assert
        await Assert.That((failure.ExpressionValueTypes![":pk"], pool.Outstanding)).IsEqualTo(("S", 0));
    }

    [Test]
    public async Task ItemsAsync_ClientDisposed_SurfacesItOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        var client = TestClient.Create(handler);
        var read = client.Table("Shop", "pk", "sk").Query(TwoPartitions).ItemsAsync();
        client.Dispose();

        // Act
        var failure = await TestClient.CaptureAsync<ObjectDisposedException>(() => read.ToListAsync());

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task PageAsync_Size_GivesThatManyItemsAndAToken()
    {
        // Arrange
        var wire = new FanOutWire().Put("p0", "1", "3").Put("p1", "2", "4");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).PageAsync(3);

        // Assert
        await Assert.That((page.Items.SortKeys(), page.Count, page.HasMore)).IsEqualTo(("1,2,3", 3, true));
    }

    [Test]
    public async Task PageAsync_NoLimit_ReadsEachPartitionInPagesOfTheSize()
    {
        // Arrange
        var wire = new FanOutWire().Put("p0", "1", "3").Put("p1", "2", "4");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk", "sk").Query(TwoPartitions).PageAsync(1);

        // Assert
        await Assert.That(handler.Requests.All(static request => request.BodyText.Contains("\"Limit\":1,", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task PageAsync_Limit_StaysWhatItWasSetTo()
    {
        // Arrange
        var wire = new FanOutWire().Put("p0", "1", "3").Put("p1", "2", "4");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk", "sk").Query(TwoPartitions).Limit(7).PageAsync(1);

        // Assert
        await Assert.That(handler.Requests.All(static request => request.BodyText.Contains("\"Limit\":7,", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task PageAsync_ScannedCount_AddsUpEveryCall()
    {
        // Arrange
        var wire = new FanOutWire();
        wire.Put("p0", "1", passes: false).Put("p0", "2", passes: false).Put("p1", "3", passes: true);
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).Filter(Attr("x").Exists()).Limit(10).PageAsync(5);

        // Assert
        await Assert.That((page.Count, page.ScannedCount, page.ConsumedCapacity)).IsEqualTo((1, 3, null));
    }

    [Test]
    public async Task PageAsync_ExactlyTheLastItems_HasNoNextPage()
    {
        // Arrange: a limit above what the partitions hold, so nothing says there could be more
        var wire = new FanOutWire().Put("p0", "1").Put("p1", "2");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).Limit(10).PageAsync(2);

        // Assert
        await Assert.That(page.HasMore).IsFalse();
    }

    [Test]
    public async Task PageAsync_NewPageWithoutWhatWasBuffered_ReadsItAgainFromTheLastItemGiven()
    {
        // Arrange: the first page takes 1 from p0, and p0 holds 3 and p1 holds 2 and 4 in memory, none of which the caller saw
        var wire = new FanOutWire().Put("p0", "1", "3").Put("p1", "2", "4");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var first = await shop.Query(TwoPartitions).Limit(10).PageAsync(1);
        var second = await shop.Query(TwoPartitions).Limit(10).PageAsync(10, first.Next);

        // Assert
        await Assert.That((second.Items.SortKeys(), second.HasMore)).IsEqualTo(("2,3,4", false));
    }

    [Test]
    public async Task PageAsync_FollowingPage_OnlyReadsThePartitionsItNeeds()
    {
        // Arrange: after the first page p0 has given up to 2, and p1 has given 8. The second page needs nothing of p1 until p0 runs out.
        var wire = new FanOutWire().Put("p0", "1", "2", "3", "4").Put("p1", "8", "9");
        using var first = new StubHandler(wire.Answer);
        using var firstClient = TestClient.Create(first);
        var token = (await firstClient.Table("Shop", "pk", "sk").Query(TwoPartitions).Limit(2).PageAsync(5)).Next;
        wire.Reads.Clear();
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act: the rest of p0 comes in one page, and only then it is the turn of p1
        var page = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).Limit(2).PageAsync(1, token);

        // Assert
        await Assert.That((page.Items.SortKeys(), string.Join(",", wire.Reads))).IsEqualTo(("9", "p1"));
    }

    [Test]
    public async Task PageAsync_TimeBuckets_ReadsOnlyTheBucketThePagesAreIn()
    {
        // Arrange: 6 days of 4 items each, and pages of 2. Every day sorts after the one before, so a page has no use for any day but the one it is in.
        const int Days = 6;
        const int ItemsOfADay = 4;
        var expected = Enumerable.Range(0, Days * ItemsOfADay).Select(static i => Number(((i / ItemsOfADay) * 10) + (i % ItemsOfADay)).PadLeft(3, '0'));
        async Task<(string Items, string ReadsPerPage, int Reads)> ReadAsync(Func<ContinuationToken, ContinuationToken> passOn)
        {
            var wire = new FanOutWire();
            for (var day = 0; day < Days; day++)
            {
                wire.Put("day" + Number(day), [.. Enumerable.Range(0, ItemsOfADay).Select(item => Number((day * 10) + item).PadLeft(3, '0'))]);
            }

            using var handler = new StubHandler(wire.Answer);
            using var client = TestClient.Create(handler);
            var shop = client.Table("Shop", "pk", "sk");
            var keys = Enumerable.Range(0, Days).Select(static day => (AttributeValue)("day" + Number(day))).ToArray();
            var seen = new List<string>();
            var perPage = new List<int>();
            var token = default(ContinuationToken);
            do
            {
                var before = wire.Reads.Count;
                var page = await shop.Query(keys).PageAsync(2, token);
                seen.AddRange(page.Items.Select(static item => item.GetString("sk")!));
                perPage.Add(wire.Reads.Count - before);
                token = page.HasMore ? passOn(ContinuationToken.Parse(page.Next.ToString())) : default;
            }
            while (token.HasValue && perPage.Count < 50);

            return (string.Join(",", seen), string.Join(",", perPage), wire.Reads.Count);
        }

        // Act: a page reads the day it is in, and the next day when it moves on. Without the hints a day stands in the way from the first page on.
        var withHints = await ReadAsync(static token => token);
        var withoutHints = await ReadAsync(WithoutHints);

        // Assert
        await Assert.That((withHints, withoutHints)).IsEqualTo(((string.Join(",", expected), "6,1,2,1,2,1,2,1,2,1,2,1,1", 23), (string.Join(",", expected), "6,6,6,5,5,4,4,3,3,2,2,1,1", 48)));
    }

    [Test]
    public async Task PageAsync_PartitionTheCallerGotNothingFrom_IsNotReadUntilItsTurn()
    {
        // Arrange: p1 starts far behind p0
        var wire = new FanOutWire().Put("p0", "1", "2", "3", "4").Put("p1", "8", "9");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        var first = await shop.Query(TwoPartitions).PageAsync(1);
        wire.Reads.Clear();

        // Act
        var second = await shop.Query(TwoPartitions).PageAsync(1, first.Next);
        var third = await shop.Query(TwoPartitions).PageAsync(1, second.Next);

        // Assert
        await Assert.That((second.Items.SortKeys(), third.Items.SortKeys(), string.Join(",", wire.Reads))).IsEqualTo(("2", "3", "p0,p0"));
    }

    [Test]
    public async Task PageAsync_Descending_APartitionTheCallerGotNothingFromIsNotReadUntilItsTurn()
    {
        // Arrange: from the top, p1 comes after all of p0
        var wire = new FanOutWire().Put("p0", "5", "6", "7").Put("p1", "1", "2");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        var first = await shop.Query(TwoPartitions).Descending().PageAsync(1);
        wire.Reads.Clear();

        // Act
        var second = await shop.Query(TwoPartitions).Descending().PageAsync(1, first.Next);

        // Assert
        await Assert.That((second.Items.SortKeys(), string.Join(",", wire.Reads))).IsEqualTo(("6", "p0"));
    }

    [Test]
    public async Task PageAsync_ReturnCapacity_SumsTheCallsOfThePage()
    {
        // Arrange: two calls of p0, of which the first is empty, and one of p1
        using var handler = new StubHandler(request =>
        {
            var units = request.BodyText.Contains("ExclusiveStartKey", StringComparison.Ordinal) ? "1.5" : "2.0";
            var items = PartitionOf(request) == "p1" ? Item("p1", "9") : request.BodyText.Contains("ExclusiveStartKey", StringComparison.Ordinal) ? Item("p0", "2") : string.Empty;
            var next = PartitionOf(request) == "p0" && !request.BodyText.Contains("ExclusiveStartKey", StringComparison.Ordinal) ? ",\"LastEvaluatedKey\":" + Item("p0", "1") : string.Empty;
            return StubResponse.Json("{\"Items\":[" + items + "]" + next + ",\"ConsumedCapacity\":{\"TableName\":\"Shop\",\"CapacityUnits\":" + units + "}}");
        });
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).ReturnCapacity().PageAsync(1);

        // Assert
        await Assert.That((page.ConsumedCapacity, handler.Requests.Count, handler.Requests[0].BodyText.Contains("\"ReturnConsumedCapacity\":\"TOTAL\"", StringComparison.Ordinal)))
            .IsEqualTo((new ConsumedCapacity { TableName = "Shop", CapacityUnits = 5.5 }, 3, true));
    }

    [Test]
    public async Task PageAsync_ReturnCapacityWithIndexes_AddsUpByTheNameOfTheIndex()
    {
        // Arrange
        using var handler = new StubHandler(request => StubResponse.Json("{\"Items\":[" + Item(PartitionOf(request), "1") + "],\"ConsumedCapacity\":" + (PartitionOf(request) == "p0"
            ? """{"TableName":"Shop","CapacityUnits":3.0,"ReadCapacityUnits":3.0,"Table":{"CapacityUnits":1.0},"GlobalSecondaryIndexes":{"A":{"CapacityUnits":2.0}},"LocalSecondaryIndexes":{"L":{"ReadCapacityUnits":1.0}}}"""
            : """{"TableName":"Shop","CapacityUnits":4.0,"WriteCapacityUnits":1.0,"Table":{"CapacityUnits":1.0,"WriteCapacityUnits":1.0},"GlobalSecondaryIndexes":{"A":{"CapacityUnits":1.0},"B":{"CapacityUnits":2.0}}}""") + "}"));
        using var client = TestClient.Create(handler);

        // Act
        var capacity = (await client.Table("Shop", "pk", "sk").Query(TwoPartitions).Limit(5).ReturnCapacity(ReturnConsumedCapacity.Indexes).PageAsync(1)).ConsumedCapacity!;

        // Assert
        await Assert.That((capacity.CapacityUnits, capacity.ReadCapacityUnits, capacity.WriteCapacityUnits, capacity.Table, capacity.GlobalSecondaryIndexes!["A"].CapacityUnits, capacity.GlobalSecondaryIndexes["B"].CapacityUnits, capacity.LocalSecondaryIndexes!["L"].ReadCapacityUnits))
            .IsEqualTo((7.0, 3.0, 1.0, new CapacityDetail { CapacityUnits = 2.0, WriteCapacityUnits = 1.0 }, 3.0, 2.0, 1.0));
    }

    [Test]
    public async Task PageAsync_OneResponseWithoutCapacity_SumsTheOthers()
    {
        // Arrange
        using var handler = new StubHandler(request => StubResponse.Json("{\"Items\":[" + Item(PartitionOf(request), "1") + "]" + (PartitionOf(request) == "p0" ? string.Empty : ",\"ConsumedCapacity\":{\"CapacityUnits\":1.0}") + "}"));
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).ReturnCapacity().PageAsync(1);

        // Assert
        await Assert.That(page.ConsumedCapacity).IsEqualTo(new ConsumedCapacity { CapacityUnits = 1.0 });
    }

    [Test]
    public async Task PageAsync_NoReturnCapacity_HasNoCapacity()
    {
        // Arrange
        var wire = new FanOutWire().Put("p0", "1").Put("p1", "2");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).PageAsync(1);

        // Assert
        await Assert.That((page.ConsumedCapacity, handler.Requests[0].BodyText.Contains("ReturnConsumedCapacity", StringComparison.Ordinal))).IsEqualTo((null, false));
    }

    [Test]
    public async Task ItemsAsync_ReturnCapacity_ReachesEveryRequest()
    {
        // Arrange
        var wire = new FanOutWire().Put("p0", "1").Put("p1", "2");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk", "sk").Query(TwoPartitions).ReturnCapacity(ReturnConsumedCapacity.Indexes).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(handler.Requests.All(static request => request.BodyText.Contains("\"ReturnConsumedCapacity\":\"INDEXES\"", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task CountAsync_ReturnCapacity_ReachesEveryRequest()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Count":1,"ScannedCount":1}"""));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk", "sk").Query(TwoPartitions).ReturnCapacity().MaxAttempts(1).CountAsync();

        // Assert
        await Assert.That(handler.Requests.All(static request => request.BodyText.Contains("\"ReturnConsumedCapacity\":\"TOTAL\"", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task MaxAttempts_One_DoesNotRetryAndHelpersApply()
    {
        // Arrange: a client that would retry
        using var handler = new StubHandler(_ => Throttled());
        using var client = CreateClient(handler);

        // Act
        await TestClient.CaptureAsync<DynamoDbException>(() => client.Table("Shop", "pk", "sk").Query(new AttributeValue[] { "p0" }).Once().ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task MaxAttempts_BelowOne_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => Task.FromResult(client.Table("Shop", "pk", "sk").Query(TwoPartitions).MaxAttempts(0).ToRequestJson()));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("attempts");
    }

    [Test]
    public async Task Query_StringKeys_ReadsThosePartitions()
    {
        // Arrange
        var wire = new FanOutWire().Put("p0", "1", "3").Put("p1", "2");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        string[] keys = ["p0", "p1"];

        // Act
        var fromTable = await shop.Query(keys).ItemsAsync().ToListAsync();
        var fromIndex = await shop.Index("ByStatus", "pk", "sk").Query(keys, SortKeyCondition.Ge("2")).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((fromTable.SortKeys(), fromIndex.Count)).IsEqualTo(("1,2,3", 3));
    }

    [Test]
    public async Task Query_StringKeyThatIsNull_ThrowsNamingThePosition()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        string[] keys = ["a", null!];

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(shop.Query(keys).ToRequestJson()));

        // Assert
        await Assert.That((failure.Message.Contains("position 1 is empty", StringComparison.Ordinal), failure.ParamName)).IsEqualTo((true, "partitionKeys"));
    }

    [Test]
    public async Task Query_RepeatedStringKey_ThrowsNamingThePositions()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        string[] keys = ["a", "a"];

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(shop.Query(keys).ToRequestJson()));

        // Assert
        await Assert.That(failure.Message).Contains("position 1 repeats the one at position 0");
    }

    [Test]
    public async Task Query_ComputedStringKeys_MergeOnATableAndAnIndex()
    {
        // Arrange (a computed list of shards is the normal case, and it is enumerated once)
        var wire = new FanOutWire().Put("p0", "1", "3").Put("p1", "2");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        var enumerations = 0;
        IEnumerable<string> Keys()
        {
            enumerations++;
            yield return "p0";
            yield return "p1";
        }

        // Act
        var fromTable = await shop.Query(Keys()).ItemsAsync().ToListAsync();
        var fromIndex = await shop.Index("ByStatus", "pk", "sk").Query(Keys(), SortKeyCondition.Ge("2")).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((fromTable.SortKeys(), fromIndex.Count, enumerations)).IsEqualTo(("1,2,3", 3, 2));
    }

    [Test]
    public async Task Query_NoComputedStringKeys_SendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var items = await shop.Query(new List<string>()).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.Count, handler.Requests.Count)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task Query_NullListOfStrings_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentNullException>(() => Task.FromResult(shop.Query((IEnumerable<string>)null!).ToRequestJson()));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("partitionKeys");
    }

    [Test]
    public async Task Query_ComputedStringKeyThatIsNull_ThrowsNamingThePosition()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        List<string> keys = ["a", null!];

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(shop.Query(keys).ToRequestJson()));

        // Assert
        await Assert.That((failure.Message.Contains("position 1 is empty", StringComparison.Ordinal), failure.ParamName)).IsEqualTo((true, "partitionKeys"));
    }

    [Test]
    public async Task Query_RepeatedComputedStringKey_ThrowsNamingThePositions()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        List<string> keys = ["a", "a"];

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(shop.Query(keys).ToRequestJson()));

        // Assert
        await Assert.That(failure.Message).Contains("position 1 repeats the one at position 0");
    }

    [Test]
    public async Task PageAsync_InvalidSize_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(async () => await client.Table("Shop", "pk", "sk").Query(TwoPartitions).PageAsync(0));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("pageSize");
    }

    [Test]
    public async Task PageAsync_Failure_LeavesNoArrayOutstanding()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "Gone."));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await TestClient.CaptureAsync<ResourceNotFoundException>(async () => await client.Table("Shop", "pk", "sk").Query(TwoPartitions).PageAsync(1));
        client.Dispose();

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task PageAsync_Done_LeavesNoArrayOutstanding()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        var wire = new FanOutWire().Put("p0", "1", "3").Put("p1", "2", "4");
        using var handler = new StubHandler(wire.Answer);
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await client.Table("Shop", "pk", "sk").Query(TwoPartitions).PageAsync(1);
        client.Dispose();

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task CountAsync_Partitions_SumsAllPagesOfAll()
    {
        // Arrange
        using var handler = new StubHandler(request => StubResponse.Json(request.BodyText.Contains("ExclusiveStartKey", StringComparison.Ordinal)
            ? """{"Count":3,"ScannedCount":3}"""
            : """{"Count":2,"ScannedCount":2,"LastEvaluatedKey":{"pk":{"S":"a"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var count = await client.Table("Shop", "pk", "sk").Query(TwoPartitions).CountAsync();

        // Assert
        await Assert.That((count, handler.Requests.Count, handler.Requests[0].BodyText.Contains("\"Select\":\"COUNT\"", StringComparison.Ordinal))).IsEqualTo((10, 4, true));
    }

    [Test]
    public async Task CountAsync_MaxConcurrency_BoundsTheRequestsInFlight()
    {
        // Arrange: one worker means one partition after the other
        var inFlight = 0;
        var most = 0;
        using var handler = new StubHandler(async (_, _) =>
        {
            InterlockedMax(ref most, Interlocked.Increment(ref inFlight));
            await Task.Yield();
            Interlocked.Decrement(ref inFlight);
            return StubResponse.Json("""{"Count":1,"ScannedCount":1}""");
        });
        using var client = TestClient.Create(handler);

        // Act
        var count = await client.Table("Shop", "pk", "sk").Query(["p0", "p1", "p2", "p3"]).MaxConcurrency(1).CountAsync();

        // Assert
        await Assert.That((count, most)).IsEqualTo((4, 1));
    }

    [Test]
    public async Task CountAsync_Projection_Throws()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(async () => await client.Table("Shop", "pk", "sk").Query(TwoPartitions).Project("x").CountAsync());
        client.Dispose();

        // Assert
        await Assert.That((failure.Message, handler.Requests.Count, pool.Outstanding)).IsEqualTo(("This Query on Shop counts items and reads none, so it has no use for Project.", 0, 0));
    }

    [Test]
    public async Task CountAsync_MoreThanAnIntHolds_Throws()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Count":2147483647,"ScannedCount":1}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<OverflowException>(async () => await client.Table("Shop", "pk", "sk").Query(TwoPartitions).CountAsync());

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task CountAsync_PartitionFails_SurfacesItAndLeavesNoArrayOutstanding()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "Gone."));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        var failure = await TestClient.CaptureAsync<ResourceNotFoundException>(async () => await client.Table("Shop", "pk", "sk").Query(TwoPartitions).CountAsync());
        client.Dispose();

        // Assert
        await Assert.That((failure.Message.StartsWith("Gone.", StringComparison.Ordinal), pool.Outstanding)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task CountAsync_Cancelled_ThrowsTheTokenOfTheCaller()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<OperationCanceledException>(async () => await client.Table("Shop", "pk", "sk").Query(TwoPartitions).CountAsync(cancellation.Token));

        // Assert
        await Assert.That(failure.CancellationToken == cancellation.Token).IsTrue();
    }

    private static string N(string number) => "{\"N\":\"" + number + "\"}";

    // What a token is worth without the second key of its entries.
    private static ContinuationToken WithoutHints(ContinuationToken token) => ContinuationToken.FromKey(new Item
    {
        ["fanOut"] = AttributeValue.FromList(token.ToKey()!["fanOut"].AsList().Select(static entry => entry.IsNull ? entry : AttributeValue.FromList([entry.AsList()[0], AttributeValue.FromMap([])]))),
    });

    private static string Number(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string PartitionOf(RecordedRequest request) =>
        JsonElement.Parse(request.BodyText).GetProperty("ExpressionAttributeValues").GetProperty(":pk").GetProperty("S").GetString()!;

    private static string Item(string partition, string sortKey) => "{\"pk\":{\"S\":\"" + partition + "\"},\"sk\":{\"S\":\"" + sortKey + "\"}}";

    private static StubResponse Partition(string partition, params string[] sortKeys) => StubResponse.Json("{\"Items\":[" + string.Join(",", sortKeys.Select(sortKey => Item(partition, sortKey))) + "]}");

    private static StubResponse More(string partition, string sortKey) => StubResponse.Json("{\"Items\":[" + Item(partition, sortKey) + "],\"LastEvaluatedKey\":" + Item(partition, sortKey) + "}");

    private static StubResponse FilteredEmpty(string partition, string sortKey) => StubResponse.Json("{\"Items\":[],\"LastEvaluatedKey\":" + Item(partition, sortKey) + "}");
}
