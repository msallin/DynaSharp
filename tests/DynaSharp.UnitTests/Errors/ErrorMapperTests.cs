using System.Net;
using System.Text;
using DynaSharp.Errors;

namespace DynaSharp.UnitTests.Errors;

public sealed class ErrorMapperTests
{
    private const string RequestId = "7QTD4FR8SJ3T2JKB5LCUTH4MJVVV4KQNSO5AEMVJF66Q9ASUAAJG";
    private const string Backslash = "\\";

    // A real response: the first action of the transaction failed its condition, the second had no problem.
    private const string TransactionCanceledBody = """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":[{"Code":"ConditionalCheckFailed","Message":"The conditional request failed"},{"Code":"None"}],"Message":"Transaction cancelled, please refer cancellation reasons for specific reasons [ConditionalCheckFailed, None]"}""";

    public static IEnumerable<ErrorCase> TypedErrors()
    {
        yield return new("AccessDeniedException", """{"__type":"com.amazon.coral.service#AccessDeniedException","Message":"User: arn:aws:iam::123456789012:user/alice is not authorized to perform: dynamodb:GetItem on resource: arn:aws:dynamodb:eu-central-1:123456789012:table/Users because no identity-based policy allows the dynamodb:GetItem action"}""", typeof(AccessDeniedException), "User: arn:aws:iam::123456789012:user/alice is not authorized to perform: dynamodb:GetItem on resource: arn:aws:dynamodb:eu-central-1:123456789012:table/Users because no identity-based policy allows the dynamodb:GetItem action");
        yield return new("ConditionalCheckFailedException", """{"__type":"com.amazonaws.dynamodb.v20120810#ConditionalCheckFailedException","message":"The conditional request failed"}""", typeof(ConditionalCheckFailedException), "The conditional request failed");
        yield return new("IdempotentParameterMismatchException", """{"__type":"com.amazonaws.dynamodb.v20120810#IdempotentParameterMismatchException","Message":"The request uses the same client token as a previous, but non-identical request."}""", typeof(IdempotentParameterMismatchException), "The request uses the same client token as a previous, but non-identical request.");
        yield return new("InternalServerError", """{"__type":"com.amazonaws.dynamodb.v20120810#InternalServerError","message":"The server encountered an internal error trying to fulfill the request"}""", typeof(InternalServerErrorException), "The server encountered an internal error trying to fulfill the request");
        yield return new("ItemCollectionSizeLimitExceededException", """{"__type":"com.amazonaws.dynamodb.v20120810#ItemCollectionSizeLimitExceededException","message":"Collection size exceeded"}""", typeof(ItemCollectionSizeLimitExceededException), "Collection size exceeded");
        yield return new("LimitExceededException", """{"__type":"com.amazonaws.dynamodb.v20120810#LimitExceededException","message":"Subscriber limit exceeded: Only 10 tables can be created, updated, or deleted simultaneously"}""", typeof(LimitExceededException), "Subscriber limit exceeded: Only 10 tables can be created, updated, or deleted simultaneously");
        yield return new("ProvisionedThroughputExceededException", """{"__type":"com.amazonaws.dynamodb.v20120810#ProvisionedThroughputExceededException","message":"The level of configured provisioned throughput for the table was exceeded. Consider increasing your provisioning level with the UpdateTable API."}""", typeof(ProvisionedThroughputExceededException), "The level of configured provisioned throughput for the table was exceeded. Consider increasing your provisioning level with the UpdateTable API.");
        yield return new("ReplicatedWriteConflictException", """{"__type":"com.amazonaws.dynamodb.v20120810#ReplicatedWriteConflictException","message":"Replicated write conflict"}""", typeof(ReplicatedWriteConflictException), "Replicated write conflict");
        yield return new("RequestLimitExceeded", """{"__type":"com.amazonaws.dynamodb.v20120810#RequestLimitExceeded","message":"Throughput exceeds the current throughput limit for your account."}""", typeof(RequestLimitExceededException), "Throughput exceeds the current throughput limit for your account.");
        yield return new("ExpiredIteratorException", """{"__type":"com.amazonaws.dynamodb.v20120810#ExpiredIteratorException","message":"The provided iterator exceeds the maximum age allowed."}""", typeof(ExpiredIteratorException), "The provided iterator exceeds the maximum age allowed.");
        yield return new("TrimmedDataAccessException", """{"__type":"com.amazonaws.dynamodb.v20120810#TrimmedDataAccessException","message":"The requested data is trimmed."}""", typeof(TrimmedDataAccessException), "The requested data is trimmed.");
        yield return new("ResourceInUseException", """{"__type":"com.amazonaws.dynamodb.v20120810#ResourceInUseException","message":"Cannot create preexisting table"}""", typeof(ResourceInUseException), "Cannot create preexisting table");
        yield return new("ResourceNotFoundException", """{"__type":"com.amazonaws.dynamodb.v20120810#ResourceNotFoundException","message":"Requested resource not found"}""", typeof(ResourceNotFoundException), "Requested resource not found");
        yield return new("ThrottlingException", """{"__type":"com.amazon.coral.availability#ThrottlingException","message":"Rate of requests exceeds the allowed throughput."}""", typeof(ThrottlingException), "Rate of requests exceeds the allowed throughput.");
        yield return new("TransactionCanceledException", TransactionCanceledBody, typeof(TransactionCanceledException), "Transaction cancelled, please refer cancellation reasons for specific reasons [ConditionalCheckFailed, None]");
        yield return new("TransactionConflictException", """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionConflictException","message":"Transaction is ongoing for the item"}""", typeof(TransactionConflictException), "Transaction is ongoing for the item");
        yield return new("TransactionInProgressException", """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionInProgressException","message":"The transaction with the given request token is already in progress."}""", typeof(TransactionInProgressException), "The transaction with the given request token is already in progress.");
        yield return new("UnrecognizedClientException", """{"__type":"com.amazon.coral.service#UnrecognizedClientException","message":"The security token included in the request is invalid."}""", typeof(UnrecognizedClientException), "The security token included in the request is invalid.");
        yield return new("ValidationException", """{"__type":"com.amazon.coral.validate#ValidationException","message":"One or more parameter values were invalid: Missing the key pk in the item"}""", typeof(ValidationException), "One or more parameter values were invalid: Missing the key pk in the item");
    }

