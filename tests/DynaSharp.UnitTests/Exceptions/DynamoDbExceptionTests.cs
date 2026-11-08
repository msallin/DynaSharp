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

    // What a caller of DbException.IsTransient expects: true where the same call can pass later without a change, false where the request, the
    // permissions or the data have to change first.
    public static IEnumerable<TransienceCase> Transience()
    {
        // Throttling and capacity, which the retry policy repeats with the throttling backoff.
        yield return new("ProvisionedThroughputExceededException", new ProvisionedThroughputExceededException(Message), true);
        yield return new("ThrottlingException", new ThrottlingException(Message), true);
        yield return new("RequestLimitExceeded", new RequestLimitExceededException(Message), true);
        yield return new("LimitExceededException", new LimitExceededException(Message), true);
        yield return new("TransactionInProgressException", new TransactionInProgressException(Message), true);

        // Server errors, whatever the code, and what the policy repeats as transient.
        yield return new("InternalServerError", new InternalServerErrorException(Message), true);
        yield return new("ValidationException 500", new ValidationException(Message, statusCode: HttpStatusCode.InternalServerError), true);
        yield return new("SomeNewException 503", new DynamoDbException(Message, "SomeNewException", HttpStatusCode.ServiceUnavailable), true);
        yield return new("UnrecognizedResponse 502", new DynamoDbException(Message, "UnrecognizedResponse", HttpStatusCode.BadGateway), true);
        yield return new("RequestTimeoutException", new DynamoDbException(Message, "RequestTimeoutException", HttpStatusCode.BadRequest), true);
        yield return new("ReplicatedWriteConflictException", new ReplicatedWriteConflictException(Message), true);

        // A conflict of a transaction, which the policy leaves to the caller: nothing has to change on this side.
        yield return new("TransactionConflictException", new TransactionConflictException(Message), true);
        yield return new("TransactionCanceledException conflict", Canceled("None", "TransactionConflict"), true);
        yield return new("TransactionCanceledException throttled and conflict", Canceled("ThrottlingError", "ProvisionedThroughputExceeded", "TransactionConflict"), true);

        // A failed condition: the same call cannot pass unless the item changes.
        yield return new("ConditionalCheckFailedException", new ConditionalCheckFailedException(Message), false);
        yield return new("TransactionCanceledException condition", Canceled("None", "ConditionalCheckFailed"), false);
        yield return new("TransactionCanceledException condition and conflict", Canceled("TransactionConflict", "ConditionalCheckFailed"), false);

        // The other reasons of a cancelled transaction, and a cancellation that says nothing.
        yield return new("TransactionCanceledException item collection", Canceled("ItemCollectionSizeLimitExceeded"), false);
        yield return new("TransactionCanceledException validation", Canceled("ValidationError", "TransactionConflict"), false);
        yield return new("TransactionCanceledException unknown code", Canceled("SomethingNew"), false);
        yield return new("TransactionCanceledException reason without a code", Canceled([null]), false);
        yield return new("TransactionCanceledException only None", Canceled("None", "None"), false);
        yield return new("TransactionCanceledException without reasons", new TransactionCanceledException(Message), false);

        // The request, the table or the data has to change.
        yield return new("ValidationException", new ValidationException(Message), false);
        yield return new("ResourceNotFoundException", new ResourceNotFoundException(Message), false);
        yield return new("ResourceInUseException", new ResourceInUseException(Message), false);
        yield return new("ItemCollectionSizeLimitExceededException", new ItemCollectionSizeLimitExceededException(Message), false);
        yield return new("ExpiredIteratorException", new ExpiredIteratorException(Message), false);
        yield return new("TrimmedDataAccessException", new TrimmedDataAccessException(Message), false);
        yield return new("IdempotentParameterMismatchException", new IdempotentParameterMismatchException(Message), false);

        // Access, signature and token errors.
        yield return new("AccessDeniedException", new AccessDeniedException(Message), false);
        yield return new("AccessDeniedException 403", new AccessDeniedException(Message, statusCode: HttpStatusCode.Forbidden), false);
        yield return new("UnrecognizedClientException", new UnrecognizedClientException(Message), false);
        yield return new("InvalidSignatureException", new DynamoDbException(Message, "InvalidSignatureException", HttpStatusCode.BadRequest), false);
        yield return new("MissingAuthenticationTokenException", new DynamoDbException(Message, "MissingAuthenticationTokenException", HttpStatusCode.BadRequest), false);
        yield return new("ExpiredTokenException", new DynamoDbException(Message, "ExpiredTokenException", HttpStatusCode.BadRequest), false);

        // A code this library does not know, and a response that cannot be read although it says success.
        yield return new("SomeNewException 400", new DynamoDbException(Message, "SomeNewException", HttpStatusCode.BadRequest), false);
        yield return new("UnrecognizedResponse 200", new DynamoDbException(Message, "UnrecognizedResponse", HttpStatusCode.OK), false);

        // A batch that gave up: on its rounds, which is throttling, or on the failure inside it.
        yield return new("BatchIncompleteException rounds", BatchWrite(null), true);
        yield return new("BatchIncompleteException throttled", BatchWrite(new ProvisionedThroughputExceededException(Message)), true);
        yield return new("BatchIncompleteException network", BatchWrite(new HttpRequestException("Connection refused")), true);
        yield return new("BatchIncompleteException timeout", BatchWrite(new TimeoutException("The attempt timed out.")), true);
        yield return new("BatchIncompleteException validation", BatchWrite(new ValidationException(Message)), false);
        yield return new(
            "BatchIncompleteException response limit",
            BatchWrite(new HttpRequestException(HttpRequestError.ConfigurationLimitExceeded, "The response is larger than the limit.")),
            false);
        yield return new("BatchGetIncompleteException rounds", BatchGet(null), true);
        yield return new("BatchGetIncompleteException access", BatchGet(new AccessDeniedException(Message)), false);
    }

    [Test]
    public async Task BatchIncompleteException_NullUnprocessedItems_ThrowsArgumentNullException()
    {
        // Arrange

        // Act
        var act = () => new BatchIncompleteException(Message, null!, 0);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("unprocessedItems");
    }

    [Test]
    public async Task BatchGetIncompleteException_NullUnprocessedKeys_ThrowsArgumentNullException()
    {
        // Arrange

        // Act
        var act = () => new BatchGetIncompleteException<Item>(Message, null!, []);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("unprocessedKeys");
    }

    [Test]
    public async Task BatchGetIncompleteException_NullItems_ThrowsArgumentNullException()
    {
        // Arrange

        // Act
        var act = () => new BatchGetIncompleteException<Item>(Message, new Dictionary<string, KeysAndAttributes>(), null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("items");
    }

    [Test]
    [MethodDataSource(nameof(Transience))]
    public async Task IsTransient_Failure_SaysWhetherTheSameCallCanPassLater(TransienceCase failure)
    {
        // Arrange (the case)

        // Act
        var transient = failure.Exception.IsTransient;

        // Assert
        await Assert.That(transient).IsEqualTo(failure.Expected);
    }

    [Test]
    public async Task IsTransient_ThrottledUntilTheAttemptsRanOut_IsTrue()
    {
        // Arrange (what a caller sees: the client retried, gave up, and the same call may pass after a wait)
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError("ProvisionedThroughputExceededException", "Throughput exceeded") };
        using var client = TestClient.Create(handler, new ManualTimeProvider(), maxAttempts: 2, random: FixedRandom.Zero);
        var exception = await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem("Orders")));

        // Act
        var transient = exception.IsTransient;

        // Assert
        await Assert.That((transient, handler.Requests.Count)).IsEqualTo((true, 2));
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

    private static TransactionCanceledException Canceled(params string?[] codes) =>
        new(Message, [.. codes.Select(static code => new CancellationReason { Code = code })]);

    private static BatchIncompleteException BatchWrite(Exception? cause) =>
        new(Message, new Dictionary<string, IReadOnlyList<WriteRequest>> { ["Orders"] = [new() { DeleteRequest = new() { Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" } } }] }, 0, cause);

    private static BatchGetIncompleteException<Item> BatchGet(Exception? cause) =>
        new(Message, new Dictionary<string, KeysAndAttributes> { ["Orders"] = new() { Keys = [new Dictionary<string, AttributeValue> { ["pk"] = "a" }] } }, [], cause);

    private sealed class FailingCredentialsProvider(Exception error) : ICredentialsProvider
    {
        public ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken) => throw error;
    }
}
