using System.Diagnostics;
using System.Text.Json;
using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.UnitTests.Telemetry.ActivityCapture;

namespace DynaSharp.UnitTests.Operations;

public sealed class BatchGetItemOperationTests
{
    // The counterparts of the parameters in generate_operation_vectors.py, by vector name. The expected JSON is what botocore wrote
    // for the parameters there.
    private static readonly Dictionary<string, BatchGetItemRequest> BotocoreRequests = new()
    {
        ["RetrieveItemsFromMultipleTablesFromApiReference"] = new()
        {
            RequestItems = new Dictionary<string, KeysAndAttributes>
            {
                ["Forum"] = new()
                {
                    Keys = [Key("Name", "Amazon DynamoDB"), Key("Name", "Amazon RDS"), Key("Name", "Amazon Redshift")],
                    ProjectionExpression = "Name, Threads, Messages, Views",
                },
                ["Thread"] = new()
                {
                    Keys = [new Dictionary<string, AttributeValue> { ["ForumName"] = "Amazon DynamoDB", ["Subject"] = "Concurrent reads" }],
                    ProjectionExpression = "Tags, Message",
                },
            },
            ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
        },
        ["Minimal"] = Request(),
        ["AllOptions"] = new()
        {
            RequestItems = new Dictionary<string, KeysAndAttributes>
            {
                ["Orders"] = new()
                {
                    Keys =
                    [
                        new Dictionary<string, AttributeValue> { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#1" },
                        new Dictionary<string, AttributeValue> { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#2" },
                    ],
                    ConsistentRead = true,
                    ProjectionExpression = "#s, total, address.city",
                    ExpressionAttributeNames = new Dictionary<string, string> { ["#s"] = "status" },
                },
                ["Customers"] = new() { Keys = [Key("pk", "CUSTOMER#1")] },
            },
            ReturnConsumedCapacity = ReturnConsumedCapacity.Indexes,
        },
        ["BinaryAndNumberKeys"] = new()
        {
            RequestItems = new Dictionary<string, KeysAndAttributes>
            {
                ["Blobs"] = new()
                {
                    Keys =
                    [
                        new Dictionary<string, AttributeValue> { ["pk"] = AttributeValue.FromBinary([0, 255, (byte)'k', (byte)'e', (byte)'y']), ["sk"] = AttributeValue.FromNumber("-42.5") },
                        new Dictionary<string, AttributeValue> { ["pk"] = AttributeValue.FromBinary([1]), ["sk"] = AttributeValue.FromNumber("1E+3") },
                    ],
                },
            },
        },
        ["TableArnAsKey"] = new()
        {
            RequestItems = new Dictionary<string, KeysAndAttributes> { [OperationVectors.TableArn] = new() { Keys = [Key("pk", "user-1")] } },
            ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
        },
        ["HundredKeys"] = new()
        {
            RequestItems = new Dictionary<string, KeysAndAttributes> { ["Users"] = new() { Keys = [.. Enumerable.Range(0, 100).Select(OperationVectors.NumberedKey)] } },
        },
        ["SpecialCharacters"] = new()
        {
            RequestItems = new Dictionary<string, KeysAndAttributes>
            {
                [OperationVectors.SpecialText] = new()
                {
                    Keys = [Key(OperationVectors.SpecialText, OperationVectors.SpecialText)],
                    ProjectionExpression = "#n",
                    ExpressionAttributeNames = new Dictionary<string, string> { ["#n"] = OperationVectors.SpecialText },
                },
            },
        },
    };

    public static IEnumerable<string> RequestVectorNames() => OperationVectors.RequestNames("BatchGetItem");

    public static IEnumerable<string> ResponseVectorNames() => OperationVectors.ResponseNames("BatchGetItem");

    [Test]
    public async Task Name_Always_IsBatchGetItem()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = BatchGetItemOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("BatchGetItem");
    }

    [Test]
    public async Task Name_Always_MatchesTheTargetBotocoreSends()
    {
        // Arrange
        var vector = OperationVectors.Request("BatchGetItem", "Minimal");

        // Act
        var target = WireProtocol.TargetPrefix + BatchGetItemOperation.Name;

        // Assert
        await Assert.That(target).IsEqualTo(vector.Target);
    }

    [Test]
    public async Task GetTableName_OneTable_ReturnsItsName()
    {
        // Arrange
        var request = Request();

        // Act
        var tableName = BatchGetItemOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsEqualTo("Users");
    }

    [Test]
    public async Task GetTableName_SeveralTables_ReturnsNull()
    {
        // Arrange
        var request = BotocoreRequests["AllOptions"];

        // Act
        var tableName = BatchGetItemOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsNull();
    }

    [Test]
    public async Task GetTableName_NoTable_ReturnsNull()
    {
        // Arrange
        var request = new BatchGetItemRequest { RequestItems = new Dictionary<string, KeysAndAttributes>() };

        // Act
        var tableName = BatchGetItemOperation.GetTableName(request);

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
        var vector = OperationVectors.Request("BatchGetItem", name);
        var request = BotocoreRequests[name];

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Body, json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task WriteRequest_RequiredMembersOnly_WritesTheKeysOfTheTable()
    {
        // Arrange
        var request = Request();

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"RequestItems":{"Users":{"Keys":[{"pk":{"S":"user-1"}}]}}}""");
    }

    [Test]
    public async Task WriteRequest_EverythingSet_WritesInRequestOrder()
    {
        // Arrange
        var request = BotocoreRequests["AllOptions"];

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"RequestItems":{"Orders":{"Keys":[{"pk":{"S":"CUSTOMER#1"},"sk":{"S":"ORDER#1"}},{"pk":{"S":"CUSTOMER#1"},"sk":{"S":"ORDER#2"}}],"ConsistentRead":true,"ProjectionExpression":"#s, total, address.city","ExpressionAttributeNames":{"#s":"status"}},"Customers":{"Keys":[{"pk":{"S":"CUSTOMER#1"}}]}},"ReturnConsumedCapacity":"INDEXES"}""");
    }

