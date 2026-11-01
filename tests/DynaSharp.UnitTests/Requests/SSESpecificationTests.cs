using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class SSESpecificationTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_EnabledIsTheDefault()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new SSESpecification { };

        // Assert
        await Assert.That(value.Enabled).IsFalse();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_SSETypeIsTheDefault()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new SSESpecification { };

        // Assert
        await Assert.That(value.SSEType).IsEqualTo(SSEType.Unspecified);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_KMSMasterKeyIdIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new SSESpecification { };

        // Assert
        await Assert.That(value.KMSMasterKeyId).IsNull();
    }
}
