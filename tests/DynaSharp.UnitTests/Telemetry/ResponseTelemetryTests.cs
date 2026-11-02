using System.Diagnostics;
using DynaSharp.Requests;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Telemetry.ActivityCapture;
using static DynaSharp.UnitTests.TestClient;

namespace DynaSharp.UnitTests.Telemetry;

public sealed class ResponseTelemetryTests
{
    private const string CapacityResponse = """{"ConsumedCapacity":{"TableName":"Users","CapacityUnits":2}}""";

    public static IEnumerable<Func<Func<DynamoDbClient, Task>>> OperationsThatReturnCapacity()
    {
        yield return () => client => client.GetItemAsync(TestClient.GetItem());
        yield return () => client => client.PutItemAsync(TestClient.PutItem());
        yield return () => client => client.UpdateItemAsync(new UpdateItemRequest { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" } });
        yield return () => client => client.DeleteItemAsync(new DeleteItemRequest { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" } });
        yield return () => client => client.QueryAsync(CreateQuery());
        yield return () => client => client.ScanAsync(new ScanRequest { TableName = "Users" });
    }

    [Test]
    [Arguments("aws.dynamodb.count", 2)]
    [Arguments("aws.dynamodb.scanned_count", 5)]
    public async Task QueryAsync_Response_SetsTheCountTags(string name, int expected)
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[],"Count":2,"ScannedCount":5}"""));

        // Act
        var activity = await RunAsync(handler, client => client.QueryAsync(CreateQuery()));

        // Assert
        await Assert.That(activity.GetTagItem(name)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("aws.dynamodb.count", 7)]
    [Arguments("aws.dynamodb.scanned_count", 9)]
    public async Task ScanAsync_Response_SetsTheCountTags(string name, int expected)
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[],"Count":7,"ScannedCount":9}"""));

        // Act
        var activity = await RunAsync(handler, client => client.ScanAsync(new ScanRequest { TableName = "Users" }));

        // Assert
        await Assert.That(activity.GetTagItem(name)).IsEqualTo(expected);
    }

    [Test]
    public async Task QueryAsync_SelectCountResponse_SetsTheCountOfMatches()
    {
        // Arrange (with SelectKind.Count there are no items; the count tag is the only trace of the result size)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Count":42,"ScannedCount":50}"""));
        var request = CreateQuery() with { Select = SelectKind.Count };

        // Act
        var activity = await RunAsync(handler, client => client.QueryAsync(request));

        // Assert
        await Assert.That(activity.GetTagItem("aws.dynamodb.count")).IsEqualTo(42);
    }

