using System.Diagnostics;
using DynaSharp.Requests;
using DynaSharp.Telemetry;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Http.RetryTestSupport;
using static DynaSharp.UnitTests.Telemetry.ActivityCapture;

namespace DynaSharp.UnitTests.Telemetry;

// Spans, the duration metric, the counters and the retry warnings of calls that address one table, several tables, or none. The meter
// and the activity source are global and tests run in parallel, so every test filters by a trace, a table or a server of its own.
public sealed class BatchTelemetryTests
{
    [Test]
    public async Task BatchGetItemAsync_OneTable_SetsTheCollectionNameAndTheTableNames()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var activity = await RunAsync(handler, client => client.BatchGetItemAsync(BatchGet("Users")));

        // Assert
        await Assert.That((activity.GetTagItem("db.collection.name"), Names(activity))).IsEqualTo(("Users", "Users"));
    }

    [Test]
    public async Task BatchGetItemAsync_SeveralTables_SetsTheTableNamesSortedAndNoCollectionName()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var activity = await RunAsync(handler, client => client.BatchGetItemAsync(BatchGet("users", "Orders", "Customers")));

        // Assert
        await Assert.That((activity.GetTagItem("db.collection.name"), Names(activity))).IsEqualTo(((object?)null, "Customers,Orders,users"));
    }

    [Test]
    public async Task BatchGetItemAsync_SeveralTables_KeepsTheOtherTags()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var activity = await RunAsync(handler, client => client.BatchGetItemAsync(BatchGet("Users", "Orders")));

        // Assert
        await Assert.That((activity.OperationName, activity.GetTagItem("db.system.name"), activity.GetTagItem("db.operation.name"), activity.GetTagItem("rpc.method"), activity.GetTagItem("cloud.region")))
            .IsEqualTo(("DynamoDB.BatchGetItem", "aws.dynamodb", "BatchGetItem", "BatchGetItem", "eu-central-2"));
    }

    [Test]
    public async Task BatchWriteItemAsync_OneTable_SetsTheCollectionNameAndTheTableNames()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var activity = await RunAsync(handler, client => client.BatchWriteItemAsync(BatchWrite("Users")));

        // Assert
        await Assert.That((activity.GetTagItem("db.collection.name"), Names(activity))).IsEqualTo(("Users", "Users"));
    }

    [Test]
    public async Task BatchWriteItemAsync_SeveralTables_SetsTheTableNamesSortedAndNoCollectionName()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var activity = await RunAsync(handler, client => client.BatchWriteItemAsync(BatchWrite("users", "Orders")));

        // Assert
        await Assert.That((activity.GetTagItem("db.collection.name"), Names(activity))).IsEqualTo(((object?)null, "Orders,users"));
    }

    [Test]
    public async Task TransactGetItemsAsync_OneTable_SetsTheCollectionNameAndTheTableNames()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var activity = await RunAsync(handler, client => client.TransactGetItemsAsync(TransactGet("Users", "Users")));

        // Assert
        await Assert.That((activity.GetTagItem("db.collection.name"), Names(activity))).IsEqualTo(("Users", "Users"));
    }

    [Test]
    public async Task TransactGetItemsAsync_SeveralTables_SetsTheDistinctTableNamesSortedAndNoCollectionName()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var activity = await RunAsync(handler, client => client.TransactGetItemsAsync(TransactGet("users", "Orders", "users")));

        // Assert
        await Assert.That((activity.GetTagItem("db.collection.name"), Names(activity))).IsEqualTo(((object?)null, "Orders,users"));
    }

    [Test]
    public async Task TransactWriteItemsAsync_OneTable_SetsTheCollectionNameAndTheTableNames()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var activity = await RunAsync(handler, client => client.TransactWriteItemsAsync(TransactWrite("Users", "Users")));

        // Assert
        await Assert.That((activity.GetTagItem("db.collection.name"), Names(activity))).IsEqualTo(("Users", "Users"));
    }

    [Test]
    public async Task TransactWriteItemsAsync_SeveralTables_SetsTheDistinctTableNamesSortedAndNoCollectionName()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var activity = await RunAsync(handler, client => client.TransactWriteItemsAsync(TransactWrite("users", "Orders", "users")));

        // Assert
        await Assert.That((activity.GetTagItem("db.collection.name"), Names(activity))).IsEqualTo(((object?)null, "Orders,users"));
    }

    [Test]
    public async Task TransactWriteItemsAsync_Always_TagsNeitherTokenNorKeysNorExpressions()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var activity = await RunAsync(handler, client => client.TransactWriteItemsAsync(TransactWrite("Users", "Orders") with { ClientRequestToken = "secret-token" }));

        // Assert
        await Assert.That(activity.TagObjects.Any(tag => tag.Value is string text && text.Contains("secret-token", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task TransactWriteItemsAsync_Canceled_MarksTheActivityAsFailedWithTheErrorCode()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(
            """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":[{"Code":"None"}],"Message":"Transaction cancelled"}""",
            System.Net.HttpStatusCode.BadRequest));

        // Act
        var activity = await RunAsync(handler, client => TestClient.CaptureAsync<TransactionCanceledException>(() => client.TransactWriteItemsAsync(TransactWrite("Users", "Orders"))));

        // Assert
        await Assert.That((activity.Status, activity.GetTagItem("error.type"), activity.GetTagItem("db.response.status_code"))).IsEqualTo((ActivityStatusCode.Error, "TransactionCanceledException", "TransactionCanceledException"));
    }

    [Test]
    public async Task BatchGetItemAsync_OneTable_RecordsTheDurationWithTheTableTag()
    {
        // Arrange
        const string table = "BatchTelemetryDurationOneTable";
        using var metrics = MeasurementCapture<double>.ForTable(table, TelemetryNames.DurationMetric);
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        await client.BatchGetItemAsync(BatchGet(table));

        // Assert
        await Assert.That((metrics.Single().Tags["db.collection.name"], metrics.Single().Tags["db.operation.name"])).IsEqualTo((table, "BatchGetItem"));
    }

    [Test]
    public async Task BatchGetItemAsync_SeveralTables_RecordsTheDurationWithoutATableTag()
    {
        // Arrange (no table to filter by: the server of the client is the filter)
        using var metrics = MeasurementCapture<double>.ForServer("duration-several-tables.test", TelemetryNames.DurationMetric);
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, serviceUrl: new Uri("http://duration-several-tables.test:8000"));

        // Act
        await client.BatchGetItemAsync(BatchGet("Users", "Orders"));

        // Assert
        var tags = metrics.Single().Tags;
        await Assert.That((tags.ContainsKey("db.collection.name"), tags["db.operation.name"], tags["db.system.name"])).IsEqualTo((false, "BatchGetItem", "aws.dynamodb"));
    }

    [Test]
    public async Task TransactWriteItemsAsync_SeveralTablesFailing_RecordsTheDurationWithTheErrorTypeAndWithoutATableTag()
    {
        // Arrange
        using var metrics = MeasurementCapture<double>.ForServer("duration-failing-transaction.test", TelemetryNames.DurationMetric);
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "bad"));
        using var client = TestClient.Create(handler, serviceUrl: new Uri("http://duration-failing-transaction.test:8000"));

        // Act
        await TestClient.CaptureAsync<ValidationException>(() => client.TransactWriteItemsAsync(TransactWrite("Users", "Orders")));

        // Assert
        var tags = metrics.Single().Tags;
        await Assert.That((tags.ContainsKey("db.collection.name"), tags["error.type"])).IsEqualTo((false, "ValidationException"));
    }

    [Test]
    public async Task BatchGetItemAsync_SeveralTablesThrottled_CountsTheRetriesWithoutATableTag()
    {
        // Arrange (no table to filter by: the server of the client is the filter)
        var host = TestClient.UniqueHost();
        using var counters = MeasurementCapture<long>.ForServer(host, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, serviceUrl: new Uri($"http://{host}:8000"));

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.BatchGetItemAsync(BatchGet("Users", "Orders")));

        // Assert
        var retries = counters.Of(TelemetryNames.RetriesMetric);
        await Assert.That((retries.Count, string.Join(',', retries[0].Tags.OrderBy(tag => tag.Key, StringComparer.Ordinal).Select(tag => $"{tag.Key}={tag.Value}"))))
            .IsEqualTo((2, $"db.operation.name=BatchGetItem,error.type=ProvisionedThroughputExceededException,server.address={host},server.port=8000"));
    }

    [Test]
    public async Task BatchWriteItemAsync_SeveralTablesThrottledOnEveryAttempt_CountsEveryThrottleWithoutATableTag()
    {
        // Arrange (no table to filter by: the server of the client is the filter)
        var host = TestClient.UniqueHost();
        using var counters = MeasurementCapture<long>.ForServer(host, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError("ThrottlingException", "Slow down.") };
        using var client = CreateClient(handler, serviceUrl: new Uri($"http://{host}:8000"));

        // Act
        await TestClient.CaptureAsync<ThrottlingException>(() => client.BatchWriteItemAsync(BatchWrite("Users", "Orders")));

        // Assert
        await Assert.That(counters.Of(TelemetryNames.ThrottlesMetric).Count(measurement => Equals(measurement.Tags["error.type"], "ThrottlingException"))).IsEqualTo(3);
    }

    [Test]
    public async Task TransactGetItemsAsync_SeveralTablesRetriesExhausted_WarnsThatTheCallHasNoSingleTable()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, loggerFactory: logs);
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.TransactGetItemsAsync(TransactGet("Users", "Orders")));

        // Act
        var message = logs.Entries.Single(entry => entry.EventId.Id == 4).Message;

        // Assert
        await Assert.That(message).IsEqualTo(
            "DynamoDB TransactGetItems on table (no single table) failed with ProvisionedThroughputExceededException on attempt 3, the last one allowed. The error is passed on to the caller.");
    }

    [Test]
    public async Task BatchWriteItemAsync_OneTableRetriesExhausted_WarnsWithTheTable()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, loggerFactory: logs);
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.BatchWriteItemAsync(BatchWrite("Orders")));

        // Act
        var message = logs.Entries.Single(entry => entry.EventId.Id == 4).Message;

        // Assert
        await Assert.That(message).IsEqualTo(
            "DynamoDB BatchWriteItem on table Orders failed with ProvisionedThroughputExceededException on attempt 3, the last one allowed. The error is passed on to the caller.");
    }

    [Test]
    public async Task TransactWriteItemsAsync_SeveralTablesQuotaRunsOut_WarnsThatSeveralTablesWereInvolved()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, loggerFactory: logs, maxAttempts: int.MaxValue);
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.TransactWriteItemsAsync(TransactWrite("Users", "Orders")));

        // Act
        var message = logs.Entries.Single(entry => entry.EventId.Id == 5).Message;

        // Assert
        await Assert.That(message).StartsWith("DynamoDB TransactWriteItems on table (no single table) failed with ProvisionedThroughputExceededException on attempt", StringComparison.Ordinal);
    }

    private static string Names(Activity activity) => string.Join(',', (string[])activity.GetTagItem("aws.dynamodb.table_names")!);

    private static BatchGetItemRequest BatchGet(params string[] tables) => new()
    {
        RequestItems = tables.Distinct(StringComparer.Ordinal).ToDictionary(
            table => table,
            table => new KeysAndAttributes { Keys = [new Dictionary<string, AttributeValue> { ["pk"] = "a" }] }),
    };

    private static BatchWriteItemRequest BatchWrite(params string[] tables) => new()
    {
        RequestItems = tables.Distinct(StringComparer.Ordinal).ToDictionary(
            table => table,
            table => (IReadOnlyList<WriteRequest>)[new WriteRequest { DeleteRequest = new DeleteRequest { Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" } } }]),
    };

    private static TransactGetItemsRequest TransactGet(params string[] tables) => new()
    {
        TransactItems = [.. tables.Select(table => new TransactGetItem { Get = new TransactGet { TableName = table, Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" } } })],
    };

    private static TransactWriteItemsRequest TransactWrite(params string[] tables) => new()
    {
        TransactItems = [.. tables.Select(table => new TransactWriteItem { Delete = new TransactDelete { TableName = table, Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" } } })],
    };
}
