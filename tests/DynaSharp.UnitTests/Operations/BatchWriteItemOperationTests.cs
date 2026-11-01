using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.UnitTests.Telemetry.ActivityCapture;

namespace DynaSharp.UnitTests.Operations;

public sealed class BatchWriteItemOperationTests
{
    // The counterparts of the parameters in generate_operation_vectors.py, by vector name. The expected JSON is what botocore wrote
    // for the parameters there.
    private static readonly Dictionary<string, BatchWriteItemRequest> BotocoreRequests = new()
    {
        ["MultipleOperationsFromApiReference"] = new()
        {
            RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>
            {
                ["Forum"] = [ForumPut("Amazon DynamoDB"), ForumPut("Amazon RDS"), ForumPut("Amazon Redshift"), ForumPut("Amazon ElastiCache")],
            },
            ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
        },
        ["Minimal"] = Request(),
        ["PutAndDeleteInOneTable"] = new()
        {
            RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>
            {
                ["Users"] =
                [
                    Put(new Dictionary<string, AttributeValue> { ["pk"] = "user-1", ["name"] = "Ada" }),
                    Delete("user-2"),
                    Delete("user-3"),
                ],
            },
        },
        ["MultipleTablesAllOptions"] = new()
        {
            RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>
            {
                ["Orders"] =
                [
                    Put(new Dictionary<string, AttributeValue> { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#1" }),
                    Delete(new Dictionary<string, AttributeValue> { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#0" }),
                ],
                ["Customers"] = [Put(new Dictionary<string, AttributeValue> { ["pk"] = "CUSTOMER#1" })],
            },
            ReturnConsumedCapacity = ReturnConsumedCapacity.Indexes,
            ReturnItemCollectionMetrics = ReturnItemCollectionMetrics.Size,
        },
        ["AllAttributeValueTypes"] = new()
        {
            RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>
            {
                ["Users"] = [Put(OperationVectors.AllValueTypes().Append(KeyValuePair.Create("pk", (AttributeValue)"user-1")).ToDictionary())],
            },
        },
        ["TwentyFivePuts"] = new()
        {
            RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>
            {
                ["Users"] = [.. Enumerable.Range(0, 25).Select(index => Put(OperationVectors.NumberedKey(index)))],
            },
        },
        ["TableArnAsKey"] = new()
        {
            RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>> { [OperationVectors.TableArn] = [Delete("user-1")] },
        },
        ["SpecialCharacters"] = new()
        {
            RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>
            {
                [OperationVectors.SpecialText] =
                [
                    Put(new Dictionary<string, AttributeValue> { [OperationVectors.SpecialText] = OperationVectors.SpecialText }),
                    Delete(new Dictionary<string, AttributeValue> { [OperationVectors.NaiveName] = ((char)0xE9).ToString() }),
                ],
            },
            ReturnItemCollectionMetrics = ReturnItemCollectionMetrics.Size,
        },
    };

    public static IEnumerable<string> RequestVectorNames() => OperationVectors.RequestNames("BatchWriteItem");

    public static IEnumerable<string> ResponseVectorNames() => OperationVectors.ResponseNames("BatchWriteItem");

    [Test]
    public async Task Name_Always_IsBatchWriteItem()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = BatchWriteItemOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("BatchWriteItem");
    }

    [Test]
    public async Task Name_Always_MatchesTheTargetBotocoreSends()
    {
        // Arrange
        var vector = OperationVectors.Request("BatchWriteItem", "Minimal");

        // Act
        var target = WireProtocol.TargetPrefix + BatchWriteItemOperation.Name;

        // Assert
        await Assert.That(target).IsEqualTo(vector.Target);
    }

    [Test]
    public async Task GetTableName_OneTable_ReturnsItsName()
    {
        // Arrange
        var request = BotocoreRequests["PutAndDeleteInOneTable"];

        // Act
        var tableName = BatchWriteItemOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsEqualTo("Users");
    }

    [Test]
    public async Task GetTableName_SeveralTables_ReturnsNull()
    {
        // Arrange
        var request = BotocoreRequests["MultipleTablesAllOptions"];

        // Act
        var tableName = BatchWriteItemOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsNull();
    }

    [Test]
    public async Task GetTableName_NoTable_ReturnsNull()
    {
        // Arrange
        var request = new BatchWriteItemRequest { RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>() };

        // Act
        var tableName = BatchWriteItemOperation.GetTableName(request);

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
        var vector = OperationVectors.Request("BatchWriteItem", name);
        var request = BotocoreRequests[name];

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Body, json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task WriteRequest_RequiredMembersOnly_WritesTheWritesOfTheTable()
    {
        // Arrange
        var request = Request();

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"RequestItems":{"Users":[{"PutRequest":{"Item":{"pk":{"S":"user-1"}}}}]}}""");
    }

    [Test]
    public async Task WriteRequest_EverythingSet_WritesInRequestOrder()
    {
        // Arrange
        var request = BotocoreRequests["MultipleTablesAllOptions"];

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"RequestItems":{"Orders":[{"PutRequest":{"Item":{"pk":{"S":"CUSTOMER#1"},"sk":{"S":"ORDER#1"}}}},{"DeleteRequest":{"Key":{"pk":{"S":"CUSTOMER#1"},"sk":{"S":"ORDER#0"}}}}],"Customers":[{"PutRequest":{"Item":{"pk":{"S":"CUSTOMER#1"}}}}]},"ReturnConsumedCapacity":"INDEXES","ReturnItemCollectionMetrics":"SIZE"}""");
    }

    [Test]
    public async Task WriteRequest_WriteWithNeitherPutNorDelete_WritesAnEmptyObjectAndLeavesTheRejectionToDynamoDb()
    {
        // Arrange
        var request = Request(new WriteRequest());

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"RequestItems":{"Users":[{}]}}""");
    }

    [Test]
    public async Task WriteRequest_WriteWithPutAndDelete_WritesBothAndLeavesTheRejectionToDynamoDb()
    {
        // Arrange
        var write = new WriteRequest
        {
            PutRequest = new PutRequest { Item = new Dictionary<string, AttributeValue> { ["pk"] = "a" } },
            DeleteRequest = new DeleteRequest { Key = new Dictionary<string, AttributeValue> { ["pk"] = "b" } },
        };

        // Act
        var json = Write(Request(write));

        // Assert
        await Assert.That(json).IsEqualTo("""{"RequestItems":{"Users":[{"PutRequest":{"Item":{"pk":{"S":"a"}}},"DeleteRequest":{"Key":{"pk":{"S":"b"}}}}]}}""");
    }

    [Test]
    public async Task WriteRequest_NoWritesForATable_WritesAnEmptyArrayAndLeavesTheRejectionToDynamoDb()
    {
        // Arrange
        var request = new BatchWriteItemRequest { RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>> { ["Users"] = [] } };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"RequestItems":{"Users":[]}}""");
    }

