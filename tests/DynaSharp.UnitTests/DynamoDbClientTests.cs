using DynaSharp.Credentials;
using DynaSharp.Http;
using DynaSharp.Requests;
using DynaSharp.Streams;
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
    public async Task Options_RegionAndEndpointFromTheEnvironment_AreThere()
    {
        // Arrange
        using var client = new DynamoDbClient(new DynamoDbClientOptions(), Variables, null, null);

        // Act
        var options = client.Options;

        // Assert
        await Assert.That((options.Region?.Name, options.ServiceUrl)).IsEqualTo(((string?)"us-west-2", new Uri("http://localhost:8000")));
    }

    [Test]
    public async Task Options_EndpointDerivedFromTheRegion_StaysNull()
    {
        // Arrange (it follows from the region, so a copy must not carry it along)
        using var client = new DynamoDbClient(new DynamoDbClientOptions(), static name => name == "AWS_REGION" ? "us-west-2" : null, null, null);

        // Act
        var endpoint = client.Options.ServiceUrl;

        // Assert
        await Assert.That(endpoint).IsNull();
    }

    [Test]
    public async Task Options_WithRegionAndADerivedEndpoint_SignsAndSendsToTheNewRegion()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Table":{"TableName":"Music"}}"""));
        var other = AwsRegion.FromName("ap-northeast-1");
        using var first = new DynamoDbClient(new DynamoDbClientOptions { Region = AwsRegion.FromName("us-west-2"), Credentials = Credentials, HttpHandler = handler }, static _ => null, null, null);
        using var second = new DynamoDbClient(first.Options with { Region = other }, static _ => null, null, null);

        // Act
        await second.DescribeTableAsync("Music");
        var sent = handler.Requests.Single();

        // Assert
        await Assert.That((sent.RequestUri, sent.Headers["Authorization"].Contains("/ap-northeast-1/dynamodb/aws4_request", StringComparison.Ordinal))).IsEqualTo((other.DynamoDbEndpoint, true));
    }

    [Test]
    public async Task Options_WithRegionAndAnExplicitServiceUrl_KeepsTheEndpointAndSignsForTheNewRegion()
    {
        // Arrange (an emulator stays where it is, whatever region a copy names)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Table":{"TableName":"Music"}}"""));
        var given = new DynamoDbClientOptions { Region = AwsRegion.FromName("us-west-2"), ServiceUrl = LocalEndpoint, Credentials = Credentials, HttpHandler = handler };
        using var first = new DynamoDbClient(given);
        using var second = new DynamoDbClient(first.Options with { Region = AwsRegion.FromName("ap-northeast-1") });

        // Act
        await second.DescribeTableAsync("Music");
        var sent = handler.Requests.Single();

        // Assert
        await Assert.That((sent.RequestUri, sent.Headers["Authorization"].Contains("/ap-northeast-1/dynamodb/aws4_request", StringComparison.Ordinal))).IsEqualTo((new Uri("http://localhost:8000/"), true));
    }

    [Test]
    public async Task Options_OfAClientWithADerivedEndpoint_GiveAStreamsClientItsOwnEndpoint()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Streams":[]}"""));
        var region = AwsRegion.FromName("us-west-2");
        using var client = new DynamoDbClient(new DynamoDbClientOptions { Region = region, Credentials = Credentials, HttpHandler = handler }, static _ => null, null, null);
        using var streams = new DynamoDbStreamsClient(client.Options, static _ => null, null, null);

        // Act
        await streams.ListStreamsAsync(new ListStreamsRequest());

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(region.DynamoDbStreamsEndpoint);
    }

    [Test]
    public async Task Options_NoCredentialsGiven_IsTheProviderInUse()
    {
        // Arrange
        using var client = new DynamoDbClient(new DynamoDbClientOptions(), Variables, null, null);

        // Act
        var provider = client.Options.Credentials;

        // Assert
        await Assert.That(provider).IsTypeOf<DefaultCredentialsProvider>();
    }

    [Test]
    public async Task Options_WhatWasGiven_StaysAsItIs()
    {
        // Arrange
        using var handler = new StubHandler();
        var credentials = new StaticCredentialsProvider("id", "secret");
        var region = AwsRegion.FromName("eu-central-2");
        var url = new Uri("http://localhost:9000");
        var given = new DynamoDbClientOptions { Region = region, ServiceUrl = url, Credentials = credentials, HttpHandler = handler, MaxAttempts = 2, AttemptTimeout = TimeSpan.FromSeconds(5) };
        using var client = new DynamoDbClient(given);

        // Act
        var options = client.Options;

        // Assert
        await Assert.That(options).IsEqualTo(given);
    }

    [Test]
    public async Task Options_HandlerTheClientMade_StaysItsOwn()
    {
        // Arrange (the handler goes when the client goes, and must not be left with a second client)
        using var client = new DynamoDbClient(new DynamoDbClientOptions(), Variables, null, null);

        // Act
        var handler = client.Options.HttpHandler;

        // Assert
        await Assert.That(handler).IsNull();
    }

    [Test]
    public async Task Options_Access_ReadsNothingAgain()
    {
        // Arrange
        var reads = 0;
        using var client = new DynamoDbClient(
            new DynamoDbClientOptions(),
            name =>
            {
                reads++;
                return Variables(name);
            },
            null,
            null);
        var before = reads;

        // Act
        var first = client.Options;
        var second = client.Options;

        // Assert
        await Assert.That((reads, ReferenceEquals(first, second))).IsEqualTo((before, true));
    }

    [Test]
    public async Task Options_With_GivesASecondClientThatWorks()
    {
        // Arrange (the first client gives a throttle a second try, and the copy does not. It goes on after the first is gone.)
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ThrottlingException", "No."));
        var first = TestClient.Create(handler, maxAttempts: 2);
        using var second = new DynamoDbClient(first.Options with { MaxAttempts = 1 });
        first.Dispose();

        // Act
        var failure = await TestClient.CaptureAsync<ThrottlingException>(() => second.DescribeTableAsync("Music"));

        // Assert
        await Assert.That((failure.TableName, handler.Requests.Count, second.Options.MaxAttempts)).IsEqualTo(("Music", 1, 1));
    }

    [Test]
    public async Task Options_WithOfAClientWithoutAHandler_SharesTheProvider()
    {
        // Arrange
        using var first = new DynamoDbClient(new DynamoDbClientOptions(), Variables, null, null);

        // Act
        using var second = new DynamoDbClient(first.Options with { MaxAttempts = 1 });

        // Assert
        await Assert.That(ReferenceEquals(second.Options.Credentials, first.Options.Credentials)).IsTrue();
    }

    [Test]
    public async Task Options_ToString_HoldsNoKey()
    {
        // Arrange (a provider of the application can be a record, which prints what it holds)
        const string Secret = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler, credentials: new RecordProvider("AKIAIOSFODNN7EXAMPLE", Secret));

        // Act
        var text = client.Options.ToString();

        // Assert
        await Assert.That((text.Contains(Secret, StringComparison.Ordinal), text.Contains("AKIA", StringComparison.Ordinal), text.Contains("RecordProvider", StringComparison.Ordinal))).IsEqualTo((false, false, true));
    }

    [Test]
    [Arguments("http://user:pass@localhost:8000", "ServiceUrl = http://localhost:8000")]
    [Arguments("relative/path", "ServiceUrl = relative/path")]
    public async Task ToString_ServiceUrl_ShowsNoUserInfo(string endpoint, string expected)
    {
        // Arrange (options can hold what a client would reject)
        var options = new DynamoDbClientOptions { ServiceUrl = new Uri(endpoint, UriKind.RelativeOrAbsolute) };

        // Act
        var text = options.ToString();

        // Assert
        await Assert.That(text).Contains(expected);
    }

    [Test]
    public async Task ToString_NothingSet_NamesEveryOption()
    {
        // Arrange
        var options = new DynamoDbClientOptions { TimeProvider = null! };

        // Act
        var text = options.ToString();

        // Assert
        await Assert.That(text).IsEqualTo("DynamoDbClientOptions { Region = , ServiceUrl = , Credentials = , HttpHandler = , AttemptTimeout = 00:00:30, MaxAttempts = 4, TimeProvider = , LoggerFactory = , TokenSigner =  }");
    }

    [Test]
    public async Task ToString_EverythingSet_NamesTheTypes()
    {
        // Arrange
        using var handler = new StubHandler();
        var options = new DynamoDbClientOptions
        {
            Region = AwsRegion.FromName("us-west-2"),
            Credentials = Credentials,
            HttpHandler = handler,
            LoggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            TokenSigner = new ContinuationTokenSigner(new byte[ContinuationTokenSigner.MinKeyLength]),
        };

        // Act
        var text = options.ToString();

        // Assert
        await Assert.That(text).IsEqualTo("DynamoDbClientOptions { Region = us-west-2, ServiceUrl = , Credentials = DynaSharp.Credentials.StaticCredentialsProvider, HttpHandler = DynaSharp.Testing.StubHandler, AttemptTimeout = 00:00:30, MaxAttempts = 4, TimeProvider = System.TimeProvider+SystemTimeProvider, LoggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory, TokenSigner = DynaSharp.ContinuationTokenSigner }");
    }

    [Test]
    public async Task DescribeTableAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.DescribeTableAsync((DescribeTableRequest)null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }

    [Test]
    public async Task DescribeTableAsync_Name_SendsWhatTheRequestSends()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Table":{"TableName":"Music","TableStatus":"ACTIVE"}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var byName = await client.DescribeTableAsync("Music");
        var byRequest = await client.DescribeTableAsync(new DescribeTableRequest { TableName = "Music" });

        // Assert
        await Assert.That((byName, handler.Requests[0].BodyText, handler.Requests[0].Operation)).IsEqualTo((byRequest, handler.Requests[1].BodyText, "DescribeTable"));
    }

    [Test]
    public async Task DescribeTableAsync_NameOnADisposedClient_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        var client = TestClient.Create(handler);
        client.Dispose();

        // Act
        var act = async () => await client.DescribeTableAsync("Music");

        // Assert
        await Assert.That(act).Throws<ObjectDisposedException>();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task DescribeTableAsync_NoName_Throws(string? name)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = TestClient.Catch(() => _ = client.DescribeTableAsync(name!));

        // Assert
        await Assert.That((failure is ArgumentException, handler.Requests.Count)).IsEqualTo((true, 0));
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

    private static string? Variables(string name) => name switch
    {
        "AWS_REGION" => "us-west-2",
        "AWS_ENDPOINT_URL_DYNAMODB" => "http://localhost:8000",
        _ => null,
    };

    private sealed record RecordProvider(string AccessKeyId, string SecretAccessKey) : ICredentialsProvider
    {
        public ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new AwsCredentials(AccessKeyId, SecretAccessKey));
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
