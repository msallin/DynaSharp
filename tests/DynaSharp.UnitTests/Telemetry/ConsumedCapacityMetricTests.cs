using System.Diagnostics;
using DynaSharp.Requests;
using DynaSharp.Telemetry;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.TestClient;

namespace DynaSharp.UnitTests.Telemetry;

public sealed class ConsumedCapacityMetricTests
{
    public static IEnumerable<Func<Func<DynamoDbClient, string, Task>>> OperationsThatReturnCapacity()
    {
        yield return () => (client, table) => client.GetItemAsync(TestClient.GetItem(table));
        yield return () => (client, table) => client.PutItemAsync(TestClient.PutItem(table));
        yield return () => (client, table) => client.UpdateItemAsync(new UpdateItemRequest { TableName = table, Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" } });
        yield return () => (client, table) => client.DeleteItemAsync(new DeleteItemRequest { TableName = table, Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" } });
        yield return () => (client, table) => client.QueryAsync(CreateQuery(table));
        yield return () => (client, table) => client.ScanAsync(new ScanRequest { TableName = table });
    }

    [Test]
    [MethodDataSource(nameof(OperationsThatReturnCapacity))]
    public async Task Operation_ResponseWithConsumedCapacity_AddsTheCapacityUnits(Func<DynamoDbClient, string, Task> call)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(call);
        var table = UniqueTable();
        using var handler = new StubHandler(_ => StubResponse.Json(CapacityResponse(table, 2.5)));
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.ConsumedCapacityMetric);
        using var client = TestClient.Create(handler);

        // Act
        await call(client, table);

        // Assert
        await Assert.That(capture.Single().Value).IsEqualTo(2.5);
    }

    [Test]
    public async Task QueryAsync_ResponseWithConsumedCapacity_TagsOperationAndTable()
    {
        // Arrange
        var table = UniqueTable();
        using var handler = new StubHandler(_ => StubResponse.Json(CapacityResponse(table, 1)));
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.ConsumedCapacityMetric);
        using var client = TestClient.Create(handler);

        // Act
        await client.QueryAsync(CreateQuery(table));

        // Assert
        await Assert.That(string.Join(',', capture.Single().Tags.OrderBy(tag => tag.Key, StringComparer.Ordinal).Select(tag => tag.Key + "=" + tag.Value)))
            .IsEqualTo($"db.collection.name={table},db.operation.name=Query,server.address=dynamodb.eu-central-2.amazonaws.com");
    }

    [Test]
    public async Task QueryAsync_NonDefaultPort_TagsServerAddressAndPort()
    {
        // Arrange
        var table = UniqueTable();
        using var handler = new StubHandler(_ => StubResponse.Json(CapacityResponse(table, 1)));
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.ConsumedCapacityMetric);
        using var client = TestClient.Create(handler, serviceUrl: new Uri("http://emulator.test:8000"));

        // Act
        await client.QueryAsync(CreateQuery(table));

        // Assert
        await Assert.That(string.Join(',', capture.Single().Tags.OrderBy(tag => tag.Key, StringComparer.Ordinal).Select(tag => tag.Key + "=" + tag.Value)))
            .IsEqualTo($"db.collection.name={table},db.operation.name=Query,server.address=emulator.test,server.port=8000");
    }

    [Test]
    public async Task QueryAsync_ResponseWithConsumedCapacity_UnitIsCapacityUnits()
    {
        // Arrange
        var table = UniqueTable();
        using var handler = new StubHandler(_ => StubResponse.Json(CapacityResponse(table, 1)));
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.ConsumedCapacityMetric);
        using var client = TestClient.Create(handler);

        // Act
        await client.QueryAsync(CreateQuery(table));

