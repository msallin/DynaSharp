namespace DynaSharp.UnitTests.Requests;

public sealed class DynamoDbRequestsTests
{
    // The operations are extension methods, so a null client reaches them: each must reject it by name instead of failing with a
    // NullReferenceException inside the library.
    [Test]
    [MethodDataSource(typeof(DynamoDbClientDisposalTests), nameof(DynamoDbClientDisposalTests.Operations))]
    public async Task Operation_NullClient_ThrowsArgumentNullExceptionNamingTheClient(DisposedCall<DynamoDbClient> operation)
    {
        // Arrange
        DynamoDbClient client = null!;

        // Act
        var exception = await TestClient.CaptureAsync<ArgumentNullException>(() => operation.Call(client));

        // Assert
        await Assert.That(exception.ParamName).IsEqualTo("client");
    }
}
