using System.Net;
using DynaSharp.Credentials;
using DynaSharp.Requests;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Exceptions;

public sealed class DynamoDbExceptionTests
{
    private const string Message = "The request failed.";
    private const string RequestId = "A1B2C3D4E5F6G7H8I9J0";

    public static IEnumerable<ExceptionCase> TypedExceptions()
    {
        yield return new("AccessDeniedException", HttpStatusCode.BadRequest, (message, requestId) => new AccessDeniedException(message, requestId));
        yield return new("ConditionalCheckFailedException", HttpStatusCode.BadRequest, (message, requestId) => new ConditionalCheckFailedException(message, null, requestId));
        yield return new("ExpiredIteratorException", HttpStatusCode.BadRequest, (message, requestId) => new ExpiredIteratorException(message, requestId));
        yield return new("IdempotentParameterMismatchException", HttpStatusCode.BadRequest, (message, requestId) => new IdempotentParameterMismatchException(message, requestId));
        yield return new("InternalServerError", HttpStatusCode.InternalServerError, (message, requestId) => new InternalServerErrorException(message, requestId));
        yield return new("ItemCollectionSizeLimitExceededException", HttpStatusCode.BadRequest, (message, requestId) => new ItemCollectionSizeLimitExceededException(message, requestId));
        yield return new("LimitExceededException", HttpStatusCode.BadRequest, (message, requestId) => new LimitExceededException(message, requestId));
        yield return new("ProvisionedThroughputExceededException", HttpStatusCode.BadRequest, (message, requestId) => new ProvisionedThroughputExceededException(message, requestId));
        yield return new("ReplicatedWriteConflictException", HttpStatusCode.BadRequest, (message, requestId) => new ReplicatedWriteConflictException(message, requestId));
        yield return new("RequestLimitExceeded", HttpStatusCode.BadRequest, (message, requestId) => new RequestLimitExceededException(message, requestId));
        yield return new("ResourceInUseException", HttpStatusCode.BadRequest, (message, requestId) => new ResourceInUseException(message, requestId));
        yield return new("ResourceNotFoundException", HttpStatusCode.BadRequest, (message, requestId) => new ResourceNotFoundException(message, requestId));
        yield return new("TrimmedDataAccessException", HttpStatusCode.BadRequest, (message, requestId) => new TrimmedDataAccessException(message, requestId));
        yield return new("ThrottlingException", HttpStatusCode.BadRequest, (message, requestId) => new ThrottlingException(message, requestId));
        yield return new("TransactionCanceledException", HttpStatusCode.BadRequest, (message, requestId) => new TransactionCanceledException(message, null, requestId));
        yield return new("TransactionConflictException", HttpStatusCode.BadRequest, (message, requestId) => new TransactionConflictException(message, requestId));
        yield return new("TransactionInProgressException", HttpStatusCode.BadRequest, (message, requestId) => new TransactionInProgressException(message, requestId));
        yield return new("UnrecognizedClientException", HttpStatusCode.BadRequest, (message, requestId) => new UnrecognizedClientException(message, requestId));
        yield return new("ValidationException", HttpStatusCode.BadRequest, (message, requestId) => new ValidationException(message, requestId));
    }

    [Test]
    [MethodDataSource(nameof(TypedExceptions))]
    public async Task Constructor_TypedException_SetsErrorCode(ExceptionCase typed)
    {
        // Arrange (nothing to arrange beyond the constants)

        // Act
        var exception = typed.Create(Message, RequestId);

        // Assert
        await Assert.That(exception.ErrorCode).IsEqualTo(typed.ErrorCode);
    }

    [Test]
    [MethodDataSource(nameof(TypedExceptions))]
    public async Task Constructor_TypedException_SetsStatusCode(ExceptionCase typed)
    {
        // Arrange (nothing to arrange beyond the constants)

        // Act
        var exception = typed.Create(Message, RequestId);

        // Assert
        await Assert.That(exception.StatusCode).IsEqualTo(typed.StatusCode);
    }

    [Test]
    [MethodDataSource(nameof(TypedExceptions))]
    public async Task Constructor_TypedException_SetsMessage(ExceptionCase typed)
    {
        // Arrange (nothing to arrange beyond the constants)

        // Act
        var exception = typed.Create(Message, RequestId);

        // Assert
        await Assert.That(exception.Message).IsEqualTo(Message);
    }

