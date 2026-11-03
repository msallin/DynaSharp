using DynaSharp.Credentials;
using DynaSharp.Requests;
using DynaSharp.Streams;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests;

/// <summary>Creating, configuring and disposing a <see cref="DynamoDbStreamsClient"/>.</summary>
public sealed class DynamoDbStreamsClientTests
{
    private const string EmptyRecords = """{"Records":[]}""";

    private static readonly StaticCredentialsProvider Credentials = new(TestClient.Credentials);

    // With every option set, a client does not read the environment, so the variables of the machine cannot fail a test.
    private static readonly Uri LocalEndpoint = new("http://localhost:8000");

    [Test]
    public async Task Constructor_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        DynamoDbClientOptions options = null!;

        // Act
        var act = () => new DynamoDbStreamsClient(options);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("options");
    }

    [Test]
    public async Task Options_RegionAndGenericEndpointFromTheEnvironment_AreThere()
    {
        // Arrange
        static string? Variables(string name) => name switch
        {
            "AWS_REGION" => "us-west-2",
            "AWS_ENDPOINT_URL" => "http://localhost:8001",
            _ => null,
        };

        using var client = new DynamoDbStreamsClient(new DynamoDbClientOptions(), Variables, null, null);

        // Act
        var options = client.Options;

        // Assert
        await Assert.That((options.Region?.Name, options.ServiceUrl, options.Credentials is DefaultCredentialsProvider)).IsEqualTo(((string?)"us-west-2", new Uri("http://localhost:8001"), true));
    }

    [Test]
    public async Task Options_EndpointFromTheServiceVariable_StaysNull()
    {
        // Arrange (the variable belongs to DynamoDB Streams, so a copy for another client must not carry it along)
        static string? Environment(string name) => name switch
        {
            "AWS_REGION" => "us-west-2",
            "AWS_ENDPOINT_URL_DYNAMODB_STREAMS" => "http://localhost:8001",
            _ => null,
        };

        using var client = new DynamoDbStreamsClient(new DynamoDbClientOptions(), Environment, null, null);

        // Act
        var endpoint = client.Options.ServiceUrl;

        // Assert
        await Assert.That(endpoint).IsNull();
    }

    [Test]
    public async Task Options_OfAStreamsClientWithServiceSpecificEndpoints_GiveAClientItsOwnEndpoint()
    {
        // Arrange
        static string? Variables(string name) => name switch
        {
            "AWS_REGION" => "us-west-2",
            "AWS_ENDPOINT_URL_DYNAMODB" => "http://localhost:8001",
            "AWS_ENDPOINT_URL_DYNAMODB_STREAMS" => "http://localhost:8002",
            _ => null,
        };

        using var handler = new StubHandler(_ => StubResponse.Json("""{"Table":{"TableName":"Music"}}"""));
        using var streams = new DynamoDbStreamsClient(new DynamoDbClientOptions { Credentials = Credentials, HttpHandler = handler }, Variables, null, null);
        using var client = new DynamoDbClient(streams.Options, Variables, null, null);

        // Act
        await client.DescribeTableAsync("Music");

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(new Uri("http://localhost:8001"));
    }

    [Test]
    public async Task Options_OfAStreamsClientWithAnExplicitServiceUrl_GiveAClientTheSameEndpoint()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Table":{"TableName":"Music"}}"""));
        using var streams = new DynamoDbStreamsClient(new DynamoDbClientOptions { Region = TestClient.Region, ServiceUrl = LocalEndpoint, Credentials = Credentials, HttpHandler = handler }, static _ => null, null, null);
        using var client = new DynamoDbClient(streams.Options, static name => name == "AWS_ENDPOINT_URL_DYNAMODB" ? "http://localhost:8001" : null, null, null);

        // Act
        await client.DescribeTableAsync("Music");

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(new Uri("http://localhost:8000/"));
    }

    [Test]
    public async Task Options_EndpointDerivedFromTheRegion_StaysNull()
    {
        // Arrange
        using var client = new DynamoDbStreamsClient(new DynamoDbClientOptions(), static name => name == "AWS_REGION" ? "us-west-2" : null, null, null);

        // Act
        var endpoint = client.Options.ServiceUrl;

        // Assert
        await Assert.That(endpoint).IsNull();
    }

    [Test]
    public async Task Options_OfAStreamsClientWithADerivedEndpoint_GiveAClientItsOwnEndpoint()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Table":{"TableName":"Music"}}"""));
        var region = AwsRegion.FromName("us-west-2");
        using var streams = new DynamoDbStreamsClient(new DynamoDbClientOptions { Region = region, Credentials = Credentials, HttpHandler = handler }, static _ => null, null, null);
        using var client = new DynamoDbClient(streams.Options, static _ => null, null, null);

        // Act
        await client.DescribeTableAsync("Music");

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(region.DynamoDbEndpoint);
    }

    [Test]
    public async Task Options_With_GivesASecondClientThatWorks()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Streams":[]}"""));
        var given = new DynamoDbClientOptions { Region = TestClient.Region, ServiceUrl = LocalEndpoint, Credentials = Credentials, HttpHandler = handler };
        using var first = new DynamoDbStreamsClient(given);
        using var second = new DynamoDbStreamsClient(first.Options with { MaxAttempts = 1 });

        // Act
        await second.ListStreamsAsync(new ListStreamsRequest());

        // Assert
        await Assert.That((handler.Requests.Count, second.Options.MaxAttempts)).IsEqualTo((1, 1));
    }

    [Test]
    public async Task Constructor_OptionsWithRegion_CreatesAClientThatCanBeDisposed()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = TestClient.Region, ServiceUrl = LocalEndpoint, Credentials = Credentials };

        // Act
        var dispose = () =>
        {
            using var client = new DynamoDbStreamsClient(options);
        };

        // Assert
        await Assert.That(dispose).ThrowsNothing();
    }

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    public async Task Constructor_Default_ReadsTheRegionFromTheProcessEnvironment()
    {
        // Arrange
        using var environment = new ScopedEnvironmentVariables(("AWS_REGION", "eu-central-2"), ("AWS_DEFAULT_REGION", null), ("AWS_ENDPOINT_URL_DYNAMODB_STREAMS", null), ("AWS_ENDPOINT_URL", null));

        // Act
        var create = () =>
        {
            using var client = new DynamoDbStreamsClient();
        };

        // Assert
        await Assert.That(create).ThrowsNothing();
    }

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    public async Task Constructor_DefaultWithoutRegionInTheProcessEnvironment_ThrowsInvalidOperationException()
    {
        // Arrange (the shared files point to nowhere, so the files of the machine cannot provide a region)
        var missing = Path.Combine(Path.GetTempPath(), $"dynasharp-test-{Guid.NewGuid():N}", "missing");
        using var environment = new ScopedEnvironmentVariables(
            ("AWS_REGION", null),
            ("AWS_DEFAULT_REGION", null),
            ("AWS_PROFILE", null),
            ("AWS_CONFIG_FILE", missing),
            ("AWS_SHARED_CREDENTIALS_FILE", missing));

        // Act
        var create = () => new DynamoDbStreamsClient();

        // Assert
        await Assert.That(create).ThrowsExactly<InvalidOperationException>().WithMessageContaining("AWS_REGION", StringComparison.Ordinal);
    }

    [Test]
    public async Task Constructor_NoRegionConfigured_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Credentials = Credentials };

        // Act
        var act = () => new DynamoDbStreamsClient(options, static _ => null, null, null, readFile: static _ => null);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("AWS_REGION", StringComparison.Ordinal);
    }

    [Test]
    public async Task Constructor_InvalidAttemptTimeout_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = TestClient.Region, AttemptTimeout = TimeSpan.FromDays(50) };

        // Act
        var act = () => new DynamoDbStreamsClient(options);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Constructor_Options_LogsRegionAndEndpointUnderTheCategoryOfTheStreamsClient()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var options = new DynamoDbClientOptions { Region = TestClient.Region, Credentials = Credentials, LoggerFactory = logs };

        // Act
        using var client = new DynamoDbStreamsClient(options, static _ => null, null, null);

        // Assert
        await Assert.That(logs.Entries.Select(entry => (entry.Category, entry.Message)).Take(2)).IsEquivalentTo(
        [
            ("DynaSharp.Streams.DynamoDbStreamsClient", "DynamoDB client uses region eu-central-2 (source: options)."),
            ("DynaSharp.Streams.DynamoDbStreamsClient", "DynamoDB client uses endpoint https://streams.dynamodb.eu-central-2.amazonaws.com/ (source: region)."),
        ]);
    }

    [Test]
    public async Task GetRecordsAsync_NoEndpointConfigured_SendsToTheStreamsEndpointOfTheRegion()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(EmptyRecords));
        using var client = TestClient.CreateStreams(handler);

        // Act
        await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(new Uri("https://streams.dynamodb.eu-central-2.amazonaws.com/"));
    }

    [Test]
    public async Task GetRecordsAsync_ChinaRegion_SendsToTheStreamsEndpointOfThePartition()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(EmptyRecords));
        var options = new DynamoDbClientOptions { Region = AwsRegion.FromName("cn-north-1"), Credentials = Credentials, HttpHandler = handler };
        using var client = new DynamoDbStreamsClient(options, static _ => null, null, null);

        // Act
        await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(new Uri("https://streams.dynamodb.cn-north-1.amazonaws.com.cn/"));
    }

    [Test]
    public async Task GetRecordsAsync_ServiceUrlInOptions_SendsToThatEndpoint()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(EmptyRecords));
        using var client = TestClient.CreateStreams(handler, serviceUrl: LocalEndpoint);

        // Act
        await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(new Uri("http://localhost:8000/"));
    }

    [Test]
    public async Task GetRecordsAsync_ServiceUrlAndEnvironment_OptionsWin()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(EmptyRecords));
        var options = TestClient.Options(handler, serviceUrl: LocalEndpoint);
        using var client = new DynamoDbStreamsClient(options, static name => name is "AWS_ENDPOINT_URL_DYNAMODB_STREAMS" or "AWS_ENDPOINT_URL" ? "http://elsewhere:9000" : null, null, null);

        // Act
        await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(new Uri("http://localhost:8000/"));
    }

    [Test]
    public async Task GetRecordsAsync_StreamsEndpointVariable_SendsToThatEndpoint()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(EmptyRecords));
        var options = TestClient.Options(handler);
        using var client = new DynamoDbStreamsClient(options, static name => name == "AWS_ENDPOINT_URL_DYNAMODB_STREAMS" ? "http://streams-a:8000" : null, null, null);

        // Act
        await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(new Uri("http://streams-a:8000/"));
    }

    [Test]
    public async Task GetRecordsAsync_GenericEndpointVariable_SendsToThatEndpoint()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(EmptyRecords));
        var options = TestClient.Options(handler);
        using var client = new DynamoDbStreamsClient(options, static name => name == "AWS_ENDPOINT_URL" ? "https://gateway.internal" : null, null, null);

        // Act
        await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(new Uri("https://gateway.internal/"));
    }

    [Test]
    public async Task GetRecordsAsync_StreamsAndGenericEndpointVariables_StreamsVariableWins()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(EmptyRecords));
        var options = TestClient.Options(handler);
        var environment = new Dictionary<string, string> { ["AWS_ENDPOINT_URL_DYNAMODB_STREAMS"] = "http://streams-a:8000", ["AWS_ENDPOINT_URL"] = "https://gateway.internal" };
        using var client = new DynamoDbStreamsClient(options, name => environment.GetValueOrDefault(name), null, null);

        // Act
        await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(new Uri("http://streams-a:8000/"));
    }

    [Test]
    public async Task GetRecordsAsync_OnlyTheDynamoDbEndpointVariable_IsNotUsedForStreams()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(EmptyRecords));
        var options = TestClient.Options(handler);
        using var client = new DynamoDbStreamsClient(options, static name => name == "AWS_ENDPOINT_URL_DYNAMODB" ? "http://dynamodb-a:8000" : null, null, null);

        // Act
        await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(new Uri("https://streams.dynamodb.eu-central-2.amazonaws.com/"));
    }

    [Test]
    public async Task GetItemAsync_OnlyTheStreamsEndpointVariable_IsNotUsedForDynamoDb()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        var options = TestClient.Options(handler);
        using var client = new DynamoDbClient(options, static name => name == "AWS_ENDPOINT_URL_DYNAMODB_STREAMS" ? "http://streams-a:8000" : null, null, null);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(new Uri("https://dynamodb.eu-central-2.amazonaws.com/"));
    }

    [Test]
    public async Task Constructor_InvalidStreamsEndpointVariable_ThrowsInvalidOperationExceptionNamingTheVariable()
    {
        // Arrange
        var options = TestClient.Options(new StubHandler());

        // Act
        var act = () => new DynamoDbStreamsClient(options, static name => name == "AWS_ENDPOINT_URL_DYNAMODB_STREAMS" ? "ftp://nowhere" : null, null, null);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("AWS_ENDPOINT_URL_DYNAMODB_STREAMS", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetRecordsAsync_Request_IsSignedForTheDynamoDbServiceInTheRegion()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(EmptyRecords));
        using var client = TestClient.CreateStreams(handler);

        // Act
        await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That(handler.Requests.Single().Headers["Authorization"]).Contains("/eu-central-2/dynamodb/aws4_request", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetRecordsAsync_Request_SignsTheStreamsHost()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(EmptyRecords));
        using var client = TestClient.CreateStreams(handler);

        // Act
        await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri?.Host).IsEqualTo("streams.dynamodb.eu-central-2.amazonaws.com");
    }

    [Test]
    public async Task Dispose_CalledTwice_ThrowsNothing()
    {
        // Arrange
        var client = TestClient.CreateStreams(new StubHandler());
        client.Dispose();

        // Act
        var act = client.Dispose;

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task Dispose_ClientWithHandlerFromTheCaller_DoesNotDisposeTheHandler()
    {
        // Arrange
        using var handler = new TrackingHandler();
        var client = TestClient.CreateStreams(handler);

        // Act
        client.Dispose();

        // Assert
        await Assert.That(handler.Disposed).IsFalse();
    }

    private sealed class TrackingHandler : HttpMessageHandler
    {
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