    [Test]
    [MethodDataSource(nameof(TypedErrors))]
    public async Task Map_TypedError_ReturnsTheMatchingExceptionType(ErrorCase error)
    {
        // Arrange (the case)

        // Act
        var exception = Map(HttpStatusCode.BadRequest, error.Body);

        // Assert
        await Assert.That(exception.GetType()).IsEqualTo(error.ExceptionType);
    }

    [Test]
    [MethodDataSource(nameof(TypedErrors))]
    public async Task Map_TypedError_UsesTheMessageOfTheBody(ErrorCase error)
    {
        // Arrange (the case)

        // Act
        var exception = Map(HttpStatusCode.BadRequest, error.Body);

        // Assert
        await Assert.That(exception.Message).IsEqualTo(error.Message);
    }

    [Test]
    [MethodDataSource(nameof(TypedErrors))]
    public async Task Map_TypedError_CarriesTheRequestId(ErrorCase error)
    {
        // Arrange (the case)

        // Act
        var exception = Map(HttpStatusCode.BadRequest, error.Body);

        // Assert
        await Assert.That(exception.RequestId).IsEqualTo(RequestId);
    }

    [Test]
    [MethodDataSource(nameof(TypedErrors))]
    public async Task Map_TypedError_ReportsTheCodeOfTheBody(ErrorCase error)
    {
        // Arrange (the case)

        // Act
        var exception = Map(HttpStatusCode.BadRequest, error.Body);

        // Assert
        await Assert.That(exception.ErrorCode).IsEqualTo(error.Name);
    }

