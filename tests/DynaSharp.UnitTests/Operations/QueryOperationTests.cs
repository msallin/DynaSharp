using System.Diagnostics;
using System.Text.Json;
using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.UnitTests.Telemetry.ActivityCapture;

namespace DynaSharp.UnitTests.Operations;

public sealed class QueryOperationTests
{
    private const string PkCondition = "pk = :pk";

    private static readonly Dictionary<string, AttributeValue> PkValue = new() { [":pk"] = "user-1" };

    // The counterparts of the parameters in generate_operation_vectors.py, by vector name.
    private static readonly Dictionary<string, QueryRequest> BotocoreRequests = new()
    {
        ["Minimal"] = Request(),
        ["RangeOfItemsFromApiReference"] = new()
        {
            TableName = "Reply",
            IndexName = "PostedBy-Index",
            Limit = 3,
            ConsistentRead = true,
            ProjectionExpression = "Id, PostedBy, ReplyDateTime",
            KeyConditionExpression = "Id = :v1 AND PostedBy BETWEEN :v2a AND :v2b",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":v1"] = "Amazon DynamoDB#DynamoDB Thread 1",
                [":v2a"] = "User A",
                [":v2b"] = "User C",
            },
            ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
        },
        ["CountItemsFromApiReference"] = new()
        {
            TableName = "Thread",
            ConsistentRead = true,
            KeyConditionExpression = "ForumName = :val",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":val"] = "Amazon DynamoDB" },
        },
        ["AllOptions"] = new()
        {
            TableName = "Orders",
            IndexName = "ByStatus",
            Select = SelectKind.SpecificAttributes,
            Limit = 25,
            ConsistentRead = true,
            ScanIndexForward = false,
            ExclusiveStartKey = new Dictionary<string, AttributeValue> { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#9", ["status"] = "OPEN" },
            ReturnConsumedCapacity = ReturnConsumedCapacity.Indexes,
            ProjectionExpression = "#s, total, address.city",
            FilterExpression = "total > :min AND #s <> :cancelled",
            KeyConditionExpression = "#s = :status AND begins_with(sk, :prefix)",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#s"] = "status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":status"] = "OPEN",
                [":prefix"] = "ORDER#",
                [":min"] = AttributeValue.FromNumber("100"),
                [":cancelled"] = "CANCELLED",
            },
        },
        ["ScanIndexForwardTrue"] = Request() with { ScanIndexForward = true },
        ["ScanIndexForwardFalseWithLimitOne"] = Request() with { ScanIndexForward = false, Limit = 1 },
        ["LimitIntMax"] = Request() with { Limit = int.MaxValue },
        ["ExclusiveStartKeyWithBinaryAndNumber"] = new()
        {
            TableName = "Blobs",
            KeyConditionExpression = PkCondition,
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":pk"] = AttributeValue.FromBinary([0, 255, (byte)'k', (byte)'e', (byte)'y']) },
            ExclusiveStartKey = new Dictionary<string, AttributeValue>
            {
                ["pk"] = AttributeValue.FromBinary([0, 255, (byte)'k', (byte)'e', (byte)'y']),
                ["sk"] = AttributeValue.FromNumber("-42.5"),
            },
        },
        ["EmptyExclusiveStartKey"] = Request() with { ExclusiveStartKey = new Dictionary<string, AttributeValue>() },
        ["SpecialCharacters"] = new()
        {
            TableName = "Users",
            KeyConditionExpression = "#n = :v",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#n"] = OperationVectors.SpecialText },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":v"] = OperationVectors.SpecialText },
            FilterExpression = "contains(#n, :v)",
        },
        ["SelectAllAttributes"] = Request() with { Select = SelectKind.AllAttributes },
        ["SelectAllProjectedAttributes"] = Request() with { Select = SelectKind.AllProjectedAttributes },
        ["SelectSpecificAttributes"] = Request() with { Select = SelectKind.SpecificAttributes },
        ["SelectCount"] = Request() with { Select = SelectKind.Count },
    };

    public static IEnumerable<string> RequestVectorNames() => OperationVectors.RequestNames("Query");

    public static IEnumerable<string> ResponseVectorNames() => OperationVectors.ResponseNames("Query");

    public static IEnumerable<(SelectKind Value, string Wire)> SelectNames()
    {
        yield return (SelectKind.AllAttributes, "ALL_ATTRIBUTES");
        yield return (SelectKind.AllProjectedAttributes, "ALL_PROJECTED_ATTRIBUTES");
        yield return (SelectKind.SpecificAttributes, "SPECIFIC_ATTRIBUTES");
        yield return (SelectKind.Count, "COUNT");
    }

    public static IEnumerable<int> PassedThroughLimits()
    {
        yield return int.MinValue;
        yield return -1;
        yield return 0;
        yield return 1;
        yield return 100;
        yield return int.MaxValue;
    }

    [Test]
    public async Task Name_Always_IsQuery()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = QueryOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("Query");
    }

    [Test]
    public async Task Name_Always_MatchesTheTargetBotocoreSends()
    {
        // Arrange
        var vector = OperationVectors.Request("Query", "Minimal");

        // Act
        var target = WireProtocol.TargetPrefix + QueryOperation.Name;

        // Assert
        await Assert.That(target).IsEqualTo(vector.Target);
    }

    [Test]
    public async Task GetTableName_Request_ReturnsTheTableName()
    {
        // Arrange
        var request = Request();

        // Act
        var tableName = QueryOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsEqualTo("Users");
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
        var vector = OperationVectors.Request("Query", name);
        var request = BotocoreRequests[name];

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Body, json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task WriteRequest_RequiredMembersOnly_WritesTableAndKeyCondition()
    {
        // Arrange
        var request = Request();

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users","KeyConditionExpression":"pk = :pk","ExpressionAttributeValues":{":pk":{"S":"user-1"}}}""");
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
            """{"TableName":"Orders","IndexName":"ByStatus","Select":"SPECIFIC_ATTRIBUTES","Limit":25,"ConsistentRead":true,"ScanIndexForward":false,"ExclusiveStartKey":{"pk":{"S":"CUSTOMER#1"},"sk":{"S":"ORDER#9"},"status":{"S":"OPEN"}},"ReturnConsumedCapacity":"INDEXES","ProjectionExpression":"#s, total, address.city","FilterExpression":"total > :min AND #s <> :cancelled","KeyConditionExpression":"#s = :status AND begins_with(sk, :prefix)","ExpressionAttributeNames":{"#s":"status"},"ExpressionAttributeValues":{":status":{"S":"OPEN"},":prefix":{"S":"ORDER#"},":min":{"N":"100"},":cancelled":{"S":"CANCELLED"}}}""");
    }

    [Test]
    [MethodDataSource(nameof(SelectNames))]
    public async Task WriteRequest_Select_WritesTheWireName((SelectKind Value, string Wire) select)
    {
        // Arrange
        var request = Request() with { Select = select.Value };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).Contains($$""","Select":"{{select.Wire}}",""", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_SelectDefault_OmitsSelect()
    {
        // Arrange
        var request = Request() with { Select = SelectKind.Unspecified };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).DoesNotContain("Select", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_UnknownSelect_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = Request() with { Select = (SelectKind)99 };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known SelectKind.", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(PassedThroughLimits))]
    public async Task WriteRequest_Limit_IsPassedThroughForDynamoDbToValidate(int limit)
    {
        // Arrange
        var request = Request() with { Limit = limit };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).Contains($$""","Limit":{{limit}},""", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_NoLimit_OmitsIt()
    {
        // Arrange
        var request = Request() with { Limit = null };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).DoesNotContain("Limit", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_ConsistentRead_WritesTrue()
    {
        // Arrange
        var request = Request() with { ConsistentRead = true };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).Contains(""","ConsistentRead":true,""", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_EventuallyConsistentRead_OmitsConsistentRead()
    {
        // Arrange
        var request = Request() with { ConsistentRead = false };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).DoesNotContain("ConsistentRead", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(true, "true")]
    [Arguments(false, "false")]
    public async Task WriteRequest_ScanIndexForward_WritesTheValue(bool scanIndexForward, string wire)
    {
        // Arrange
        var request = Request() with { ScanIndexForward = scanIndexForward };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).Contains($$""","ScanIndexForward":{{wire}},""", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_ScanIndexForwardUnset_OmitsIt()
    {
        // Arrange
        var request = Request() with { ScanIndexForward = null };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).DoesNotContain("ScanIndexForward", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_ExclusiveStartKey_WritesTheKey()
    {
        // Arrange
        var request = Request() with { ExclusiveStartKey = new Dictionary<string, AttributeValue> { ["pk"] = "user-1", ["sk"] = AttributeValue.FromNumber(5) } };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).Contains(""","ExclusiveStartKey":{"pk":{"S":"user-1"},"sk":{"N":"5"}},""", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_EmptyExclusiveStartKey_WritesItSoDynamoDbRejectsIt()
    {
        // Arrange (silently starting over would turn a bug into an endless loop)
        var request = Request() with { ExclusiveStartKey = new Dictionary<string, AttributeValue>() };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).Contains(""","ExclusiveStartKey":{},""", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_IndexProjectionAndFilter_WritesThem()
    {
        // Arrange
        var request = Request() with { IndexName = "ByEmail", ProjectionExpression = "#n", FilterExpression = "age > :age" };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"TableName":"Users","IndexName":"ByEmail","ProjectionExpression":"#n","FilterExpression":"age > :age","KeyConditionExpression":"pk = :pk","ExpressionAttributeValues":{":pk":{"S":"user-1"}}}""");
    }

    [Test]
    public async Task WriteRequest_EmptyExpressionMaps_OmitsThem()
    {
        // Arrange
        var request = new QueryRequest
        {
            TableName = "Users",
            KeyConditionExpression = PkCondition,
            ExpressionAttributeNames = new Dictionary<string, string>(),
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>(),
        };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users","KeyConditionExpression":"pk = :pk"}""");
    }

    [Test]
    public async Task WriteRequest_ReturnConsumedCapacityTotal_WritesTotal()
    {
        // Arrange
        var request = Request() with { ReturnConsumedCapacity = ReturnConsumedCapacity.Total };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).Contains(""","ReturnConsumedCapacity":"TOTAL",""", StringComparison.Ordinal);
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
        // Arrange (every number differs, so a swapped property cannot go unnoticed)
        const string json = """
            {"Items":[{"pk":{"S":"a"}},{"pk":{"S":"b"}}],"Count":2,"ScannedCount":7,
             "LastEvaluatedKey":{"pk":{"S":"b"}},"ConsumedCapacity":{"TableName":"Users","CapacityUnits":3.5}}
            """;

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(JsonComparer.Differences(json, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_EmptyObject_IsAnEmptyLastPage()
    {
        // Arrange
        const string json = "{}";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That((response.Items.Count, response.Count, response.ScannedCount, response.LastEvaluatedKey, response.ConsumedCapacity))
            .IsEqualTo((0, 0, 0, (IReadOnlyDictionary<string, AttributeValue>?)null, (ConsumedCapacity?)null));
    }

    [Test]
    [MethodDataSource(nameof(ResponseVectorNames))]
    public async Task ReadResponse_BotocoreVector_ReturnsWhatBotocoreParsed(string name)
    {
        // Arrange
        var vector = OperationVectors.Response("Query", name);

        // Act
        var response = Read(vector.Wire);

        // Assert
        await Assert.That(JsonComparer.Differences(ResponseJson.WithEmptyItemsIfAbsent(vector.Parsed), ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_NotAnObject_ThrowsJsonException()
    {
        // Arrange
        const string json = "[]";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>();
    }

    [Test]
    public async Task SetActivityTags_NoOptionalMembers_SetsOnlyConsistentRead()
    {
        // Arrange
        var request = Request();
        using var activity = new Activity("test");

        // Act
        QueryOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(Tags(activity)).IsEqualTo("aws.dynamodb.consistent_read=False");
    }

    [Test]
    public async Task SetActivityTags_EveryMember_SetsEveryTag()
    {
        // Arrange
        var request = Request() with
        {
            ConsistentRead = true,
            ProjectionExpression = "#n",
            IndexName = "ByStatus",
            Limit = 25,
            Select = SelectKind.Count,
            ScanIndexForward = false,
        };
        using var activity = new Activity("test");

        // Act
        QueryOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(Tags(activity)).IsEqualTo(
            "aws.dynamodb.consistent_read=True,aws.dynamodb.projection=#n,aws.dynamodb.index_name=ByStatus,aws.dynamodb.limit=25,aws.dynamodb.select=COUNT,aws.dynamodb.scan_forward=False");
    }

    [Test]
    public async Task SetActivityTags_ScanIndexForwardTrue_SetsTheTagToTrue()
    {
        // Arrange
        var request = Request() with { ScanIndexForward = true };
        using var activity = new Activity("test");

        // Act
        QueryOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That((bool)activity.GetTagItem("aws.dynamodb.scan_forward")!).IsTrue();
    }

    [Test]
    public async Task SetActivityTags_UnknownSelect_SkipsTheTagInsteadOfThrowing()
    {
        // Arrange (the span starts before the request is serialized, so tagging must not be where an invalid value surfaces)
        var request = Request() with { Select = (SelectKind)99 };
        using var activity = new Activity("test");

        // Act
        QueryOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(activity.GetTagItem("aws.dynamodb.select")).IsNull();
    }

    private static QueryRequest Request() => new()
    {
        TableName = "Users",
        KeyConditionExpression = PkCondition,
        ExpressionAttributeValues = PkValue,
    };

    private static string Write(QueryRequest request) => TestJson.Write(writer => QueryOperation.WriteRequest(writer, request));

    private static QueryResponse Read(string json) => TestJson.Read(json, static (ref reader) => QueryOperation.ReadResponse(ref reader));
}