    [Test]
    public async Task GetItemAsync_Response_HasNoCountTags()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));

        // Act
        var activity = await RunAsync(handler, client => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(activity.GetTagItem("aws.dynamodb.count")).IsNull();
    }

    [Test]
    public async Task QueryAsync_PropagationOnlySampling_RecordsNoResponseTags()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Count":2,"ScannedCount":5,"ConsumedCapacity":{"CapacityUnits":1}}"""));

        // Act
        var activity = await RunAsync(handler, client => client.QueryAsync(CreateQuery()), ActivitySamplingResult.PropagationData);

        // Assert
        await Assert.That(activity.TagObjects).IsEmpty();
    }

    [Test]
    public async Task QueryAsync_ErrorResponse_HasNoCountTags()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "bad"));

        // Act
        var activity = await RunAsync(handler, client => TestClient.CaptureAsync<ValidationException>(() => client.QueryAsync(CreateQuery())));

        // Assert
        await Assert.That(activity.GetTagItem("aws.dynamodb.count")).IsNull();
    }

    [Test]
    [MethodDataSource(nameof(OperationsThatReturnCapacity))]
    public async Task Operation_ResponseWithConsumedCapacity_TagsOneJsonDocumentPerCapacity(Func<DynamoDbClient, Task> call)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(call);
        using var handler = new StubHandler(_ => StubResponse.Json(CapacityResponse));

        // Act
        var activity = await RunAsync(handler, call);

        // Assert
        await Assert.That(string.Join('|', (string[])activity.GetTagItem("aws.dynamodb.consumed_capacity")!)).IsEqualTo("""{"CapacityUnits":2,"TableName":"Users"}""");
    }

    [Test]
    public async Task QueryAsync_ConsumedCapacityPerIndex_TagsTheShapeOfTheResponse()
    {
        // Arrange
        const string response = """
            {"Count":0,"ScannedCount":0,"ConsumedCapacity":{"TableName":"Users","CapacityUnits":3,"ReadCapacityUnits":3,
            "Table":{"CapacityUnits":1,"ReadCapacityUnits":1},"GlobalSecondaryIndexes":{"byEmail":{"CapacityUnits":2,"ReadCapacityUnits":2}}}}
            """;
        using var handler = new StubHandler(_ => StubResponse.Json(response));

        // Act
        var activity = await RunAsync(handler, client => client.QueryAsync(CreateQuery()));

        // Assert
        await Assert.That(((string[])activity.GetTagItem("aws.dynamodb.consumed_capacity")!).Single()).IsEqualTo(
            """{"CapacityUnits":3,"ReadCapacityUnits":3,"TableName":"Users","Table":{"CapacityUnits":1,"ReadCapacityUnits":1},"GlobalSecondaryIndexes":{"byEmail":{"CapacityUnits":2,"ReadCapacityUnits":2}}}""");
    }

    [Test]
    public async Task GetItemAsync_ResponseWithoutConsumedCapacity_HasNoCapacityTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));

        // Act
        var activity = await RunAsync(handler, client => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(activity.GetTagItem("aws.dynamodb.consumed_capacity")).IsNull();
    }

    [Test]
    public async Task BatchGetItemAsync_ResponseWithoutConsumedCapacity_HasNoCapacityTag()
    {
        // Arrange (the response lists a capacity per table, and here no table: an empty list is not worth a tag)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":{},"UnprocessedKeys":{}}"""));
        var request = new BatchGetItemRequest
        {
            RequestItems = new Dictionary<string, KeysAndAttributes> { ["Users"] = new() { Keys = [new Dictionary<string, AttributeValue> { ["pk"] = "a" }] } },
        };

        // Act
        var activity = await RunAsync(handler, client => client.BatchGetItemAsync(request));

        // Assert
        await Assert.That(activity.GetTagItem("aws.dynamodb.consumed_capacity")).IsNull();
    }

    [Test]
    public async Task DescribeTableAsync_Response_HasNoCapacityTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Table":{"TableName":"Users"}}"""));

        // Act
        var activity = await RunAsync(handler, client => client.DescribeTableAsync(TestClient.DescribeTable()));

        // Assert
        await Assert.That(activity.GetTagItem("aws.dynamodb.consumed_capacity")).IsNull();
    }

    [Test]
    public async Task PutItemAsync_ResponseWithItemCollectionMetrics_PutsNoKeyOrItemDataInAnyTag()
    {
        // Arrange (the item collection key is the partition key value)
        const string response = """
            {"Attributes":{"ssn":{"S":"SECRET-ATTRIBUTE-VALUE"}},"ConsumedCapacity":{"TableName":"Users","CapacityUnits":1},
            "ItemCollectionMetrics":{"ItemCollectionKey":{"pk":{"S":"SECRET-KEY-VALUE"}},"SizeEstimateRangeGB":[1,2]}}
            """;
        using var handler = new StubHandler(_ => StubResponse.Json(response));
        var request = TestClient.PutItem() with { ReturnItemCollectionMetrics = ReturnItemCollectionMetrics.Size, ReturnValues = ReturnValue.AllOld };

        // Act
        var activity = await RunAsync(handler, client => client.PutItemAsync(request));

        // Assert
        await Assert.That(string.Join('\n', activity.TagObjects.Select(tag => tag.Key + "=" + (tag.Value is string[] values ? string.Join('|', values) : tag.Value)))).DoesNotContain("SECRET");
    }
}
