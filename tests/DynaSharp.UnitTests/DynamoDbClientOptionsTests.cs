using DynaSharp.Credentials;
using DynaSharp.Requests;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests;

public sealed class DynamoDbClientOptionsTests
{
    [Test]
    public async Task Constructor_Default_RegionIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var options = new DynamoDbClientOptions();

        // Assert
        await Assert.That(options.Region).IsNull();
    }

    [Test]
    public async Task Constructor_Default_ServiceUrlIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var options = new DynamoDbClientOptions();

        // Assert
        await Assert.That(options.ServiceUrl).IsNull();
    }

    [Test]
    public async Task Constructor_Default_CredentialsIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var options = new DynamoDbClientOptions();

        // Assert
        await Assert.That(options.Credentials).IsNull();
    }

    [Test]
    public async Task Constructor_Default_HttpHandlerIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var options = new DynamoDbClientOptions();

        // Assert
        await Assert.That(options.HttpHandler).IsNull();
    }

    [Test]
    public async Task Constructor_Default_AttemptTimeoutIsThirtySeconds()
    {
        // Arrange (nothing to arrange)

        // Act
        var options = new DynamoDbClientOptions();

        // Assert
        await Assert.That(options.AttemptTimeout).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task Constructor_Default_MaxAttemptsIsFour()
    {
        // Arrange (nothing to arrange)

        // Act
        var options = new DynamoDbClientOptions();

        // Assert
        await Assert.That(options.MaxAttempts).IsEqualTo(4);
    }

    [Test]
    public async Task Constructor_Default_TimeProviderIsSystem()
    {
        // Arrange (nothing to arrange)

        // Act
        var options = new DynamoDbClientOptions();

        // Assert
        await Assert.That(options.TimeProvider).IsSameReferenceAs(TimeProvider.System);
    }

    [Test]
    public async Task Constructor_Default_LoggerFactoryIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var options = new DynamoDbClientOptions();

        // Assert
        await Assert.That(options.LoggerFactory).IsNull();
    }

    [Test]
    public async Task With_ChangedMaxAttempts_ChangesOnlyMaxAttempts()
    {
        // Arrange
        using var handler = new StubHandler();
        var provider = new StaticCredentialsProvider("AKIAIOSFODNN7EXAMPLE", "secret");
        var options = new DynamoDbClientOptions
        {
            Region = AwsRegion.FromName("eu-central-2"),
            ServiceUrl = new Uri("http://localhost:8000"),
            Credentials = provider,
            HttpHandler = handler,
            AttemptTimeout = TimeSpan.FromSeconds(5),
            MaxAttempts = 4,
        };

        // Act
        var copy = options with { MaxAttempts = 1 };

        // Assert
        await Assert.That((copy.MaxAttempts, copy with { MaxAttempts = 4 })).IsEqualTo((1, options));
    }

    [Test]
    public async Task With_ChangedMaxAttempts_LeavesTheOriginalAlone()
    {
        // Arrange
        var options = new DynamoDbClientOptions { MaxAttempts = 4 };

        // Act
        _ = options with { MaxAttempts = 1 };

        // Assert
        await Assert.That(options.MaxAttempts).IsEqualTo(4);
    }

    [Test]
    public async Task With_CopyOfOptions_CreatesAClientThatDoesNotRetry()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError("InternalServerError", "Internal server error", System.Net.HttpStatusCode.InternalServerError) };
        var shared = TestClient.Options(handler, maxAttempts: 4);
        using var client = new DynamoDbClient(shared with { MaxAttempts = 1 }, static _ => null, null, null);

        // Act
        await TestClient.CaptureAsync<InternalServerErrorException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }
}
