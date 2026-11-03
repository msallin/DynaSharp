using System.Net;
using System.Text.Json;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// The error bodies of the API reference, "Error handling with DynamoDB": {"__type":"com.amazonaws.dynamodb.v20120810#Code","message":"..."}, with Item for a failed
// condition that asked for it and CancellationReasons for a cancelled transaction.
internal sealed class FakeErrorTests
{
    public static IEnumerable<Func<(FakeError Error, string Body)>> Errors()
    {
        const string Prefix = "com.amazonaws.dynamodb.v20120810#";
        yield return () => (FakeError.Validation("bad"), $$"""{"__type":"{{Prefix}}ValidationException","message":"bad"}""");
        yield return () => (FakeError.Serialization("odd"), $$"""{"__type":"{{Prefix}}SerializationException","message":"odd"}""");
        yield return () => (FakeError.ResourceNotFound(), $$"""{"__type":"{{Prefix}}ResourceNotFoundException","message":"Requested resource not found"}""");
        yield return () => (FakeError.ResourceInUse("busy"), $$"""{"__type":"{{Prefix}}ResourceInUseException","message":"busy"}""");
        yield return () => (FakeError.LimitExceeded("much"), $$"""{"__type":"{{Prefix}}LimitExceededException","message":"much"}""");
        yield return () => (FakeError.ConditionalCheckFailed(null), $$"""{"__type":"{{Prefix}}ConditionalCheckFailedException","message":"The conditional request failed"}""");
        yield return () => (FakeError.ConditionalCheckFailed(new Item { ["pk"] = "a" }),
            $$$$"""{"__type":"{{{{Prefix}}}}ConditionalCheckFailedException","message":"The conditional request failed","Item":{"pk":{"S":"a"}}}""");
        yield return () => (FakeError.IdempotentParameterMismatch(),
            $$"""{"__type":"{{Prefix}}IdempotentParameterMismatchException","message":"The request uses the same client token as a previous, but non-identical request."}""");
        yield return () => (FakeError.TransactionCanceled([new CancellationReason { Code = "ConditionalCheckFailed", Message = "The conditional request failed", Item = new Item { ["pk"] = "a" } }, new CancellationReason()]),
            $$$$"""
            {"__type":"{{{{Prefix}}}}TransactionCanceledException","message":"Transaction cancelled, please refer cancellation reasons for specific reasons [ConditionalCheckFailed, None]",
             "CancellationReasons":[{"Code":"ConditionalCheckFailed","Message":"The conditional request failed","Item":{"pk":{"S":"a"}}},{}]}
            """);
    }

    [Test]
    [MethodDataSource(nameof(Errors))]
    public async Task RenderBody_Error_IsTheBodyOfDynamoDb(FakeError error, string body)
    {
        // Arrange
        // Act
        var rendered = error.RenderBody();

        // Assert
        await Assert.That(FakeCalls.SameJson(body, JsonElement.Parse(rendered))).IsTrue();
    }

    [Test]
    public async Task Unsupported_WithOperation_NamesWhatAndWhereAndTheDocs()
    {
        // Arrange
        // Act
        var error = FakeError.Unsupported("the member Tags", "CreateTable");

        // Assert
        await Assert.That((error.Type, error.Code, error.Status, error.Message)).IsEqualTo((
            "DynaSharp.Testing#UnsupportedByFakeException",
            "UnsupportedByFakeException",
            HttpStatusCode.BadRequest,
            "FakeDynamoDb does not support the member Tags in CreateTable. It answers what https://msallin.github.io/DynaSharp/testing.html#what-the-fake-does lists, and nothing it does not implement is ignored."));
    }

    [Test]
    public async Task Unsupported_WithoutOperation_LeavesItOut()
    {
        // Arrange
        // Act
        var error = FakeError.Unsupported("a page of a read");

        // Assert
        await Assert.That(error.Message).StartsWith("FakeDynamoDb does not support a page of a read. It answers", StringComparison.Ordinal);
    }

    [Test]
    public async Task ToDynamoDbException_Validation_IsTheExceptionOfTheClient()
    {
        // Arrange
        var error = FakeError.Validation("bad");

        // Act
        var exception = error.ToDynamoDbException("PutItem", "Orders");

        // Assert
        await Assert.That((exception is ValidationException, exception.Message, exception.OperationName, exception.TableName)).IsEqualTo((true, "bad", "PutItem", "Orders"));
    }

    [Test]
    public async Task ToDynamoDbException_Unsupported_HasTheCodeOfTheFake()
    {
        // Arrange
        var error = FakeError.Unsupported("a stream", "CreateTable");

        // Act
        var exception = error.ToDynamoDbException("CreateTable", null);

        // Assert
        await Assert.That(exception.ErrorCode).IsEqualTo("UnsupportedByFakeException");
    }

    [Test]
    public async Task TransactionCanceled_Null_ThrowsArgumentNullException()
    {
        // Arrange
        IReadOnlyList<CancellationReason> reasons = null!;

        // Act
        var act = () => FakeError.TransactionCanceled(reasons);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }
}