        // Assert
        await Assert.That(capture.Single().Unit).IsEqualTo("{capacity_unit}");
    }

    [Test]
    public async Task QueryAsync_ResponseWithConsumedCapacity_InstrumentHasADescription()
    {
        // Arrange
        var table = UniqueTable();
        using var handler = new StubHandler(_ => StubResponse.Json(CapacityResponse(table, 1)));
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.ConsumedCapacityMetric);
        using var client = TestClient.Create(handler);

        // Act
        await client.QueryAsync(CreateQuery(table));

        // Assert
        await Assert.That(capture.Single().Description).StartsWith("Read and write capacity units");
    }

    [Test]
    public async Task QueryAsync_CapacityWithoutTableName_UsesTheTableOfTheRequest()
    {
        // Arrange
        var table = UniqueTable();
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Count":0,"ScannedCount":0,"ConsumedCapacity":{"CapacityUnits":4}}"""));
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.ConsumedCapacityMetric);
        using var client = TestClient.Create(handler);

        // Act
        await client.QueryAsync(CreateQuery(table));

        // Assert
        await Assert.That(capture.Single().Value).IsEqualTo(4);
    }

    [Test]
    public async Task QueryAsync_CapacityWithIndexes_AddsTheTotalOnce()
    {
        // Arrange
        var table = UniqueTable();
        var response = $$$"""
            {"Count":0,"ScannedCount":0,"ConsumedCapacity":{"TableName":"{{{table}}}","CapacityUnits":3,
            "Table":{"CapacityUnits":1},"GlobalSecondaryIndexes":{"byEmail":{"CapacityUnits":2}}
            }}
            """;
        using var handler = new StubHandler(_ => StubResponse.Json(response));
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.ConsumedCapacityMetric);
        using var client = TestClient.Create(handler);

        // Act
        await client.QueryAsync(CreateQuery(table));

        // Assert
        await Assert.That(capture.All.Sum(measurement => measurement.Value)).IsEqualTo(3);
    }

    [Test]
    public async Task QueryAsync_CapacityWithoutTotal_RecordsNothing()
    {
        // Arrange
        var table = UniqueTable();
        var response = $$$"""{"Count":0,"ScannedCount":0,"ConsumedCapacity":{"TableName":"{{{table}}}","ReadCapacityUnits":1}}""";
        using var handler = new StubHandler(_ => StubResponse.Json(response));
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.ConsumedCapacityMetric);
        using var client = TestClient.Create(handler);

        // Act
        await client.QueryAsync(CreateQuery(table));

        // Assert
        await Assert.That(capture.All).IsEmpty();
    }

    [Test]
    public async Task QueryAsync_ResponseWithoutConsumedCapacity_RecordsNothing()
    {
        // Arrange
        var table = UniqueTable();
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Count":0,"ScannedCount":0}"""));
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.ConsumedCapacityMetric);
        using var client = TestClient.Create(handler);

        // Act
        await client.QueryAsync(CreateQuery(table));

        // Assert
        await Assert.That(capture.All).IsEmpty();
    }

    [Test]
    public async Task QueryAsync_ErrorResponse_RecordsNothing()
    {
        // Arrange
        var table = UniqueTable();
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "bad"));
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.ConsumedCapacityMetric);
        using var client = TestClient.Create(handler);

        // Act
        await TestClient.CaptureAsync<ValidationException>(() => client.QueryAsync(CreateQuery(table)));

        // Assert
        await Assert.That(capture.All).IsEmpty();
    }

    [Test]
    public async Task DescribeTableAsync_Response_RecordsNothing()
    {
        // Arrange
        var table = UniqueTable();
        using var handler = new StubHandler(_ => StubResponse.Json($$$"""{"Table":{"TableName":"{{{table}}}"}}"""));
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.ConsumedCapacityMetric);
        using var client = TestClient.Create(handler);

        // Act
        await client.DescribeTableAsync(TestClient.DescribeTable(table));

        // Assert
        await Assert.That(capture.All).IsEmpty();
    }

    [Test]
    public async Task QueryAsync_SpanRecordsNothingButMetricHasAListener_StillAddsTheCapacity()
    {
        // Arrange
        var table = UniqueTable();
        using var handler = new StubHandler(_ => StubResponse.Json(CapacityResponse(table, 2)));
        using var activities = new ActivityCapture(ActivitySamplingResult.PropagationData);
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.ConsumedCapacityMetric);
        using var client = TestClient.Create(handler);

        // Act
        await client.QueryAsync(CreateQuery(table));

        // Assert
        await Assert.That(capture.Single().Value).IsEqualTo(2);
    }

    [Test]
    public async Task QueryAsync_SpanAndMetricListening_FeedsBoth()
    {
        // Arrange
        var table = UniqueTable();
        using var handler = new StubHandler(_ => StubResponse.Json(CapacityResponse(table, 2)));
        using var activities = new ActivityCapture();
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.ConsumedCapacityMetric);
        using var client = TestClient.Create(handler);

        // Act
        await client.QueryAsync(CreateQuery(table));

        // Assert
        await Assert.That($"{capture.Single().Value} {activities.Single().GetTagItem("aws.dynamodb.consumed_capacity") is string[]}").IsEqualTo("2 True");
    }

    [Test]
    public async Task BatchGetItemAsync_CapacityOfTwoTables_AddsOneMeasurementPerTable()
    {
        // Arrange
        var first = UniqueTable();
        var second = UniqueTable();
        var body = $$$"""{"Responses":{},"ConsumedCapacity":[{"TableName":"{{{first}}}","CapacityUnits":1.5},{"TableName":"{{{second}}}","CapacityUnits":2.5}]}""";
        using var handler = new StubHandler(_ => StubResponse.Json(body));
        using var firstCapture = MeasurementCapture<double>.ForTable(first, TelemetryNames.ConsumedCapacityMetric);
        using var secondCapture = MeasurementCapture<double>.ForTable(second, TelemetryNames.ConsumedCapacityMetric);
        using var client = TestClient.Create(handler);
        var keys = new KeysAndAttributes { Keys = [new Dictionary<string, AttributeValue> { ["pk"] = "a" }] };

        // Act
        await client.BatchGetItemAsync(new BatchGetItemRequest { RequestItems = new Dictionary<string, KeysAndAttributes> { [first] = keys, [second] = keys } });

        // Assert
        await Assert.That((firstCapture.Single().Value, secondCapture.Single().Value)).IsEqualTo((1.5, 2.5));
    }

    [Test]
    public async Task TransactWriteItemsAsync_SeveralTablesAndCapacityWithoutATableName_AddsItWithoutATableTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"ConsumedCapacity":[{"CapacityUnits":4.0}]}"""));
        var host = TestClient.UniqueHost();
        using var capture = MeasurementCapture<double>.ForServer(host, TelemetryNames.ConsumedCapacityMetric);
        using var client = TestClient.Create(handler, serviceUrl: new Uri($"http://{host}:8000"));
        var key = new Dictionary<string, AttributeValue> { ["pk"] = "a" };
        var request = new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem { Delete = new TransactDelete { TableName = UniqueTable(), Key = key } },
                new TransactWriteItem { Delete = new TransactDelete { TableName = UniqueTable(), Key = key } },
            ],
        };

        // Act
        await client.TransactWriteItemsAsync(request);

        // Assert
        await Assert.That(capture.Single().Value).IsEqualTo(4.0);
    }

    [Test]
    public async Task TransactGetItemsAsync_ResponseWithConsumedCapacity_AddsTheCapacityUnits()
    {
        // Arrange
        var table = UniqueTable();
        var body = $$$"""{"ConsumedCapacity":[{"TableName":"{{{table}}}","CapacityUnits":6.0}]}""";
        using var handler = new StubHandler(_ => StubResponse.Json(body));
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.ConsumedCapacityMetric);
        using var client = TestClient.Create(handler);
        var request = new TransactGetItemsRequest
        {
            TransactItems = [new TransactGetItem { Get = new TransactGet { TableName = table, Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" } } }],
        };

        // Act
        await client.TransactGetItemsAsync(request);

        // Assert
        await Assert.That(capture.Single().Value).IsEqualTo(6.0);
    }

    [Test]
    public async Task BatchWriteItemAsync_ResponseWithConsumedCapacity_AddsTheCapacityUnits()
    {
        // Arrange
        var table = UniqueTable();
        var body = $$$"""{"ConsumedCapacity":[{"TableName":"{{{table}}}","CapacityUnits":3.0}]}""";
        using var handler = new StubHandler(_ => StubResponse.Json(body));
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.ConsumedCapacityMetric);
        using var client = TestClient.Create(handler);
        var writes = new WriteRequest { DeleteRequest = new DeleteRequest { Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" } } };

        // Act
        await client.BatchWriteItemAsync(new BatchWriteItemRequest { RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>> { [table] = [writes] } });

        // Assert
        await Assert.That(capture.Single().Value).IsEqualTo(3.0);
    }

    private static string CapacityResponse(string table, double units) =>
        $$$"""{"Count":0,"ScannedCount":0,"ConsumedCapacity":{"TableName":"{{{table}}}","CapacityUnits":{{{units.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}}}""";
}
