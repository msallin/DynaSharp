using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class SSEDescriptionTests
{
    [Test]
    public async Task Constructor_NoMembers_StatusAndTypeAreTheDefaults()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new SSEDescription();

        // Assert
        await Assert.That((value.Status, value.SSEType)).IsEqualTo((SSEStatus.Unknown, SSEType.Unspecified));
    }

    [Test]
    public async Task Constructor_NoMembers_KeyAndDateAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new SSEDescription();

        // Assert
        await Assert.That((value.KMSMasterKeyArn, value.InaccessibleEncryptionDateTime)).IsEqualTo(((string?)null, (DateTimeOffset?)null));
    }
}
