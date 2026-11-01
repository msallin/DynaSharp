using DynaSharp.Credentials;

namespace DynaSharp.UnitTests.Credentials;

public sealed class CredentialsOutcomeTests
{
    [Test]
    public async Task Default_NoAnswerAtAll_CountsAsBrokenSoTheChainStops()
    {
        // Arrange
        CredentialsOutcome outcome = default;

        // Act
        var kind = outcome.Kind;

        // Assert
        await Assert.That(kind).IsEqualTo(CredentialsOutcomeKind.Broken);
    }

    [Test]
    public async Task Found_Credentials_HasThemAndNoReason()
    {
        // Arrange
        var credentials = new AwsCredentials("id", "secret");

        // Act
        var outcome = CredentialsOutcome.Found(credentials);

        // Assert
        await Assert.That((outcome.Kind, ReferenceEquals(outcome.Credentials, credentials), outcome.Reason)).IsEqualTo((CredentialsOutcomeKind.Found, true, (string?)null));
    }

    [Test]
    public async Task DoesNotApply_Reason_HasItAndNoCredentials()
    {
        // Arrange (the reason of a source that is not configured here)

        // Act
        var outcome = CredentialsOutcome.DoesNotApply("not set");

        // Assert
        await Assert.That((outcome.Kind, outcome.Credentials, outcome.Reason)).IsEqualTo((CredentialsOutcomeKind.DoesNotApply, (AwsCredentials?)null, "not set"));
    }

    [Test]
    public async Task GetCredentials_Broken_ThrowsWithTheSourceAndTheReason()
    {
        // Arrange
        var outcome = CredentialsOutcome.Broken("it is stuck");

        // Act
        var act = () => outcome.GetCredentials("test source");

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessage("No credentials from the test source: it is stuck.", StringComparison.Ordinal);
    }
}
