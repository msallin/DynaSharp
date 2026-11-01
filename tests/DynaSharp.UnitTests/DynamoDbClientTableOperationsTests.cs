using DynaSharp.Requests;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests;

/// <summary>
/// <c>CreateTableAsync</c>, <c>DeleteTableAsync</c> and <c>UpdateTimeToLiveAsync</c> end to end over a <see cref="StubHandler"/>:
/// what goes over the wire, what comes back, and that the pooled buffers are returned.
/// </summary>
public sealed class DynamoDbClientTableOperationsTests
{
    // The response of the CreateTable example in the Amazon DynamoDB API Reference (Thread table).
    private const string ThreadTableCreating = """
        {"TableDescription":{"TableArn":"arn:aws:dynamodb:us-west-2:123456789012:table/Thread","AttributeDefinitions":[{"AttributeName":"ForumName","AttributeType":"S"}],
         "CreationDateTime":1.36372808007E9,"ItemCount":0,"KeySchema":[{"AttributeName":"ForumName","KeyType":"HASH"}],"TableName":"Thread","TableSizeBytes":0,"TableStatus":"CREATING"}}
        """;

    [Test]
    public async Task CreateTableAsync_Request_SendsTheCreateTableOperationWithTheRequestBody()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(ThreadTableCreating));
        using var client = TestClient.Create(handler);

        // Act
        await client.CreateTableAsync(TestClient.CreateTable());

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That((sent.Operation, sent.Headers["X-Amz-Target"], sent.BodyText)).IsEqualTo((
            "CreateTable",
            "DynamoDB_20120810.CreateTable",
            """{"AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"TableName":"Users","KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST"}"""));
    }

    [Test]
    public async Task CreateTableAsync_Created_ReturnsTheTableInTheCreatingState()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(ThreadTableCreating));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.CreateTableAsync(TestClient.CreateTable());

        // Assert
        await Assert.That((response.TableDescription!.TableName, response.TableDescription!.TableStatus)).IsEqualTo(("Thread", TableStatus.Creating));
    }

    [Test]
    public async Task CreateTableAsync_Success_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json(ThreadTableCreating));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await client.CreateTableAsync(TestClient.CreateTable());

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((0, 2));
    }

    [Test]
    public async Task CreateTableAsync_TableExists_ThrowsResourceInUseExceptionAndDoesNotRetry()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError("ResourceInUseException", "Cannot create preexisting table") };
        using var client = TestClient.Create(handler, maxAttempts: 3);

        // Act
        var exception = await TestClient.CaptureAsync<ResourceInUseException>(() => client.CreateTableAsync(TestClient.CreateTable()));

        // Assert
        await Assert.That((exception.Message, handler.Requests.Count)).IsEqualTo(("Cannot create preexisting table", 1));
    }

    [Test]
    public async Task CreateTableAsync_TooManyTables_ThrowsLimitExceededException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("LimitExceededException", "Subscriber limit exceeded: Only 10 tables can be created, updated, or deleted simultaneously"));
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.CreateTableAsync(TestClient.CreateTable());

        // Assert
        await Assert.That(act).ThrowsExactly<LimitExceededException>();
    }

    [Test]
    public async Task CreateTableAsync_KeyTypeNotSet_ThrowsArgumentOutOfRangeExceptionAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var request = TestClient.CreateTable() with { KeySchema = [new("pk", KeyType.Unknown)] };

        // Act
        var act = async () => await client.CreateTableAsync(request);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>();
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task CreateTableAsync_KeyTypeNotSet_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler, pool: pool);
        var request = TestClient.CreateTable() with { KeySchema = [new("pk", KeyType.Unknown)] };

        // Act
        await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => client.CreateTableAsync(request));

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task CreateTableAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var client = TestClient.Create(new StubHandler());

        // Act
        var act = async () => await client.CreateTableAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }

    [Test]
    public async Task DeleteTableAsync_Request_SendsTheDeleteTableOperationWithTheTableName()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TableDescription":{"TableName":"Users","TableStatus":"DELETING"}}"""));
        using var client = TestClient.Create(handler);

        // Act
        await client.DeleteTableAsync(TestClient.DeleteTable());

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That((sent.Operation, sent.BodyText)).IsEqualTo(("DeleteTable", """{"TableName":"Users"}"""));
    }

    [Test]
    public async Task DeleteTableAsync_Deleting_ReturnsTheTableInTheDeletingState()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TableDescription":{"TableName":"Users","TableStatus":"DELETING","DeletionProtectionEnabled":false}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.DeleteTableAsync(TestClient.DeleteTable());

        // Assert
        await Assert.That(response.TableDescription!.TableStatus).IsEqualTo(TableStatus.Deleting);
    }

    [Test]
    public async Task DeleteTableAsync_Success_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TableDescription":{"TableName":"Users"}}"""));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await client.DeleteTableAsync(TestClient.DeleteTable());

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((0, 2));
    }

    [Test]
    public async Task DeleteTableAsync_MissingTable_ThrowsResourceNotFoundException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "Requested resource not found"));
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.DeleteTableAsync(TestClient.DeleteTable());

        // Assert
        await Assert.That(act).ThrowsExactly<ResourceNotFoundException>();
    }

    [Test]
    public async Task DeleteTableAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var client = TestClient.Create(new StubHandler());

        // Act
        var act = async () => await client.DeleteTableAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }

    [Test]
    public async Task UpdateTimeToLiveAsync_Request_SendsTheUpdateTimeToLiveOperationWithTheRequestBody()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TimeToLiveSpecification":{"Enabled":true,"AttributeName":"ttl"}}"""));
        using var client = TestClient.Create(handler);

        // Act
        await client.UpdateTimeToLiveAsync(TestClient.UpdateTimeToLive());

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That((sent.Operation, sent.BodyText)).IsEqualTo(("UpdateTimeToLive", """{"TableName":"Users","TimeToLiveSpecification":{"Enabled":true,"AttributeName":"ttl"}}"""));
    }

    [Test]
    public async Task UpdateTimeToLiveAsync_Accepted_ReturnsTheSpecification()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TimeToLiveSpecification":{"Enabled":true,"AttributeName":"ttl"}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.UpdateTimeToLiveAsync(TestClient.UpdateTimeToLive());

        // Assert
        await Assert.That((response.TimeToLiveSpecification?.Enabled, response.TimeToLiveSpecification?.AttributeName)).IsEqualTo(((bool?)true, "ttl"));
    }

    [Test]
    public async Task UpdateTimeToLiveAsync_Success_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await client.UpdateTimeToLiveAsync(TestClient.UpdateTimeToLive());

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((0, 2));
    }

    [Test]
    public async Task UpdateTimeToLiveAsync_SettingAlreadyInEffect_ThrowsValidationException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "TimeToLive is already enabled"));
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.UpdateTimeToLiveAsync(TestClient.UpdateTimeToLive());

        // Assert
        await Assert.That(act).ThrowsExactly<ValidationException>();
    }

    [Test]
    public async Task UpdateTimeToLiveAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var client = TestClient.Create(new StubHandler());

        // Act
        var act = async () => await client.UpdateTimeToLiveAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }
}
