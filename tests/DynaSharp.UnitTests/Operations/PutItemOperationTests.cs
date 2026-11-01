using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

public sealed class PutItemOperationTests
{
    private static readonly Dictionary<string, AttributeValue> Item = new() { ["pk"] = "user-1", ["age"] = AttributeValue.FromNumber(42) };

    public static IEnumerable<(ReturnValue Value, string Wire)> ReturnValueNames()
    {
        yield return (ReturnValue.AllOld, "ALL_OLD");
        yield return (ReturnValue.UpdatedOld, "UPDATED_OLD");
        yield return (ReturnValue.AllNew, "ALL_NEW");
        yield return (ReturnValue.UpdatedNew, "UPDATED_NEW");
    }

    [Test]
    public async Task Name_Always_IsPutItem()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = PutItemOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("PutItem");
    }

    [Test]
    public async Task GetTableName_Request_ReturnsTheTableName()
    {
        // Arrange
        var request = new PutItemRequest { TableName = "Users", Item = Item };

        // Act
        var tableName = PutItemOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsEqualTo("Users");
    }

    [Test]
    public async Task WriteRequest_TableAndItem_WritesOnlyThose()
    {
        // Arrange
        var request = new PutItemRequest { TableName = "Users", Item = Item };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users","Item":{"pk":{"S":"user-1"},"age":{"N":"42"}}}""");
    }

    [Test]
    public async Task WriteRequest_ConditionWithNamesAndValues_WritesAllThree()
    {
        // Arrange
        var request = new PutItemRequest
        {
            TableName = "Users",
            Item = Item,
            ConditionExpression = "attribute_not_exists(#pk) OR version = :v",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#pk"] = "pk" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":v"] = AttributeValue.FromNumber(3) },
        };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"TableName":"Users","Item":{"pk":{"S":"user-1"},"age":{"N":"42"}},"ConditionExpression":"attribute_not_exists(#pk) OR version = :v","ExpressionAttributeNames":{"#pk":"pk"},"ExpressionAttributeValues":{":v":{"N":"3"}}}""");
    }

    [Test]
    public async Task WriteRequest_EmptyExpressionAttributeValues_OmitsThem()
    {
        // Arrange
        var request = new PutItemRequest { TableName = "Users", Item = Item, ExpressionAttributeValues = new Dictionary<string, AttributeValue>() };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users","Item":{"pk":{"S":"user-1"},"age":{"N":"42"}}}""");
    }

    [Test]
    [MethodDataSource(nameof(ReturnValueNames))]
    public async Task WriteRequest_ReturnValues_WritesTheWireName((ReturnValue Value, string Wire) returnValue)
    {
        // Arrange
        var request = new PutItemRequest { TableName = "Users", Item = Item, ReturnValues = returnValue.Value };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).EndsWith($$""","ReturnValues":"{{returnValue.Wire}}"}""");
    }

    [Test]
    public async Task WriteRequest_UnknownReturnValue_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = new PutItemRequest { TableName = "Users", Item = Item, ReturnValues = (ReturnValue)99 };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known ReturnValue.", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_ReturnValuesOnConditionCheckFailureAllOld_WritesAllOld()
    {
        // Arrange
        var request = new PutItemRequest { TableName = "Users", Item = Item, ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).EndsWith(""","ReturnValuesOnConditionCheckFailure":"ALL_OLD"}""");
    }

    [Test]
    public async Task WriteRequest_UnknownReturnValuesOnConditionCheckFailure_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = new PutItemRequest { TableName = "Users", Item = Item, ReturnValuesOnConditionCheckFailure = (ReturnValuesOnConditionCheckFailure)99 };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known ReturnValuesOnConditionCheckFailure", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_ReturnConsumedCapacityTotal_WritesTotal()
    {
        // Arrange
        var request = new PutItemRequest { TableName = "Users", Item = Item, ReturnConsumedCapacity = ReturnConsumedCapacity.Total };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).EndsWith(""","ReturnConsumedCapacity":"TOTAL"}""");
    }

    [Test]
    public async Task WriteRequest_ReturnItemCollectionMetricsSize_WritesSize()
    {
        // Arrange
        var request = new PutItemRequest { TableName = "Users", Item = Item, ReturnItemCollectionMetrics = ReturnItemCollectionMetrics.Size };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).EndsWith(""","ReturnItemCollectionMetrics":"SIZE"}""");
    }

    [Test]
    public async Task WriteRequest_UnknownReturnItemCollectionMetrics_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = new PutItemRequest { TableName = "Users", Item = Item, ReturnItemCollectionMetrics = (ReturnItemCollectionMetrics)99 };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known ReturnItemCollectionMetrics", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_BotocoreVectorForItemCollectionMetrics_WritesTheJsonThatBotocoreWrites()
    {
        // Arrange
        var vector = OperationVectors.Request("PutItem", "ReturnItemCollectionMetrics");
        var request = new PutItemRequest
        {
            TableName = "Orders",
            Item = new Dictionary<string, AttributeValue> { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#1" },
            ReturnItemCollectionMetrics = ReturnItemCollectionMetrics.Size,
        };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Body, json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task WriteRequest_AllOptions_WritesThemInRequestOrder()
    {
        // Arrange
        var request = new PutItemRequest
        {
            TableName = "Users",
            Item = new Dictionary<string, AttributeValue> { ["pk"] = "a" },
            ConditionExpression = "attribute_not_exists(pk)",
            ReturnValues = ReturnValue.AllOld,
            ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld,
            ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
        };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"TableName":"Users","Item":{"pk":{"S":"a"}},"ConditionExpression":"attribute_not_exists(pk)","ReturnValues":"ALL_OLD","ReturnValuesOnConditionCheckFailure":"ALL_OLD","ReturnConsumedCapacity":"TOTAL"}""");
    }

    [Test]
    public async Task ReadResponse_EmptyObject_HasNoAttributesAndNoCapacity()
    {
        // Arrange
        const string json = "{}";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response).IsEqualTo(new PutItemResponse());
    }

    [Test]
    public async Task ReadResponse_Attributes_ReturnsTheReplacedItem()
    {
        // Arrange
        const string json = """{"Attributes":{"pk":{"S":"user-1"},"age":{"N":"41"}}}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.Attributes!["age"].AsNumber<int>()).IsEqualTo(41);
    }

    [Test]
    public async Task ReadResponse_ConsumedCapacityAndUnknownProperties_ReturnsTheCapacity()
    {
        // Arrange
        const string json = """{"Future":{"ItemCollectionKey":{"pk":{"S":"a"}},"SizeEstimateRangeGB":[0.0,1.0]},"ConsumedCapacity":{"TableName":"Users","CapacityUnits":1.0}}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.ConsumedCapacity).IsEqualTo(new ConsumedCapacity { TableName = "Users", CapacityUnits = 1 });
    }

    [Test]
    public async Task ReadResponse_ItemCollectionMetrics_ReturnsKeyAndRange()
    {
        // Arrange
        const string json = """{"ItemCollectionMetrics":{"ItemCollectionKey":{"pk":{"S":"a"}},"SizeEstimateRangeGB":[0.0,1.0]}}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That((response.ItemCollectionMetrics!.ItemCollectionKey!["pk"].AsString(), response.ItemCollectionMetrics.SizeEstimateRangeGB![1])).IsEqualTo(("a", 1.0));
    }

    [Test]
    public async Task ReadResponse_BotocoreVectorForItemCollectionMetrics_ReturnsWhatBotocoreParsed()
    {
        // Arrange
        var vector = OperationVectors.Response("PutItem", "ItemCollectionMetrics");

        // Act
        var response = Read(vector.Wire);

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Parsed, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    private static string Write(PutItemRequest request) => TestJson.Write(writer => PutItemOperation.WriteRequest(writer, request));

    private static PutItemResponse Read(string json) => TestJson.Read(json, static (ref reader) => PutItemOperation.ReadResponse(ref reader));
}
