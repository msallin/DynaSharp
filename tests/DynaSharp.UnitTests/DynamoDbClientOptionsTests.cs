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
    public async Task ForEmulator_Url_SetsTheUrlARegionAndStaticKeysAndNothingElse()
    {
        // Arrange
        var url = new Uri("http://localhost:8000");

        // Act
        var options = DynamoDbClientOptions.ForEmulator(url);

        // Assert
        await Assert.That((options.ServiceUrl, options.Region?.Name, options.Credentials?.GetType(), options with { ServiceUrl = null, Region = null, Credentials = null }))
            .IsEqualTo((url, (string?)"us-east-1", typeof(StaticCredentialsProvider), new DynamoDbClientOptions()));
    }

    [Test]
    public async Task ForEmulator_EnvironmentThatNamesOtherValues_SignsWithTheStaticKeysForTheEmulator()
    {
        // Arrange (a machine set up for AWS still talks to the emulator)
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        var options = DynamoDbClientOptions.ForEmulator(new Uri("http://localhost:8000")) with { HttpHandler = handler };
        Func<string, string?> environment = static name => name switch
        {
            "AWS_ENDPOINT_URL_DYNAMODB" => "https://dynamodb.eu-west-1.amazonaws.com",
            "AWS_REGION" => "eu-west-1",
            "AWS_ACCESS_KEY_ID" => "AKIDFROMENVIRONMENT",
            "AWS_SECRET_ACCESS_KEY" => "secret",
            _ => null,
        };
        using var client = new DynamoDbClient(options, environment, null, null);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That((sent.RequestUri, sent.Headers["Authorization"].Contains("Credential=local/", StringComparison.Ordinal), sent.Headers["Authorization"].Contains("/us-east-1/dynamodb/", StringComparison.Ordinal)))
            .IsEqualTo((new Uri("http://localhost:8000/"), true, true));
    }

    [Test]
    public async Task ForEmulator_NullUrl_Throws()
    {
        // Arrange
        Uri? url = null;

        // Act
        var act = () => DynamoDbClientOptions.ForEmulator(url!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ForEmulator_UrlAndRegion_SetsThatRegionAndOtherwiseWhatTheOneArgumentFormSets()
    {
        // Arrange (DynamoDB Local without -sharedDb keeps a database per region, so a tool of another region needs the client in it)
        var url = new Uri("http://localhost:8000");

        // Act
        var options = DynamoDbClientOptions.ForEmulator(url, "eu-central-2");

        // Assert
        await Assert.That((options.Region?.Name, options with { Region = null, Credentials = null }, options.Credentials?.GetType()))
            .IsEqualTo(((string?)"eu-central-2", DynamoDbClientOptions.ForEmulator(url) with { Region = null, Credentials = null }, typeof(StaticCredentialsProvider)));
    }

    [Test]
    public async Task ForEmulator_UrlAndRegion_SignsInThatRegionWithTheStaticKeys()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        var options = DynamoDbClientOptions.ForEmulator(new Uri("http://localhost:8000"), "eu-west-3") with { HttpHandler = handler };
        using var client = new DynamoDbClient(options, static _ => null, null, null);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        var authorization = handler.Requests.Single().Headers["Authorization"];
        await Assert.That(authorization.Contains("Credential=local/", StringComparison.Ordinal) && authorization.Contains("/eu-west-3/dynamodb/", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ForEmulator_NullUrlAndARegion_ThrowsNamingTheUrl()
    {
        // Arrange
        Uri? url = null;

        // Act
        var failure = TestClient.Catch(() => DynamoDbClientOptions.ForEmulator(url!, "us-east-1"));

        // Assert
        await Assert.That(((ArgumentNullException)failure).ParamName).IsEqualTo("serviceUrl");
    }

    [Test]
    public async Task ForEmulator_NullRegion_ThrowsNamingIt()
    {
        // Arrange
        string? region = null;

        // Act
        var failure = TestClient.Catch(() => DynamoDbClientOptions.ForEmulator(new Uri("http://localhost:8000"), region!));

        // Assert
        await Assert.That((failure.GetType(), ((ArgumentException)failure).ParamName)).IsEqualTo((typeof(ArgumentNullException), (string?)"region"));
    }

    [Test]
    [Arguments("")]
    [Arguments("EU-CENTRAL-2")]
    [Arguments("-eu")]
    [Arguments("eu central")]
    public async Task ForEmulator_RegionThatIsNoRegionName_ThrowsNamingIt(string region)
    {
        // Arrange (the rules of AwsRegion.FromName, with the parameter of this method in the exception)
        var url = new Uri("http://localhost:8000");

        // Act
        var failure = TestClient.Catch(() => DynamoDbClientOptions.ForEmulator(url, region));

        // Assert
        await Assert.That((failure.GetType(), ((ArgumentException)failure).ParamName)).IsEqualTo((typeof(ArgumentException), (string?)"region"));
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
