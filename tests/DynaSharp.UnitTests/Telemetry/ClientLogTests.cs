using DynaSharp.Credentials;
using DynaSharp.Requests;
using DynaSharp.Testing;
using Microsoft.Extensions.Logging;

namespace DynaSharp.UnitTests.Telemetry;

public sealed class ClientLogTests
{
    [Test]
    public async Task Constructor_DebugEnabled_LogsRegionEndpointAndCredentialsInOrder()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler();

        // Act
        using var client = TestClient.Create(handler, loggerFactory: logs);

        // Assert
        await Assert.That(string.Join(',', logs.Entries.Select(entry => entry.EventId.Id))).IsEqualTo("1,2,3");
    }

    [Test]
    public async Task Constructor_DebugEnabled_LogsAtDebugLevelUnderTheClientCategory()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler();

        // Act
        using var client = TestClient.Create(handler, loggerFactory: logs);

        // Assert
        await Assert.That(logs.Entries.Select(entry => (entry.Level, entry.Category)).Distinct().Single()).IsEqualTo((LogLevel.Debug, "DynaSharp.DynamoDbClient"));
    }

    [Test]
    public async Task Constructor_RegionInOptions_LogsRegionAndItsSource()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler();

        // Act
        using var client = TestClient.Create(handler, loggerFactory: logs);

        // Assert
        await Assert.That(logs.Entries[0].Message).IsEqualTo("DynamoDB client uses region eu-central-2 (source: options).");
    }

    [Test]
    public async Task Constructor_RegionFromEnvironment_LogsTheVariableAsSource()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler();
        var options = new DynamoDbClientOptions { Credentials = new StaticCredentialsProvider(TestClient.Credentials), HttpHandler = handler, LoggerFactory = logs };

        // Act
        using var client = new DynamoDbClient(options, static name => name == "AWS_DEFAULT_REGION" ? "us-west-2" : null, null, null);

        // Assert
        await Assert.That(logs.Entries[0].Message).IsEqualTo("DynamoDB client uses region us-west-2 (source: AWS_DEFAULT_REGION).");
    }

    [Test]
    public async Task Constructor_DefaultEndpoint_LogsEndpointAndRegionAsSource()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler();

        // Act
        using var client = TestClient.Create(handler, loggerFactory: logs);

        // Assert
        await Assert.That(logs.Entries[1].Message).IsEqualTo("DynamoDB client uses endpoint https://dynamodb.eu-central-2.amazonaws.com/ (source: region).");
    }

    [Test]
    public async Task Constructor_EndpointInOptions_LogsEndpointAndOptionsAsSource()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler();

        // Act
        using var client = TestClient.Create(handler, serviceUrl: new Uri("http://localhost:8000"), loggerFactory: logs);

        // Assert
        await Assert.That(logs.Entries[1].Message).IsEqualTo("DynamoDB client uses endpoint http://localhost:8000/ (source: options).");
    }

    [Test]
    public async Task Constructor_EndpointFromEnvironment_LogsTheVariableAsSource()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler();
        var options = new DynamoDbClientOptions { Region = TestClient.Region, Credentials = new StaticCredentialsProvider(TestClient.Credentials), HttpHandler = handler, LoggerFactory = logs };

        // Act
        using var client = new DynamoDbClient(options, static name => name == "AWS_ENDPOINT_URL" ? "http://localhost:4566" : null, null, null);

        // Assert
        await Assert.That(logs.Entries[1].Message).IsEqualTo("DynamoDB client uses endpoint http://localhost:4566/ (source: AWS_ENDPOINT_URL).");
    }

    [Test]
    public async Task Constructor_CredentialsProvider_LogsItsType()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler();

        // Act
        using var client = TestClient.Create(handler, loggerFactory: logs);

        // Assert
        await Assert.That(logs.Entries[2].Message).IsEqualTo("DynamoDB client uses credentials provider DynaSharp.Credentials.StaticCredentialsProvider.");
    }

    [Test]
    public async Task Constructor_NoCredentialsInOptions_LogsTheDefaultProviderType()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler();
        var options = new DynamoDbClientOptions { Region = TestClient.Region, HttpHandler = handler, LoggerFactory = logs };

        // Act
        using var client = new DynamoDbClient(options, static _ => null, null, null);

        // Assert
        await Assert.That(logs.Entries[2].Message).IsEqualTo("DynamoDB client uses credentials provider DynaSharp.Credentials.DefaultCredentialsProvider.");
    }

    [Test]
    public async Task Constructor_Always_LogsNoSecrets()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler();
        var credentials = new AwsCredentials("AKIASECRETKEYID", "SECRET-ACCESS-KEY", "SECRET-SESSION-TOKEN");

        // Act
        using var client = TestClient.Create(handler, credentials: new StaticCredentialsProvider(credentials), loggerFactory: logs);

        // Assert
        await Assert.That(string.Join('\n', logs.Entries.Select(entry => entry.Message))).DoesNotContain("SECRET");
    }

    [Test]
    public async Task Constructor_InformationLevel_LogsNothing()
    {
        // Arrange
        var logs = new CapturingLoggerFactory(LogLevel.Information);
        using var handler = new StubHandler();

        // Act
        using var client = TestClient.Create(handler, loggerFactory: logs);

        // Assert
        await Assert.That(logs.Entries).IsEmpty();
    }

    [Test]
    public async Task GetItemAsync_AfterConstruction_LogsNothingMore()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler, loggerFactory: logs);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(logs.Entries.Count).IsEqualTo(3);
    }

    [Test]
    [Arguments("http://localhost:8000")]
    [Arguments("http://127.0.0.1:8000")]
    [Arguments("http://[::1]:8000")]
    public async Task Constructor_HttpOnLoopback_LogsNoWarning(string endpoint)
    {
        // Arrange
        var logs = new CapturingLoggerFactory(LogLevel.Warning);
        using var handler = new StubHandler();

        // Act
        using var client = TestClient.Create(handler, serviceUrl: new Uri(endpoint), loggerFactory: logs);

        // Assert
        await Assert.That(logs.Entries).IsEmpty();
    }

    [Test]
    public async Task Constructor_HttpsOnRemoteHost_LogsNoWarning()
    {
        // Arrange
        var logs = new CapturingLoggerFactory(LogLevel.Warning);
        using var handler = new StubHandler();

        // Act
        using var client = TestClient.Create(handler, serviceUrl: new Uri("https://dynamodb-local:8000"), loggerFactory: logs);

        // Assert
        await Assert.That(logs.Entries).IsEmpty();
    }

    [Test]
    public async Task GetItemAsync_HttpOnRemoteHost_WarnsOnceNamingTheHost()
    {
        // Arrange: DynamoDB Local in docker-compose, reached by its service name
        var logs = new CapturingLoggerFactory(LogLevel.Warning);
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler, serviceUrl: new Uri("http://dynamodb-local:8000"), loggerFactory: logs);

        // Act
        for (var i = 0; i < 3; i++)
        {
            await client.GetItemAsync(TestClient.GetItem());
        }

        // Assert
        await Assert.That(logs.Entries.Select(static entry => (entry.EventId.Id, entry.Level, entry.Message)).Single()).IsEqualTo((
            9,
            LogLevel.Warning,
            "DynamoDB client sends requests to dynamodb-local over plain http. The requests, their signatures and the session token travel unencrypted."));
    }
}
