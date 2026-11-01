using System.Text.Json;
using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

public sealed class DeleteItemOperationTests
{
    private static readonly Dictionary<string, AttributeValue> Key = new() { ["pk"] = "user-1" };

    // The counterparts of the parameters in generate_operation_vectors.py, by vector name.
    private static readonly Dictionary<string, DeleteItemRequest> BotocoreRequests = new()
    {
        ["Minimal"] = new() { TableName = "Users", Key = Key },
        ["FromApiReference"] = new()
        {
            TableName = "Thread",
            Key = new Dictionary<string, AttributeValue> { ["ForumName"] = "Amazon DynamoDB", ["Subject"] = "How do I update multiple items?" },
            ConditionExpression = "attribute_not_exists(Replies)",
            ReturnValues = ReturnValue.AllOld,
        },
        ["AllOptions"] = new()
        {
            TableName = "Orders",
            Key = new Dictionary<string, AttributeValue> { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#1" },
            ConditionExpression = "#s = :expected AND version = :version",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#s"] = "status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":expected"] = "CANCELLED", [":version"] = AttributeValue.FromNumber("7") },
            ReturnValues = ReturnValue.AllOld,
            ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld,
            ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
            ReturnItemCollectionMetrics = ReturnItemCollectionMetrics.Size,
        },
        ["ConsumedCapacityIndexes"] = new() { TableName = "Users", Key = Key, ReturnConsumedCapacity = ReturnConsumedCapacity.Indexes },
    };

    public static IEnumerable<string> RequestVectorNames() => OperationVectors.RequestNames("DeleteItem");

    public static IEnumerable<string> ResponseVectorNames() => OperationVectors.ResponseNames("DeleteItem");

    [Test]
    public async Task Name_Always_IsDeleteItem()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = DeleteItemOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("DeleteItem");
    }

    [Test]
    public async Task Name_Always_MatchesTheTargetBotocoreSends()
    {
        // Arrange
        var vector = OperationVectors.Request("DeleteItem", "Minimal");

        // Act
        var target = WireProtocol.TargetPrefix + DeleteItemOperation.Name;

        // Assert
        await Assert.That(target).IsEqualTo(vector.Target);
    }

    [Test]
    public async Task GetTableName_Request_ReturnsTheTableName()
    {
        // Arrange
        var request = new DeleteItemRequest { TableName = "Users", Key = Key };

        // Act
        var tableName = DeleteItemOperation.GetTableName(request);

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
        var vector = OperationVectors.Request("DeleteItem", name);
        var request = BotocoreRequests[name];

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Body, json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task WriteRequest_TableAndKey_WritesOnlyThose()
    {
        // Arrange
        var request = new DeleteItemRequest { TableName = "Users", Key = Key };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users","Key":{"pk":{"S":"user-1"}}}""");
    }

    [Test]
    public async Task WriteRequest_ConditionWithNamesAndValues_WritesAllThreeInRequestOrder()
    {
        // Arrange
        var request = new DeleteItemRequest
        {
            TableName = "Users",
            Key = Key,
            ConditionExpression = "#v = :v",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#v"] = "version" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":v"] = AttributeValue.FromNumber(3) },
        };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"TableName":"Users","Key":{"pk":{"S":"user-1"}},"ConditionExpression":"#v = :v","ExpressionAttributeNames":{"#v":"version"},"ExpressionAttributeValues":{":v":{"N":"3"}}}""");
    }

    [Test]
    public async Task WriteRequest_EmptyExpressionMaps_OmitsThem()
    {
        // Arrange
        var request = new DeleteItemRequest
        {
            TableName = "Users",
            Key = Key,
            ExpressionAttributeNames = new Dictionary<string, string>(),
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>(),
        };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users","Key":{"pk":{"S":"user-1"}}}""");
    }

    [Test]
    public async Task WriteRequest_ReturnValuesAllOld_WritesAllOld()
    {
        // Arrange
        var request = new DeleteItemRequest { TableName = "Users", Key = Key, ReturnValues = ReturnValue.AllOld };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).EndsWith(""","ReturnValues":"ALL_OLD"}""");
    }

    [Test]
    public async Task WriteRequest_UnknownReturnValue_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = new DeleteItemRequest { TableName = "Users", Key = Key, ReturnValues = (ReturnValue)99 };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known ReturnValue.", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_ReturnValuesOnConditionCheckFailureAllOld_WritesAllOld()
    {
        // Arrange
        var request = new DeleteItemRequest { TableName = "Users", Key = Key, ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).EndsWith(""","ReturnValuesOnConditionCheckFailure":"ALL_OLD"}""");
    }

    [Test]
    public async Task WriteRequest_UnknownReturnValuesOnConditionCheckFailure_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = new DeleteItemRequest { TableName = "Users", Key = Key, ReturnValuesOnConditionCheckFailure = (ReturnValuesOnConditionCheckFailure)99 };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known ReturnValuesOnConditionCheckFailure", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_ReturnConsumedCapacityTotal_WritesTotal()
    {
        // Arrange
        var request = new DeleteItemRequest { TableName = "Users", Key = Key, ReturnConsumedCapacity = ReturnConsumedCapacity.Total };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).EndsWith(""","ReturnConsumedCapacity":"TOTAL"}""");
    }

    [Test]
    public async Task WriteRequest_ReturnItemCollectionMetricsSize_WritesSize()
    {
        // Arrange
        var request = new DeleteItemRequest { TableName = "Users", Key = Key, ReturnItemCollectionMetrics = ReturnItemCollectionMetrics.Size };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).EndsWith(""","ReturnItemCollectionMetrics":"SIZE"}""");
    }

    [Test]
    public async Task WriteRequest_UnknownReturnItemCollectionMetrics_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = new DeleteItemRequest { TableName = "Users", Key = Key, ReturnItemCollectionMetrics = (ReturnItemCollectionMetrics)99 };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known ReturnItemCollectionMetrics", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_EveryMember_MapsEachToItsProperty()
    {
        // Arrange
        const string json = """
            {"Attributes":{"pk":{"S":"user-1"},"views":{"N":"6"}},
             "ConsumedCapacity":{"TableName":"Users","CapacityUnits":2.0},
             "ItemCollectionMetrics":{"ItemCollectionKey":{"pk":{"S":"user-1"}},"SizeEstimateRangeGB":[0.5,1.5]}}
            """;

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(JsonComparer.Differences(json, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_NothingDeleted_HasNothing()
    {
        // Arrange (DynamoDB answers a delete of a missing item without attributes)
        const string json = "{}";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response).IsEqualTo(new DeleteItemResponse());
    }

    [Test]
    [MethodDataSource(nameof(ResponseVectorNames))]
    public async Task ReadResponse_BotocoreVector_ReturnsWhatBotocoreParsed(string name)
    {
        // Arrange
        var vector = OperationVectors.Response("DeleteItem", name);

        // Act
        var response = Read(vector.Wire);

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Parsed, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
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

    private static string Write(DeleteItemRequest request) => TestJson.Write(writer => DeleteItemOperation.WriteRequest(writer, request));

    private static DeleteItemResponse Read(string json) => TestJson.Read(json, static (ref reader) => DeleteItemOperation.ReadResponse(ref reader));
}
