using System.Net;
using System.Text.Json;
using DynaSharp.Retries;

namespace DynaSharp.UnitTests.Retries;

internal sealed class FailureClassifierTests
{
    // The oracle is the standard retry mode of botocore (1.42, retries/standard.py): ThrottledRetryableChecker._THROTTLED_ERROR_CODES,
    // TransientRetryableChecker._TRANSIENT_ERROR_CODES and _TRANSIENT_STATUS_CODES, the connection errors of its HTTP layer, and the
    // errors that the DynamoDB service model (dynamodb/2012-08-10/service-2.json) marks "retryable". Only DynamoDB's JSON protocol is
    // kept: the codes of S3, Route 53, EC2 and the Query protocol are listed under "not retried" so that leaving them out is deliberate.
    public static IEnumerable<FailureCase> Failures()
    {
        // Throttling error codes of botocore that DynamoDB uses.
        yield return Response("ThrottlingException", HttpStatusCode.BadRequest, FailureKind.Throttling);
        yield return Response("ProvisionedThroughputExceededException", HttpStatusCode.BadRequest, FailureKind.Throttling);
        yield return Response("TransactionInProgressException", HttpStatusCode.BadRequest, FailureKind.Throttling);
        yield return Response("RequestLimitExceeded", HttpStatusCode.BadRequest, FailureKind.Throttling);
        yield return Response("LimitExceededException", HttpStatusCode.BadRequest, FailureKind.Throttling);

        // The code decides, whatever the status says.
        yield return Response("ThrottlingException", HttpStatusCode.ServiceUnavailable, FailureKind.Throttling);

        // Transient error codes: the JSON form of botocore's list, and the modeled retryable error of DynamoDB.
        yield return Response("RequestTimeoutException", HttpStatusCode.BadRequest, FailureKind.Transient);
        yield return Response("ReplicatedWriteConflictException", HttpStatusCode.BadRequest, FailureKind.Transient);

        // Transient HTTP statuses of botocore: 500, 502, 503, 504, with the code DynamoDB uses or with none that is known.
        yield return Response("InternalServerError", HttpStatusCode.InternalServerError, FailureKind.Transient);
        yield return Response("ServiceUnavailable", HttpStatusCode.ServiceUnavailable, FailureKind.Transient);
        yield return Response("UnrecognizedResponse", HttpStatusCode.BadGateway, FailureKind.Transient);
        yield return Response("UnrecognizedResponse", HttpStatusCode.ServiceUnavailable, FailureKind.Transient);
        yield return Response("UnrecognizedResponse", HttpStatusCode.GatewayTimeout, FailureKind.Transient);
        yield return Response("ValidationException", HttpStatusCode.InternalServerError, FailureKind.Transient);

        // Connection failures (botocore ConnectionError and HTTPClientError): a refused connection and a response that ends early.
        yield return new("HttpRequestException", new HttpRequestException("Connection refused"), FailureKind.Transient);

        // A limit of the client, for example a response body above the ceiling: the same request gets the same response.
        yield return new(
            "HttpRequestException ConfigurationLimitExceeded",
            new HttpRequestException(HttpRequestError.ConfigurationLimitExceeded, "The response is larger than the limit."),
            FailureKind.Permanent);
        yield return new("HttpRequestException ResponseEnded", new HttpRequestException(HttpRequestError.ResponseEnded, "The response ended prematurely."), FailureKind.Transient);

        // The timeout of an attempt, and the connect timeout of the HTTP handler (botocore ConnectTimeoutError and ReadTimeoutError,
        // which also cost more quota).
        yield return new("TimeoutException", new TimeoutException("The attempt timed out."), FailureKind.Timeout);
        yield return new(
            "HttpRequestException with TimeoutException",
            new HttpRequestException(HttpRequestError.ConnectionError, "A connection attempt timed out.", new TimeoutException("The connect timeout elapsed.")),
            FailureKind.Timeout);
        yield return new(
            "HttpRequestException with IOException",
            new HttpRequestException(HttpRequestError.ResponseEnded, "The response ended.", new IOException("The connection was closed.")),
            FailureKind.Transient);

        // Not retried: the request is wrong or not allowed, and repeating it cannot change that.
        yield return Response("ValidationException", HttpStatusCode.BadRequest, FailureKind.Permanent);
        yield return Response("ConditionalCheckFailedException", HttpStatusCode.BadRequest, FailureKind.Permanent);
        yield return Response("AccessDeniedException", HttpStatusCode.BadRequest, FailureKind.Permanent);
        yield return Response("AccessDeniedException", HttpStatusCode.Forbidden, FailureKind.Permanent);
        yield return Response("ResourceNotFoundException", HttpStatusCode.BadRequest, FailureKind.Permanent);
        yield return Response("ResourceInUseException", HttpStatusCode.BadRequest, FailureKind.Permanent);
        yield return Response("ExpiredIteratorException", HttpStatusCode.BadRequest, FailureKind.Permanent);
        yield return Response("TrimmedDataAccessException", HttpStatusCode.BadRequest, FailureKind.Permanent);
        yield return Response("UnrecognizedClientException", HttpStatusCode.BadRequest, FailureKind.Permanent);
        yield return Response("ItemCollectionSizeLimitExceededException", HttpStatusCode.BadRequest, FailureKind.Permanent);
        yield return Response("TransactionConflictException", HttpStatusCode.BadRequest, FailureKind.Permanent);
        yield return Response("TransactionCanceledException", HttpStatusCode.BadRequest, FailureKind.Permanent);
        yield return Response("IdempotentParameterMismatchException", HttpStatusCode.BadRequest, FailureKind.Permanent);
        yield return Response("SomeNewException", HttpStatusCode.BadRequest, FailureKind.Permanent);

        // The typed exceptions of the transactions are classified by their wire code like the generic ones: a transaction in progress is
        // throttling and is repeated with the same token, a replicated write conflict is transient, and the others are for the caller to
        // decide, also a cancellation whose reasons are all throttling.
        yield return new("TransactionInProgressException typed", new TransactionInProgressException("The transaction is in progress."), FailureKind.Throttling);
        yield return new("ReplicatedWriteConflictException typed", new ReplicatedWriteConflictException("A write in another Region conflicts."), FailureKind.Transient);
        yield return new("TransactionCanceledException typed", new TransactionCanceledException("Transaction cancelled."), FailureKind.Permanent);
        yield return new(
            "TransactionCanceledException typed with throttling reasons",
            new TransactionCanceledException("Transaction cancelled.", [new() { Code = "ThrottlingError" }, new() { Code = "ProvisionedThroughputExceeded" }, new() { Code = "TransactionConflict" }]),
            FailureKind.Permanent);
        yield return new("TransactionConflictException typed", new TransactionConflictException("Transaction is ongoing for the item."), FailureKind.Permanent);
        yield return new("IdempotentParameterMismatchException typed", new IdempotentParameterMismatchException("The token was used for another request."), FailureKind.Permanent);

        // Statuses that are not in botocore's list.
        yield return Response("UnrecognizedResponse", HttpStatusCode.NotFound, FailureKind.Permanent);
        yield return Response("UnrecognizedResponse", HttpStatusCode.TooManyRequests, FailureKind.Permanent);
        yield return Response("UnrecognizedResponse", HttpStatusCode.NotImplemented, FailureKind.Permanent);
        yield return Response("UnrecognizedResponse", HttpStatusCode.HttpVersionNotSupported, FailureKind.Permanent);

        // A response that arrived but cannot be read is not a sign of a passing problem.
        yield return Response("UnrecognizedResponse", HttpStatusCode.OK, FailureKind.Permanent);

        // Codes of other services and protocols that botocore lists. DynamoDB does not send them.
        yield return Response("SlowDown", HttpStatusCode.BadRequest, FailureKind.Permanent);
        yield return Response("PriorRequestNotComplete", HttpStatusCode.BadRequest, FailureKind.Permanent);
        yield return Response("EC2ThrottledException", HttpStatusCode.BadRequest, FailureKind.Permanent);
        yield return Response("Throttling", HttpStatusCode.BadRequest, FailureKind.Permanent);
        yield return Response("RequestTimeout", HttpStatusCode.BadRequest, FailureKind.Permanent);

        // Codes are case sensitive, as on the wire.
        yield return Response("throttlingexception", HttpStatusCode.BadRequest, FailureKind.Permanent);

        // Failures that are none of the service's business.
        yield return new("OperationCanceledException", new OperationCanceledException("Cancelled by the caller."), FailureKind.Permanent);
        yield return new("TaskCanceledException", new TaskCanceledException("Cancelled by the caller."), FailureKind.Permanent);
        yield return new("CredentialsUnavailableException", new CredentialsUnavailableException("No credentials."), FailureKind.Permanent);
        yield return new("ArgumentException", new ArgumentException("A default AttributeValue has no type."), FailureKind.Permanent);
        yield return new("InvalidOperationException", new InvalidOperationException("The stream broke."), FailureKind.Permanent);
        yield return new("JsonException", new JsonException("Not JSON."), FailureKind.Permanent);
        yield return new("IOException", new IOException("Disk gone."), FailureKind.Permanent);
    }