    [Test]
    public async Task WriteRequest_EventuallyConsistentRead_OmitsConsistentRead()
    {
        // Arrange
        var request = Request("Users", new KeysAndAttributes { Keys = [Key("pk", "a")], ConsistentRead = false });

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).DoesNotContain("ConsistentRead", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_EmptyExpressionAttributeNames_OmitsThem()
    {
        // Arrange
        var request = Request("Users", new KeysAndAttributes { Keys = [Key("pk", "a")], ExpressionAttributeNames = new Dictionary<string, string>() });

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).DoesNotContain("ExpressionAttributeNames", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_NoKeys_WritesAnEmptyArrayAndLeavesTheRejectionToDynamoDb()
    {
        // Arrange
        var request = Request("Users", new KeysAndAttributes { Keys = [] });

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"RequestItems":{"Users":{"Keys":[]}}}""");
    }

    [Test]
    public async Task WriteRequest_NoTables_WritesAnEmptyMapAndLeavesTheRejectionToDynamoDb()
    {
        // Arrange
        var request = new BatchGetItemRequest { RequestItems = new Dictionary<string, KeysAndAttributes>() };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"RequestItems":{}}""");
    }

    [Test]
    public async Task WriteRequest_ReturnConsumedCapacityTotal_WritesTotal()
    {
        // Arrange
        var request = Request() with { ReturnConsumedCapacity = ReturnConsumedCapacity.Total };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).EndsWith(""","ReturnConsumedCapacity":"TOTAL"}""", StringComparison.Ordinal);
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
    public async Task WriteRequest_UnprocessedKeysOfAResponse_AreWrittenAsTheyWereRead()
    {
        // Arrange (DynamoDB documents UnprocessedKeys as the RequestItems of the next call)
        var vector = OperationVectors.Response("BatchGetItem", "PartialResultAcrossTables");
        var response = Read(vector.Wire);

        // Act
        var json = Write(new BatchGetItemRequest { RequestItems = response.UnprocessedKeys });

        // Assert
        await Assert.That(JsonComparer.Differences("""{"RequestItems":{"Orders":{"Keys":[{"pk":{"S":"CUSTOMER#1"},"sk":{"S":"ORDER#2"}},{"pk":{"S":"CUSTOMER#1"},"sk":{"S":"ORDER#3"}}],"ConsistentRead":true,"ProjectionExpression":"#s, total","ExpressionAttributeNames":{"#s":"status"}}}}""", json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_EveryMember_MapsEachToItsProperty()
    {
        // Arrange (every value differs, so a swapped property cannot go unnoticed)
        const string json = """
            {"Responses":{"Forum":[{"Name":{"S":"a"}},{"Name":{"S":"b"}}],"Thread":[{"Subject":{"S":"c"}}]},
             "UnprocessedKeys":{"Forum":{"Keys":[{"Name":{"S":"d"}}],"ConsistentRead":true,"ProjectionExpression":"Name","ExpressionAttributeNames":{"#n":"Name"}}},
             "ConsumedCapacity":[{"TableName":"Forum","CapacityUnits":3.5},{"TableName":"Thread","CapacityUnits":1.5}]}
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
        await Assert.That((response.Responses.Count, response.UnprocessedKeys.Count, response.ConsumedCapacity.Count)).IsEqualTo((0, 0, 0));
    }

    [Test]
    [MethodDataSource(nameof(ResponseVectorNames))]
    public async Task ReadResponse_BotocoreVector_ReturnsWhatBotocoreParsed(string name)
    {
        // Arrange
        var vector = OperationVectors.Response("BatchGetItem", name);

        // Act
        var response = Read(vector.Wire);

        // Assert
        var expected = ResponseJson.WithDefaults(vector.Parsed, ("Responses", new System.Text.Json.Nodes.JsonObject()), ("UnprocessedKeys", new System.Text.Json.Nodes.JsonObject()), ("ConsumedCapacity", new System.Text.Json.Nodes.JsonArray()));
        await Assert.That(JsonComparer.Differences(expected, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_ItemsOfATable_AreNotAnArrayOrListThatTheCallerCouldChange()
    {
        // Arrange
        const string json = """{"Responses":{"Forum":[{"Name":{"S":"a"}}]},"ConsumedCapacity":[{"TableName":"Forum"}]}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.Responses["Forum"] is Array or List<IReadOnlyDictionary<string, AttributeValue>> or List<Dictionary<string, AttributeValue>>
            || response.ConsumedCapacity is Array or List<ConsumedCapacity>).IsFalse();
    }

    [Test]
    public async Task ReadResponse_UnprocessedKeysWithoutKeys_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"UnprocessedKeys":{"Forum":{"ConsistentRead":true}}}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessageContaining("no Keys", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("""{"Responses":[]}""")]
    [Arguments("""{"Responses":{"Forum":{"Name":{"S":"a"}}}}""")]
    [Arguments("""{"UnprocessedKeys":{"Forum":[]}}""")]
    [Arguments("""{"ConsumedCapacity":{"TableName":"Forum"}}""")]
    [Arguments("""{"Responses":{"Forum":[{"Name":{"X":"a"}}]}}""")]
    [Arguments("""{"Responses":{"Forum":[{"Name":{"S":"a"}}""")]
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
    [Arguments("""{"UnprocessedKeys":{"Forum":{"Keys":[],"ConsistentRead":"yes"}}}""")]
    [Arguments("""{"UnprocessedKeys":{"Forum":{"Keys":[],"ProjectionExpression":1}}}""")]
    public async Task ReadResponse_MemberOfTheWrongType_ThrowsInvalidOperationException(string json)
    {
        // Arrange (see json)

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task SetActivityTags_OneTable_SetsTableNamesToThatTable()
    {
        // Arrange
        var request = Request();
        using var activity = new Activity("test");

        // Act
        BatchGetItemOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(Tags(activity)).IsEqualTo("aws.dynamodb.table_names=Users");
    }

    [Test]
    public async Task SetActivityTags_SeveralTables_SetsTableNamesSortedOrdinally()
    {
        // Arrange (ordinal: uppercase before lowercase, whatever the order of the request)
        var request = new BatchGetItemRequest
        {
            RequestItems = new Dictionary<string, KeysAndAttributes>
            {
                ["users"] = new() { Keys = [] },
                ["Zebra"] = new() { Keys = [] },
                ["apple"] = new() { Keys = [] },
                ["Orders"] = new() { Keys = [] },
            },
        };
        using var activity = new Activity("test");

        // Act
        BatchGetItemOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(Tags(activity)).IsEqualTo("aws.dynamodb.table_names=Orders,Zebra,apple,users");
    }

    [Test]
    public async Task SetActivityTags_Request_SetsNoKeysOrExpressions()
    {
        // Arrange
        var request = BotocoreRequests["AllOptions"];
        using var activity = new Activity("test");

        // Act
        BatchGetItemOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(activity.TagObjects.Select(tag => tag.Key).ToArray()).IsEquivalentTo(["aws.dynamodb.table_names"]);
    }

    private static Dictionary<string, AttributeValue> Key(string name, AttributeValue value) => new() { [name] = value };

    private static BatchGetItemRequest Request(string table = "Users", KeysAndAttributes? keys = null) => new()
    {
        RequestItems = new Dictionary<string, KeysAndAttributes> { [table] = keys ?? new KeysAndAttributes { Keys = [Key("pk", "user-1")] } },
    };

    private static string Write(BatchGetItemRequest request) => TestJson.Write(writer => BatchGetItemOperation.WriteRequest(writer, request));

    private static BatchGetItemResponse Read(string json) => TestJson.Read(json, static (ref reader) => BatchGetItemOperation.ReadResponse(ref reader));
}
