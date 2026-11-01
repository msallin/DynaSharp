using DynaSharp.Credentials;

namespace DynaSharp.UnitTests.Credentials;

public sealed class StaticCredentialsProviderTests
{
    [Test]
    public async Task GetCredentialsAsync_Always_ReturnsTheGivenInstance()
    {
        // Arrange
        var credentials = new AwsCredentials("AKIAIOSFODNN7EXAMPLE", "secret");
        var provider = new StaticCredentialsProvider(credentials);

        // Act
        var result = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(result).IsSameReferenceAs(credentials);
    }

    [Test]
    public async Task GetCredentialsAsync_CalledTwice_ReturnsTheSameInstance()
    {
        // Arrange
        var provider = new StaticCredentialsProvider(new AwsCredentials("AKIAIOSFODNN7EXAMPLE", "secret"));
        var first = await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var second = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(second).IsSameReferenceAs(first);
    }

    [Test]
    public async Task Constructor_NullCredentials_ThrowsArgumentNullException()
    {
        // Arrange
        AwsCredentials? credentials = null;

        // Act
        var act = () => new StaticCredentialsProvider(credentials!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("credentials");
    }

    [Test]
    public async Task GetCredentialsAsync_KeyPair_ReturnsCredentialsWithoutToken()
    {
        // Arrange
        var provider = new StaticCredentialsProvider("AKIAIOSFODNN7EXAMPLE", "secret");

        // Act
        var result = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((result.AccessKeyId, result.SecretAccessKey, result.SessionToken, result.Expiration)).IsEqualTo(("AKIAIOSFODNN7EXAMPLE", "secret", (string?)null, (DateTimeOffset?)null));
    }

    [Test]
    public async Task GetCredentialsAsync_KeyPairWithToken_ReturnsCredentialsWithTheToken()
    {
        // Arrange
        var provider = new StaticCredentialsProvider("ASIAIOSFODNN7EXAMPLE", "secret", "token");

        // Act
        var result = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(result.SessionToken).IsEqualTo("token");
    }

    [Test]
    [Arguments(null, "secret")]
    [Arguments("", "secret")]
    [Arguments("AKIAIOSFODNN7EXAMPLE", null)]
    [Arguments("AKIAIOSFODNN7EXAMPLE", "")]
    public async Task Constructor_KeyPairMissingAPart_ThrowsArgumentException(string? accessKeyId, string? secretAccessKey)
    {
        // Arrange (nothing to arrange)

        // Act
        var act = () => new StaticCredentialsProvider(accessKeyId!, secretAccessKey!);

        // Assert
        await Assert.That(act).Throws<ArgumentException>();
    }
}
