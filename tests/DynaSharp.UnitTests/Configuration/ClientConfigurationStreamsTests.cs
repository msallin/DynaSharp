using DynaSharp.Configuration;

namespace DynaSharp.UnitTests.Configuration;

/// <summary>
/// <see cref="ClientConfiguration.Resolve"/> for DynamoDB Streams: the endpoint comes from the options, then from
/// <c>AWS_ENDPOINT_URL_DYNAMODB_STREAMS</c>, then from <c>AWS_ENDPOINT_URL</c>, and otherwise from the region. Every test sets the
/// region and passes the environment, so none reads the files of the machine.
/// </summary>
public sealed class ClientConfigurationStreamsTests
{
    private static readonly AwsRegion OptionsRegion = AwsRegion.FromName("eu-central-2");

    [Test]
    public async Task Resolve_NoEndpointConfigured_UsesTheStreamsEndpointOfTheRegion()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = Resolve(options);

        // Assert
        await Assert.That(configuration.Endpoint).IsSameReferenceAs(OptionsRegion.DynamoDbStreamsEndpoint);
    }

    [Test]
    public async Task Resolve_NoEndpointConfigured_EndpointSourceIsRegion()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = Resolve(options);

        // Assert
        await Assert.That(configuration.EndpointSource).IsEqualTo("region");
    }

    [Test]
    public async Task Resolve_DynamoDbService_UsesTheDynamoDbEndpointOfTheRegion()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = ClientConfiguration.Resolve(options, static _ => null, static _ => null, AwsService.DynamoDb);

        // Assert
        await Assert.That(configuration.Endpoint).IsSameReferenceAs(OptionsRegion.DynamoDbEndpoint);
    }

    [Test]
    public async Task Resolve_ServiceUrlInOptionsAndEnvironment_OptionsWin()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion, ServiceUrl = new Uri("http://localhost:8000") };

        // Act
        var configuration = Resolve(options, ("AWS_ENDPOINT_URL_DYNAMODB_STREAMS", "http://streams-a:8000"), ("AWS_ENDPOINT_URL", "http://gateway:8000"));

        // Assert
        await Assert.That((configuration.Endpoint, configuration.EndpointSource)).IsEqualTo((new Uri("http://localhost:8000/"), "options"));
    }

    [Test]
    public async Task Resolve_StreamsEndpointVariable_UsesThatEndpoint()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = Resolve(options, ("AWS_ENDPOINT_URL_DYNAMODB_STREAMS", "http://streams-a:8000"));

        // Assert
        await Assert.That((configuration.Endpoint, configuration.EndpointSource)).IsEqualTo((new Uri("http://streams-a:8000/"), "AWS_ENDPOINT_URL_DYNAMODB_STREAMS"));
    }

    [Test]
    public async Task Resolve_GenericEndpointVariable_UsesThatEndpoint()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = Resolve(options, ("AWS_ENDPOINT_URL", "https://gateway.internal"));

        // Assert
        await Assert.That((configuration.Endpoint, configuration.EndpointSource)).IsEqualTo((new Uri("https://gateway.internal/"), "AWS_ENDPOINT_URL"));
    }

    [Test]
    public async Task Resolve_BothVariables_StreamsVariableWins()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = Resolve(options, ("AWS_ENDPOINT_URL_DYNAMODB_STREAMS", "http://streams-a:8000"), ("AWS_ENDPOINT_URL", "https://gateway.internal"));

        // Assert
        await Assert.That(configuration.EndpointSource).IsEqualTo("AWS_ENDPOINT_URL_DYNAMODB_STREAMS");
    }

    [Test]
    public async Task Resolve_DynamoDbEndpointVariable_IsIgnored()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = Resolve(options, ("AWS_ENDPOINT_URL_DYNAMODB", "http://dynamodb-a:8000"));

        // Assert
        await Assert.That(configuration.EndpointSource).IsEqualTo("region");
    }

    [Test]
    public async Task Resolve_DynamoDbEndpointVariableAndGenericVariable_GenericVariableIsUsedForStreams()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = Resolve(options, ("AWS_ENDPOINT_URL_DYNAMODB", "http://dynamodb-a:8000"), ("AWS_ENDPOINT_URL", "https://gateway.internal"));

        // Assert
        await Assert.That(configuration.EndpointSource).IsEqualTo("AWS_ENDPOINT_URL");
    }

    [Test]
    public async Task Resolve_EmptyStreamsVariable_FallsBackToTheGenericVariable()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var configuration = Resolve(options, ("AWS_ENDPOINT_URL_DYNAMODB_STREAMS", string.Empty), ("AWS_ENDPOINT_URL", "https://gateway.internal"));

        // Assert
        await Assert.That(configuration.EndpointSource).IsEqualTo("AWS_ENDPOINT_URL");
    }

    [Test]
    public async Task Resolve_StreamsVariableOnly_DoesNotChangeTheDynamoDbEndpoint()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };
        var environment = new Dictionary<string, string> { ["AWS_ENDPOINT_URL_DYNAMODB_STREAMS"] = "http://streams-a:8000" };

        // Act
        var configuration = ClientConfiguration.Resolve(options, name => environment.GetValueOrDefault(name), static _ => null);

        // Assert
        await Assert.That(configuration.EndpointSource).IsEqualTo("region");
    }

    [Test]
    [Arguments("localhost:8000")]
    [Arguments("not a url")]
    [Arguments("ftp://localhost:8000")]
    [Arguments("http://localhost:8000/streams")]
    [Arguments("http://user:hunter2@localhost:8000")]
    public async Task Resolve_InvalidStreamsEndpointVariable_ThrowsInvalidOperationExceptionNamingTheVariable(string value)
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var act = () => Resolve(options, ("AWS_ENDPOINT_URL_DYNAMODB_STREAMS", value));

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("AWS_ENDPOINT_URL_DYNAMODB_STREAMS", StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_InvalidStreamsEndpointVariable_ExceptionMessageDoesNotEchoTheValue()
    {
        // Arrange
        var options = new DynamoDbClientOptions { Region = OptionsRegion };

        // Act
        var act = () => Resolve(options, ("AWS_ENDPOINT_URL_DYNAMODB_STREAMS", "http://user:hunter2@localhost:8000"));

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageNotContaining("hunter2", StringComparison.Ordinal);
    }

    [Test]
    public async Task Resolve_ChinaRegionFromEnvironment_UsesTheStreamsEndpointOfThePartition()
    {
        // Arrange
        var options = new DynamoDbClientOptions();

        // Act
        var configuration = Resolve(options, ("AWS_REGION", "cn-north-1"));

        // Assert
        await Assert.That(configuration.Endpoint).IsEqualTo(new Uri("https://streams.dynamodb.cn-north-1.amazonaws.com.cn/"));
    }

    private static ClientConfiguration Resolve(DynamoDbClientOptions options, params (string Name, string Value)[] variables)
    {
        var environment = variables.ToDictionary(variable => variable.Name, variable => variable.Value);
        return ClientConfiguration.Resolve(options, name => environment.GetValueOrDefault(name), static _ => null, AwsService.DynamoDbStreams);
    }
}
