using System.Diagnostics;
using System.Net;
using DynaSharp.Requests;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Telemetry.ActivityCapture;
using static DynaSharp.UnitTests.TestClient;

namespace DynaSharp.UnitTests.Telemetry;

public sealed class ActivityTests
{
    private const string CannedGetItem = TestClient.GetItemResponseJson;

    [Test]
    public async Task GetItemAsync_Listener_StartsAnActivityNamedAfterServiceAndOperation()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(CannedGetItem));

        // Act
        var activity = await RunAsync(handler, client => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(activity.OperationName).IsEqualTo("DynamoDB.GetItem");
    }

    [Test]
    public async Task PutItemAsync_Listener_StartsAnActivityNamedAfterServiceAndOperation()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var activity = await RunAsync(handler, client => client.PutItemAsync(TestClient.PutItem()));

        // Assert
        await Assert.That(activity.OperationName).IsEqualTo("DynamoDB.PutItem");
    }

    [Test]
    public async Task DescribeTableAsync_Listener_StartsAnActivityNamedAfterServiceAndOperation()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Table":{"TableName":"Users"}}"""));

        // Act
        var activity = await RunAsync(handler, client => client.DescribeTableAsync(TestClient.DescribeTable()));

        // Assert
        await Assert.That(activity.OperationName).IsEqualTo("DynamoDB.DescribeTable");
    }

    [Test]
    public async Task GetItemAsync_Listener_ActivityIsAClientSpan()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(CannedGetItem));

        // Act
        var activity = await RunAsync(handler, client => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(activity.Kind).IsEqualTo(ActivityKind.Client);
    }

    [Test]
    public async Task GetItemAsync_Listener_ActivityIsAChildOfTheCurrentActivity()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(CannedGetItem));
        using var capture = new ActivityCapture();
        using var client = TestClient.Create(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(capture.Single().TraceId).IsEqualTo(capture.ParentTraceId);
    }

    [Test]
    public async Task GetItemAsync_Listener_ActivityIsStoppedWhenTheCallCompletes()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(CannedGetItem));
        using var capture = new ActivityCapture();
        using var client = TestClient.Create(handler);
        var parent = Activity.Current;

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(Activity.Current).IsSameReferenceAs(parent);
    }

    [Test]
    [Arguments("db.system.name", "aws.dynamodb")]
    [Arguments("db.operation.name", "GetItem")]
    [Arguments("db.collection.name", "Users")]
    [Arguments("rpc.system", "aws-api")]
    [Arguments("rpc.system.name", "aws-api")]
    [Arguments("rpc.service", "DynamoDB")]
    [Arguments("rpc.method", "GetItem")]
    [Arguments("cloud.region", "eu-central-2")]
    [Arguments("server.address", "dynamodb.eu-central-2.amazonaws.com")]
    public async Task GetItemAsync_Listener_SetsTheTag(string name, string expected)
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(CannedGetItem));

        // Act
        var activity = await RunAsync(handler, client => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(activity.GetTagItem(name)).IsEqualTo(expected);
    }

    [Test]
    public async Task GetItemAsync_Listener_TableNamesIsASingleElementArray()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(CannedGetItem));

        // Act
        var activity = await RunAsync(handler, client => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(string.Join(',', (string[])activity.GetTagItem("aws.dynamodb.table_names")!)).IsEqualTo("Users");
    }

    [Test]
    public async Task GetItemAsync_DefaultPort_HasNoServerPortTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(CannedGetItem));

        // Act
        var activity = await RunAsync(handler, client => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(activity.GetTagItem("server.port")).IsNull();
    }

    [Test]
    public async Task GetItemAsync_EndpointWithPort_SetsTheServerPortTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(CannedGetItem));

        // Act
        var activity = await RunAsync(handler, client => client.GetItemAsync(TestClient.GetItem()), serviceUrl: new Uri("http://localhost:8000"));

        // Assert
        await Assert.That(activity.GetTagItem("server.port")).IsEqualTo(8000);
    }

    [Test]
    public async Task GetItemAsync_ConsistentRead_SetsTheTagToTrue()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(CannedGetItem));
        var request = TestClient.GetItem() with { ConsistentRead = true };

        // Act
        var activity = await RunAsync(handler, client => client.GetItemAsync(request));

        // Assert
        await Assert.That((bool)activity.GetTagItem("aws.dynamodb.consistent_read")!).IsTrue();
    }

    [Test]
    public async Task GetItemAsync_EventuallyConsistentRead_SetsTheTagToFalse()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(CannedGetItem));

        // Act
        var activity = await RunAsync(handler, client => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((bool)activity.GetTagItem("aws.dynamodb.consistent_read")!).IsFalse();
    }

    [Test]
    public async Task GetItemAsync_ProjectionExpression_SetsTheProjectionTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(CannedGetItem));
        var request = TestClient.GetItem() with { ProjectionExpression = "#n, Address.City" };

        // Act
        var activity = await RunAsync(handler, client => client.GetItemAsync(request));

        // Assert
        await Assert.That(activity.GetTagItem("aws.dynamodb.projection")).IsEqualTo("#n, Address.City");
    }

    [Test]
    public async Task PutItemAsync_Listener_HasNoReadTags()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var activity = await RunAsync(handler, client => client.PutItemAsync(TestClient.PutItem()));

        // Assert
        await Assert.That(activity.GetTagItem("aws.dynamodb.consistent_read")).IsNull();
    }

    [Test]
    public async Task GetItemAsync_ResponseWithRequestId_SetsTheRequestIdTag()
    {
        // Arrange
        var headers = new Dictionary<string, string> { ["x-amzn-RequestId"] = "REQ-123" };
        using var handler = new StubHandler(_ => StubResponse.Json(CannedGetItem, headers: headers));

        // Act
        var activity = await RunAsync(handler, client => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(activity.GetTagItem("aws.request_id")).IsEqualTo("REQ-123");
    }

    [Test]
    public async Task GetItemAsync_ErrorResponseWithRequestId_SetsTheRequestIdTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "bad", requestId: "REQ-456"));

        // Act
        var activity = await RunAsync(handler, client => TestClient.CaptureAsync<ValidationException>(() => client.GetItemAsync(TestClient.GetItem())));

        // Assert
        await Assert.That(activity.GetTagItem("aws.request_id")).IsEqualTo("REQ-456");
    }

    [Test]
    public async Task GetItemAsync_ResponseWithoutRequestId_HasNoRequestIdTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(CannedGetItem));

        // Act
        var activity = await RunAsync(handler, client => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(activity.GetTagItem("aws.request_id")).IsNull();
    }

    [Test]
    public async Task GetItemAsync_Success_StatusIsUnset()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(CannedGetItem));

        // Act
        var activity = await RunAsync(handler, client => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(activity.Status).IsEqualTo(ActivityStatusCode.Unset);
    }

    [Test]
    public async Task GetItemAsync_Success_HasNoErrorTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(CannedGetItem));

        // Act
        var activity = await RunAsync(handler, client => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(activity.GetTagItem("error.type")).IsNull();
    }

    [Test]
    public async Task GetItemAsync_DynamoDbError_StatusIsError()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "Requested resource not found"));

        // Act
        var activity = await RunAsync(handler, client => TestClient.CaptureAsync<ResourceNotFoundException>(() => client.GetItemAsync(TestClient.GetItem())));

        // Assert
        await Assert.That(activity.Status).IsEqualTo(ActivityStatusCode.Error);
    }

    [Test]
    public async Task GetItemAsync_DynamoDbError_StatusHasNoDescription()
    {
        // Arrange (service messages can quote the request)
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "Requested resource not found"));

        // Act
        var activity = await RunAsync(handler, client => TestClient.CaptureAsync<ResourceNotFoundException>(() => client.GetItemAsync(TestClient.GetItem())));

        // Assert
        await Assert.That(activity.StatusDescription).IsNull();
    }

    [Test]
    public async Task GetItemAsync_DynamoDbError_ErrorTypeIsTheErrorCode()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "Requested resource not found"));

        // Act
        var activity = await RunAsync(handler, client => TestClient.CaptureAsync<ResourceNotFoundException>(() => client.GetItemAsync(TestClient.GetItem())));

        // Assert
        await Assert.That(activity.GetTagItem("error.type")).IsEqualTo("ResourceNotFoundException");
    }

    [Test]
    public async Task GetItemAsync_DynamoDbError_ResponseStatusCodeIsTheErrorCode()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "Requested resource not found"));

        // Act
        var activity = await RunAsync(handler, client => TestClient.CaptureAsync<ResourceNotFoundException>(() => client.GetItemAsync(TestClient.GetItem())));

        // Assert
        await Assert.That(activity.GetTagItem("db.response.status_code")).IsEqualTo("ResourceNotFoundException");
    }

    [Test]
    public async Task GetItemAsync_UnrecognizedResponse_ErrorTypeIsTheDocumentedCode()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("<html></html>", HttpStatusCode.ServiceUnavailable));

        // Act
        var activity = await RunAsync(handler, client => TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem())));

        // Assert
        await Assert.That(activity.GetTagItem("error.type")).IsEqualTo("UnrecognizedResponse");
    }

    [Test]
    public async Task GetItemAsync_UnrecognizedResponse_HasNoResponseStatusCode()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("<html></html>", HttpStatusCode.ServiceUnavailable));

        // Act
        var activity = await RunAsync(handler, client => TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem())));

        // Assert
        await Assert.That(activity.GetTagItem("db.response.status_code")).IsNull();
    }

    [Test]
    public async Task GetItemAsync_NetworkFailure_ErrorTypeIsTheExceptionType()
    {
        // Arrange
        using var handler = new StubHandler(_ => throw new HttpRequestException("Connection refused"));

        // Act
        var activity = await RunAsync(handler, client => TestClient.CaptureAsync<HttpRequestException>(() => client.GetItemAsync(TestClient.GetItem())));

        // Assert
        await Assert.That(activity.GetTagItem("error.type")).IsEqualTo("System.Net.Http.HttpRequestException");
    }

    [Test]
    public async Task GetItemAsync_NetworkFailure_HasNoResponseStatusCode()
    {
        // Arrange
        using var handler = new StubHandler(_ => throw new HttpRequestException("Connection refused"));

        // Act
        var activity = await RunAsync(handler, client => TestClient.CaptureAsync<HttpRequestException>(() => client.GetItemAsync(TestClient.GetItem())));

        // Assert
        await Assert.That(activity.GetTagItem("db.response.status_code")).IsNull();
    }

    [Test]
    public async Task GetItemAsync_Timeout_ErrorTypeIsTimeout()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, cancellationToken) =>
        {
            arrived.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return StubResponse.Json("{}");
        });

        async Task TimeOut(DynamoDbClient client)
        {
            var call = client.GetItemAsync(TestClient.GetItem());
            await arrived.Task;
            time.Advance(TimeSpan.FromSeconds(30));
            await TestClient.CaptureAsync<TimeoutException>(() => call);
        }

        // Act
        var activity = await RunAsync(handler, TimeOut, time);

        // Assert
        await Assert.That(activity.GetTagItem("error.type")).IsEqualTo("timeout");
    }

    [Test]
    public async Task GetItemAsync_CallerCancels_ErrorTypeIsTheCancellationException()
    {
        // Arrange
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, cancellationToken) =>
        {
            arrived.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return StubResponse.Json("{}");
        });
        using var cancellation = new CancellationTokenSource();

        // Act
        var activity = await RunAsync(handler, async client =>
        {
            var call = client.GetItemAsync(TestClient.GetItem(), cancellation.Token);
            await arrived.Task;
            await cancellation.CancelAsync();
            await TestClient.CaptureAsync<OperationCanceledException>(() => call);
        });

        // Assert
        await Assert.That(activity.GetTagItem("error.type")).IsEqualTo("System.OperationCanceledException");
    }

    [Test]
    public async Task GetItemAsync_PropagationOnlySampling_RecordsNoTags()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(CannedGetItem));

        // Act
        var activity = await RunAsync(handler, client => client.GetItemAsync(TestClient.GetItem()), sampling: ActivitySamplingResult.PropagationData);

        // Assert
        await Assert.That(activity.TagObjects).IsEmpty();
    }

    [Test]
    public async Task GetItemAsync_PropagationOnlySamplingAndError_StillSetsTheErrorStatus()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "bad"));

        // Act
        var activity = await RunAsync(handler, client => TestClient.CaptureAsync<ValidationException>(() => client.GetItemAsync(TestClient.GetItem())), sampling: ActivitySamplingResult.PropagationData);

        // Assert
        await Assert.That(activity.Status).IsEqualTo(ActivityStatusCode.Error);
    }

    [Test]
    public async Task GetItemAsync_KeyValues_AreNotInAnyTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"pk":{"S":"SECRET-KEY-VALUE"},"name":{"S":"SECRET-ATTRIBUTE-VALUE"}}}"""));
        var request = TestClient.GetItem(key: "SECRET-KEY-VALUE");

        // Act
        var activity = await RunAsync(handler, client => client.GetItemAsync(request));

        // Assert
        await Assert.That(Describe(activity)).DoesNotContain("SECRET");
    }

    [Test]
    public async Task PutItemAsync_ItemAndConditionValues_AreNotInAnyTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ConditionalCheckFailedException", "The conditional request failed"));
        var request = new PutItemRequest
        {
            TableName = "Users",
            Item = new Dictionary<string, AttributeValue> { ["pk"] = "SECRET-KEY-VALUE", ["ssn"] = "SECRET-ATTRIBUTE-VALUE" },
            ConditionExpression = "ssn <> :ssn",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":ssn"] = "SECRET-CONDITION-VALUE" },
        };

        // Act
        var activity = await RunAsync(handler, client => TestClient.CaptureAsync<ConditionalCheckFailedException>(() => client.PutItemAsync(request)));

        // Assert
        await Assert.That(Describe(activity)).DoesNotContain("SECRET");
    }

    [Test]
    public async Task QueryAsync_Listener_StartsAnActivityNamedAfterServiceAndOperation()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Count":0,"ScannedCount":0}"""));

        // Act
        var activity = await RunAsync(handler, client => client.QueryAsync(CreateQuery()));

        // Assert
        await Assert.That(activity.OperationName).IsEqualTo("DynamoDB.Query");
    }

    [Test]
    public async Task ScanAsync_Listener_StartsAnActivityNamedAfterServiceAndOperation()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Count":0,"ScannedCount":0}"""));

        // Act
        var activity = await RunAsync(handler, client => client.ScanAsync(new ScanRequest { TableName = "Users" }));

        // Assert
        await Assert.That(activity.OperationName).IsEqualTo("DynamoDB.Scan");
    }

    [Test]
    public async Task UpdateItemAsync_Listener_StartsAnActivityNamedAfterServiceAndOperation()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        var request = new UpdateItemRequest { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-1" } };

        // Act
        var activity = await RunAsync(handler, client => client.UpdateItemAsync(request));

        // Assert
        await Assert.That(activity.OperationName).IsEqualTo("DynamoDB.UpdateItem");
    }

    [Test]
    public async Task DeleteItemAsync_Listener_StartsAnActivityNamedAfterServiceAndOperation()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        var request = new DeleteItemRequest { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-1" } };

        // Act
        var activity = await RunAsync(handler, client => client.DeleteItemAsync(request));

        // Assert
        await Assert.That(activity.OperationName).IsEqualTo("DynamoDB.DeleteItem");
    }

    [Test]
    public async Task QueryAsync_EveryReadOption_SetsTheSemanticConventionTags()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Count":0,"ScannedCount":0}"""));
        var request = CreateQuery() with
        {
            IndexName = "ByStatus",
            ConsistentRead = true,
            ProjectionExpression = "#s",
            Limit = 25,
            Select = SelectKind.SpecificAttributes,
            ScanIndexForward = false,
        };

        // Act
        var activity = await RunAsync(handler, client => client.QueryAsync(request));

        // Assert
        await Assert.That(SemanticConventionTags(activity)).IsEqualTo(
            "aws.dynamodb.consistent_read=True,aws.dynamodb.count=0,aws.dynamodb.index_name=ByStatus,aws.dynamodb.limit=25,aws.dynamodb.projection=#s,aws.dynamodb.scan_forward=False,aws.dynamodb.scanned_count=0,aws.dynamodb.select=SPECIFIC_ATTRIBUTES,aws.dynamodb.table_names=Users");
    }

    [Test]
    public async Task ScanAsync_ParallelScan_SetsTheSegmentTags()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Count":0,"ScannedCount":0}"""));
        var request = new ScanRequest { TableName = "Users", Segment = 2, TotalSegments = 8, Limit = 1000, Select = SelectKind.Count };

        // Act
        var activity = await RunAsync(handler, client => client.ScanAsync(request));

        // Assert
        await Assert.That(SemanticConventionTags(activity)).IsEqualTo(
            "aws.dynamodb.consistent_read=False,aws.dynamodb.count=0,aws.dynamodb.limit=1000,aws.dynamodb.scanned_count=0,aws.dynamodb.segment=2,aws.dynamodb.select=COUNT,aws.dynamodb.table_names=Users,aws.dynamodb.total_segments=8");
    }

    [Test]
    public async Task UpdateItemAsync_Listener_SetsOnlyTheTableNameFromTheAwsDynamoDbTags()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        var request = new UpdateItemRequest { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-1" } };

        // Act
        var activity = await RunAsync(handler, client => client.UpdateItemAsync(request));

        // Assert
        await Assert.That(SemanticConventionTags(activity)).IsEqualTo("aws.dynamodb.table_names=Users");
    }

    [Test]
    public async Task QueryAsync_KeyConditionFilterValuesAndStartKey_AreNotInAnyTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{"pk":{"S":"SECRET-RESULT-VALUE"}}],"Count":1,"ScannedCount":1,"LastEvaluatedKey":{"pk":{"S":"SECRET-RESULT-KEY"}}}"""));
        var request = new QueryRequest
        {
            TableName = "Users",
            KeyConditionExpression = "pk = :SECRET-KEY-CONDITION",
            FilterExpression = "ssn = :SECRET-FILTER",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#s"] = "SECRET-NAME" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":SECRET-KEY-CONDITION"] = "SECRET-KEY-VALUE", [":SECRET-FILTER"] = "SECRET-FILTER-VALUE" },
            ExclusiveStartKey = new Dictionary<string, AttributeValue> { ["pk"] = "SECRET-START-KEY" },
        };

        // Act
        var activity = await RunAsync(handler, client => client.QueryAsync(request));

        // Assert
        await Assert.That(Describe(activity)).DoesNotContain("SECRET");
    }

    [Test]
    public async Task ScanAsync_FilterValuesAndStartKey_AreNotInAnyTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{"pk":{"S":"SECRET-RESULT-VALUE"}}],"Count":1,"ScannedCount":1}"""));
        var request = new ScanRequest
        {
            TableName = "Users",
            FilterExpression = "ssn = :SECRET-FILTER",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":SECRET-FILTER"] = "SECRET-FILTER-VALUE" },
            ExclusiveStartKey = new Dictionary<string, AttributeValue> { ["pk"] = "SECRET-START-KEY" },
        };

        // Act
        var activity = await RunAsync(handler, client => client.ScanAsync(request));

        // Assert
        await Assert.That(Describe(activity)).DoesNotContain("SECRET");
    }

    [Test]
    public async Task UpdateItemAsync_ExpressionsKeyAndValues_AreNotInAnyTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"ssn":{"S":"SECRET-RESULT-VALUE"}}}"""));
        var request = new UpdateItemRequest
        {
            TableName = "Users",
            Key = new Dictionary<string, AttributeValue> { ["pk"] = "SECRET-KEY-VALUE" },
            UpdateExpression = "SET ssn = :SECRET-UPDATE",
            ConditionExpression = "ssn <> :SECRET-CONDITION",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":SECRET-UPDATE"] = "SECRET-UPDATE-VALUE", [":SECRET-CONDITION"] = "SECRET-CONDITION-VALUE" },
            ReturnValues = ReturnValue.AllNew,
        };

        // Act
        var activity = await RunAsync(handler, client => client.UpdateItemAsync(request));

        // Assert
        await Assert.That(Describe(activity)).DoesNotContain("SECRET");
    }

    [Test]
    public async Task DeleteItemAsync_KeyAndConditionValues_AreNotInAnyTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"ssn":{"S":"SECRET-RESULT-VALUE"}}}"""));
        var request = new DeleteItemRequest
        {
            TableName = "Users",
            Key = new Dictionary<string, AttributeValue> { ["pk"] = "SECRET-KEY-VALUE" },
            ConditionExpression = "ssn = :SECRET-CONDITION",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":SECRET-CONDITION"] = "SECRET-CONDITION-VALUE" },
            ReturnValues = ReturnValue.AllOld,
        };

        // Act
        var activity = await RunAsync(handler, client => client.DeleteItemAsync(request));

        // Assert
        await Assert.That(Describe(activity)).DoesNotContain("SECRET");
    }

    [Test]
    public async Task GetItemAsync_SourceWithoutListener_ReturnsTheResult()
    {
        // Arrange (a disposed source starts no activities, as when nobody listens)
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler, activitySource: UnlistenedSource());

        // Act
        var response = await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(response.Item!["pk"].AsString()).IsEqualTo("user-1");
    }

    [Test]
    public async Task GetItemAsync_SourceWithoutListenerAndError_ThrowsTheError()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "Requested resource not found"));
        using var client = TestClient.Create(handler, activitySource: UnlistenedSource());

        // Act
        var act = async () => await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(act).ThrowsExactly<ResourceNotFoundException>();
    }

    [Test]
    public async Task GetItemAsync_SourceWithoutListener_StartsNoActivity()
    {
        // Arrange (the premise of the two tests above)
        using var source = new ActivitySource("DynaSharp.Test." + Guid.NewGuid().ToString("N"));
        source.Dispose();

        // Act
        using var activity = source.StartActivity("DynamoDB.GetItem");

        // Assert
        await Assert.That(activity).IsNull();
    }

    private static ActivitySource UnlistenedSource()
    {
        // The test host listens to every source it can see, so only a disposed source is guaranteed to start no activities.
        var source = new ActivitySource("DynaSharp.Test." + Guid.NewGuid().ToString("N"));
        source.Dispose();
        return source;
    }

    private static string SemanticConventionTags(Activity activity) =>
        string.Join(',', activity.TagObjects.Where(tag => tag.Key.StartsWith("aws.dynamodb.", StringComparison.Ordinal)).OrderBy(tag => tag.Key, StringComparer.Ordinal).Select(tag => tag.Key + "=" + (tag.Value is string[] names ? string.Join('|', names) : tag.Value)));

    private static string Describe(Activity activity) =>
        string.Join('\n', activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}").Append(activity.DisplayName).Append(activity.StatusDescription));
}
