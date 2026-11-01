using System.Diagnostics;
using DynaSharp.Requests;
using DynaSharp.Telemetry;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Telemetry.ActivityCapture;

namespace DynaSharp.UnitTests.Telemetry;

// Spans and the duration metric of ListTables, DescribeTimeToLive and UpdateTable. The meter and the activity source are global and tests
// run in parallel, so every test filters by a trace of its own, a table of its own or a host name of its own.
public sealed class TableOperationsTelemetryTests
{
    [Test]
    [Arguments("ListTables", "DynamoDB.ListTables")]
    [Arguments("DescribeTimeToLive", "DynamoDB.DescribeTimeToLive")]
    [Arguments("UpdateTable", "DynamoDB.UpdateTable")]
    public async Task Call_Listener_StartsAnActivityNamedAfterServiceAndOperation(string operation, string expected)
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TableDescription":{"TableName":"Users"}}"""));

        // Act
        var activity = await RunAsync(handler, client => operation switch
        {
            "ListTables" => client.ListTablesAsync(TestClient.ListTables()),
            "DescribeTimeToLive" => client.DescribeTimeToLiveAsync(TestClient.DescribeTimeToLive()),
            _ => client.UpdateTableAsync(TestClient.UpdateTable()),
        });

        // Assert
        await Assert.That(activity.OperationName).IsEqualTo(expected);
    }

    [Test]
    public async Task ListTablesAsync_Listener_HasNoTableTags()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TableNames":["a-table"]}"""));

        // Act
        var activity = await RunAsync(handler, client => client.ListTablesAsync(TestClient.ListTables()));

        // Assert
        await Assert.That((activity.GetTagItem("db.collection.name"), activity.GetTagItem("aws.dynamodb.table_names"))).IsEqualTo(((object?)null, (object?)null));
    }

    [Test]
    public async Task ListTablesAsync_PagedRequest_SetsTheLimitTheStartTableAndTheTableCount()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TableNames":["b-table","c-table"],"LastEvaluatedTableName":"c-table"}"""));
        var request = TestClient.ListTables() with { ExclusiveStartTableName = "a-table", Limit = 2 };

        // Act
        var activity = await RunAsync(handler, client => client.ListTablesAsync(request));

        // Assert
        await Assert.That(Tags(activity)).IsEqualTo("aws.dynamodb.exclusive_start_table=a-table,aws.dynamodb.limit=2,aws.dynamodb.table_count=2");
    }

    [Test]
    public async Task ListTablesAsync_Listener_NoTagCarriesATableName()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TableNames":["secret-table-name"]}"""));

        // Act
        var activity = await RunAsync(handler, client => client.ListTablesAsync(TestClient.ListTables()));

        // Assert
        await Assert.That(string.Join('|', activity.TagObjects.Select(tag => tag.Value?.ToString()))).DoesNotContain("secret-table-name", StringComparison.Ordinal);
    }

    [Test]
    public async Task DescribeTimeToLiveAsync_Listener_ReportsTheTable()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TimeToLiveDescription":{"TimeToLiveStatus":"DISABLED"}}"""));

        // Act
        var activity = await RunAsync(handler, client => client.DescribeTimeToLiveAsync(TestClient.DescribeTimeToLive("Orders")));

        // Assert
        await Assert.That((activity.GetTagItem("db.collection.name"), ((string[])activity.GetTagItem("aws.dynamodb.table_names")!).Single())).IsEqualTo(((object?)"Orders", "Orders"));
    }

    [Test]
    public async Task UpdateTableAsync_ProvisionedThroughput_SetsTheTableAndTheCapacityTags()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TableDescription":{"TableName":"Orders"}}"""));
        var request = TestClient.UpdateTable("Orders") with { ProvisionedThroughput = new() { ReadCapacityUnits = 7, WriteCapacityUnits = 3 } };

        // Act
        var activity = await RunAsync(handler, client => client.UpdateTableAsync(request));

        // Assert
        await Assert.That((activity.GetTagItem("db.collection.name"), Tags(activity))).IsEqualTo(((object?)"Orders", "aws.dynamodb.provisioned_read_capacity=7,aws.dynamodb.provisioned_write_capacity=3,aws.dynamodb.table_names=Orders"));
    }

    [Test]
    public async Task UpdateTableAsync_IndexAndBillingChanges_AreNotInAnyTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TableDescription":{"TableName":"Orders"}}"""));
        var request = TestClient.UpdateTable("Orders") with
        {
            GlobalSecondaryIndexUpdates = [new() { Delete = new() { IndexName = "SECRET-INDEX" } }],
        };

        // Act
        var activity = await RunAsync(handler, client => client.UpdateTableAsync(request));

        // Assert
        await Assert.That(string.Join('|', activity.TagObjects.Select(tag => tag.Value is string[] names ? string.Join(',', names) : tag.Value?.ToString()))).DoesNotContain("SECRET", StringComparison.Ordinal);
    }

    [Test]
    public async Task ListTablesAsync_Listener_RecordsTheDurationWithoutATable()
    {
        // Arrange
        var host = TestClient.UniqueHost();
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TableNames":[]}"""));
        using var capture = MeasurementCapture<double>.ForServer(host, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler, serviceUrl: new Uri($"http://{host}:8000"));

        // Act
        await client.ListTablesAsync(TestClient.ListTables());

        // Assert
        var tags = capture.Single().Tags;
        await Assert.That((tags.ContainsKey("db.collection.name"), tags["db.operation.name"], tags["db.system.name"])).IsEqualTo((false, (object?)"ListTables", "aws.dynamodb"));
    }

    [Test]
    public async Task UpdateTableAsync_Listener_RecordsTheDurationForTheTable()
    {
        // Arrange
        var table = $"orders-{Guid.NewGuid():N}";
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TableDescription":{"TableName":"Orders"}}"""));
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler);

        // Act
        await client.UpdateTableAsync(TestClient.UpdateTable(table));

        // Assert
        var tags = capture.Single().Tags;
        await Assert.That((tags["db.collection.name"], tags["db.operation.name"])).IsEqualTo(((object?)table, (object?)"UpdateTable"));
    }

    [Test]
    public async Task UpdateTableAsync_TableInUse_RecordsTheErrorOnTheSpan()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceInUseException", "Table is being updated"));
        using var capture = new ActivityCapture();
        using var client = TestClient.Create(handler);

        // Act
        await TestClient.CaptureAsync<ResourceInUseException>(() => client.UpdateTableAsync(TestClient.UpdateTable()));

        // Assert
        var activity = capture.Single();
        await Assert.That((activity.Status, activity.GetTagItem("error.type"))).IsEqualTo((ActivityStatusCode.Error, (object?)"ResourceInUseException"));
    }

    private static string Tags(Activity activity) =>
        string.Join(',', activity.TagObjects.Where(tag => tag.Key.StartsWith("aws.dynamodb.", StringComparison.Ordinal)).OrderBy(tag => tag.Key, StringComparer.Ordinal).Select(tag => tag.Key + "=" + (tag.Value is string[] names ? string.Join('|', names) : tag.Value)));
}