    [Test]
    [MethodDataSource(nameof(TypedErrors))]
    public async Task Map_TypedError_KeepsTheHttpStatusOfTheResponse(ErrorCase error)
    {
        // Arrange (the case)

        // Act
        var exception = Map(HttpStatusCode.ServiceUnavailable, error.Body);

        // Assert
        await Assert.That(exception.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
    }

    [Test]
    [MethodDataSource(nameof(TypedErrors))]
    public async Task Map_TypedError_NamesTheCall(ErrorCase error)
    {
        // Arrange

        // Act
        var exception = ErrorMapper.Map(HttpStatusCode.BadRequest, RequestId, Encoding.UTF8.GetBytes(error.Body), "PutItem", "Orders");

        // Assert
        await Assert.That((exception.OperationName, exception.TableName)).IsEqualTo(((string?)"PutItem", (string?)"Orders"));
    }

    [Test]
    public async Task Map_UnknownErrorCode_NamesTheCall()
    {
        // Arrange
        const string Body = """{"__type":"com.amazonaws.dynamodb.v20120810#SomethingNew","message":"new"}""";

        // Act
        var exception = ErrorMapper.Map(HttpStatusCode.BadRequest, RequestId, Encoding.UTF8.GetBytes(Body), "PutItem", "Orders");

        // Assert
        await Assert.That((exception.OperationName, exception.TableName)).IsEqualTo(((string?)"PutItem", (string?)"Orders"));
    }

    [Test]
    public async Task Map_SuffixedFormOfAnUnsuffixedCode_IsNotTreatedAsTheKnownError()
    {
        // Arrange: DynamoDB never sends this; it is a code the library does not know.
        const string body = """{"__type":"com.amazonaws.dynamodb.v20120810#InternalServerErrorException","message":"Internal server error"}""";

        // Act
        var exception = Map(HttpStatusCode.InternalServerError, body);

        // Assert
        await Assert.That(exception.GetType()).IsEqualTo(typeof(DynamoDbException));
    }

    [Test]
    public async Task Map_EmptyMessage_DescribesTheError()
    {
        // Arrange: as DynamoDB Local answers a repeated ClientRequestToken with other actions.
        const string body = """{"__type":"com.amazonaws.dynamodb.v20120810#IdempotentParameterMismatchException","Message":""}""";

        // Act
        var exception = Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception.Message).IsEqualTo("DynamoDB returned the error IdempotentParameterMismatchException (HTTP 400) without a message.");
    }

    [Test]
    public async Task Map_UnknownErrorCode_ReturnsDynamoDbExceptionWithThatCode()
    {
        // Arrange
        const string body = """{"__type":"com.amazonaws.dynamodb.v20120810#SomethingNewException","message":"A code from a later version of the service"}""";

        // Act
        var exception = Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That((exception.GetType(), exception.ErrorCode)).IsEqualTo((typeof(DynamoDbException), "SomethingNewException"));
    }

    [Test]
    public async Task Map_UnknownErrorCode_KeepsMessageStatusAndRequestId()
    {
        // Arrange
        const string body = """{"__type":"com.amazonaws.dynamodb.v20120810#SomethingNewException","message":"Transaction is ongoing"}""";

        // Act
        var exception = Map(HttpStatusCode.TooManyRequests, body);

        // Assert
        await Assert.That((exception.Message, exception.StatusCode, exception.RequestId)).IsEqualTo(("Transaction is ongoing", HttpStatusCode.TooManyRequests, RequestId));
    }

