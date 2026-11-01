using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class IndexProjectionTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_NonKeyAttributesIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new IndexProjection { ProjectionType = ProjectionType.KeysOnly };

        // Assert
        await Assert.That(value.NonKeyAttributes).IsNull();
    }
}