    [Test]
    public async Task WriteRequest_UnknownReturnItemCollectionMetrics_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = Request() with { ReturnItemCollectionMetrics = (ReturnItemCollectionMetrics)99 };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known ReturnItemCollectionMetrics", StringComparison.Ordinal);
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
    public async Task WriteRequest_UnprocessedItemsOfAResponse_AreWrittenAsTheyWereRead()
    {
        // Arrange (DynamoDB documents UnprocessedItems as the RequestItems of the next call)
        const string unprocessed = """{"Orders":[{"PutRequest":{"Item":{"pk":{"S":"a"},"n":{"N":"1"}}}},{"DeleteRequest":{"Key":{"pk":{"S":"b"}}}}],"Users":[{"DeleteRequest":{"Key":{"pk":{"B":"AP8="}}}}]}""";
        var response = Read("{\"UnprocessedItems\":" + unprocessed + "}");

        // Act
        var json = Write(new BatchWriteItemRequest { RequestItems = response.UnprocessedItems });

        // Assert
        await Assert.That(JsonComparer.Differences("{\"RequestItems\":" + unprocessed + "}", json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_EveryMember_MapsEachToItsProperty()
    {
        // Arrange (every value differs, so a swapped property cannot go unnoticed)
        const string json = """
            {"UnprocessedItems":{"Forum":[{"PutRequest":{"Item":{"Name":{"S":"a"}}}},{"DeleteRequest":{"Key":{"Name":{"S":"b"}}}}]},
             "ItemCollectionMetrics":{"Forum":[{"ItemCollectionKey":{"Name":{"S":"c"}},"SizeEstimateRangeGB":[0.5,1.5]}]},
             "ConsumedCapacity":[{"TableName":"Forum","CapacityUnits":3.5}]}
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
        await Assert.That((response.UnprocessedItems.Count, response.ItemCollectionMetrics.Count, response.ConsumedCapacity.Count)).IsEqualTo((0, 0, 0));
    }

    [Test]
    [MethodDataSource(nameof(ResponseVectorNames))]
    public async Task ReadResponse_BotocoreVector_ReturnsWhatBotocoreParsed(string name)
    {
        // Arrange
        var vector = OperationVectors.Response("BatchWriteItem", name);

        // Act
        var response = Read(vector.Wire);

        // Assert
        var expected = ResponseJson.WithDefaults(vector.Parsed, ("UnprocessedItems", new JsonObject()), ("ItemCollectionMetrics", new JsonObject()), ("ConsumedCapacity", new JsonArray()));
        await Assert.That(JsonComparer.Differences(expected, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_UnprocessedWrites_AreNotAnArrayOrListThatTheCallerCouldChange()
    {
        // Arrange
        const string json = """{"UnprocessedItems":{"Forum":[{"DeleteRequest":{"Key":{"Name":{"S":"a"}}}}]},"ItemCollectionMetrics":{"Forum":[{}]}}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.UnprocessedItems["Forum"] is Array or List<WriteRequest> || response.ItemCollectionMetrics["Forum"] is Array or List<ItemCollectionMetrics>).IsFalse();
    }

    [Test]
    public async Task ReadResponse_PutRequestWithoutItem_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"UnprocessedItems":{"Forum":[{"PutRequest":{}}]}}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessageContaining("PutRequest has no Item", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_DeleteRequestWithoutKey_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"UnprocessedItems":{"Forum":[{"DeleteRequest":{"Unknown":1}}]}}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessageContaining("DeleteRequest has no Key", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("""{"UnprocessedItems":[]}""")]
    [Arguments("""{"UnprocessedItems":{"Forum":{}}}""")]
    [Arguments("""{"UnprocessedItems":{"Forum":["PutRequest"]}}""")]
    [Arguments("""{"UnprocessedItems":{"Forum":[{"PutRequest":[]}]}}""")]
    [Arguments("""{"ItemCollectionMetrics":[]}""")]
    [Arguments("""{"ItemCollectionMetrics":{"Forum":{}}}""")]
    [Arguments("""{"ItemCollectionMetrics":{"Forum":[1]}}""")]
    [Arguments("""{"ConsumedCapacity":[1]}""")]
    [Arguments("""{"ConsumedCapacity":[{"TableName":"Forum"}""")]
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
        var request = Request();
        using var activity = new Activity("test");

        // Act
        BatchWriteItemOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(Tags(activity)).IsEqualTo("aws.dynamodb.table_names=Users");
    }