    [Test]
    public async Task Map_ConditionalCheckFailedWithItem_ReturnsTheItem()
    {
        // Arrange
        const string body = """{"__type":"com.amazonaws.dynamodb.v20120810#ConditionalCheckFailedException","Item":{"pk":{"S":"user-1"},"version":{"N":"3"}},"message":"The conditional request failed"}""";

        // Act
        var exception = (ConditionalCheckFailedException)Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception.Item!["version"].AsNumber<int>()).IsEqualTo(3);
    }

    [Test]
    public async Task Map_ConditionalCheckFailedWithItemBeforeType_ReturnsTheItem()
    {
        // Arrange
        const string body = """{"Item":{"pk":{"S":"user-1"}},"message":"The conditional request failed","__type":"com.amazonaws.dynamodb.v20120810#ConditionalCheckFailedException"}""";

        // Act
        var exception = (ConditionalCheckFailedException)Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception.Item!["pk"].AsString()).IsEqualTo("user-1");
    }

    [Test]
    public async Task Map_ConditionalCheckFailedWithoutItem_ItemIsNull()
    {
        // Arrange
        const string body = """{"__type":"com.amazonaws.dynamodb.v20120810#ConditionalCheckFailedException","message":"The conditional request failed"}""";

        // Act
        var exception = (ConditionalCheckFailedException)Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception.Item).IsNull();
    }

    [Test]
    public async Task Map_ConditionalCheckFailedWithMalformedItem_ReturnsUnrecognizedResponse()
    {
        // Arrange
        const string body = """{"__type":"com.amazonaws.dynamodb.v20120810#ConditionalCheckFailedException","Item":{"pk":{"Q":"x"}}}""";

        // Act
        var exception = Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception.ErrorCode).IsEqualTo("UnrecognizedResponse");
    }

    [Test]
    public async Task Map_TransactionCanceled_ReturnsTheReasonsInTheOrderOfTheActions()
    {
        // Arrange (a real response)

        // Act
        var exception = (TransactionCanceledException)Map(HttpStatusCode.BadRequest, TransactionCanceledBody);

        // Assert
        await Assert.That(string.Join(';', exception.CancellationReasons.Select(reason => $"{reason.Code}:{reason.Message}")))
            .IsEqualTo("ConditionalCheckFailed:The conditional request failed;None:");
    }

    [Test]
    public async Task Map_TransactionCanceled_KeepsStatusAndRequestId()
    {
        // Arrange (a real response)

        // Act
        var exception = (TransactionCanceledException)Map(HttpStatusCode.Conflict, TransactionCanceledBody);

        // Assert
        await Assert.That((exception.StatusCode, exception.RequestId)).IsEqualTo((HttpStatusCode.Conflict, RequestId));
    }

    [Test]
    public async Task Map_TransactionCanceledWithoutReasons_ReasonsAreEmpty()
    {
        // Arrange
        const string body = """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","Message":"Transaction cancelled"}""";

        // Act
        var exception = (TransactionCanceledException)Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception.CancellationReasons).IsEmpty();
    }

    [Test]
    public async Task Map_TransactionCanceledWithReasonsBeforeType_ReturnsTheReasons()
    {
        // Arrange
        const string body = """{"CancellationReasons":[{"Code":"TransactionConflict","Message":"Transaction is ongoing for the item"}],"Message":"Transaction cancelled","__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException"}""";

        // Act
        var exception = (TransactionCanceledException)Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception.CancellationReasons.Single().Code).IsEqualTo("TransactionConflict");
    }

    [Test]
    public async Task Map_CancellationReasonWithItem_ReturnsTheItem()
    {
        // Arrange
        const string body = """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":[{"Code":"None"},{"Code":"ConditionalCheckFailed","Message":"The conditional request failed","Item":{"pk":{"S":"user-1"},"version":{"N":"3"}}}],"Message":"Transaction cancelled"}""";

        // Act
        var exception = (TransactionCanceledException)Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That((exception.CancellationReasons[0].Item, exception.CancellationReasons[1].Item!["version"].AsNumber<int>())).IsEqualTo(((IReadOnlyDictionary<string, DynaSharp.AttributeValue>?)null, 3));
    }

    [Test]
    public async Task Map_CancellationReasonWithUnknownMembers_SkipsThem()
    {
        // Arrange
        const string body = """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":[{"Future":{"a":[1,{"b":null}]},"Code":"ThrottlingError","Other":[true],"Message":"Slow down"}],"Message":"Transaction cancelled"}""";

        // Act
        var exception = (TransactionCanceledException)Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That((exception.CancellationReasons.Single().Code, exception.CancellationReasons.Single().Message)).IsEqualTo(("ThrottlingError", "Slow down"));
    }

    [Test]
    public async Task Map_CancellationReasonWithANullMessage_HasNoMessage()
    {
        // Arrange (DynamoDB Local leaves the member out for the code None, and the API reference speaks of a null message)
        const string body = """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":[{"Code":"None","Message":null}],"Message":"Transaction cancelled"}""";

        // Act
        var exception = (TransactionCanceledException)Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That((exception.CancellationReasons.Single().Code, exception.CancellationReasons.Single().Message)).IsEqualTo(("None", (string?)null));
    }

    [Test]
    public async Task Map_CancellationReasonWithoutCode_HasNoCode()
    {
        // Arrange
        const string body = """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":[{}],"Message":"Transaction cancelled"}""";

        // Act
        var exception = (TransactionCanceledException)Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That((exception.CancellationReasons.Single().Code, exception.CancellationReasons.Single().Message)).IsEqualTo(((string?)null, (string?)null));
    }

    [Test]
    public async Task Map_CancellationReasonsOnAnotherError_AreIgnored()
    {
        // Arrange
        const string body = """{"__type":"com.amazonaws.dynamodb.v20120810#ValidationException","CancellationReasons":[{"Code":"None"}],"message":"bad"}""";

        // Act
        var exception = Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception).IsTypeOf<ValidationException>();
    }

    [Test]
    [Arguments("""{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":{"Code":"None"},"Message":"m"}""")]
    [Arguments("""{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":["None"],"Message":"m"}""")]
    [Arguments("""{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":[{"Code":5}],"Message":"m"}""")]
    [Arguments("""{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":[{"Item":{"pk":{"Q":"x"}}}],"Message":"m"}""")]
    [Arguments("""{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":[{"Code":"None"}""")]
    public async Task Map_MalformedCancellationReasons_ReturnsUnrecognizedResponse(string body)
    {
        // Arrange (see body)

        // Act
        var exception = Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception.ErrorCode).IsEqualTo("UnrecognizedResponse");
    }

    [Test]
    public async Task Map_TypeWithoutNamespace_UsesItAsTheCode()
    {
        // Arrange
        const string body = """{"__type":"ValidationException","message":"bad"}""";

        // Act
        var exception = Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception).IsTypeOf<ValidationException>();
    }

    [Test]
    public async Task Map_TypeWithUriSuffix_UsesTheCodeBeforeTheColon()
    {
        // Arrange
        const string body = """{"__type":"ValidationException:http://internal.amazon.com/coral/com.amazon.coral.validate/","message":"bad"}""";

        // Act
        var exception = Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception).IsTypeOf<ValidationException>();
    }

    [Test]
    public async Task Map_NamespaceAndUriSuffix_UsesTheCodeBetweenHashAndColon()
    {
        // Arrange
        const string body = """{"__type":"com.amazon.coral.validate#ValidationException:http://internal.amazon.com/x#y","message":"bad"}""";

        // Act
        var exception = Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception).IsTypeOf<ValidationException>();
    }

    [Test]
    public async Task Map_UnknownCodeWithNamespace_ErrorCodeHasNoNamespace()
    {
        // Arrange
        const string body = """{"__type":"com.example#SomethingNew","message":"new"}""";

        // Act
        var exception = Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception.ErrorCode).IsEqualTo("SomethingNew");
    }

    [Test]
    public async Task Map_NoMessage_UsesAMessageWithCodeAndStatus()
    {
        // Arrange
        const string body = """{"__type":"com.amazonaws.dynamodb.v20120810#ResourceNotFoundException"}""";

        // Act
        var exception = Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception.Message).IsEqualTo("DynamoDB returned the error ResourceNotFoundException (HTTP 400) without a message.");
    }

    [Test]
    public async Task Map_UnknownPropertiesAreSkipped_StillMapsTheError()
    {
        // Arrange
        const string body = """{"Extra":{"a":[1,2,{"b":null}]},"__type":"com.amazonaws.dynamodb.v20120810#ResourceNotFoundException","Other":[true],"message":"gone"}""";

        // Act
        var exception = Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception.Message).IsEqualTo("gone");
    }

    [Test]
    public async Task Map_EscapedPropertyNameAndText_AreUnescaped()
    {
        // Arrange (the JSON text contains the escape sequences __type and ü)
        var body = "{\"" + Backslash + "u005f" + Backslash + "u005ftype\":\"com.amazonaws.dynamodb.v20120810#ResourceNotFoundException\",\"message\":\"Z" + Backslash + "u00fcrich\"}";

        // Act
        var exception = Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That((exception.GetType(), exception.Message)).IsEqualTo((typeof(ResourceNotFoundException), "Zürich"));
    }

    [Test]
    public async Task Map_RequestIdNull_RequestIdIsNull()
    {
        // Arrange
        const string body = """{"__type":"com.amazonaws.dynamodb.v20120810#ResourceNotFoundException","message":"gone"}""";

        // Act
        var exception = ErrorMapper.Map(HttpStatusCode.BadRequest, null, Encoding.UTF8.GetBytes(body));

        // Assert
        await Assert.That(exception.RequestId).IsNull();
    }

    [Test]
    public async Task Map_ProxyHtmlWith503_ReturnsUnrecognizedResponseWithStatus()
    {
        // Arrange
        const string body = "<html><head><title>503 Service Unavailable</title></head><body>Service Unavailable</body></html>";

        // Act
        var exception = Map(HttpStatusCode.ServiceUnavailable, body);

        // Assert
        await Assert.That((exception.GetType(), exception.ErrorCode, exception.StatusCode)).IsEqualTo((typeof(DynamoDbException), "UnrecognizedResponse", HttpStatusCode.ServiceUnavailable));
    }

    [Test]
    public async Task Map_ProxyHtml_MessageNamesTheStatus()
    {
        // Arrange
        const string body = "<html>Service Unavailable</html>";

        // Act
        var exception = Map(HttpStatusCode.ServiceUnavailable, body);

        // Assert
        await Assert.That(exception.Message).IsEqualTo("DynamoDB returned HTTP 503 (ServiceUnavailable) with a body that is not a DynamoDB response.");
    }

    [Test]
    public async Task Map_ProxyHtml_KeepsTheRequestId()
    {
        // Arrange
        const string body = "<html>Service Unavailable</html>";

        // Act
        var exception = Map(HttpStatusCode.ServiceUnavailable, body);

        // Assert
        await Assert.That(exception.RequestId).IsEqualTo(RequestId);
    }

    [Test]
    public async Task Map_ProxyHtml_InnerExceptionIsTheParseFailure()
    {
        // Arrange
        const string body = "<html>Service Unavailable</html>";

        // Act
        var exception = Map(HttpStatusCode.ServiceUnavailable, body);

        // Assert
        await Assert.That(exception.InnerException).IsNotNull();
    }

    [Test]
    public async Task Map_EmptyBody_ReturnsUnrecognizedResponse()
    {
        // Arrange (a gateway timeout without a body)

        // Act
        var exception = Map(HttpStatusCode.GatewayTimeout, string.Empty);

        // Assert
        await Assert.That((exception.ErrorCode, exception.StatusCode)).IsEqualTo(("UnrecognizedResponse", HttpStatusCode.GatewayTimeout));
    }

    [Test]
    [Arguments("[]")]
    [Arguments("\"text\"")]
    [Arguments("42")]
    [Arguments("null")]
    public async Task Map_JsonThatIsNotAnObject_ReturnsUnrecognizedResponse(string body)
    {
        // Arrange (the body)

        // Act
        var exception = Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception.ErrorCode).IsEqualTo("UnrecognizedResponse");
    }

    [Test]
    public async Task Map_ObjectWithoutType_ReturnsUnrecognizedResponseWithTheMessageOfTheBody()
    {
        // Arrange (the shape of an API gateway error)
        const string body = """{"message":"Missing Authentication Token"}""";

        // Act
        var exception = Map(HttpStatusCode.Forbidden, body);

        // Assert
        await Assert.That((exception.ErrorCode, exception.Message, exception.InnerException)).IsEqualTo(("UnrecognizedResponse", "Missing Authentication Token", null));
    }

    [Test]
    public async Task Map_EmptyType_ReturnsUnrecognizedResponse()
    {
        // Arrange
        const string body = """{"__type":"","message":"odd"}""";

        // Act
        var exception = Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception.ErrorCode).IsEqualTo("UnrecognizedResponse");
    }

    [Test]
    public async Task Map_NullType_ReturnsUnrecognizedResponse()
    {
        // Arrange
        const string body = """{"__type":null,"message":"odd"}""";

        // Act
        var exception = Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception.ErrorCode).IsEqualTo("UnrecognizedResponse");
    }

    [Test]
    public async Task Map_TypeThatIsNotAString_ReturnsUnrecognizedResponse()
    {
        // Arrange
        const string body = """{"__type":5,"message":"odd"}""";

        // Act
        var exception = Map(HttpStatusCode.BadRequest, body);

        // Assert
        await Assert.That(exception.ErrorCode).IsEqualTo("UnrecognizedResponse");
    }

    [Test]
    public async Task Unrecognized_NoMessage_UsesTheStatusInTheMessage()
    {
        // Arrange (nothing to arrange)

        // Act
        var exception = ErrorMapper.Unrecognized(HttpStatusCode.OK, null, null);

        // Assert
        await Assert.That(exception.Message).IsEqualTo("DynamoDB returned HTTP 200 (OK) with a body that is not a DynamoDB response.");
    }

    [Test]
    public async Task Unrecognized_Always_UsesTheDocumentedErrorCode()
    {
        // Arrange (nothing to arrange)

        // Act
        var exception = ErrorMapper.Unrecognized(HttpStatusCode.OK, null, null);

        // Assert
        await Assert.That(exception.ErrorCode).IsEqualTo(ErrorMapper.UnrecognizedResponseCode);
    }

    private static DynamoDbException Map(HttpStatusCode statusCode, string body) =>
        ErrorMapper.Map(statusCode, RequestId, Encoding.UTF8.GetBytes(body));
}
