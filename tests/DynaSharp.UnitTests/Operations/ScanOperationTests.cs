using System.Diagnostics;
using System.Text.Json;
using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.UnitTests.Telemetry.ActivityCapture;

namespace DynaSharp.UnitTests.Operations;

public sealed class ScanOperationTests
{
    // The counterparts of the parameters in generate_operation_vectors.py, by vector name.
    private static readonly Dictionary<string, ScanRequest> BotocoreRequests = new()
    {
        ["Minimal"] = Request(),
        ["ReturnAllItemsFromApiReference"] = new() { TableName = "Reply", ReturnConsumedCapacity = ReturnConsumedCapacity.Total },
        ["FilterExpressionFromApiReference"] = new()
        {
            TableName = "Reply",
            FilterExpression = "PostedBy = :val",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":val"] = "joe@example.com" },
            ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
        },
        ["AllOptions"] = new()
        {
            TableName = "Orders",
            IndexName = "ByStatus",
            Select = Select.AllProjectedAttributes,
            Limit = 500,
            ConsistentRead = true,
            ExclusiveStartKey = new Dictionary<string, AttributeValue> { ["pk"] = "CUSTOMER#7", ["sk"] = "ORDER#2" },
            TotalSegments = 8,
            Segment = 5,
            ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
            ProjectionExpression = "#s, total",
            FilterExpression = "total > :min",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#s"] = "status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":min"] = AttributeValue.FromNumber("100") },
        },
        ["ParallelSegment"] = Request() with { Segment = 3, TotalSegments = 4 },
        ["ParallelSegmentWithStartKey"] = Request() with
        {
            Segment = 3,
            TotalSegments = 4,
            Limit = 100,
            ExclusiveStartKey = new Dictionary<string, AttributeValue> { ["pk"] = "user-77" },
        },
        ["SegmentZeroOfOne"] = Request() with { Segment = 0, TotalSegments = 1 },
        ["SegmentBounds"] = Request() with { Segment = 999_999, TotalSegments = 1_000_000 },
        ["LimitIntMax"] = Request() with { Limit = int.MaxValue },
        ["SecondaryIndex"] = new() { TableName = "Orders", IndexName = "ByStatus", ConsistentRead = true },
        ["SelectAllAttributes"] = Request() with { Select = Select.AllAttributes },
        ["SelectAllProjectedAttributes"] = Request() with { Select = Select.AllProjectedAttributes },
        ["SelectSpecificAttributes"] = Request() with { Select = Select.SpecificAttributes },
        ["SelectCount"] = Request() with { Select = Select.Count },
    };

    public static IEnumerable<string> RequestVectorNames() => OperationVectors.RequestNames("Scan");

    public static IEnumerable<string> ResponseVectorNames() => OperationVectors.ResponseNames("Scan");

    public static IEnumerable<int> PassedThroughLimits()
    {
        yield return int.MinValue;
        yield return 0;
        yield return 1;
        yield return int.MaxValue;
    }

    public static IEnumerable<(int Segment, int TotalSegments)> PassedThroughSegments()
    {
        yield return (0, 1);
        yield return (0, 4);
        yield return (3, 4);
        yield return (999_999, 1_000_000);

        // Invalid combinations are DynamoDB's to reject.
        yield return (-1, 0);
        yield return (4, 4);
        yield return (int.MaxValue, int.MaxValue);
        yield return (int.MinValue, int.MinValue);
    }

    [Test]
    public async Task Name_Always_IsScan()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = ScanOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("Scan");
    }

    [Test]
    public async Task Name_Always_MatchesTheTargetBotocoreSends()
    {
        // Arrange
        var vector = OperationVectors.Request("Scan", "Minimal");

        // Act
        var target = WireProtocol.TargetPrefix + ScanOperation.Name;

        // Assert
        await Assert.That(target).IsEqualTo(vector.Target);
    }

    [Test]
    public async Task GetTableName_Request_ReturnsTheTableName()
    {
        // Arrange
        var request = Request();

        // Act
        var tableName = ScanOperation.GetTableName(request);

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
        var vector = OperationVectors.Request("Scan", name);
        var request = BotocoreRequests[name];

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Body, json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task WriteRequest_TableOnly_WritesOnlyTheTable()
    {
        // Arrange
        var request = Request();

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users"}""");
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
            """{"TableName":"Orders","IndexName":"ByStatus","Select":"ALL_PROJECTED_ATTRIBUTES","Limit":500,"ConsistentRead":true,"ExclusiveStartKey":{"pk":{"S":"CUSTOMER#7"},"sk":{"S":"ORDER#2"}},"TotalSegments":8,"Segment":5,"ReturnConsumedCapacity":"TOTAL","ProjectionExpression":"#s, total","FilterExpression":"total > :min","ExpressionAttributeNames":{"#s":"status"},"ExpressionAttributeValues":{":min":{"N":"100"}}}""");
    }

    [Test]
    [Arguments(Select.AllAttributes, "ALL_ATTRIBUTES")]
    [Arguments(Select.AllProjectedAttributes, "ALL_PROJECTED_ATTRIBUTES")]
    [Arguments(Select.SpecificAttributes, "SPECIFIC_ATTRIBUTES")]
    [Arguments(Select.Count, "COUNT")]
    public async Task WriteRequest_Select_WritesTheWireName(Select select, string wire)
    {
        // Arrange
        var request = Request() with { Select = select };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo($$"""{"TableName":"Users","Select":"{{wire}}"}""");
    }

    [Test]
    public async Task WriteRequest_UnknownSelect_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = Request() with { Select = (Select)99 };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known Select.", StringComparison.Ordinal);
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
        await Assert.That(json).IsEqualTo($$"""{"TableName":"Users","Limit":{{limit}}}""");
    }

    [Test]
    [MethodDataSource(nameof(PassedThroughSegments))]
    public async Task WriteRequest_SegmentAndTotalSegments_ArePassedThroughForDynamoDbToValidate((int Segment, int TotalSegments) segments)
    {
        // Arrange
        var request = Request() with { Segment = segments.Segment, TotalSegments = segments.TotalSegments };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo($$"""{"TableName":"Users","TotalSegments":{{segments.TotalSegments}},"Segment":{{segments.Segment}}}""");
    }

    [Test]
    public async Task WriteRequest_OnlySegment_WritesOnlySegment()
    {
        // Arrange (the pairing is DynamoDB's to validate)
        var request = Request() with { Segment = 2 };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users","Segment":2}""");
    }

    [Test]
    public async Task WriteRequest_OnlyTotalSegments_WritesOnlyTotalSegments()
    {
        // Arrange
        var request = Request() with { TotalSegments = 4 };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users","TotalSegments":4}""");
    }

    [Test]
    public async Task WriteRequest_ConsistentRead_WritesTrue()
    {
        // Arrange
        var request = Request() with { ConsistentRead = true };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users","ConsistentRead":true}""");
    }

    [Test]
    public async Task WriteRequest_ExclusiveStartKey_WritesTheKey()
    {
        // Arrange
        var request = Request() with { ExclusiveStartKey = new Dictionary<string, AttributeValue> { ["pk"] = "user-1" } };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users","ExclusiveStartKey":{"pk":{"S":"user-1"}}}""");
    }

    [Test]
    public async Task WriteRequest_EmptyExclusiveStartKey_WritesItSoDynamoDbRejectsIt()
    {
        // Arrange (silently starting over would turn a bug into an endless loop)
        var request = Request() with { ExclusiveStartKey = new Dictionary<string, AttributeValue>() };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users","ExclusiveStartKey":{}}""");
    }

    [Test]
    public async Task WriteRequest_EmptyExpressionMaps_OmitsThem()
    {
        // Arrange
        var request = Request() with
        {
            ExpressionAttributeNames = new Dictionary<string, string>(),
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>(),
        };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users"}""");
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
            {"Items":[{"pk":{"S":"a"}},{"pk":{"S":"b"}}],"Count":2,"ScannedCount":9,
             "LastEvaluatedKey":{"pk":{"S":"b"}},"ConsumedCapacity":{"TableName":"Users","CapacityUnits":4.5}}
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
        var vector = OperationVectors.Response("Scan", name);

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
        ScanOperation.SetActivityTags(activity, request);

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
            Limit = 500,
            Select = Select.SpecificAttributes,
            Segment = 0,
            TotalSegments = 4,
        };
        using var activity = new Activity("test");

        // Act
        ScanOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(Tags(activity)).IsEqualTo(
            "aws.dynamodb.consistent_read=True,aws.dynamodb.projection=#n,aws.dynamodb.index_name=ByStatus,aws.dynamodb.limit=500,aws.dynamodb.select=SPECIFIC_ATTRIBUTES,aws.dynamodb.segment=0,aws.dynamodb.total_segments=4");
    }

    [Test]
    public async Task SetActivityTags_OnlySegment_SetsNoTotalSegmentsTag()
    {
        // Arrange
        var request = Request() with { Segment = 2 };
        using var activity = new Activity("test");

        // Act
        ScanOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(activity.GetTagItem("aws.dynamodb.total_segments")).IsNull();
    }

    [Test]
    public async Task SetActivityTags_OnlyTotalSegments_SetsNoSegmentTag()
    {
        // Arrange
        var request = Request() with { TotalSegments = 4 };
        using var activity = new Activity("test");

        // Act
        ScanOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(activity.GetTagItem("aws.dynamodb.segment")).IsNull();
    }

    private static ScanRequest Request() => new() { TableName = "Users" };

    private static string Write(ScanRequest request) => TestJson.Write(writer => ScanOperation.WriteRequest(writer, request));

    private static ScanResponse Read(string json) => TestJson.Read(json, static (ref reader) => ScanOperation.ReadResponse(ref reader));
}