    [Test]
    public async Task SetActivityTags_SeveralTables_SetsTableNamesSortedOrdinally()
    {
        // Arrange
        var request = new BatchWriteItemRequest
        {
            RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>
            {
                ["users"] = [],
                ["Orders"] = [],
                ["apple"] = [],
            },
        };
        using var activity = new Activity("test");

        // Act
        BatchWriteItemOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(Tags(activity)).IsEqualTo("aws.dynamodb.table_names=Orders,apple,users");
    }

    private static WriteRequest ForumPut(string name) =>
        Put(new Dictionary<string, AttributeValue> { ["Name"] = name, ["Category"] = "Amazon Web Services" });

    private static WriteRequest Put(Dictionary<string, AttributeValue> item) => new() { PutRequest = new PutRequest { Item = item } };

    private static WriteRequest Delete(Dictionary<string, AttributeValue> key) => new() { DeleteRequest = new DeleteRequest { Key = key } };

    private static WriteRequest Delete(string pk) => Delete(new Dictionary<string, AttributeValue> { ["pk"] = pk });

    private static BatchWriteItemRequest Request(WriteRequest? write = null) => new()
    {
        RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>
        {
            ["Users"] = [write ?? Put(new Dictionary<string, AttributeValue> { ["pk"] = "user-1" })],
        },
    };

    private static string Write(BatchWriteItemRequest request) => TestJson.Write(writer => BatchWriteItemOperation.WriteRequest(writer, request));

    private static BatchWriteItemResponse Read(string json) => TestJson.Read(json, static (ref reader) => BatchWriteItemOperation.ReadResponse(ref reader));
}
