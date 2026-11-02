using DynaSharp.Requests;
using DynaSharp.Streams;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Testing;

public sealed class StubClientTests
{
    private static readonly GetItemRequest Get = new() { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-1" } };

    [Test]
    public async Task Create_Handler_SendsThroughTheHandler()
    {
        // Arrange
        using var stub = new StubHandler(_ => new StubResponse());
        using var client = StubClient.Create(stub);

        // Act
        await client.GetItemAsync(Get);

        // Assert
        await Assert.That(stub.Requests.Single().Operation).IsEqualTo("GetItem");
    }

    [Test]
    public async Task Create_Handler_SignsForTheFixedRegionWithTheFakeCredentials()
    {
        // Arrange
        using var stub = new StubHandler(_ => new StubResponse());
        using var client = StubClient.Create(stub);

        // Act
        await client.GetItemAsync(Get);

        // Assert
        await Assert.That(stub.Requests.Single().Headers["Authorization"]).Contains("Credential=test/").And.Contains("/us-east-1/dynamodb/aws4_request");
    }

    [Test]
    public async Task Create_Handler_SendsAFailedRequestOnce()
    {
        // Arrange
        using var stub = new StubHandler(_ => StubResponse.DynamoDbError("InternalServerError", "boom", System.Net.HttpStatusCode.InternalServerError));
        using var client = StubClient.Create(stub);

        // Act
        await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(Get));

        // Assert
        await Assert.That(stub.Requests).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Create_Handler_HasNoAttemptTimeout()
    {
        // Arrange
        using var stub = new StubHandler();

        // Act
        using var client = StubClient.Create(stub);

        // Assert
        await Assert.That(client.Options.AttemptTimeout).IsEqualTo(Timeout.InfiniteTimeSpan);
    }

    // A fake clock that a test moves past the default of 30 seconds must not time out a request that the test is still answering.
    [Test]
    public async Task Create_FakeClockMovesPastTheDefaultTimeout_LeavesARequestInFlightAlone()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var requestArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource<StubResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stub = new StubHandler((_, _) =>
        {
            requestArrived.SetResult();
            return answer.Task;
        });
        using var client = StubClient.Create(stub, options => options with { TimeProvider = clock });

        // Act
        var call = client.GetItemAsync(Get);
        await requestArrived.Task;
        clock.Advance(TimeSpan.FromHours(1));
        answer.SetResult(StubResponse.Json("""{"Item":{"pk":{"S":"user-1"}}}"""));
        var response = await call;

        // Assert
        await Assert.That(response.Item).IsNotNull();
    }

    [Test]
    public async Task Create_Configure_KeepsWhatTheDelegateSets()
    {
        // Arrange
        using var stub = new StubHandler();

        // Act
        using var client = StubClient.Create(stub, options => options with { MaxAttempts = 3 });

        // Assert
        await Assert.That(client.Options.MaxAttempts).IsEqualTo(3);
    }

    [Test]
    public async Task Create_Configure_StartsFromTheFixedOptions()
    {
        // Arrange
        using var stub = new StubHandler();
        DynamoDbClientOptions? seen = null;

        // Act
        using var client = StubClient.Create(stub, options => seen = options);

        // Assert
        await Assert.That(seen!.HttpHandler).IsSameReferenceAs(stub);
    }

    [Test]
    public async Task Create_ConfigureBreaksAnOption_FailsAsTheClientDoes()
    {
        // Arrange
        using var stub = new StubHandler();

        // Act
        var act = () => StubClient.Create(stub, options => options with { MaxAttempts = 0 });

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    public async Task Create_EndpointInTheEnvironment_GoesToTheEndpointOfTheRegion()
    {
        // Arrange
        using var environment = new ScopedEnvironmentVariables(("AWS_ENDPOINT_URL_DYNAMODB", "http://environment.test"), ("AWS_ENDPOINT_URL", "http://environment.test"));
        using var stub = new StubHandler(_ => new StubResponse());
        using var client = StubClient.Create(stub);

        // Act
        await client.GetItemAsync(Get);

        // Assert
        await Assert.That(stub.Requests.Single().RequestUri!.Host).IsEqualTo("dynamodb.us-east-1.amazonaws.com");
    }

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    public async Task Create_RegionClearedAndOneInTheEnvironment_FindsNoRegion()
    {
        // Arrange
        using var environment = new ScopedEnvironmentVariables(("AWS_REGION", "eu-west-1"));
        using var stub = new StubHandler();

        // Act
        var act = () => StubClient.Create(stub, options => options with { Region = null });

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Create_NullHandler_Throws()
    {
        // Arrange
        HttpMessageHandler handler = null!;

        // Act
        var act = () => StubClient.Create(handler);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task CreateStreams_Handler_GoesToTheStreamsEndpointOfTheRegion()
    {
        // Arrange
        using var stub = new StubHandler(_ => new StubResponse());
        using var client = StubClient.CreateStreams(stub);

        // Act
        await client.GetRecordsAsync(new GetRecordsRequest { ShardIterator = "iterator" });

        // Assert
        await Assert.That(stub.Requests.Single().RequestUri!.Host).IsEqualTo("streams.dynamodb.us-east-1.amazonaws.com");
    }

    [Test]
    public async Task CreateStreams_Configure_KeepsWhatTheDelegateSets()
    {
        // Arrange
        using var stub = new StubHandler();

        // Act
        using var client = StubClient.CreateStreams(stub, options => options with { MaxAttempts = 3 });

        // Assert
        await Assert.That(client.Options.MaxAttempts).IsEqualTo(3);
    }

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    public async Task CreateStreams_EndpointInTheEnvironment_GoesToTheEndpointOfTheRegion()
    {
        // Arrange
        using var environment = new ScopedEnvironmentVariables(("AWS_ENDPOINT_URL_DYNAMODB_STREAMS", "http://environment.test"), ("AWS_ENDPOINT_URL", "http://environment.test"));
        using var stub = new StubHandler(_ => new StubResponse());
        using var client = StubClient.CreateStreams(stub);

        // Act
        await client.GetRecordsAsync(new GetRecordsRequest { ShardIterator = "iterator" });

        // Assert
        await Assert.That(stub.Requests.Single().RequestUri!.Host).IsEqualTo("streams.dynamodb.us-east-1.amazonaws.com");
    }

    [Test]
    public async Task CreateStreams_ConfigureClearsTheRegion_Throws()
    {
        // Arrange
        using var stub = new StubHandler();

        // Act
        var act = () => StubClient.CreateStreams(stub, options => options with { Region = null });

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CreateStreams_NullHandler_Throws()
    {
        // Arrange
        HttpMessageHandler handler = null!;

        // Act
        var act = () => StubClient.CreateStreams(handler);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }
}
