using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DynaSharp.Errors;
using DynaSharp.UnitTests.Operations;

namespace DynaSharp.UnitTests.Errors;

// The oracle is botocore's parser: the error bodies of operation_vectors.json went through it, once for the code and the message and
// once with the shape of the modeled exception, which is where it reads the CancellationReasons.
public sealed class ErrorMapperVectorTests
{
    private const string RequestId = "7QTD4FR8SJ3T2JKB5LCUTH4MJVVV4KQNSO5AEMVJF66Q9ASUAAJG";

    private static readonly Dictionary<string, Type> ExceptionTypes = new()
    {
        ["TransactionCanceledException"] = typeof(TransactionCanceledException),
        ["TransactionInProgressException"] = typeof(TransactionInProgressException),
        ["IdempotentParameterMismatchException"] = typeof(IdempotentParameterMismatchException),
        ["TransactionConflictException"] = typeof(TransactionConflictException),
        ["ReplicatedWriteConflictException"] = typeof(ReplicatedWriteConflictException),
    };

    public static IEnumerable<string> ErrorVectorNames() => OperationVectors.ErrorNames();

    public static IEnumerable<string> CancellationVectorNames() =>
        OperationVectors.Errors.Where(vector => vector.Code == "TransactionCanceledException").Select(vector => vector.Name);

    [Test]
    public async Task ErrorVectors_Always_CoverEveryTransactionException()
    {
        // Arrange (the vectors come from the generator, the types from this class)

        // Act
        var missing = ExceptionTypes.Keys.Except(OperationVectors.Errors.Select(vector => vector.Code));

        // Assert
        await Assert.That(missing).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(ErrorVectorNames))]
    public async Task Map_BotocoreVector_ReturnsTheCodeAndMessageThatBotocoreReads(string name)
    {
        // Arrange
        var vector = OperationVectors.Error(name);

        // Act
        var exception = Map(vector);

        // Assert
        await Assert.That((exception.ErrorCode, exception.Message)).IsEqualTo((vector.Code, vector.Message));
    }

    [Test]
    [MethodDataSource(nameof(ErrorVectorNames))]
    public async Task Map_BotocoreVector_ReturnsTheTypedExceptionOfTheCode(string name)
    {
        // Arrange
        var vector = OperationVectors.Error(name);

        // Act
        var exception = Map(vector);

        // Assert
        await Assert.That(exception.GetType()).IsEqualTo(ExceptionTypes[vector.Code]);
    }

    [Test]
    [MethodDataSource(nameof(ErrorVectorNames))]
    public async Task Map_BotocoreVector_KeepsStatusAndRequestId(string name)
    {
        // Arrange
        var vector = OperationVectors.Error(name);

        // Act
        var exception = Map(vector);

        // Assert
        await Assert.That((exception.StatusCode, exception.RequestId)).IsEqualTo(((HttpStatusCode)vector.Status, RequestId));
    }

    [Test]
    [MethodDataSource(nameof(CancellationVectorNames))]
    public async Task Map_BotocoreVector_ReturnsTheCancellationReasonsThatBotocoreParsed(string name)
    {
        // Arrange
        var vector = OperationVectors.Error(name);

        // Act
        var exception = (TransactionCanceledException)Map(vector);

        // Assert
        var expected = new JsonObject { ["CancellationReasons"] = JsonNode.Parse(vector.Parsed)!["CancellationReasons"]?.DeepClone() ?? new JsonArray() };
        await Assert.That(JsonComparer.Differences(expected.ToJsonString(), ResponseJson.Describe(exception))).IsEqualTo(string.Empty);
    }

    private static DynamoDbException Map(ErrorVector vector) =>
        ErrorMapper.Map((HttpStatusCode)vector.Status, RequestId, Encoding.UTF8.GetBytes(vector.Wire));
}
