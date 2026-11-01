using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class LocalSecondaryIndexDescriptionTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_KeySchemaIsEmpty()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new LocalSecondaryIndexDescription { IndexName = "ByDate" };

        // Assert
        await Assert.That(value.KeySchema).IsEmpty();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ProjectionIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new LocalSecondaryIndexDescription { IndexName = "ByDate" };

        // Assert
        await Assert.That(value.Projection).IsNull();
    }
}
