using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Streams;

public sealed class RecordIdentityTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_PrincipalIdIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new RecordIdentity { };

        // Assert
        await Assert.That(value.PrincipalId).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_TypeIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new RecordIdentity { };

        // Assert
        await Assert.That(value.Type).IsNull();
    }
}
