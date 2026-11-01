using System.Text.Json;
using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

public sealed class UpdateItemOperationTests
{
    private static readonly Dictionary<string, AttributeValue> Key = new() { ["pk"] = "user-1" };

    // The counterparts of the parameters in generate_operation_vectors.py, by vector name. The expected JSON is what botocore
    // wrote for the parameters there.
    private static readonly Dictionary<string, UpdateItemRequest> BotocoreRequests = new()
    {
        ["Minimal"] = new() { TableName = "Users", Key = Key },
        ["ConditionalUpdateFromApiReference"] = new()
        {
            TableName = "Thread",
            Key = new Dictionary<string, AttributeValue> { ["ForumName"] = "Amazon DynamoDB", ["Subject"] = "Maximum number of items?" },
            UpdateExpression = "set LastPostedBy = :val1",
            ConditionExpression = "LastPostedBy = :val2",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":val1"] = "alice@example.com", [":val2"] = "fred@example.com" },
            ReturnValues = ReturnValue.AllNew,
        },
        ["AtomicCounterFromApiReference"] = new()
        {
            TableName = "Thread",
            Key = new Dictionary<string, AttributeValue> { ["ForumName"] = "Amazon DynamoDB", ["Subject"] = "A question about updates" },
            UpdateExpression = "set Replies = Replies + :num",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":num"] = AttributeValue.FromNumber("1") },
        },
        ["AllOptions"] = new()
        {
            TableName = "Orders",
            Key = new Dictionary<string, AttributeValue> { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#1" },
            UpdateExpression = "SET #s = :new, visits = visits + :one REMOVE tempFlag ADD tags :tags DELETE oldTags :old",
            ConditionExpression = "#s = :expected AND version = :version",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#s"] = "status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":new"] = "SHIPPED",
                [":one"] = AttributeValue.FromNumber("1"),
                [":tags"] = AttributeValue.FromStringSet(["fragile"]),
                [":old"] = AttributeValue.FromStringSet(["draft"]),
                [":expected"] = "OPEN",
                [":version"] = AttributeValue.FromNumber("7"),
            },
            ReturnValues = ReturnValue.UpdatedNew,
            ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld,
            ReturnConsumedCapacity = ReturnConsumedCapacity.Indexes,
            ReturnItemCollectionMetrics = ReturnItemCollectionMetrics.Size,
        },
        ["AllAttributeValueTypes"] = new()
        {
            TableName = "Users",
            Key = Key,
            UpdateExpression = "SET a = :s",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":s"] = "text",
                [":n"] = AttributeValue.FromNumber("-12.5E+3"),
                [":b"] = AttributeValue.FromBinary([0, 1, 2, 253, 254, 255]),
                [":t"] = AttributeValue.FromBool(true),
                [":f"] = AttributeValue.FromBool(false),
                [":null"] = AttributeValue.Null,
                [":ss"] = AttributeValue.FromStringSet(["a", "b"]),
                [":ns"] = AttributeValue.FromNumberSet(["1", "2.5"]),
                [":bs"] = AttributeValue.FromBinarySet([[0, 1], [255]]),
                [":l"] = AttributeValue.FromList(["x", AttributeValue.FromNumber("1"), AttributeValue.FromList([]), AttributeValue.FromMap([]), AttributeValue.FromBool(false)]),
                [":m"] = AttributeValue.FromMap(
                [
                    KeyValuePair.Create("inner", AttributeValue.FromMap([KeyValuePair.Create("deep", AttributeValue.FromList([AttributeValue.FromNumber("1"), "two"]))])),
                    KeyValuePair.Create("empty", (AttributeValue)string.Empty),
                ]),
            },
        },
        ["SpecialCharacters"] = new()
        {
            TableName = "Users",
            Key = new Dictionary<string, AttributeValue> { [OperationVectors.SpecialText] = OperationVectors.SpecialText, [OperationVectors.NaiveName] = string.Empty },
            UpdateExpression = "SET #n = :v",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#n"] = OperationVectors.SpecialText },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":v"] = OperationVectors.SpecialText },
        },
        ["ReturnValuesAllOld"] = ReturnValuesRequest(ReturnValue.AllOld),
        ["ReturnValuesUpdatedOld"] = ReturnValuesRequest(ReturnValue.UpdatedOld),
        ["ReturnValuesAllNew"] = ReturnValuesRequest(ReturnValue.AllNew),
        ["ReturnValuesUpdatedNew"] = ReturnValuesRequest(ReturnValue.UpdatedNew),
    };

    public static IEnumerable<string> RequestVectorNames() => OperationVectors.RequestNames("UpdateItem");

    public static IEnumerable<string> ResponseVectorNames() => OperationVectors.ResponseNames("UpdateItem");

    public static IEnumerable<(ReturnValue Value, string Wire)> ReturnValueNames()
    {
        yield return (ReturnValue.AllOld, "ALL_OLD");
        yield return (ReturnValue.UpdatedOld, "UPDATED_OLD");
        yield return (ReturnValue.AllNew, "ALL_NEW");
        yield return (ReturnValue.UpdatedNew, "UPDATED_NEW");
    }

    [Test]
    public async Task Name_Always_IsUpdateItem()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = UpdateItemOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("UpdateItem");
    }

    [Test]
    public async Task Name_Always_MatchesTheTargetBotocoreSends()
    {
        // Arrange
        var vector = OperationVectors.Request("UpdateItem", "Minimal");

        // Act
        var target = WireProtocol.TargetPrefix + UpdateItemOperation.Name;

        // Assert
        await Assert.That(target).IsEqualTo(vector.Target);
    }

    [Test]
    public async Task GetTableName_Request_ReturnsTheTableName()
    {
        // Arrange
        var request = new UpdateItemRequest { TableName = "Users", Key = Key };

        // Act
        var tableName = UpdateItemOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsEqualTo("Users");
    }

    [Test]
    public async Task BotocoreRequests_Always_CoverEveryVector()
    {
        // Arrange (the vectors come from the generator, the requests from this class)

        // Act
        var missing = RequestVectorNames().Order(StringComparer.Ordinal).Except(BotocoreRequests.Keys).Concat(BotocoreRequests.Keys.Except(RequestVectorNames()));

        // Assert
        await Assert.That(missing).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(RequestVectorNames))]
    public async Task WriteRequest_BotocoreVector_WritesTheJsonThatBotocoreWrites(string name)
    {
        // Arrange
        var vector = OperationVectors.Request("UpdateItem", name);
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
        var request = new UpdateItemRequest { TableName = "Users", Key = Key };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users","Key":{"pk":{"S":"user-1"}}}""");
    }

    [Test]
    public async Task WriteRequest_UpdateAndCondition_WritesEverythingInRequestOrder()
    {
        // Arrange
        var request = new UpdateItemRequest
        {
            TableName = "Users",
            Key = Key,
            UpdateExpression = "SET #n = :name",
            ConditionExpression = "attribute_exists(pk)",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#n"] = "name" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":name"] = "Ada" },
        };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"TableName":"Users","Key":{"pk":{"S":"user-1"}},"UpdateExpression":"SET #n = :name","ConditionExpression":"attribute_exists(pk)","ExpressionAttributeNames":{"#n":"name"},"ExpressionAttributeValues":{":name":{"S":"Ada"}}}""");
    }

    [Test]
    public async Task WriteRequest_EmptyExpressionMaps_OmitsThem()
    {
        // Arrange
        var request = new UpdateItemRequest
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
    [MethodDataSource(nameof(ReturnValueNames))]
    public async Task WriteRequest_ReturnValues_WritesTheWireName((ReturnValue Value, string Wire) returnValue)
    {
        // Arrange
        var request = new UpdateItemRequest { TableName = "Users", Key = Key, ReturnValues = returnValue.Value };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).EndsWith($$""","ReturnValues":"{{returnValue.Wire}}"}""");
    }

    [Test]
    public async Task WriteRequest_ReturnValuesNone_OmitsIt()
    {
        // Arrange
        var request = new UpdateItemRequest { TableName = "Users", Key = Key, ReturnValues = ReturnValue.None };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).DoesNotContain("ReturnValues", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_UnknownReturnValue_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = new UpdateItemRequest { TableName = "Users", Key = Key, ReturnValues = (ReturnValue)99 };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known ReturnValue.", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_ReturnValuesOnConditionCheckFailureAllOld_WritesAllOld()
    {
        // Arrange
        var request = new UpdateItemRequest { TableName = "Users", Key = Key, ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).EndsWith(""","ReturnValuesOnConditionCheckFailure":"ALL_OLD"}""");
    }

    [Test]
    public async Task WriteRequest_UnknownReturnValuesOnConditionCheckFailure_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = new UpdateItemRequest { TableName = "Users", Key = Key, ReturnValuesOnConditionCheckFailure = (ReturnValuesOnConditionCheckFailure)99 };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known ReturnValuesOnConditionCheckFailure", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_ReturnConsumedCapacityIndexes_WritesIndexes()
    {
        // Arrange
        var request = new UpdateItemRequest { TableName = "Users", Key = Key, ReturnConsumedCapacity = ReturnConsumedCapacity.Indexes };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).EndsWith(""","ReturnConsumedCapacity":"INDEXES"}""");
    }

    [Test]
    public async Task WriteRequest_ReturnItemCollectionMetricsSize_WritesSize()
    {
        // Arrange
        var request = new UpdateItemRequest { TableName = "Users", Key = Key, ReturnItemCollectionMetrics = ReturnItemCollectionMetrics.Size };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).EndsWith(""","ReturnItemCollectionMetrics":"SIZE"}""");
    }

    [Test]
    public async Task WriteRequest_UnknownReturnItemCollectionMetrics_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = new UpdateItemRequest { TableName = "Users", Key = Key, ReturnItemCollectionMetrics = (ReturnItemCollectionMetrics)99 };

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
            {"Attributes":{"views":{"N":"6"}},
             "ConsumedCapacity":{"TableName":"Users","CapacityUnits":2.0},
             "ItemCollectionMetrics":{"ItemCollectionKey":{"pk":{"S":"user-1"}},"SizeEstimateRangeGB":[0.5,1.5]}}
            """;

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(JsonComparer.Differences(json, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_EmptyObject_HasNothing()
    {
        // Arrange
        const string json = "{}";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response).IsEqualTo(new UpdateItemResponse());
    }

    [Test]
    [MethodDataSource(nameof(ResponseVectorNames))]
    public async Task ReadResponse_BotocoreVector_ReturnsWhatBotocoreParsed(string name)
    {
        // Arrange
        var vector = OperationVectors.Response("UpdateItem", name);

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

    private static UpdateItemRequest ReturnValuesRequest(ReturnValue returnValue) => new()
    {
        TableName = "Users",
        Key = Key,
        UpdateExpression = "SET a = :v",
        ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":v"] = AttributeValue.FromNumber("1") },
        ReturnValues = returnValue,
    };

    private static string Write(UpdateItemRequest request) => TestJson.Write(writer => UpdateItemOperation.WriteRequest(writer, request));

    private static UpdateItemResponse Read(string json) => TestJson.Read(json, static (ref reader) => UpdateItemOperation.ReadResponse(ref reader));
}
