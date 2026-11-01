using DynaSharp.Requests;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests;

/// <summary>
/// <c>ListTablesAsync</c>, <c>DescribeTimeToLiveAsync</c> and <c>UpdateTableAsync</c> end to end over a <see cref="StubHandler"/>: what
/// goes over the wire, what comes back, and that the pooled buffers are returned.
/// </summary>
public sealed class DynamoDbClientTableManagementTests
{
    private const string UpdatingOrders = """{"TableDescription":{"TableName":"Orders","TableStatus":"UPDATING","BillingModeSummary":{"BillingMode":"PAY_PER_REQUEST"}}}""";

    [Test]
    public async Task ListTablesAsync_Request_SendsTheListTablesOperationWithTheRequestBody()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TableNames":[]}"""));
        using var client = TestClient.Create(handler);

        // Act
        await client.ListTablesAsync(new ListTablesRequest { ExclusiveStartTableName = "Forum", Limit = 3 });

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That((sent.Operation, sent.Headers["X-Amz-Target"], sent.BodyText)).IsEqualTo((
            "ListTables",
            "DynamoDB_20120810.ListTables",
            """{"ExclusiveStartTableName":"Forum","Limit":3}"""));
    }

    [Test]
    public async Task ListTablesAsync_Page_ReturnsTheNamesAndTheLastEvaluatedTableName()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"LastEvaluatedTableName":"Reply","TableNames":["Forum","ProductCatalog","Reply"]}"""));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.ListTablesAsync(TestClient.ListTables());

        // Assert
        await Assert.That((string.Join(",", response.TableNames), response.LastEvaluatedTableName)).IsEqualTo(("Forum,ProductCatalog,Reply", (string?)"Reply"));
    }

    [Test]
    public async Task ListTablesAsync_Success_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TableNames":["a-table"]}"""));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await client.ListTablesAsync(TestClient.ListTables());

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((0, 2));
    }

    [Test]
    public async Task ListTablesAsync_LimitOutOfRange_ThrowsValidationExceptionWithoutATable()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "1 validation error detected: Value '0' at 'limit' failed to satisfy constraint"));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<ValidationException>(() => client.ListTablesAsync(new ListTablesRequest { Limit = 0 }));

        // Assert
        await Assert.That((exception.OperationName, exception.TableName)).IsEqualTo(("ListTables", (string?)null));
    }

    [Test]
    public async Task ListTablesAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var client = TestClient.Create(new StubHandler());

        // Act
        var act = async () => await client.ListTablesAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }

    [Test]
    public async Task ListTablesAsync_Disposed_ThrowsObjectDisposedException()
    {
        // Arrange
        var client = TestClient.Create(new StubHandler());
        client.Dispose();

        // Act
        var act = async () => await client.ListTablesAsync(TestClient.ListTables());

        // Assert
        await Assert.That(act).ThrowsExactly<ObjectDisposedException>();
    }

    [Test]
    public async Task DescribeTimeToLiveAsync_Request_SendsTheDescribeTimeToLiveOperationWithTheTableName()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TimeToLiveDescription":{"TimeToLiveStatus":"DISABLED"}}"""));
        using var client = TestClient.Create(handler);

        // Act
        await client.DescribeTimeToLiveAsync(TestClient.DescribeTimeToLive());

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That((sent.Operation, sent.BodyText)).IsEqualTo(("DescribeTimeToLive", """{"TableName":"Users"}"""));
    }

    [Test]
    public async Task DescribeTimeToLiveAsync_Enabled_ReturnsTheStatusAndTheAttribute()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TimeToLiveDescription":{"AttributeName":"ttl","TimeToLiveStatus":"ENABLED"}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.DescribeTimeToLiveAsync(TestClient.DescribeTimeToLive());

        // Assert
        await Assert.That((response.TimeToLiveDescription?.TimeToLiveStatus, response.TimeToLiveDescription?.AttributeName)).IsEqualTo(((TimeToLiveStatus?)TimeToLiveStatus.Enabled, "ttl"));
    }

    [Test]
    public async Task DescribeTimeToLiveAsync_Success_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await client.DescribeTimeToLiveAsync(TestClient.DescribeTimeToLive());

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((0, 2));
    }

    [Test]
    public async Task DescribeTimeToLiveAsync_MissingTable_ThrowsResourceNotFoundExceptionNamingTheCall()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "Requested resource not found"));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<ResourceNotFoundException>(() => client.DescribeTimeToLiveAsync(TestClient.DescribeTimeToLive("Orders")));

        // Assert
        await Assert.That((exception.OperationName, exception.TableName)).IsEqualTo(("DescribeTimeToLive", (string?)"Orders"));
    }

    [Test]
    public async Task DescribeTimeToLiveAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var client = TestClient.Create(new StubHandler());

        // Act
        var act = async () => await client.DescribeTimeToLiveAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }

    [Test]
    public async Task DescribeTimeToLiveAsync_Disposed_ThrowsObjectDisposedException()
    {
        // Arrange
        var client = TestClient.Create(new StubHandler());
        client.Dispose();

        // Act
        var act = async () => await client.DescribeTimeToLiveAsync(TestClient.DescribeTimeToLive());

        // Assert
        await Assert.That(act).ThrowsExactly<ObjectDisposedException>();
    }

    [Test]
    public async Task UpdateTableAsync_Request_SendsTheUpdateTableOperationWithTheRequestBody()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(UpdatingOrders));
        using var client = TestClient.Create(handler);

        // Act
        await client.UpdateTableAsync(TestClient.UpdateTable("Orders"));

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That((sent.Operation, sent.Headers["X-Amz-Target"], sent.BodyText)).IsEqualTo((
            "UpdateTable",
            "DynamoDB_20120810.UpdateTable",
            """{"TableName":"Orders","BillingMode":"PAY_PER_REQUEST"}"""));
    }

    [Test]
    public async Task UpdateTableAsync_Updating_ReturnsTheTableInTheUpdatingState()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(UpdatingOrders));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.UpdateTableAsync(TestClient.UpdateTable("Orders"));

        // Assert
        await Assert.That((response.TableDescription.TableStatus, response.TableDescription.BillingModeSummary?.BillingMode)).IsEqualTo((TableStatus.Updating, (BillingMode?)BillingMode.PayPerRequest));
    }

    [Test]
    public async Task UpdateTableAsync_Success_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json(UpdatingOrders));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await client.UpdateTableAsync(TestClient.UpdateTable("Orders"));

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((0, 2));
    }

    [Test]
    public async Task UpdateTableAsync_TableBeingUpdated_ThrowsResourceInUseExceptionAndDoesNotRetry()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError("ResourceInUseException", "Table is being updated: Orders") };
        using var client = TestClient.Create(handler, maxAttempts: 3);

        // Act
        var exception = await TestClient.CaptureAsync<ResourceInUseException>(() => client.UpdateTableAsync(TestClient.UpdateTable("Orders")));

        // Assert
        await Assert.That((exception.Message, exception.OperationName, exception.TableName, handler.Requests.Count)).IsEqualTo(("Table is being updated: Orders", (string?)"UpdateTable", (string?)"Orders", 1));
    }

    [Test]
    public async Task UpdateTableAsync_NothingToChange_ThrowsValidationException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "At least one of ProvisionedThroughput, BillingMode, UpdateStreamEnabled, GlobalSecondaryIndexUpdates or SSESpecification or ReplicaUpdates is required"));
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.UpdateTableAsync(new UpdateTableRequest { TableName = "Orders" });

        // Assert
        await Assert.That(act).ThrowsExactly<ValidationException>();
    }

    [Test]
    public async Task UpdateTableAsync_BillingModeUndefined_ThrowsArgumentOutOfRangeExceptionAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var request = TestClient.UpdateTable() with { BillingMode = (BillingMode)99 };

        // Act
        var act = async () => await client.UpdateTableAsync(request);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>();
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task UpdateTableAsync_BillingModeUndefined_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler, pool: pool);
        var request = TestClient.UpdateTable() with { BillingMode = (BillingMode)99 };

        // Act
        await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => client.UpdateTableAsync(request));

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task UpdateTableAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var client = TestClient.Create(new StubHandler());

        // Act
        var act = async () => await client.UpdateTableAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }

    [Test]
    public async Task UpdateTableAsync_Disposed_ThrowsObjectDisposedException()
    {
        // Arrange
        var client = TestClient.Create(new StubHandler());
        client.Dispose();

        // Act
        var act = async () => await client.UpdateTableAsync(TestClient.UpdateTable());

        // Assert
        await Assert.That(act).ThrowsExactly<ObjectDisposedException>();
    }
}
