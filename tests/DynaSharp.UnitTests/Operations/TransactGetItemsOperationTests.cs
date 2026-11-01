using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.UnitTests.Telemetry.ActivityCapture;

namespace DynaSharp.UnitTests.Operations;

public sealed class TransactGetItemsOperationTests
{
    // The counterparts of the parameters in generate_operation_vectors.py, by vector name. The expected JSON is what botocore wrote
    // for the parameters there.
    private static readonly Dictionary<string, TransactGetItemsRequest> BotocoreRequests = new()
    {
        ["Minimal"] = Request(),
        ["AllOptionsOnTwoTables"] = new()
        {
            TransactItems =
            [
                new TransactGetItem
                {
                    Get = new TransactGet
                    {
                        TableName = "Orders",
                        Key = new Dictionary<string, AttributeValue> { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#1" },
                        ProjectionExpression = "#s, total",
                        ExpressionAttributeNames = new Dictionary<string, string> { ["#s"] = "status" },
                    },
                },
                Get("Customers", "CUSTOMER#1"),
                new TransactGetItem
                {
                    Get = new TransactGet { TableName = "Orders", Key = new Dictionary<string, AttributeValue> { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#2" } },
                },
            ],
            ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
        },
        ["ReturnConsumedCapacityIndexes"] = Request() with { ReturnConsumedCapacity = ReturnConsumedCapacity.Indexes },
        ["HundredActions"] = new()
        {
            TransactItems = [.. Enumerable.Range(0, 100).Select(index => new TransactGet { TableName = "Users", Key = OperationVectors.NumberedKey(index) }).Select(get => new TransactGetItem { Get = get })],
        },
        ["SpecialCharacters"] = new()
        {
            TransactItems =
            [
                new TransactGetItem
                {
                    Get = new TransactGet
                    {
                        TableName = OperationVectors.SpecialText,
                        Key = new Dictionary<string, AttributeValue> { [OperationVectors.SpecialText] = OperationVectors.SpecialText },
                        ProjectionExpression = "#n",
                        ExpressionAttributeNames = new Dictionary<string, string> { ["#n"] = OperationVectors.SpecialText },
                    },
                },
            ],
        },
    };

    public static IEnumerable<string> RequestVectorNames() => OperationVectors.RequestNames("TransactGetItems");

    public static IEnumerable<string> ResponseVectorNames() => OperationVectors.ResponseNames("TransactGetItems");

    [Test]
    public async Task Name_Always_IsTransactGetItems()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = TransactGetItemsOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("TransactGetItems");
    }

    [Test]
    public async Task Name_Always_MatchesTheTargetBotocoreSends()
    {
        // Arrange
        var vector = OperationVectors.Request("TransactGetItems", "Minimal");

        // Act
        var target = WireProtocol.TargetPrefix + TransactGetItemsOperation.Name;

        // Assert
        await Assert.That(target).IsEqualTo(vector.Target);
    }

    [Test]
    public async Task GetTableName_OneTableOnSeveralActions_ReturnsItsName()
    {
        // Arrange
        var request = new TransactGetItemsRequest { TransactItems = [Get("Users", "a"), Get("Users", "b"), Get("Users", "c")] };

        // Act
        var tableName = TransactGetItemsOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsEqualTo("Users");
    }

    [Test]
    public async Task GetTableName_SeveralTables_ReturnsNull()
    {
        // Arrange
        var request = BotocoreRequests["AllOptionsOnTwoTables"];

        // Act
        var tableName = TransactGetItemsOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsNull();
    }

    [Test]
    public async Task GetTableName_NoAction_ReturnsNull()
    {
        // Arrange
        var request = new TransactGetItemsRequest { TransactItems = [] };

        // Act
        var tableName = TransactGetItemsOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsNull();
    }

    [Test]
    public async Task BotocoreRequests_Always_CoverEveryVector()
    {
        // Arrange (the vectors come from the generator, the requests from this class)

        // Act
        var missing = RequestVectorNames().Except(BotocoreRequests.Keys).Concat(BotocoreRequests.Keys.Except(RequestVectorNames()));

        // Assert
        await Assert.That(missing).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(RequestVectorNames))]
    public async Task WriteRequest_BotocoreVector_WritesTheJsonThatBotocoreWrites(string name)
    {
        // Arrange
        var vector = OperationVectors.Request("TransactGetItems", name);
        var request = BotocoreRequests[name];

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Body, json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task WriteRequest_RequiredMembersOnly_WritesTheGet()
    {
        // Arrange
        var request = Request();

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TransactItems":[{"Get":{"TableName":"Users","Key":{"pk":{"S":"user-1"}}}}]}""");
    }

    [Test]
    public async Task WriteRequest_EverythingSet_WritesInRequestOrder()
    {
        // Arrange
        var request = BotocoreRequests["AllOptionsOnTwoTables"];

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"TransactItems":[{"Get":{"TableName":"Orders","Key":{"pk":{"S":"CUSTOMER#1"},"sk":{"S":"ORDER#1"}},"ProjectionExpression":"#s, total","ExpressionAttributeNames":{"#s":"status"}}},{"Get":{"TableName":"Customers","Key":{"pk":{"S":"CUSTOMER#1"}}}},{"Get":{"TableName":"Orders","Key":{"pk":{"S":"CUSTOMER#1"},"sk":{"S":"ORDER#2"}}}}],"ReturnConsumedCapacity":"TOTAL"}""");
    }

    [Test]
    public async Task WriteRequest_EmptyExpressionAttributeNames_OmitsThem()
    {
        // Arrange
        var request = new TransactGetItemsRequest
        {
            TransactItems = [new TransactGetItem { Get = new TransactGet { TableName = "Users", Key = new Dictionary<string, AttributeValue>(), ExpressionAttributeNames = new Dictionary<string, string>() } }],
        };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TransactItems":[{"Get":{"TableName":"Users","Key":{}}}]}""");
    }

    [Test]
    public async Task WriteRequest_NoActions_WritesAnEmptyArrayAndLeavesTheRejectionToDynamoDb()
    {
        // Arrange
        var request = new TransactGetItemsRequest { TransactItems = [] };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TransactItems":[]}""");
    }

    [Test]
    public async Task WriteRequest_UnknownReturnConsumedCapacity_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = Request() with { ReturnConsumedCapacity = (ReturnConsumedCapacity)99 };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known ReturnConsumedCapacity", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_EveryMember_MapsEachToItsProperty()
    {
        // Arrange (every value differs, so a swapped property cannot go unnoticed)
        const string json = """
            {"Responses":[{"Item":{"pk":{"S":"a"}}},{},{"Item":{"pk":{"S":"c"}}}],
             "ConsumedCapacity":[{"TableName":"Users","CapacityUnits":3.5}]}
            """;

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(JsonComparer.Differences(json, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_EmptyObject_HasEmptyCollections()
    {
        // Arrange
        const string json = "{}";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That((response.Responses.Count, response.ConsumedCapacity.Count)).IsEqualTo((0, 0));
    }

    [Test]
    [MethodDataSource(nameof(ResponseVectorNames))]
    public async Task ReadResponse_BotocoreVector_ReturnsWhatBotocoreParsed(string name)
    {
        // Arrange
        var vector = OperationVectors.Response("TransactGetItems", name);

        // Act
        var response = Read(vector.Wire);

        // Assert
        var expected = ResponseJson.WithDefaults(vector.Parsed, ("Responses", new JsonArray()), ("ConsumedCapacity", new JsonArray()));
        await Assert.That(JsonComparer.Differences(expected, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_MissingItem_HasNoItem()
    {
        // Arrange (DynamoDB answers {} for an item that does not exist)
        const string json = """{"Responses":[{"Item":{"pk":{"S":"a"}}},{}]}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.Responses[1].Item).IsNull();
    }

    [Test]
    public async Task ReadResponse_NullEntry_IsAResponseWithoutItem()
    {
        // Arrange (the API reference describes a missing item as null)
        const string json = """{"Responses":[null,{"Item":{"pk":{"S":"b"}}}]}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That((response.Responses.Count, response.Responses[0].Item, response.Responses[1].Item!["pk"].AsString())).IsEqualTo((2, (IReadOnlyDictionary<string, AttributeValue>?)null, "b"));
    }

    [Test]
    public async Task ReadResponse_ExistingItemWithoutProjectedAttributes_HasAnEmptyItem()
    {
        // Arrange
        const string json = """{"Responses":[{"Item":{}}]}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.Responses[0].Item).IsEmpty();
    }

    [Test]
    public async Task ReadResponse_EntryThatIsNeitherObjectNorNull_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"Responses":[{"Item":{"pk":{"S":"a"}}},"text"]}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessageContaining("Expected an item response", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_Responses_AreNotAnArrayOrListThatTheCallerCouldChange()
    {
        // Arrange
        const string json = """{"Responses":[{}]}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.Responses is Array or List<ItemResponse>).IsFalse();
    }

    [Test]
    [Arguments("""{"Responses":{}}""")]
    [Arguments("""{"Responses":[{"Item":[]}]}""")]
    [Arguments("""{"Responses":[{"Item":{"pk":{"X":"a"}}}]}""")]
    [Arguments("""{"Responses":[{"Item":{"pk":{"S":"a"}}}""")]
    [Arguments("""{"ConsumedCapacity":{}}""")]
    [Arguments("[]")]
    public async Task ReadResponse_MalformedBody_ThrowsJsonException(string json)
    {
        // Arrange (see json)

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>();
    }

    [Test]
    public async Task SetActivityTags_OneTable_SetsTableNamesToThatTable()
    {
        // Arrange
        var request = new TransactGetItemsRequest { TransactItems = [Get("Users", "a"), Get("Users", "b")] };
        using var activity = new Activity("test");

        // Act
        TransactGetItemsOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(Tags(activity)).IsEqualTo("aws.dynamodb.table_names=Users");
    }

    [Test]
    public async Task SetActivityTags_SeveralTables_SetsDistinctTableNamesSortedOrdinally()
    {
        // Arrange
        var request = new TransactGetItemsRequest { TransactItems = [Get("users", "a"), Get("Orders", "b"), Get("users", "c"), Get("apple", "d"), Get("Orders", "e")] };
        using var activity = new Activity("test");

        // Act
        TransactGetItemsOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(Tags(activity)).IsEqualTo("aws.dynamodb.table_names=Orders,apple,users");
    }

    private static TransactGetItem Get(string table, string pk) => new()
    {
        Get = new TransactGet { TableName = table, Key = new Dictionary<string, AttributeValue> { ["pk"] = pk } },
    };

    private static TransactGetItemsRequest Request() => new() { TransactItems = [Get("Users", "user-1")] };

    private static string Write(TransactGetItemsRequest request) => TestJson.Write(writer => TransactGetItemsOperation.WriteRequest(writer, request));

    private static TransactGetItemsResponse Read(string json) => TestJson.Read(json, static (ref reader) => TransactGetItemsOperation.ReadResponse(ref reader));
}