    [Test]
    [MethodDataSource(nameof(TypedExceptions))]
    public async Task Constructor_TypedExceptionWithRequestId_SetsRequestId(ExceptionCase typed)
    {
        // Arrange (nothing to arrange beyond the constants)

        // Act
        var exception = typed.Create(Message, RequestId);

        // Assert
        await Assert.That(exception.RequestId).IsEqualTo(RequestId);
    }

    [Test]
    [MethodDataSource(nameof(TypedExceptions))]
    public async Task Constructor_TypedExceptionWithoutRequestId_RequestIdIsNull(ExceptionCase typed)
    {
        // Arrange (nothing to arrange beyond the constants)

        // Act
        var exception = typed.Create(Message, null);

        // Assert
        await Assert.That(exception.RequestId).IsNull();
    }

    [Test]
    [MethodDataSource(nameof(TypedExceptions))]
    public async Task Constructor_TypedException_HasNoInnerException(ExceptionCase typed)
    {
        // Arrange (nothing to arrange beyond the constants)

        // Act
        var exception = typed.Create(Message, RequestId);

        // Assert
        await Assert.That(exception.InnerException).IsNull();
    }

    [Test]
    public async Task Constructor_UntypedError_SetsErrorCode()
    {
        // Arrange (nothing to arrange beyond the constants)

        // Act
        var exception = new DynamoDbException(Message, "SomethingNewException", HttpStatusCode.BadRequest);

        // Assert
        await Assert.That(exception.ErrorCode).IsEqualTo("SomethingNewException");
    }

