using DynaSharp.Credentials;
using DynaSharp.Http;
using DynaSharp.Requests;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests;

public sealed class DynamoDbClientTests
{
    private static readonly StaticCredentialsProvider Credentials = new(TestClient.Credentials);

    // With every option set, a client does not read the environment, so the variables of the machine cannot fail a test.
    private static readonly Uri LocalEndpoint = new("http://localhost:8000");

    [Test]
    public async Task Constructor_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        DynamoDbClientOptions options = null!;

        // Act
        var act = () => new DynamoDbClient(options);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("options");
    }

    [Test]
    public async Task Constructor_OptionsWithRegion_CreatesAClientThatCanBeDisposed()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = TestClient.Region, ServiceUrl = LocalEndpoint, Credentials = Credentials };

        // Act
        var dispose = () =>
        {
            using var client = new DynamoDbClient(options);
        };

        // Assert
        await Assert.That(dispose).ThrowsNothing();
    }

    [Test]
    public async Task Constructor_NoRegionConfigured_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Credentials = Credentials };

        // Act
        var act = () => new DynamoDbClient(options, static _ => null, null, null, readFile: static _ => null);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("AWS_REGION", StringComparison.Ordinal);
    }

    [Test]
    public async Task Constructor_RegionOnlyInTheSharedConfigFile_UsesIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        var options = new DynamoDbClientOptions { Credentials = Credentials, HttpHandler = handler };
        using var client = new DynamoDbClient(
            options,
            static name => name == "AWS_CONFIG_FILE" ? "config" : null,
            null,
            null,
            readFile: static path => path == "config" ? "[default]\nregion = ap-northeast-1\n" : null);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(handler.Requests.Single().Headers["Authorization"]).Contains("/ap-northeast-1/dynamodb/aws4_request", StringComparison.Ordinal);
    }

    [Test]
    public async Task Constructor_RegionFromProfile_LogsProfileAsTheSource()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var options = new DynamoDbClientOptions { Credentials = Credentials, LoggerFactory = logs };

        // Act
        using var client = new DynamoDbClient(
            options,
            static name => name == "AWS_CONFIG_FILE" ? "config" : null,
            null,
            null,
            readFile: static path => path == "config" ? "[default]\nregion = ap-northeast-1\n" : null);

        // Assert
        await Assert.That(logs.Entries[0].Message).IsEqualTo("DynamoDB client uses region ap-northeast-1 (source: profile).");
    }

    [Test]
    public async Task Constructor_InvalidAttemptTimeout_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = TestClient.Region, AttemptTimeout = TimeSpan.FromDays(50) };

        // Act
        var act = () => new DynamoDbClient(options);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    public async Task Constructor_Default_ReadsTheRegionFromTheProcessEnvironment()
    {
        // Arrange
        using var environment = new ScopedEnvironmentVariables(("AWS_REGION", "eu-central-2"), ("AWS_DEFAULT_REGION", null));

        // Act
        var create = () =>
        {
            using var client = new DynamoDbClient();
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
        var create = () => new DynamoDbClient();

        // Assert
        await Assert.That(create).ThrowsExactly<InvalidOperationException>().WithMessageContaining("AWS_REGION", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetItemAsync_RegionFromEnvironment_SignsForThatRegion()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        var options = new DynamoDbClientOptions { Credentials = Credentials, HttpHandler = handler };
        using var client = new DynamoDbClient(options, static name => name == "AWS_REGION" ? "us-west-2" : null, null, null);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(handler.Requests.Single().Headers["Authorization"]).Contains("/us-west-2/dynamodb/aws4_request", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetItemAsync_EndpointFromEnvironment_SendsToThatEndpoint()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        var options = new DynamoDbClientOptions { Region = TestClient.Region, Credentials = Credentials, HttpHandler = handler };
        using var client = new DynamoDbClient(options, static name => name == "AWS_ENDPOINT_URL_DYNAMODB" ? "http://localhost:8000" : null, null, null);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(new Uri("http://localhost:8000/"));
    }

    [Test]
    public async Task GetItemAsync_CredentialsFromEnvironment_SignsWithThem()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        var options = new DynamoDbClientOptions { Region = TestClient.Region, HttpHandler = handler };
        using var client = new DynamoDbClient(
            options,
            static name => name switch
            {
                "AWS_ACCESS_KEY_ID" => "AKIDFROMENVIRONMENT",
                "AWS_SECRET_ACCESS_KEY" => "secret-from-environment",
                _ => null,
            },
            null,
            null);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(handler.Requests.Single().Headers["Authorization"]).Contains("Credential=AKIDFROMENVIRONMENT/", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetItemAsync_NoCredentialsAnywhere_ThrowsCredentialsUnavailableException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        var options = new DynamoDbClientOptions { Region = TestClient.Region, HttpHandler = handler };

        // The metadata service is switched off, so that the test does not call the network.
        using var client = new DynamoDbClient(
            options,
            static name => name == "AWS_EC2_METADATA_DISABLED" ? "true" : null,
            null,
            null,
            readFile: static _ => null);

        // Act
        var act = async () => await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("AWS_ACCESS_KEY_ID", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetItemAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.GetItemAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }

    [Test]
    public async Task PutItemAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.PutItemAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }

    [Test]
    public async Task DescribeTableAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.DescribeTableAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }

    [Test]
    public async Task UpdateItemAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.UpdateItemAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }

    [Test]
    public async Task DeleteItemAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.DeleteItemAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }

    [Test]
    public async Task QueryAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.QueryAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }

    [Test]
    public async Task ScanAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.ScanAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }

    [Test]
    public async Task Dispose_CalledTwice_DoesNotThrow()
    {
        // Arrange
        using var handler = new StubHandler();
        var client = TestClient.Create(handler);
        client.Dispose();

        // Act
        var act = client.Dispose;

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task Dispose_GivenHandler_DoesNotDisposeIt()
    {
        // Arrange
        using var handler = new TrackingHandler();
        var client = TestClient.Create(handler);

        // Act
        client.Dispose();

        // Assert
        await Assert.That(handler.Disposed).IsFalse();
    }

    [Test]
    public async Task Dispose_ClientWithoutGivenHandler_DoesNotThrow()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = TestClient.Region, ServiceUrl = LocalEndpoint, Credentials = Credentials };
        using var client = new DynamoDbClient(options);

        // Act
        var act = client.Dispose;

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task CreateOwnedHandler_Always_RecyclesConnectionsAfterTwoMinutes()
    {
        // Arrange (nothing to arrange)

        // Act
        using var handler = ClientCore.CreateOwnedHandler();

        // Assert
        await Assert.That(handler.PooledConnectionLifetime).IsEqualTo(TimeSpan.FromMinutes(2));
    }

    [Test]
    public async Task CreateOwnedHandler_Always_DoesNotFollowRedirects()
    {
        // Arrange (nothing to arrange)

        // Act
        using var handler = ClientCore.CreateOwnedHandler();

        // Assert
        await Assert.That(handler.AllowAutoRedirect).IsFalse();
    }

    [Test]
    public async Task CreateOwnedHandler_Always_DoesNotUseCookies()
    {
        // Arrange (nothing to arrange)

        // Act
        using var handler = ClientCore.CreateOwnedHandler();

        // Assert
        await Assert.That(handler.UseCookies).IsFalse();
    }

    private sealed class ScopedEnvironmentVariables : IDisposable
    {
        private readonly (string Name, string? Previous)[] _previous;

        public ScopedEnvironmentVariables(params (string Name, string? Value)[] variables)
        {
            _previous = [.. variables.Select(variable => (variable.Name, Environment.GetEnvironmentVariable(variable.Name)))];
            foreach (var (name, value) in variables)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, previous) in _previous)
            {
                Environment.SetEnvironmentVariable(name, previous);
            }
        }
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
