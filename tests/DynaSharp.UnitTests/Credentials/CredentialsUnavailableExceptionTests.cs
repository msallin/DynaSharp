namespace DynaSharp.UnitTests.Credentials;

public sealed class CredentialsUnavailableExceptionTests
{
    [Test]
    public async Task Constructor_Message_SetsMessage()
    {
        // Arrange
        const string message = "No credentials in the environment.";

        // Act
        var exception = new CredentialsUnavailableException(message);

        // Assert
        await Assert.That(exception.Message).IsEqualTo(message);
    }

    [Test]
    public async Task Constructor_NoInnerException_InnerExceptionIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var exception = new CredentialsUnavailableException("message");

        // Assert
        await Assert.That(exception.InnerException).IsNull();
    }

    [Test]
    public async Task Constructor_InnerException_SetsInnerException()
    {
        // Arrange
        var inner = new IOException("endpoint unreachable");

        // Act
        var exception = new CredentialsUnavailableException("message", inner);

        // Assert
        await Assert.That(exception.InnerException).IsSameReferenceAs(inner);
    }
}
