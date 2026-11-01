namespace DynaSharp.UnitTests;

public sealed class AwsRegionTests
{
    [Test]
    [Arguments("us-east-1")]
    [Arguments("eu-central-2")]
    [Arguments("us-gov-west-1")]
    public async Task FromName_ValidName_ReturnsRegionWithThatName(string name)
    {
        // Arrange (nothing to arrange beyond the argument)

        // Act
        var region = AwsRegion.FromName(name);

        // Assert
        await Assert.That(region.Name).IsEqualTo(name);
    }

    [Test]
    [Arguments("eu-central-2", "https://dynamodb.eu-central-2.amazonaws.com/")]
    [Arguments("us-gov-west-1", "https://dynamodb.us-gov-west-1.amazonaws.com/")]
    public async Task FromName_StandardPartitionRegion_ResolvesAmazonawsComEndpoint(string name, string expected)
    {
        // Arrange (nothing to arrange beyond the arguments)

        // Act
        var region = AwsRegion.FromName(name);

        // Assert
        await Assert.That(region.DynamoDbEndpoint).IsEqualTo(new Uri(expected));
    }

    [Test]
    public async Task FromName_ChinaRegion_ResolvesAmazonawsComCnEndpoint()
    {
        // Arrange
        const string name = "cn-north-1";

        // Act
        var region = AwsRegion.FromName(name);

        // Assert
        await Assert.That(region.DynamoDbEndpoint).IsEqualTo(new Uri("https://dynamodb.cn-north-1.amazonaws.com.cn/"));
    }

    [Test]
    public async Task FromName_NameContainingCnNotAsPrefix_ResolvesAmazonawsComEndpoint()
    {
        // Arrange
        const string name = "xcn-north-1";

        // Act
        var region = AwsRegion.FromName(name);

        // Assert
        await Assert.That(region.DynamoDbEndpoint).IsEqualTo(new Uri("https://dynamodb.xcn-north-1.amazonaws.com/"));
    }

    [Test]
    public async Task FromName_Null_ThrowsArgumentNullException()
    {
        // Arrange
        string? name = null;

        // Act
        var act = () => AwsRegion.FromName(name!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    [Arguments("")]
    [Arguments("EU-CENTRAL-2")]
    [Arguments("eu central 2")]
    [Arguments("evil.com/")]
    [Arguments("-eu-central-2")]
    [Arguments("eu-central-2-")]
    public async Task FromName_InvalidName_ThrowsArgumentException(string name)
    {
        // Arrange (nothing to arrange beyond the argument)

        // Act
        var act = () => AwsRegion.FromName(name);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("name");
    }

    [Test]
    public async Task FromName_InvalidName_ExceptionMessageNamesOffendingValue()
    {
        // Arrange
        const string name = "EU-CENTRAL-2";

        // Act
        var act = () => AwsRegion.FromName(name);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithMessageContaining("'EU-CENTRAL-2'", StringComparison.Ordinal);
    }

    [Test]
    public async Task Equals_SameName_ReturnsTrue()
    {
        // Arrange
        var first = AwsRegion.FromName("eu-central-2");
        var second = AwsRegion.FromName("eu-central-2");

        // Act
        var equal = first.Equals(second);

        // Assert
        await Assert.That(equal).IsTrue();
    }

    [Test]
    public async Task ToString_Always_ReturnsName()
    {
        // Arrange
        var region = AwsRegion.FromName("eu-central-2");

        // Act
        var text = region.ToString();

        // Assert
        await Assert.That(text).IsEqualTo("eu-central-2");
    }

    [Test]
    [Arguments("eu-central-2", "https://streams.dynamodb.eu-central-2.amazonaws.com/")]
    [Arguments("us-gov-west-1", "https://streams.dynamodb.us-gov-west-1.amazonaws.com/")]
    [Arguments("cn-north-1", "https://streams.dynamodb.cn-north-1.amazonaws.com.cn/")]
    public async Task FromName_Region_ResolvesTheStreamsEndpointOfItsPartition(string name, string expected)
    {
        // Arrange (nothing to arrange beyond the arguments)

        // Act
        var region = AwsRegion.FromName(name);

        // Assert
        await Assert.That(region.DynamoDbStreamsEndpoint).IsEqualTo(new Uri(expected));
    }

    [Test]
    public async Task FromName_Region_StreamsEndpointIsAnotherHostThanTheDynamoDbEndpoint()
    {
        // Arrange (nothing to arrange)

        // Act
        var region = AwsRegion.FromName("us-east-1");

        // Assert
        await Assert.That(region.DynamoDbStreamsEndpoint.Host).IsNotEqualTo(region.DynamoDbEndpoint.Host);
    }
}
