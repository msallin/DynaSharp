using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// The envelope of the AWS JSON 1.0 protocol: X-Amz-Target names the operation, errors are {"__type":"namespace#Code","message":"..."} with HTTP 400, and DynamoDB Local
// answers an operation it does not know with UnknownOperationException.
public sealed class FakeWireTests
{
    [Test]
    [Arguments("DynamoDB_20120810.DescribeLimits")]
    [Arguments("DescribeLimits")]
    [Arguments("DynamoDBStreams_20120810.ListStreams")]
    [Arguments("ListStreams")]
    public async Task Handle_TargetOrBareName_FindsTheOperation(string target)
    {
        // Arrange
        var db = new FakeDatabase(TimeProvider.System);

        // Act
        var answer = FakeWire.Handle(db, target, "{}"u8);

        // Assert
        await Assert.That(answer.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    [Arguments(null, "FakeDynamoDb does not implement a request without X-Amz-Target. It is not supported.")]
    [Arguments("DynamoDB_20120810.ExecuteStatement", "FakeDynamoDb does not implement ExecuteStatement. It is not supported.")]
    [Arguments("DynamoDBStreams_20120810.ListShards", "FakeDynamoDb does not implement ListShards. It is not supported.")]
    [Arguments("DynamoDBStreams_20120810.DescribeLimits", "FakeDynamoDb does not implement DescribeLimits. It is not supported.")]
    [Arguments("ListShards", "FakeDynamoDb does not implement ListShards. It is not supported.")]
    [Arguments("Other_2020.GetItem", "FakeDynamoDb does not implement Other_2020.GetItem. It is not supported.")]
    public async Task Handle_OperationTheFakeDoesNotAnswer_AnswersUnknownOperation(string? target, string message)
    {
        // Arrange
        var db = new FakeDatabase(TimeProvider.System);

        // Act
        var answer = FakeWire.Handle(db, target, "not even JSON"u8);

        // Assert
        await Assert.That(FakeCalls.SameJson(
            $$"""{"__type":"com.amazonaws.dynamodb.v20120810#UnknownOperationException","message":"{{message}}"}""",
            JsonElement.Parse(answer.Body.Span))).IsTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments("{")]
    [Arguments("[]")]
    [Arguments("\"text\"")]
    public async Task Handle_BodyThatIsNoObject_AnswersSerialization(string body)
    {
        // Arrange
        var db = new FakeDatabase(TimeProvider.System);

        // Act
        var answer = FakeWire.Handle(db, "DescribeLimits", Encoding.UTF8.GetBytes(body));

        // Assert
        await Assert.That((answer.StatusCode, JsonElement.Parse(answer.Body.Span).GetProperty("__type").GetString()))
            .IsEqualTo((HttpStatusCode.BadRequest, "com.amazonaws.dynamodb.v20120810#SerializationException"));
    }

    [Test]
    public async Task Handle_TwoRequests_GiveDifferentRequestIds()
    {
        // Arrange
        var db = new FakeDatabase(TimeProvider.System);

        // Act
        var first = FakeWire.Handle(db, "DescribeLimits", "{}"u8);
        var second = FakeWire.Handle(db, "DescribeLimits", "{}"u8);

        // Assert
        await Assert.That(first.RequestId).IsNotEqualTo(second.RequestId);
    }

    [Test]
    public async Task Handle_NullDatabase_ThrowsArgumentNullException()
    {
        // Arrange
        FakeDatabase db = null!;

        // Act
        var act = () => FakeWire.Handle(db, "DescribeLimits", []);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Registry_EveryOperationOfTheClient_IsAnsweredOrDeclined()
    {
        // Arrange: every IOperation of the library, so that an operation added there fails here until the fake decides about it.
        var names = typeof(IOperation<,>).Assembly.GetTypes()
            .Where(type => type.GetInterfaces().Any(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IOperation<,>)))
            .Select(type => (string)type.GetProperty("Name", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!)
            .ToList();

        // Act
        var undecided = names.Where(name => FakeWire.Implements(name) == FakeWire.NotImplemented.Contains(name)).ToList();

        // Assert
        await Assert.That((names.Count, undecided.Count)).IsEqualTo((21, 0));
    }
}