    [Test]
    public async Task Constructor_UntypedError_SetsStatusCode()
    {
        // Arrange (nothing to arrange beyond the constants)

        // Act
        var exception = new DynamoDbException(Message, "ServiceUnavailable", HttpStatusCode.ServiceUnavailable);

        // Assert
        await Assert.That(exception.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
    }

    [Test]
    public async Task Constructor_UntypedError_SetsMessage()
    {
        // Arrange (nothing to arrange beyond the constants)

        // Act
        var exception = new DynamoDbException(Message, "ServiceUnavailable", HttpStatusCode.ServiceUnavailable);

        // Assert
        await Assert.That(exception.Message).IsEqualTo(Message);
    }

    [Test]
    public async Task Constructor_UntypedErrorWithRequestId_SetsRequestId()
    {
        // Arrange (nothing to arrange beyond the constants)

        // Act
        var exception = new DynamoDbException(Message, "ServiceUnavailable", HttpStatusCode.ServiceUnavailable, RequestId);

        // Assert
        await Assert.That(exception.RequestId).IsEqualTo(RequestId);
    }

    [Test]
    public async Task Constructor_UntypedErrorWithoutRequestId_RequestIdIsNull()
    {
        // Arrange (nothing to arrange beyond the constants)

        // Act
        var exception = new DynamoDbException(Message, "ServiceUnavailable", HttpStatusCode.ServiceUnavailable);

        // Assert
        await Assert.That(exception.RequestId).IsNull();
    }

    [Test]
    public async Task Constructor_UntypedErrorWithInnerException_SetsInnerException()
    {
        // Arrange
        var inner = new FormatException("The body is not JSON.");

        // Act
        var exception = new DynamoDbException(Message, "ServiceUnavailable", HttpStatusCode.ServiceUnavailable, RequestId, inner);

        // Assert
        await Assert.That(exception.InnerException).IsSameReferenceAs(inner);
    }

    [Test]
    public async Task Constructor_UntypedError_NamesNoOperationAndNoTable()
    {
        // Arrange (nothing to arrange beyond the constants)

        // Act
        var exception = new DynamoDbException(Message, "ServiceUnavailable", HttpStatusCode.ServiceUnavailable);

        // Assert
        await Assert.That((exception.OperationName, exception.TableName)).IsEqualTo(((string?)null, (string?)null));
    }

    [Test]
    public async Task ToString_NoOperation_IsTheTextOfAnyException()
    {
        // Arrange
        var exception = new DynamoDbException(Message, "ValidationException", HttpStatusCode.BadRequest, RequestId);

        // Act
        var text = exception.ToString();

        // Assert
        await Assert.That(text).IsEqualTo($"{typeof(DynamoDbException).FullName}: {Message}");
    }

    [Test]
    public async Task ToString_OperationAndTable_EndsWithBoth()
    {
        // Arrange
        var exception = new DynamoDbException(Message, "ValidationException", HttpStatusCode.BadRequest, RequestId) { OperationName = "PutItem", TableName = "Orders" };

        // Act
        var text = exception.ToString();

        // Assert
        await Assert.That(text).EndsWith(Environment.NewLine + "Operation: PutItem, table: Orders", StringComparison.Ordinal);
    }

    [Test]
    public async Task ToString_OperationWithoutTable_EndsWithTheOperationOnly()
    {
        // Arrange
        var exception = new DynamoDbException(Message, "ValidationException", HttpStatusCode.BadRequest, RequestId) { OperationName = "ListTables" };

        // Act
        var text = exception.ToString();

        // Assert
        await Assert.That(text).EndsWith(Environment.NewLine + "Operation: ListTables", StringComparison.Ordinal);
    }

    [Test]
    public async Task ToString_OperationAndTable_KeepsTheServiceMessage()
    {
        // Arrange
        var exception = new DynamoDbException(Message, "ValidationException", HttpStatusCode.BadRequest, RequestId) { OperationName = "PutItem", TableName = "Orders" };

        // Act
        var text = exception.ToString();

        // Assert
        await Assert.That(text).Contains(Message, StringComparison.Ordinal);
    }

    [Test]
    public async Task ExecuteAsync_SingleTableCallFails_NamesTheOperationAndTheTable()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "One or more parameter values were invalid"));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<ValidationException>(() => client.PutItemAsync(TestClient.PutItem("Orders")));

        // Assert
        await Assert.That((exception.OperationName, exception.TableName, exception.Message)).IsEqualTo(("PutItem", (string?)"Orders", "One or more parameter values were invalid"));
    }

    [Test]
    public async Task ExecuteAsync_BatchCallFails_NamesTheOperationButNoTable()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "Too many items requested for the BatchGetItem call"));
        using var client = TestClient.Create(handler);
        var keys = new KeysAndAttributes { Keys = [new Dictionary<string, AttributeValue> { ["pk"] = "a" }] };
        var request = new BatchGetItemRequest { RequestItems = new Dictionary<string, KeysAndAttributes> { ["Orders"] = keys, ["Users"] = keys } };

        // Act
        var exception = await TestClient.CaptureAsync<ValidationException>(() => client.BatchGetItemAsync(request));

        // Assert
        await Assert.That((exception.OperationName, exception.TableName)).IsEqualTo(("BatchGetItem", (string?)null));
    }

    [Test]
    public async Task ExecuteAsync_RetriedUntilTheLastAttemptFails_NamesTheSurfacedException()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError("InternalServerError", "Internal server error", HttpStatusCode.InternalServerError) };
        using var client = TestClient.Create(handler, new ManualTimeProvider(), maxAttempts: 2, random: FixedRandom.Zero);

        // Act
        var exception = await TestClient.CaptureAsync<InternalServerErrorException>(() => client.GetItemAsync(TestClient.GetItem("Orders")));

        // Assert
        await Assert.That((exception.OperationName, exception.TableName, handler.Requests.Count)).IsEqualTo(("GetItem", (string?)"Orders", 2));
    }

    [Test]
    public async Task ExecuteAsync_ResponseThatIsNotDynamoDbJson_NamesTheOperationAndTheTable()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("<html>Bad gateway</html>", HttpStatusCode.BadGateway));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem("Orders")));

        // Assert
        await Assert.That((exception.OperationName, exception.TableName)).IsEqualTo(("GetItem", (string?)"Orders"));
    }

    [Test]
    public async Task ExecuteAsync_SuccessfulResponseThatCannotBeParsed_NamesTheOperationAndTheTable()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":[]}"""));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem("Orders")));

        // Assert
        await Assert.That((exception.OperationName, exception.TableName)).IsEqualTo(("GetItem", (string?)"Orders"));
    }

    [Test]
    public async Task ExecuteAsync_CredentialsProviderThrowsTheErrorOfItsOwnClient_LeavesThatExceptionAsItIs()
    {
        // Arrange (a provider that reads its keys with a client of its own: its error names that call, and it may be one shared instance)
        var error = new ValidationException("The table is not there.") { OperationName = "GetItem", TableName = "Secrets" };
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler, credentials: new FailingCredentialsProvider(error));

        // Act
        var exception = await TestClient.CaptureAsync<ValidationException>(() => client.PutItemAsync(TestClient.PutItem("Orders")));

        // Assert
        await Assert.That((exception.OperationName, exception.TableName)).IsEqualTo(("GetItem", (string?)"Secrets"));
    }

    private sealed class FailingCredentialsProvider(Exception error) : ICredentialsProvider
    {
        public ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken) => throw error;
    }
}
