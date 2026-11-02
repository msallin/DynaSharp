using System.Diagnostics;
using DynaSharp.Requests;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Telemetry;
using static DynaSharp.Expr;
using static DynaSharp.UnitTests.Telemetry.ActivityCapture;

namespace DynaSharp.UnitTests.Fluent;

// A command and the request API run on one pipeline, so telemetry must not tell them apart: the same span, tags and metrics for the same
// request. Each test runs both and compares; no key, value or expression may reach a tag.
public sealed class CommandTelemetryTests
{
    private const string Item = """{"Item":{"pk":{"S":"a"}}}""";

    [Test]
    public async Task SendAsync_GetItem_HasTheTagsOfTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Item));

        // Act
        var request = await RunAsync(handler, client => client.GetItemAsync(new GetItemRequest { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" }, ConsistentRead = true, ProjectionExpression = "fullName, address.city" }));
        var command = await RunAsync(handler, async client => await client.Table("Users", "pk").Get("a").Consistent().Project("fullName", "address.city").SendAsync());

        // Assert
        await Assert.That((command.OperationName, Normal(command))).IsEqualTo((request.OperationName, Normal(request)));
    }

    [Test]
    public async Task ItemAsync_GetItem_HasTheTagsOfTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Item));

        // Act
        var request = await RunAsync(handler, client => client.GetItemAsync(new GetItemRequest { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" }, ConsistentRead = true, ProjectionExpression = "fullName, address.city" }));
        var command = await RunAsync(handler, async client => await client.Table("Users", "pk").Get("a").Consistent().Project("fullName", "address.city").ItemAsync());

        // Assert
        await Assert.That((command.OperationName, Normal(command))).IsEqualTo((request.OperationName, Normal(request)));
    }

    [Test]
    public async Task SendAsync_PutItem_HasTheTagsOfTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var request = await RunAsync(handler, client => client.PutItemAsync(TestClient.PutItem()));
        var command = await RunAsync(handler, async client => await client.Table("Users", "pk").Put(new Item { ["pk"] = "a" }).SendAsync());

        // Assert
        await Assert.That((command.OperationName, Normal(command))).IsEqualTo((request.OperationName, Normal(request)));
    }

    [Test]
    public async Task SendAsync_UpdateItem_HasTheTagsOfTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var request = await RunAsync(handler, client => client.UpdateItemAsync(new UpdateItemRequest { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" }, UpdateExpression = "SET a = :a", ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":a"] = 1 } }));
        var command = await RunAsync(handler, async client => await client.Table("Users", "pk").Update("a").Set("a", 1).SendAsync());

        // Assert
        await Assert.That((command.OperationName, Normal(command))).IsEqualTo((request.OperationName, Normal(request)));
    }

    [Test]
    public async Task SendAsync_DeleteItem_HasTheTagsOfTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var request = await RunAsync(handler, client => client.DeleteItemAsync(new DeleteItemRequest { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" } }));
        var command = await RunAsync(handler, async client => await client.Table("Users", "pk").Delete("a").SendAsync());

        // Assert
        await Assert.That((command.OperationName, Normal(command))).IsEqualTo((request.OperationName, Normal(request)));
    }

    [Test]
    public async Task PageAsync_Query_HasTheTagsOfTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{"pk":{"S":"a"}}],"Count":1,"ScannedCount":3,"ConsumedCapacity":{"TableName":"Users","CapacityUnits":1.0}}"""));

        // Act
        var request = await RunAsync(handler, client => client.QueryAsync(new QueryRequest { TableName = "Users", IndexName = "ByName", KeyConditionExpression = "#n0 = :v0", ProjectionExpression = "fullName, address.city", ConsistentRead = true, Limit = 5, ScanIndexForward = false, ReturnConsumedCapacity = ReturnConsumedCapacity.Total, ExpressionAttributeNames = new Dictionary<string, string> { ["#n0"] = "name" }, ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":v0"] = "a" } }));
        var command = await RunAsync(handler, async client => await client.Table("Users", "pk").Index("ByName", "pk", "name").Query("a").Project("fullName", "address.city").Consistent().Limit(5).Descending().ReturnCapacity().PageAsync());

        // Assert
        await Assert.That((command.OperationName, Normal(command))).IsEqualTo((request.OperationName, Normal(request)));
    }

    [Test]
    public async Task PageAsync_Scan_HasTheTagsOfTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{"pk":{"S":"a"}}],"Count":1,"ScannedCount":3}"""));

        // Act
        var request = await RunAsync(handler, client => client.ScanAsync(new ScanRequest { TableName = "Users", Limit = 5, Segment = 1, TotalSegments = 4 }));
        var command = await RunAsync(handler, async client => await client.Table("Users", "pk").Scan().Limit(5).Segment(1, of: 4).PageAsync());

        // Assert
        await Assert.That((command.OperationName, Normal(command))).IsEqualTo((request.OperationName, Normal(request)));
    }

    [Test]
    public async Task SendAsync_SelectOfAGet_HasTheTagsOfTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"total":{"N":"1"}},"ConsumedCapacity":{"TableName":"Users","CapacityUnits":1.0}}"""));

        // Act
        var request = await RunAsync(handler, client => client.GetItemAsync(new GetItemRequest { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" }, ConsistentRead = true, ProjectionExpression = "total", ReturnConsumedCapacity = ReturnConsumedCapacity.Total }));
        var command = await RunAsync(handler, async client => await client.Table("Users", "pk").Get("a").Consistent().ReturnCapacity().Select(Attr<int>("total")).SendAsync());

        // Assert
        await Assert.That((command.OperationName, Normal(command))).IsEqualTo((request.OperationName, Normal(request)));
    }

    [Test]
    public async Task PageAsync_SelectOfAQuery_HasTheTagsOfTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{"total":{"N":"1"}}],"Count":1,"ScannedCount":3,"ConsumedCapacity":{"TableName":"Users","CapacityUnits":1.0}}"""));

        // Act
        var request = await RunAsync(handler, client => client.QueryAsync(new QueryRequest { TableName = "Users", IndexName = "ByName", KeyConditionExpression = "#n0 = :v0", ProjectionExpression = "total", ConsistentRead = true, Limit = 5, ScanIndexForward = false, ReturnConsumedCapacity = ReturnConsumedCapacity.Total, ExpressionAttributeNames = new Dictionary<string, string> { ["#n0"] = "name" }, ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":v0"] = "a" } }));
        var command = await RunAsync(handler, async client => await client.Table("Users", "pk").Index("ByName", "pk", "name").Query("a").Consistent().Limit(5).Descending().ReturnCapacity().Select(Attr<int>("total")).PageAsync());

        // Assert
        await Assert.That((command.OperationName, Normal(command))).IsEqualTo((request.OperationName, Normal(request)));
    }

    [Test]
    public async Task PageAsync_SelectOfAScan_HasTheTagsOfTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{"total":{"N":"1"}}],"Count":1,"ScannedCount":3}"""));

        // Act
        var request = await RunAsync(handler, client => client.ScanAsync(new ScanRequest { TableName = "Users", ProjectionExpression = "total", Limit = 5, Segment = 1, TotalSegments = 4 }));
        var command = await RunAsync(handler, async client => await client.Table("Users", "pk").Scan().Limit(5).Segment(1, of: 4).Select(Attr<int>("total")).PageAsync());

        // Assert
        await Assert.That((command.OperationName, Normal(command))).IsEqualTo((request.OperationName, Normal(request)));
    }

    [Test]
    public async Task CountAsync_Query_HasTheTagsOfTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Count":2,"ScannedCount":3}"""));

        // Act
        var request = await RunAsync(handler, client => client.QueryAsync(new QueryRequest { TableName = "Users", KeyConditionExpression = "#n0 = :v0", Select = SelectKind.Count, ExpressionAttributeNames = new Dictionary<string, string> { ["#n0"] = "pk" }, ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":v0"] = "a" } }));
        var command = await RunAsync(handler, async client => await client.Table("Users", "pk").Query("a").CountAsync());

        // Assert
        await Assert.That((command.OperationName, Normal(command))).IsEqualTo((request.OperationName, Normal(request)));
    }

    [Test]
    public async Task ItemsAsync_SeveralPages_HasOneSpanPerPage()
    {
        // Arrange
        var table = TestClient.UniqueTable();
        using var capture = new ActivityCapture();
        using var handler = new StubHandler();
        handler.Enqueue(PageJson.FilteredEmpty("1"));
        handler.Enqueue(PageJson.More("2"));
        handler.Enqueue(PageJson.Last("3", "4"));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table(table, "pk", "sk").Query("a").ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", capture.All.Select(static activity => $"{activity.OperationName}:{activity.GetTagItem("aws.dynamodb.count")}/{activity.GetTagItem("aws.dynamodb.scanned_count")}")))
            .IsEqualTo("DynamoDB.Query:0/7,DynamoDB.Query:1/1,DynamoDB.Query:2/2");
    }

    [Test]
    public async Task ItemsAsync_SeveralPages_HasOneDurationPerPage()
    {
        // Arrange
        var table = TestClient.UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, "db.client.operation.duration");
        using var handler = new StubHandler();
        handler.Enqueue(PageJson.More("1"));
        handler.Enqueue(PageJson.Last("2"));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table(table, "pk", "sk").Scan().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(capture.All.Select(static measurement => measurement.Tags["db.operation.name"])).IsEquivalentTo(new object?[] { "Scan", "Scan" });
    }

    [Test]
    public async Task ItemsAsync_ConsumedCapacity_ReachesSpanAndMetricPerPage()
    {
        // Arrange
        var table = TestClient.UniqueTable();
        using var measurements = MeasurementCapture<double>.ForTable(table, "dynasharp.client.consumed_capacity");
        using var spans = new ActivityCapture();
        var capacity = "\"ConsumedCapacity\":{\"TableName\":\"" + table + "\",\"CapacityUnits\":2.0}";
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{" + capacity + ",\"LastEvaluatedKey\":" + PageJson.Key("1") + "}"));
        handler.Enqueue(StubResponse.Json("{" + capacity + "}"));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table(table, "pk", "sk").Query("a").ReturnCapacity().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((measurements.All.Sum(static measurement => measurement.Value), spans.All.Count(static span => span.GetTagItem("aws.dynamodb.consumed_capacity") is string[]))).IsEqualTo((4.0, 2));
    }

    [Test]
    public async Task ItemsAsync_Failure_MarksTheSpanOfThatPage()
    {
        // Arrange
        var table = TestClient.UniqueTable();
        using var capture = new ActivityCapture();
        using var handler = new StubHandler();
        handler.Enqueue(PageJson.More("1"));
        handler.Enqueue(StubResponse.DynamoDbError("ResourceNotFoundException", "Gone."));
        using var client = TestClient.Create(handler);
        var shop = client.Table(table, "pk", "sk");

        // Act
        await TestClient.CaptureAsync<ResourceNotFoundException>(() => shop.Query("a").ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(string.Join(",", capture.All.Select(static activity => activity.Status))).IsEqualTo("Unset,Error");
    }

    [Test]
    public async Task ItemsAsync_ParallelScan_TagsTheSegments()
    {
        // Arrange
        var table = TestClient.UniqueTable();
        using var capture = new ActivityCapture();
        using var handler = new StubHandler(_ => PageJson.Last());
        using var client = TestClient.Create(handler);

        // Act
        await client.Table(table, "pk", "sk").Scan().Project("x").Parallel(3).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", capture.All.Select(static activity => $"{activity.GetTagItem("aws.dynamodb.segment")}/{activity.GetTagItem("aws.dynamodb.total_segments")}/{activity.GetTagItem("aws.dynamodb.projection")}").Order(StringComparer.Ordinal)))
            .IsEqualTo("0/3/x,1/3/x,2/3/x");
    }

    [Test]
    public async Task ItemsAsync_Query_NoTagCarriesAKeyAValueOrAnExpression()
    {
        // Arrange
        using var handler = new StubHandler(_ => PageJson.Last());

        // Act
        var activity = await RunAsync(handler, async client => await client.Table("Users", "pk", "sk").Query("key-secret", SortKeyCondition.BeginsWith("sort-secret")).Filter(Attr("m").Eq("value-secret")).ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(Tags(activity)).DoesNotContain("secret").And.DoesNotContain(":v0").And.DoesNotContain("#n0");
    }

    [Test]
    public async Task ItemsAsync_FanOut_HasOneSpanPerCallAndNoKeyInAnyTag()
    {
        // Arrange
        var wire = new FanOutWire { ItemsPerResponse = 1 }.Put("key-secret-0", "1").Put("key-secret-1", "2");
        using var handler = new StubHandler(wire.Answer);
        using var capture = new ActivityCapture();
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Users", "pk", "sk").Query(["key-secret-0", "key-secret-1"], SortKeyCondition.Ge("sort-secret")).Filter(Attr("m").Eq("value-secret")).Descending().Project("x").ItemsAsync().ToListAsync();
        var activities = capture.All;

        // Assert
        await Assert.That((activities.Count, activities.All(static activity => activity.OperationName == "DynamoDB.Query"), activities.Any(static activity => Tags(activity).Contains("secret", StringComparison.Ordinal)), activities[0].GetTagItem("aws.dynamodb.scan_forward")))
            .IsEqualTo((handler.Requests.Count, true, false, (object?)false));
    }

    [Test]
    public async Task PageAsync_FanOutOverAnIndex_HasTheTagsOfAQueryOfTheIndex()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{"pk":{"S":"a"},"sk":{"S":"1"},"status":{"S":"p"}}],"Count":1,"ScannedCount":3}"""));

        // Act
        var plain = await RunAsync(handler, async client => await client.Table("Users", "pk", "sk").Index("ByStatus", "status").Query("p").Limit(5).PageAsync());
        var fanOut = await RunAsync(handler, async client => await client.Table("Users", "pk", "sk").Index("ByStatus", "status").Query(["p"]).PageAsync(5));

        // Assert
        await Assert.That((fanOut.OperationName, Normal(fanOut))).IsEqualTo((plain.OperationName, Normal(plain)));
    }

    [Test]
    public async Task CountAsync_FanOut_HasOneSpanPerPartition()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Count":2,"ScannedCount":3}"""));
        using var capture = new ActivityCapture();
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Users", "pk", "sk").Query(["a", "b", "c"]).CountAsync();

        // Assert
        await Assert.That(capture.All.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ItemsAsync_MixedQuery_HasTheTagsOfTheQuery()
    {
        // Arrange
        using var handler = new StubHandler(_ => Mix.MixJson.Page(Mix.MixJson.AlphaItem));

        // Act
        var activity = await RunAsync(handler, async client => await client.Table("Users", "pk", "sk").Query("key-secret").Of<Mix.Alpha, Mix.Beta>().ItemsAsync().ToListAsync());

        // Assert
        await Assert.That((activity.OperationName, Tags(activity).Contains("key-secret", StringComparison.Ordinal))).IsEqualTo(("DynamoDB.Query", false));
    }

    [Test]
    public async Task SendAsync_GetWithoutProjection_HasNoProjectionTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Item));

        // Act
        var activity = await RunAsync(handler, async client => await client.Table("Users", "pk").Get("a").SendAsync());

        // Assert
        await Assert.That((activity.GetTagItem("aws.dynamodb.consistent_read"), activity.GetTagItem("aws.dynamodb.projection"))).IsEqualTo(((object?)false, (object?)null));
    }

    [Test]
    public async Task SendAsync_Update_NoTagCarriesAKeyAValueOrAnExpression()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var activity = await RunAsync(handler, async client => await client.Table("Users", "pk").Update("key-secret").Set("n", "value-secret").If(Attr("m").Eq("value-secret-2")).SendAsync());

        // Assert
        await Assert.That(Tags(activity)).DoesNotContain("secret").And.DoesNotContain(":v0").And.DoesNotContain("#n0");
    }

    [Test]
    public async Task SendAsync_ConsumedCapacity_ReachesTheSpan()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"ConsumedCapacity":{"TableName":"Users","CapacityUnits":1.0}}"""));

        // Act
        var activity = await RunAsync(handler, async client => await client.Table("Users", "pk").Put(new Item { ["pk"] = "a" }).ReturnCapacity().SendAsync());

        // Assert
        await Assert.That(activity.GetTagItem("aws.dynamodb.consumed_capacity") is string[] { Length: 1 }).IsTrue();
    }

    [Test]
    public async Task SendAsync_ReturnOfOneAttribute_KeepsTheCapacityForTheSpan()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"n":{"N":"1"}},"ConsumedCapacity":{"TableName":"Users","CapacityUnits":1.0}}"""));

        // Act
        var activity = await RunAsync(handler, async client => await client.Table("Users", "pk").Update("a").Add("n", 1).ReturnCapacity().Return(Attr<int>("n")).SendAsync());

        // Assert
        await Assert.That(activity.GetTagItem("aws.dynamodb.consumed_capacity") is string[] { Length: 1 }).IsTrue();
    }

    [Test]
    public async Task SendAsync_ConsumedCapacity_ReachesTheMetricWithOperationAndTable()
    {
        // Arrange
        var table = TestClient.UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, "dynasharp.client.consumed_capacity");
        using var handler = new StubHandler(_ => StubResponse.Json("{\"ConsumedCapacity\":{\"TableName\":\"" + table + "\",\"CapacityUnits\":2.0}}"));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table(table, "pk").Delete("a").ReturnCapacity().SendAsync();

        // Assert
        var measurement = capture.All.Single();
        await Assert.That((measurement.Value, measurement.Tags["db.operation.name"])).IsEqualTo((2.0, (object?)"DeleteItem"));
    }

    [Test]
    public async Task SendAsync_Duration_ReachesTheMetric()
    {
        // Arrange
        var table = TestClient.UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, "db.client.operation.duration");
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table(table, "pk").Update("a").Set("n", 1).SendAsync();

        // Assert
        await Assert.That(capture.All.Single().Tags["db.operation.name"]).IsEqualTo("UpdateItem");
    }

    [Test]
    public async Task SendAsync_Failure_MarksTheSpanWithTheErrorCode()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "No."));

        // Act
        var activity = await RunAsync(handler, async client =>
        {
            try
            {
                await client.Table("Users", "pk").Update("a").Set("n", 1).SendAsync();
            }
            catch (ValidationException)
            {
                // The span is what the test looks at.
            }
        });

        // Assert
        await Assert.That((activity.Status, activity.GetTagItem("error.type"))).IsEqualTo((ActivityStatusCode.Error, (object?)"ValidationException"));
    }

    [Test]
    public async Task SendAsync_ItemThatDoesNotFit_LeavesItsKeyOutOfTheSpan()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"pk":{"S":"TENANT#acme"},"sk":{"S":"INV#2026-09#000002"},"_type":{"S":"INVOICE"},"Status":{"S":"Refunded"}}}"""));

        // Act
        var activity = await RunAsync(handler, async client =>
        {
            try
            {
                await client.Table<TenantInvoice>("Users").Get("TENANT#acme", "INV#2026-09#000002").SendAsync();
            }
            catch (DynamoDbMappingException)
            {
                // The span is what the test looks at.
            }
        });

        // Assert
        await Assert.That(activity.TagObjects.Where(static tag => tag.Value?.ToString()?.Contains("acme", StringComparison.Ordinal) == true)).IsEmpty();
    }

    [Test]
    public async Task SendAsync_BatchWrite_HasTheTagsOfTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        var items = new Dictionary<string, IReadOnlyList<WriteRequest>> { ["Users"] = [new WriteRequest { DeleteRequest = new DeleteRequest { Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" } } }] };

        // Act
        var request = await RunAsync(handler, client => client.BatchWriteItemAsync(new BatchWriteItemRequest { RequestItems = items }));
        var command = await RunAsync(handler, async client => await client.Table("Users", "pk").BatchWrite().Delete("a").SendAsync());

        // Assert
        await Assert.That((command.OperationName, Normal(command))).IsEqualTo((request.OperationName, Normal(request)));
    }

    [Test]
    public async Task SendAsync_BatchWriteOnTwoTables_HasTheTagsOfTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        static WriteRequest Write() => new() { DeleteRequest = new DeleteRequest { Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" } } };
        var items = new Dictionary<string, IReadOnlyList<WriteRequest>> { ["Users"] = [Write()], ["Audit"] = [Write()] };

        // Act
        var request = await RunAsync(handler, client => client.BatchWriteItemAsync(new BatchWriteItemRequest { RequestItems = items }));
        var command = await RunAsync(handler, async client => await client.Table("Users", "pk").BatchWrite().Delete("a").Delete(client.Table("Audit", "pk"), "a").SendAsync());

        // Assert
        await Assert.That(Normal(command)).IsEqualTo(Normal(request));
    }

    [Test]
    public async Task SendAsync_BatchGet_HasTheTagsOfTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        var items = new Dictionary<string, KeysAndAttributes> { ["Users"] = new KeysAndAttributes { Keys = [new Dictionary<string, AttributeValue> { ["pk"] = "a" }] } };

        // Act
        var request = await RunAsync(handler, client => client.BatchGetItemAsync(new BatchGetItemRequest { RequestItems = items }));
        var command = await RunAsync(handler, async client => await client.Table("Users", "pk").BatchGet().Key("a").SendAsync());

        // Assert
        await Assert.That((command.OperationName, Normal(command))).IsEqualTo((request.OperationName, Normal(request)));
    }

    [Test]
    public async Task SendAsync_Transaction_HasTheTagsOfTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        var key = new Dictionary<string, AttributeValue> { ["pk"] = "a" };

        // Act
        var request = await RunAsync(handler, client => client.TransactWriteItemsAsync(new TransactWriteItemsRequest { TransactItems = [new TransactWriteItem { Delete = new TransactDelete { TableName = "Users", Key = key } }, new TransactWriteItem { Delete = new TransactDelete { TableName = "Audit", Key = key } }] }));
        var command = await RunAsync(handler, async client => await client.TransactWrite().Delete(client.Table("Users", "pk"), "a").Delete(client.Table("Audit", "pk"), "a").SendAsync());

        // Assert
        await Assert.That((command.OperationName, Normal(command))).IsEqualTo((request.OperationName, Normal(request)));
    }

    [Test]
    public async Task SendAsync_TransactGet_HasTheTagsOfTheRequestApi()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        var key = new Dictionary<string, AttributeValue> { ["pk"] = "a" };

        // Act
        var request = await RunAsync(handler, client => client.TransactGetItemsAsync(new TransactGetItemsRequest { TransactItems = [new TransactGetItem { Get = new TransactGet { TableName = "Users", Key = key } }] }));
        var command = await RunAsync(handler, async client => await client.TransactGet().Get(client.Table("Users", "pk"), "a", default, out _).SendAsync());

        // Assert
        await Assert.That((command.OperationName, Normal(command))).IsEqualTo((request.OperationName, Normal(request)));
    }

    [Test]
    public async Task SendAsync_BatchOfSeveralRequests_MakesOneSpanPerRequest()
    {
        // Arrange
        var table = TestClient.UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, "db.client.operation.duration");
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var ids = Enumerable.Range(0, 26).Select(static i => "item-" + i).ToList();

        // Act
        await SendEachAsync(client.Table(table, "pk"), ids);

        // Assert
        await Assert.That(capture.All.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SendAsync_BatchOfTwoTablesInTwoRequests_NamesTheTablesOfEachRequest()
    {
        // Arrange (the first request holds the first table alone, so the span and the metric have that table)
        var table = TestClient.UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, "db.client.operation.duration");
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var ids = Enumerable.Range(0, 25).Select(static i => "item-" + i).ToList();

        // Act
        await SendEachAsync(client.Table(table, "pk"), ids, client.Table(TestClient.UniqueTable(), "pk"));

        // Assert
        await Assert.That(capture.All.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_BatchCapacity_ReachesTheSpanAndTheMetricPerRequest()
    {
        // Arrange
        var table = TestClient.UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, "dynasharp.client.consumed_capacity");
        using var handler = new StubHandler(_ => StubResponse.Json("{\"ConsumedCapacity\":[{\"TableName\":\"" + table + "\",\"CapacityUnits\":3.0}]}"));
        using var client = TestClient.Create(handler);
        var ids = Enumerable.Range(0, 26).Select(static i => "item-" + i).ToList();

        // Act
        await SendEachAsync(client.Table(table, "pk"), ids);

        // Assert
        await Assert.That(capture.All.Sum(static measurement => measurement.Value)).IsEqualTo(6.0);
    }

    [Test]
    public async Task SendAsync_TransactionCapacity_ReachesTheSpan()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"ConsumedCapacity":[{"TableName":"Users","CapacityUnits":2.0}]}"""));

        // Act
        var activity = await RunAsync(handler, async client => await client.TransactWrite().Delete(client.Table("Users", "pk"), "a").ReturnCapacity().SendAsync());

        // Assert
        await Assert.That(activity.GetTagItem("aws.dynamodb.consumed_capacity") is string[] { Length: 1 }).IsTrue();
    }

    [Test]
    public async Task SendAsync_Batches_NoTagCarriesAKeyOrAValue()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var write = await RunAsync(handler, async client => await client.Table("Users", "pk").BatchWrite().Put(new Item { ["pk"] = "key-secret", ["n"] = "value-secret" }).Delete("key-secret-2").SendAsync());
        var get = await RunAsync(handler, async client => await client.Table("Users", "pk").BatchGet().Key("key-secret").SendAsync());
        var transaction = await RunAsync(handler, async client => await client.TransactWrite().Update(client.Table("Users", "pk"), "key-secret").Set("n", "value-secret").SendAsync());

        // Assert
        await Assert.That(Tags(write) + Tags(get) + Tags(transaction)).DoesNotContain("secret");
    }

    [Test]
    public async Task SendAsync_BatchThatGivesUp_LogsAWarningWithoutItems()
    {
        // Arrange
        using var factory = new CapturingLoggerFactory();
        using var handler = new StubHandler(BatchStubs.LeaveAllWrites);
        using var client = TestClient.Create(handler, loggerFactory: factory, random: FixedRandom.Zero);

        // Act
        await TestClient.CaptureAsync<BatchIncompleteException>(async () => await client.Table("Users", "pk").BatchWrite().Delete("key-secret").SendAsync());

        // Assert
        var entry = factory.Entries.Single(static entry => entry.EventId.Id == 7);
        await Assert.That((entry.Level, entry.Message.Contains("BatchWriteItem on table Users", StringComparison.Ordinal), entry.Message.Contains("secret", StringComparison.Ordinal))).IsEqualTo((Microsoft.Extensions.Logging.LogLevel.Warning, true, false));
    }

    private static ValueTask<BatchWriteResult> SendEachAsync(Table<Item> table, List<string> ids, Table<Item>? other = null)
    {
        var command = table.BatchWrite().ReturnCapacity();
        foreach (var id in ids)
        {
            command = command.Delete(id);
        }

        return (other is null ? command : command.Delete(other, "x")).SendAsync();
    }

    // The tags without what differs between two runs by nature: the request id of the stub.
    private static string Normal(Activity activity) => string.Join(',', activity.TagObjects.Where(tag => tag.Key != "aws.request_id").Select(tag => $"{tag.Key}={(tag.Value is string[] names ? string.Join(',', names) : tag.Value)}"));
}
