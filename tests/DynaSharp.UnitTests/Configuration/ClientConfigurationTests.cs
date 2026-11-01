using DynaSharp.Configuration;
using DynaSharp.Credentials;
using DynaSharp.Retries;
using DynaSharp.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using static DynaSharp.UnitTests.Credentials.CredentialsTestSupport;

namespace DynaSharp.UnitTests.Configuration;

public sealed class ClientConfigurationTests
{
    private static readonly AwsRegion OptionsRegion = AwsRegion.FromName("eu-central-2");

    [Test]
    public async Task Resolve_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        DynamoDbClientOptions? options = null;

        // Act
        var act = () => Resolve(options!, Environment());

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("options");
    }

    [Test]
    public async Task Resolve_RegionInOptions_UsesThatRegion()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.Region).IsSameReferenceAs(OptionsRegion);
    }

    [Test]
    public async Task Resolve_RegionInOptions_RegionSourceIsOptions()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.RegionSource).IsEqualTo("options");
    }

    [Test]
    public async Task Resolve_RegionInOptionsAndEnvironment_OptionsWin()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };
        var environment = Environment(("AWS_REGION", "us-east-1"), ("AWS_DEFAULT_REGION", "us-west-2"));

        // Act
        var configuration = Resolve(options, environment);

        // Assert
        await Assert.That(configuration.Region).IsSameReferenceAs(OptionsRegion);
    }

    [Test]
    public async Task Resolve_RegionInOptionsAndInvalidRegionInEnvironment_IgnoresEnvironment()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };
        var environment = Environment(("AWS_REGION", "NOT A REGION"));

        // Act
        var configuration = Resolve(options, environment);

        // Assert
        await Assert.That(configuration.Region).IsSameReferenceAs(OptionsRegion);
    }

    [Test]
    public async Task Resolve_AwsRegionVariable_UsesThatRegion()
    {
        // Arrange
        var environment = Environment(("AWS_REGION", "us-east-1"));

        // Act
        var configuration = Resolve(new DynamoDbClientOptions(), environment);

        // Assert
        await Assert.That(configuration.Region.Name).IsEqualTo("us-east-1");
    }

    [Test]
    public async Task Resolve_AwsRegionVariable_RegionSourceIsTheVariable()
    {
        // Arrange
        var environment = Environment(("AWS_REGION", "us-east-1"));

        // Act
        var configuration = Resolve(new DynamoDbClientOptions(), environment);

        // Assert
        await Assert.That(configuration.RegionSource).IsEqualTo("AWS_REGION");
    }

    [Test]
    public async Task Resolve_BothRegionVariables_AwsRegionWins()
    {
        // Arrange
        var environment = Environment(("AWS_REGION", "us-east-1"), ("AWS_DEFAULT_REGION", "us-west-2"));

        // Act
        var configuration = Resolve(new DynamoDbClientOptions(), environment);

        // Assert
        await Assert.That(configuration.Region.Name).IsEqualTo("us-east-1");
    }

    [Test]
    public async Task Resolve_OnlyAwsDefaultRegionVariable_UsesThatRegion()
    {
        // Arrange
        var environment = Environment(("AWS_DEFAULT_REGION", "us-west-2"));

        // Act
        var configuration = Resolve(new DynamoDbClientOptions(), environment);

        // Assert
        await Assert.That(configuration.Region.Name).IsEqualTo("us-west-2");
    }

    [Test]
    public async Task Resolve_OnlyAwsDefaultRegionVariable_RegionSourceIsTheVariable()
    {
        // Arrange
        var environment = Environment(("AWS_DEFAULT_REGION", "us-west-2"));

        // Act
        var configuration = Resolve(new DynamoDbClientOptions(), environment);

        // Assert
        await Assert.That(configuration.RegionSource).IsEqualTo("AWS_DEFAULT_REGION");
    }

    [Test]
    public async Task Resolve_EmptyAwsRegionVariable_FallsBackToAwsDefaultRegion()
    {
        // Arrange
        var environment = Environment(("AWS_REGION", string.Empty), ("AWS_DEFAULT_REGION", "us-west-2"));

        // Act
        var configuration = Resolve(new DynamoDbClientOptions(), environment);

        // Assert
        await Assert.That(configuration.RegionSource).IsEqualTo("AWS_DEFAULT_REGION");
    }

    [Test]
    public async Task Resolve_NoRegionAnywhere_ThrowsInvalidOperationException()
    {
        // Arrange (no options, empty environment)

        // Act
        var act = () => Resolve(new DynamoDbClientOptions(), Environment());

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Resolve_EmptyRegionVariables_ThrowsInvalidOperationException()
    {
        // Arrange
        var environment = Environment(("AWS_REGION", string.Empty), ("AWS_DEFAULT_REGION", string.Empty));

        // Act
        var act = () => Resolve(new DynamoDbClientOptions(), environment);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    [Arguments("AWS_REGION")]
    [Arguments("AWS_DEFAULT_REGION")]
    [Arguments("DynamoDbClientOptions.Region")]
    public async Task Resolve_NoRegionAnywhere_ExceptionMessageNamesWhereToSetIt(string expected)
    {
        // Arrange (no options, empty environment)

        // Act
        var act = () => Resolve(new DynamoDbClientOptions(), Environment());

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining(expected, StringComparison.Ordinal);
    }

    [Test]
    [Arguments("AWS_REGION")]
    [Arguments("AWS_DEFAULT_REGION")]
    public async Task Resolve_InvalidRegionInEnvironment_ThrowsInvalidOperationExceptionNamingTheVariable(string variable)
    {
        // Arrange
        var environment = Environment((variable, "EU-CENTRAL-2"));

        // Act
        var act = () => Resolve(new DynamoDbClientOptions(), environment);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining(variable, StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_InvalidRegionInEnvironment_ExceptionMessageNamesTheValue()
    {
        // Arrange
        var environment = Environment(("AWS_REGION", "EU-CENTRAL-2"));

        // Act
        var act = () => Resolve(new DynamoDbClientOptions(), environment);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("'EU-CENTRAL-2'", StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_InvalidRegionInEnvironment_InnerExceptionIsTheArgumentException()
    {
        // Arrange
        var environment = Environment(("AWS_REGION", "EU-CENTRAL-2"));

        // Act
        var act = () => Resolve(new DynamoDbClientOptions(), environment);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithInnerException<ArgumentException>();
    }

    [Test]
    public async Task Resolve_ServiceUrlInOptions_UsesThatEndpoint()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, ServiceUrl = new Uri("http://localhost:8000") };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.Endpoint).IsEqualTo(new Uri("http://localhost:8000/"));
    }

    [Test]
    public async Task Resolve_ServiceUrlInOptions_EndpointSourceIsOptions()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, ServiceUrl = new Uri("http://localhost:8000") };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.EndpointSource).IsEqualTo("options");
    }

    [Test]
    public async Task Resolve_ServiceUrlInOptionsAndEnvironment_OptionsWin()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, ServiceUrl = new Uri("http://localhost:8000") };
        var environment = Environment(("AWS_ENDPOINT_URL_DYNAMODB", "http://dynamodb-a:8000"), ("AWS_ENDPOINT_URL", "http://dynamodb-b:8000"));

        // Act
        var configuration = Resolve(options, environment);

        // Assert
        await Assert.That(configuration.Endpoint).IsEqualTo(new Uri("http://localhost:8000/"));
    }

    [Test]
    public async Task Resolve_ServiceUrlInOptionsAndInvalidEndpointInEnvironment_IgnoresEnvironment()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, ServiceUrl = new Uri("http://localhost:8000") };
        var environment = Environment(("AWS_ENDPOINT_URL_DYNAMODB", "ftp://invalid/path?query"), ("AWS_ENDPOINT_URL", "not a url"));

        // Act
        var configuration = Resolve(options, environment);

        // Assert
        await Assert.That(configuration.EndpointSource).IsEqualTo("options");
    }

    [Test]
    public async Task Resolve_DynamoDbEndpointVariable_UsesThatEndpoint()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };
        var environment = Environment(("AWS_ENDPOINT_URL_DYNAMODB", "http://dynamodb-a:8000"));

        // Act
        var configuration = Resolve(options, environment);

        // Assert
        await Assert.That(configuration.Endpoint).IsEqualTo(new Uri("http://dynamodb-a:8000/"));
    }

    [Test]
    public async Task Resolve_DynamoDbEndpointVariable_EndpointSourceIsTheVariable()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };
        var environment = Environment(("AWS_ENDPOINT_URL_DYNAMODB", "http://dynamodb-a:8000"));

        // Act
        var configuration = Resolve(options, environment);

        // Assert
        await Assert.That(configuration.EndpointSource).IsEqualTo("AWS_ENDPOINT_URL_DYNAMODB");
    }

    [Test]
    public async Task Resolve_GenericEndpointVariable_UsesThatEndpoint()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };
        var environment = Environment(("AWS_ENDPOINT_URL", "https://gateway.internal"));

        // Act
        var configuration = Resolve(options, environment);

        // Assert
        await Assert.That(configuration.Endpoint).IsEqualTo(new Uri("https://gateway.internal/"));
    }

    [Test]
    public async Task Resolve_GenericEndpointVariable_EndpointSourceIsTheVariable()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };
        var environment = Environment(("AWS_ENDPOINT_URL", "https://gateway.internal"));

        // Act
        var configuration = Resolve(options, environment);

        // Assert
        await Assert.That(configuration.EndpointSource).IsEqualTo("AWS_ENDPOINT_URL");
    }

    [Test]
    public async Task Resolve_BothEndpointVariables_ServiceSpecificOneWins()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };
        var environment = Environment(("AWS_ENDPOINT_URL_DYNAMODB", "http://dynamodb-a:8000"), ("AWS_ENDPOINT_URL", "https://gateway.internal"));

        // Act
        var configuration = Resolve(options, environment);

        // Assert
        await Assert.That(configuration.EndpointSource).IsEqualTo("AWS_ENDPOINT_URL_DYNAMODB");
    }

    [Test]
    public async Task Resolve_ValidServiceSpecificVariableAndInvalidGenericVariable_IgnoresGenericVariable()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };
        var environment = Environment(("AWS_ENDPOINT_URL_DYNAMODB", "http://dynamodb-a:8000"), ("AWS_ENDPOINT_URL", "not a url"));

        // Act
        var configuration = Resolve(options, environment);

        // Assert
        await Assert.That(configuration.EndpointSource).IsEqualTo("AWS_ENDPOINT_URL_DYNAMODB");
    }

    [Test]
    public async Task Resolve_EmptyServiceSpecificVariable_FallsBackToGenericVariable()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };
        var environment = Environment(("AWS_ENDPOINT_URL_DYNAMODB", string.Empty), ("AWS_ENDPOINT_URL", "https://gateway.internal"));

        // Act
        var configuration = Resolve(options, environment);

        // Assert
        await Assert.That(configuration.EndpointSource).IsEqualTo("AWS_ENDPOINT_URL");
    }

    [Test]
    public async Task Resolve_NoEndpointConfigured_UsesTheRegionEndpoint()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.Endpoint).IsSameReferenceAs(OptionsRegion.DynamoDbEndpoint);
    }

    [Test]
    public async Task Resolve_NoEndpointConfigured_EndpointSourceIsRegion()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.EndpointSource).IsEqualTo("region");
    }

    [Test]
    public async Task Resolve_EmptyEndpointVariables_UsesTheRegionEndpoint()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };
        var environment = Environment(("AWS_ENDPOINT_URL_DYNAMODB", string.Empty), ("AWS_ENDPOINT_URL", string.Empty));

        // Act
        var configuration = Resolve(options, environment);

        // Assert
        await Assert.That(configuration.EndpointSource).IsEqualTo("region");
    }

    [Test]
    public async Task Resolve_RegionFromEnvironment_EndpointIsDerivedFromThatRegion()
    {
        // Arrange
        var environment = Environment(("AWS_REGION", "cn-north-1"));

        // Act
        var configuration = Resolve(new DynamoDbClientOptions(), environment);

        // Assert
        await Assert.That(configuration.Endpoint).IsEqualTo(new Uri("https://dynamodb.cn-north-1.amazonaws.com.cn/"));
    }

    [Test]
    [Arguments("http://localhost:8000")]
    [Arguments("http://localhost:8000/")]
    [Arguments("https://dynamodb.eu-central-2.amazonaws.com")]
    [Arguments("HTTP://LOCALHOST:8000")]
    [Arguments("http://127.0.0.1:8000")]
    [Arguments("http://[::1]:8000")]
    [Arguments("http://localhost")]
    public async Task Resolve_ValidServiceUrl_IsAccepted(string endpoint)
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, ServiceUrl = new Uri(endpoint) };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.Endpoint).IsSameReferenceAs(options.ServiceUrl);
    }

    [Test]
    [Arguments("/relative/path")]
    [Arguments("localhost:8000")]
    [Arguments("ftp://localhost:8000")]
    [Arguments("file:///tmp/dynamodb")]
    [Arguments("http://localhost:8000/dynamodb")]
    [Arguments("http://localhost:8000/dynamodb/")]
    [Arguments("http://localhost:8000/?table=Orders")]
    [Arguments("http://localhost:8000/#fragment")]
    [Arguments("http://user:hunter2@localhost:8000")]
    [Arguments("http://user@localhost:8000")]
    public async Task Resolve_InvalidServiceUrl_ThrowsArgumentException(string endpoint)
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, ServiceUrl = new Uri(endpoint, UriKind.RelativeOrAbsolute) };

        // Act
        var act = () => Resolve(options, Environment());

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("options");
    }

    [Test]
    [Arguments("/relative/path", "absolute")]
    [Arguments("ftp://localhost:8000", "scheme")]
    [Arguments("http://localhost:8000/dynamodb", "path")]
    [Arguments("http://localhost:8000/?table=Orders", "query")]
    [Arguments("http://localhost:8000/#fragment", "fragment")]
    [Arguments("http://user:hunter2@localhost:8000", "user info")]
    [Arguments("http://user@localhost:8000", "user info")]
    public async Task Resolve_InvalidServiceUrl_ExceptionMessageNamesTheProblem(string endpoint, string expected)
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, ServiceUrl = new Uri(endpoint, UriKind.RelativeOrAbsolute) };

        // Act
        var act = () => Resolve(options, Environment());

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithMessageContaining(expected, StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_ServiceUrlWithUserInfo_ExceptionMessageDoesNotEchoTheCredentials()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, ServiceUrl = new Uri("http://user:hunter2@localhost:8000") };

        // Act
        var act = () => Resolve(options, Environment());

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithMessageNotContaining("hunter2", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("AWS_ENDPOINT_URL_DYNAMODB", "http://localhost:8000")]
    [Arguments("AWS_ENDPOINT_URL", "https://gateway.internal:8443")]
    public async Task Resolve_ValidEndpointInEnvironment_IsAccepted(string variable, string value)
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = Resolve(options, Environment((variable, value)));

        // Assert
        await Assert.That(configuration.Endpoint).IsEqualTo(new Uri(value));
    }

    [Test]
    [Arguments("AWS_ENDPOINT_URL_DYNAMODB", "localhost:8000")]
    [Arguments("AWS_ENDPOINT_URL_DYNAMODB", "not a url")]
    [Arguments("AWS_ENDPOINT_URL_DYNAMODB", "ftp://localhost:8000")]
    [Arguments("AWS_ENDPOINT_URL_DYNAMODB", "http://localhost:8000/dynamodb")]
    [Arguments("AWS_ENDPOINT_URL_DYNAMODB", "http://localhost:8000/?table=Orders")]
    [Arguments("AWS_ENDPOINT_URL_DYNAMODB", "http://localhost:8000/#fragment")]
    [Arguments("AWS_ENDPOINT_URL", "localhost:8000")]
    [Arguments("AWS_ENDPOINT_URL", "not a url")]
    [Arguments("AWS_ENDPOINT_URL", "ftp://localhost:8000")]
    [Arguments("AWS_ENDPOINT_URL", "http://localhost:8000/dynamodb")]
    [Arguments("AWS_ENDPOINT_URL", "http://localhost:8000/?table=Orders")]
    [Arguments("AWS_ENDPOINT_URL", "http://localhost:8000/#fragment")]
    [Arguments("AWS_ENDPOINT_URL_DYNAMODB", "http://user:hunter2@localhost:8000")]
    [Arguments("AWS_ENDPOINT_URL", "http://user:hunter2@localhost:8000")]
    public async Task Resolve_InvalidEndpointInEnvironment_ThrowsInvalidOperationExceptionNamingTheVariable(string variable, string value)
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var act = () => Resolve(options, Environment((variable, value)));

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining(variable, StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_InvalidEndpointInEnvironment_ExceptionMessageDoesNotEchoTheValue()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };
        var environment = Environment(("AWS_ENDPOINT_URL", "http://user:hunter2@localhost:8000/path"));

        // Act
        var act = () => Resolve(options, environment);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageNotContaining("hunter2", StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_EndpointWithUserInfoInEnvironment_ExceptionMessageDoesNotEchoTheCredentials()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };
        var environment = Environment(("AWS_ENDPOINT_URL_DYNAMODB", "http://user:hunter2@localhost:8000"));

        // Act
        var act = () => Resolve(options, environment);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageNotContaining("hunter2", StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_EndpointNotAbsoluteInEnvironment_ExceptionMessageNamesTheProblem()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };
        var environment = Environment(("AWS_ENDPOINT_URL", "not a url"));

        // Act
        var act = () => Resolve(options, environment);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("absolute", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("ftp://localhost:8000", "scheme")]
    [Arguments("http://localhost:8000/dynamodb", "path")]
    [Arguments("http://localhost:8000/?table=Orders", "query")]
    [Arguments("http://localhost:8000/#fragment", "fragment")]
    [Arguments("http://user:hunter2@localhost:8000", "user info")]
    public async Task Resolve_InvalidEndpointInEnvironment_ExceptionMessageNamesTheProblem(string value, string expected)
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };
        var environment = Environment(("AWS_ENDPOINT_URL_DYNAMODB", value));

        // Act
        var act = () => Resolve(options, environment);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining(expected, StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_CredentialsInOptions_UsesThatProvider()
    {
        // Arrange
        var provider = new StaticCredentialsProvider(new AwsCredentials("AKIAIOSFODNN7EXAMPLE", "secret"));
        var options = new DynamoDbClientOptions { Region = OptionsRegion, Credentials = provider };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.Credentials).IsSameReferenceAs(provider);
    }

    [Test]
    public async Task Resolve_NoCredentialsInOptions_UsesTheDefaultCredentialsChain()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.Credentials).IsTypeOf<DefaultCredentialsProvider>();
    }

    [Test]
    public async Task Resolve_NoCredentialsInOptions_ReadsOnlyTheEndpointVariablesAndNoFiles()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };
        var reads = new List<string>();
        Func<string, string?> environment = name =>
        {
            reads.Add(name);
            return null;
        };

        // Act
        _ = Resolve(options, environment, path => throw new InvalidOperationException($"Unexpected read of {path}"));

        // Assert
        await Assert.That(reads.Order().ToArray()).IsEquivalentTo(["AWS_ENDPOINT_URL", "AWS_ENDPOINT_URL_DYNAMODB"]);
    }

    [Test]
    public async Task Resolve_NoCredentialsInOptions_DefaultChainLogsTheChosenSourceToTheLoggerFactory()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var options = new DynamoDbClientOptions { Region = OptionsRegion, LoggerFactory = logs };
        var environment = Environment(("AWS_ACCESS_KEY_ID", "AKIAIOSFODNN7EXAMPLE"), ("AWS_SECRET_ACCESS_KEY", "secret"));
        var configuration = Resolve(options, environment);

        // Act
        await configuration.Credentials.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(logs.Entries.Single().Message).IsEqualTo("Credentials come from the environment variables.");
    }

    [Test]
    public async Task Resolve_NoCredentialsInOptions_DefaultChainAsksStsInTheRegionOfTheOptions()
    {
        // Arrange (AWS_REGION says otherwise: for the client, the options win)
        using var sts = new StubHandler(_ => new StubResponse { StatusCode = System.Net.HttpStatusCode.Forbidden, Body = "<ErrorResponse><Error><Code>AccessDenied</Code></Error></ErrorResponse>" });
        var environment = Environment(("AWS_REGION", "us-west-2"), ("AWS_WEB_IDENTITY_TOKEN_FILE", "/token"), ("AWS_ROLE_ARN", "arn:aws:iam::123456789012:role/pod"));
        var configuration = ClientConfiguration.Resolve(new DynamoDbClientOptions { Region = AwsRegion.FromName("ap-south-1") }, environment, Files(("/token", "the-token")), AwsService.DynamoDb, sts);

        // Act
        await Assert.That(async () => await configuration.Credentials.GetCredentialsAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>();

        // Assert
        await Assert.That(sts.Requests.Single().RequestUri!.Host).IsEqualTo("sts.ap-south-1.amazonaws.com");
    }

    [Test]
    public async Task Resolve_NoRegionInOptionsOrEnvironment_UsesTheRegionOfTheDefaultProfile()
    {
        // Arrange
        var files = Files((Path.Combine("/home/a", ".aws", "config"), "[default]\nregion = ap-southeast-2\n"));

        // Act
        var configuration = Resolve(new DynamoDbClientOptions(), Environment(("HOME", "/home/a")), files);

        // Assert
        await Assert.That(configuration.Region.Name).IsEqualTo("ap-southeast-2");
    }

    [Test]
    public async Task Resolve_RegionFromProfile_RegionSourceIsProfile()
    {
        // Arrange
        var files = Files((Path.Combine("/home/a", ".aws", "config"), "[default]\nregion = ap-southeast-2\n"));

        // Act
        var configuration = Resolve(new DynamoDbClientOptions(), Environment(("HOME", "/home/a")), files);

        // Assert
        await Assert.That(configuration.RegionSource).IsEqualTo("profile");
    }

    [Test]
    public async Task Resolve_AwsProfileSet_UsesTheRegionOfThatProfile()
    {
        // Arrange
        var files = Files(("/etc/aws/config", "[default]\nregion = eu-west-1\n[profile dev]\nregion = us-west-1\n"));
        var environment = Environment(("AWS_PROFILE", "dev"), ("AWS_CONFIG_FILE", "/etc/aws/config"));

        // Act
        var configuration = Resolve(new DynamoDbClientOptions(), environment, files);

        // Assert
        await Assert.That(configuration.Region.Name).IsEqualTo("us-west-1");
    }

    [Test]
    public async Task Resolve_RegionInCredentialsFile_UsesIt()
    {
        // Arrange
        var files = Files(("/etc/aws/credentials", "[default]\nregion = eu-north-1\n"));
        var environment = Environment(("AWS_SHARED_CREDENTIALS_FILE", "/etc/aws/credentials"), ("AWS_CONFIG_FILE", "/etc/aws/config"));

        // Act
        var configuration = Resolve(new DynamoDbClientOptions(), environment, files);

        // Assert
        await Assert.That(configuration.Region.Name).IsEqualTo("eu-north-1");
    }

    [Test]
    public async Task Resolve_RegionInEnvironmentAndProfile_EnvironmentWins()
    {
        // Arrange
        var files = Files((Path.Combine("/home/a", ".aws", "config"), "[default]\nregion = ap-southeast-2\n"));
        var environment = Environment(("HOME", "/home/a"), ("AWS_DEFAULT_REGION", "us-west-2"));

        // Act
        var configuration = Resolve(new DynamoDbClientOptions(), environment, files);

        // Assert
        await Assert.That(configuration.Region.Name).IsEqualTo("us-west-2");
    }

    [Test]
    public async Task Resolve_RegionInOptionsAndProfile_DoesNotReadTheFiles()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var act = () => Resolve(options, Environment(), path => throw new InvalidOperationException($"Unexpected read of {path}"));

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task Resolve_RegionInEnvironmentAndProfile_DoesNotReadTheFiles()
    {
        // Arrange
        var environment = Environment(("AWS_REGION", "us-east-1"));

        // Act
        var act = () => Resolve(new DynamoDbClientOptions(), environment, path => throw new InvalidOperationException($"Unexpected read of {path}"));

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task Resolve_ProfileHasNoRegion_ThrowsInvalidOperationExceptionSayingSo()
    {
        // Arrange
        var files = Files((Path.Combine("/home/a", ".aws", "config"), "[default]\noutput = json\n"));

        // Act
        var act = () => Resolve(new DynamoDbClientOptions(), Environment(("HOME", "/home/a")), files);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("profile 'default' has no region setting", StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_ExplicitProfileMissing_ThrowsInvalidOperationExceptionNamingTheProfile()
    {
        // Arrange
        var environment = Environment(("HOME", "/home/a"), ("AWS_PROFILE", "dev"));

        // Act
        var act = () => Resolve(new DynamoDbClientOptions(), environment);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("profile 'dev' was not found", StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_UnreadableFile_DoesNotFailWhenTheRegionIsSet()
    {
        // Arrange
        var environment = Environment(("AWS_REGION", "us-east-1"));
        ReadTextFile unreadable = _ => throw new UnauthorizedAccessException();

        // Act
        var configuration = Resolve(new DynamoDbClientOptions(), environment, unreadable);

        // Assert
        await Assert.That(configuration.Region.Name).IsEqualTo("us-east-1");
    }

    [Test]
    public async Task Resolve_UnreadableFileAndNoRegion_ThrowsInvalidOperationExceptionNamingTheProblem()
    {
        // Arrange
        ReadTextFile unreadable = _ => throw new UnauthorizedAccessException();

        // Act
        var act = () => Resolve(new DynamoDbClientOptions(), Environment(("HOME", "/home/a")), unreadable);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("access denied", StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_MalformedFileAndNoRegion_ThrowsInvalidOperationExceptionWithFileAndLineButNotTheContent()
    {
        // Arrange
        var files = Files((Path.Combine("/home/a", ".aws", "config"), "[default]\nregion = us-east-1\nthis is a secret line\n"));

        // Act
        var act = () => Resolve(new DynamoDbClientOptions(), Environment(("HOME", "/home/a")), files);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("malformed at line 3", StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_MalformedFileAndNoRegion_ExceptionMessageDoesNotEchoTheContent()
    {
        // Arrange
        var files = Files((Path.Combine("/home/a", ".aws", "config"), "[default]\nregion = us-east-1\nthis is a secret line\n"));

        // Act
        var exception = await TestClient.CaptureAsync<InvalidOperationException>(() =>
        {
            Resolve(new DynamoDbClientOptions(), Environment(("HOME", "/home/a")), files);
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(exception.Message).DoesNotContain("secret line", StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_InvalidRegionInProfile_ThrowsInvalidOperationExceptionNamingTheProfileAndTheValue()
    {
        // Arrange
        var files = Files((Path.Combine("/home/a", ".aws", "config"), "[default]\nregion = US-EAST-1\n"));

        // Act
        var act = () => Resolve(new DynamoDbClientOptions(), Environment(("HOME", "/home/a")), files);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("region setting of the profile 'default' contains 'US-EAST-1'", StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_NoReadFileGiven_ReadsTheDefaultLocationFromDisk()
    {
        // Arrange (a profile that cannot exist keeps the machine's own files out of it)
        var environment = Environment(("AWS_PROFILE", $"dynasharp-test-{Guid.NewGuid():N}"), ("HOME", "/dynasharp-test-nonexistent-home"), ("USERPROFILE", "/dynasharp-test-nonexistent-home"));

        // Act
        var act = () => ClientConfiguration.Resolve(new DynamoDbClientOptions(), environment);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("was not found", StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_NoCredentialsInOptions_DefaultProviderReadsThroughTheGivenAccessor()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };
        var environment = Environment(("AWS_ACCESS_KEY_ID", "AKIAIOSFODNN7EXAMPLE"), ("AWS_SECRET_ACCESS_KEY", "secret"));
        var configuration = Resolve(options, environment);

        // Act
        var credentials = await configuration.Credentials.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo("AKIAIOSFODNN7EXAMPLE");
    }

    [Test]
    public async Task Resolve_NoCredentialsInEnvironment_DoesNotFailUntilCredentialsAreRequested()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.Credentials).IsNotNull();
    }

    [Test]
    public async Task Resolve_DefaultAttemptTimeout_IsThirtySeconds()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.AttemptTimeout).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task Resolve_PositiveAttemptTimeout_IsKept()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, AttemptTimeout = TimeSpan.FromMilliseconds(250) };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.AttemptTimeout).IsEqualTo(TimeSpan.FromMilliseconds(250));
    }

    [Test]
    public async Task Resolve_SmallestPositiveAttemptTimeout_IsAccepted()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, AttemptTimeout = TimeSpan.FromTicks(1) };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.AttemptTimeout).IsEqualTo(TimeSpan.FromTicks(1));
    }

    [Test]
    public async Task Resolve_InfiniteAttemptTimeout_IsAccepted()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, AttemptTimeout = Timeout.InfiniteTimeSpan };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.AttemptTimeout).IsEqualTo(Timeout.InfiniteTimeSpan);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-2)]
    [Arguments(-1000)]
    public async Task Resolve_ZeroOrNegativeAttemptTimeoutThatIsNotInfinite_ThrowsArgumentOutOfRangeException(int milliseconds)
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, AttemptTimeout = TimeSpan.FromMilliseconds(milliseconds) };

        // Act
        var act = () => Resolve(options, Environment());

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("options");
    }

    [Test]
    public async Task Resolve_LargestSupportedAttemptTimeout_IsAccepted()
    {
        // Arrange (CancellationTokenSource.CancelAfter accepts at most 4294967294 milliseconds)
        var largest = TimeSpan.FromMilliseconds(4_294_967_294);
        var options = new DynamoDbClientOptions { Region = OptionsRegion, AttemptTimeout = largest };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.AttemptTimeout).IsEqualTo(largest);
    }

    [Test]
    public async Task Resolve_LargestSupportedAttemptTimeout_IsAcceptedByCancelAfter()
    {
        // Arrange
        var largest = TimeSpan.FromMilliseconds(4_294_967_294);
        using var source = new CancellationTokenSource();

        // Act
        var act = () => source.CancelAfter(largest);

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task Resolve_AttemptTimeoutOneMillisecondAboveTheLimit_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, AttemptTimeout = TimeSpan.FromMilliseconds(4_294_967_295) };

        // Act
        var act = () => Resolve(options, Environment());

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("options");
    }

    [Test]
    public async Task Resolve_AttemptTimeoutAboveTheLimit_IsRejectedByCancelAfterToo()
    {
        // Arrange (the reason for the limit)
        using var source = new CancellationTokenSource();

        // Act
        var act = () => source.CancelAfter(TimeSpan.FromMilliseconds(4_294_967_295));

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Resolve_AttemptTimeoutAboveTheLimit_ExceptionMessageNamesTheLimit()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, AttemptTimeout = TimeSpan.MaxValue };

        // Act
        var act = () => Resolve(options, Environment());

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("4294967294", StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_InvalidAttemptTimeout_ExceptionMessageNamesTheSetting()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, AttemptTimeout = TimeSpan.Zero };

        // Act
        var act = () => Resolve(options, Environment());

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("AttemptTimeout", StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_DefaultMaxAttempts_IsFour()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = ClientConfiguration.Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.MaxAttempts).IsEqualTo(4);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(10)]
    [Arguments(int.MaxValue)]
    public async Task Resolve_MaxAttemptsOfAtLeastOne_IsKept(int maxAttempts)
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, MaxAttempts = maxAttempts };

        // Act
        var configuration = ClientConfiguration.Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.MaxAttempts).IsEqualTo(maxAttempts);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(int.MinValue)]
    public async Task Resolve_MaxAttemptsBelowOne_ThrowsArgumentOutOfRangeException(int maxAttempts)
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, MaxAttempts = maxAttempts };

        // Act
        var act = () => ClientConfiguration.Resolve(options, Environment());

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("options");
    }

    [Test]
    public async Task Resolve_InvalidMaxAttempts_ExceptionMessageNamesTheSetting()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, MaxAttempts = 0 };

        // Act
        var act = () => ClientConfiguration.Resolve(options, Environment());

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("MaxAttempts", StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_NullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, TimeProvider = null! };

        // Act
        var act = () => Resolve(options, Environment());

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("options.TimeProvider");
    }

    [Test]
    public async Task Resolve_DefaultTimeProvider_IsSystem()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.TimeProvider).IsSameReferenceAs(TimeProvider.System);
    }

    [Test]
    public async Task Resolve_TimeProviderInOptions_UsesThatTimeProvider()
    {
        // Arrange
        var timeProvider = new TestTimeProvider();
        var options = new DynamoDbClientOptions { Region = OptionsRegion, TimeProvider = timeProvider };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.TimeProvider).IsSameReferenceAs(timeProvider);
    }

    [Test]
    public async Task Resolve_TimeProviderInOptions_TheClockFollowsIt()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var options = new DynamoDbClientOptions { Region = OptionsRegion, TimeProvider = time };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.Clock.GetUtcNow()).IsEqualTo(time.GetUtcNow());
    }

    [Test]
    public async Task Resolve_NoLoggerFactory_UsesNullLoggerFactory()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.LoggerFactory).IsSameReferenceAs(NullLoggerFactory.Instance);
    }

    [Test]
    public async Task Resolve_LoggerFactoryInOptions_UsesThatLoggerFactory()
    {
        // Arrange
        using var loggerFactory = new NullLoggerFactory();
        var options = new DynamoDbClientOptions { Region = OptionsRegion, LoggerFactory = loggerFactory };

        // Act
        var configuration = Resolve(options, Environment());

        // Assert
        await Assert.That(configuration.LoggerFactory).IsSameReferenceAs(loggerFactory);
    }

    [Test]
    public async Task Constructor_NoLoggerFactorySpecified_DefaultsToNullLoggerFactory()
    {
        // Arrange (nothing to arrange)

        // Act
        var configuration = new ClientConfiguration
        {
            Region = OptionsRegion,
            RegionSource = "options",
            Endpoint = OptionsRegion.DynamoDbEndpoint,
            EndpointSource = "region",
            Credentials = new StaticCredentialsProvider(new AwsCredentials("AKIAIOSFODNN7EXAMPLE", "secret")),
            AttemptTimeout = TimeSpan.FromSeconds(30),
            MaxAttempts = 4,
            TimeProvider = TimeProvider.System,
            Clock = new ClockSkewCorrection(TimeProvider.System),
        };

        // Assert
        await Assert.That(configuration.LoggerFactory).IsSameReferenceAs(NullLoggerFactory.Instance);
    }

    // Hermetic by default: no shared AWS files, so the files of the machine cannot decide a test.
    private static ClientConfiguration Resolve(DynamoDbClientOptions options, Func<string, string?> getEnvironmentVariable, ReadTextFile? readFile = null) =>
        ClientConfiguration.Resolve(options, getEnvironmentVariable, readFile ?? NoFiles);

    private static Func<string, string?> Environment(params (string Name, string Value)[] variables) => Env(variables);

    private sealed class TestTimeProvider : TimeProvider
    {
    }
}
