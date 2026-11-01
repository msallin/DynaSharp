using DynaSharp.Credentials;

namespace DynaSharp.UnitTests.Credentials;

public sealed class AwsCredentialsTests
{
    private const string AccessKeyId = "AKIAIOSFODNN7EXAMPLE";
    private const string SecretAccessKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";
    private const string SessionToken = "IQoJb3JpZ2luX2VjEXAMPLETOKEN";

    [Test]
    public async Task Constructor_ValidArguments_SetsAccessKeyId()
    {
        // Arrange (nothing to arrange beyond the constants)

        // Act
        var credentials = new AwsCredentials(AccessKeyId, SecretAccessKey);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task Constructor_ValidArguments_SetsSecretAccessKey()
    {
        // Arrange (nothing to arrange beyond the constants)

        // Act
        var credentials = new AwsCredentials(AccessKeyId, SecretAccessKey);

        // Assert
        await Assert.That(credentials.SecretAccessKey).IsEqualTo(SecretAccessKey);
    }

    [Test]
    public async Task Constructor_SessionToken_SetsSessionToken()
    {
        // Arrange (nothing to arrange beyond the constants)

        // Act
        var credentials = new AwsCredentials(AccessKeyId, SecretAccessKey, SessionToken);

        // Assert
        await Assert.That(credentials.SessionToken).IsEqualTo(SessionToken);
    }

    [Test]
    public async Task Constructor_NoSessionToken_SessionTokenIsNull()
    {
        // Arrange (nothing to arrange beyond the constants)

        // Act
        var credentials = new AwsCredentials(AccessKeyId, SecretAccessKey);

        // Assert
        await Assert.That(credentials.SessionToken).IsNull();
    }

    [Test]
    public async Task Constructor_EmptySessionToken_NormalizesToNull()
    {
        // Arrange (nothing to arrange beyond the constants)

        // Act
        var credentials = new AwsCredentials(AccessKeyId, SecretAccessKey, string.Empty);

        // Assert
        await Assert.That(credentials.SessionToken).IsNull();
    }

    [Test]
    public async Task Constructor_Expiration_SetsExpiration()
    {
        // Arrange
        var expiration = new DateTimeOffset(2026, 10, 31, 12, 30, 0, TimeSpan.Zero);

        // Act
        var credentials = new AwsCredentials(AccessKeyId, SecretAccessKey, SessionToken, expiration);

        // Assert
        await Assert.That(credentials.Expiration).IsEqualTo(expiration);
    }

    [Test]
    public async Task Constructor_NoExpiration_ExpirationIsNull()
    {
        // Arrange (nothing to arrange beyond the constants)

        // Act
        var credentials = new AwsCredentials(AccessKeyId, SecretAccessKey);

        // Assert
        await Assert.That(credentials.Expiration).IsNull();
    }

    [Test]
    public async Task Constructor_NullAccessKeyId_ThrowsArgumentNullException()
    {
        // Arrange
        string? accessKeyId = null;

        // Act
        var act = () => new AwsCredentials(accessKeyId!, SecretAccessKey);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("accessKeyId");
    }

    [Test]
    public async Task Constructor_EmptyAccessKeyId_ThrowsArgumentException()
    {
        // Arrange
        var accessKeyId = string.Empty;

        // Act
        var act = () => new AwsCredentials(accessKeyId, SecretAccessKey);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("accessKeyId");
    }

    [Test]
    public async Task Constructor_NullSecretAccessKey_ThrowsArgumentNullException()
    {
        // Arrange
        string? secretAccessKey = null;

        // Act
        var act = () => new AwsCredentials(AccessKeyId, secretAccessKey!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("secretAccessKey");
    }

    [Test]
    public async Task Constructor_EmptySecretAccessKey_ThrowsArgumentException()
    {
        // Arrange
        var secretAccessKey = string.Empty;

        // Act
        var act = () => new AwsCredentials(AccessKeyId, secretAccessKey);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("secretAccessKey");
    }

    [Test]
    public async Task ToString_LongTermKeys_ContainsAccessKeyIdAndNoExpiration()
    {
        // Arrange
        var credentials = new AwsCredentials(AccessKeyId, SecretAccessKey);

        // Act
        var text = credentials.ToString();

        // Assert
        await Assert.That(text).IsEqualTo("AwsCredentials { AccessKeyId = AKIAIOSFODNN7EXAMPLE, Expiration = never }");
    }

    [Test]
    public async Task ToString_Expiration_ContainsExpirationInRoundTripFormat()
    {
        // Arrange
        var expiration = new DateTimeOffset(2026, 10, 31, 12, 30, 0, TimeSpan.FromHours(1));
        var credentials = new AwsCredentials(AccessKeyId, SecretAccessKey, SessionToken, expiration);

        // Act
        var text = credentials.ToString();

        // Assert
        await Assert.That(text).IsEqualTo("AwsCredentials { AccessKeyId = AKIAIOSFODNN7EXAMPLE, Expiration = 2026-10-31T12:30:00.0000000+01:00 }");
    }

    [Test]
    public async Task ToString_SessionCredentials_DoesNotContainSecretAccessKey()
    {
        // Arrange
        var credentials = new AwsCredentials(AccessKeyId, SecretAccessKey, SessionToken, DateTimeOffset.UnixEpoch);

        // Act
        var text = credentials.ToString();

        // Assert
        await Assert.That(text).DoesNotContain(SecretAccessKey);
    }

    [Test]
    public async Task ToString_SessionCredentials_DoesNotContainSessionToken()
    {
        // Arrange
        var credentials = new AwsCredentials(AccessKeyId, SecretAccessKey, SessionToken, DateTimeOffset.UnixEpoch);

        // Act
        var text = credentials.ToString();

        // Assert
        await Assert.That(text).DoesNotContain(SessionToken);
    }
}