    // Every response the retry policy repeats, so that IsTransient cannot say false where the client retried.
    public static IEnumerable<FailureCase> RepeatedResponses() =>
        Failures().Where(static failure => failure is { Exception: DynamoDbException, Expected: not FailureKind.Permanent });

    // Every response it does not repeat, but for the conflicts of a transaction, which IsTransient counts as passing on their own.
    public static IEnumerable<FailureCase> SurfacedResponses() =>
        Failures().Where(static failure => failure is { Exception: DynamoDbException { ErrorCode: not ("TransactionConflictException" or "TransactionCanceledException") }, Expected: FailureKind.Permanent });

    [Test]
    [Arguments("ExpiredTokenException")]
    [Arguments("ExpiredToken")]
    public async Task IsExpiredCredentialsCode_CodeOfAnExpiredToken_ReturnsTrue(string code)
    {
        // Arrange (the case)

        // Act
        var result = FailureClassifier.IsExpiredCredentialsCode(code);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    [Arguments("ExpiredIteratorException")]
    [Arguments("RequestExpired")]
    [Arguments("InvalidSignatureException")]
    [Arguments("UnrecognizedClientException")]
    [Arguments("expiredtokenexception")]
    [Arguments("UnrecognizedResponse")]
    [Arguments("")]
    public async Task IsExpiredCredentialsCode_AnyOtherCode_ReturnsFalse(string code)
    {
        // Arrange (the case)

        // Act
        var result = FailureClassifier.IsExpiredCredentialsCode(code);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    [MethodDataSource(nameof(Failures))]
    public async Task Classify_Failure_ReturnsTheKindOfTheStandardRetryMode(FailureCase failure)
    {
        // Arrange (the case)

        // Act
        var kind = FailureClassifier.Classify(failure.Exception);

        // Assert
        await Assert.That(kind).IsEqualTo(failure.Expected);
    }

    [Test]
    [MethodDataSource(nameof(RepeatedResponses))]
    public async Task IsTransient_ResponseThatTheRetryPolicyRepeats_IsTrue(FailureCase failure)
    {
        // Arrange (the case)

        // Act
        var transient = FailureClassifier.IsTransient((DynamoDbException)failure.Exception);

        // Assert
        await Assert.That(transient).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(SurfacedResponses))]
    public async Task IsTransient_ResponseThatTheRetryPolicySurfaces_IsFalse(FailureCase failure)
    {
        // Arrange (the case)

        // Act
        var transient = FailureClassifier.IsTransient((DynamoDbException)failure.Exception);

        // Assert
        await Assert.That(transient).IsFalse();
    }

    [Test]
    public async Task Classify_TypedThrottlingException_IsThrottling()
    {
        // Arrange
        var exception = new ProvisionedThroughputExceededException("The level of configured provisioned throughput was exceeded.");

        // Act
        var kind = FailureClassifier.Classify(exception);

        // Assert
        await Assert.That(kind).IsEqualTo(FailureKind.Throttling);
    }

    [Test]
    public async Task Classify_TypedInternalServerError_IsTransient()
    {
        // Arrange
        var exception = new InternalServerErrorException("The server encountered an internal error.");

        // Act
        var kind = FailureClassifier.Classify(exception);

        // Assert
        await Assert.That(kind).IsEqualTo(FailureKind.Transient);
    }

    private static FailureCase Response(string errorCode, HttpStatusCode statusCode, FailureKind expected) =>
        new($"{errorCode} {(int)statusCode}", new DynamoDbException("message", errorCode, statusCode), expected);
}
