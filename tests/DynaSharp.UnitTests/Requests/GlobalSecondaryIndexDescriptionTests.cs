using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class GlobalSecondaryIndexDescriptionTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_KeySchemaIsEmpty()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new GlobalSecondaryIndexDescription { IndexName = "ByStatus" };

        // Assert
        await Assert.That(value.KeySchema).IsEmpty();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ProjectionIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new GlobalSecondaryIndexDescription { IndexName = "ByStatus" };

        // Assert
        await Assert.That(value.Projection).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_IndexStatusIsTheDefault()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new GlobalSecondaryIndexDescription { IndexName = "ByStatus" };

        // Assert
        await Assert.That(value.IndexStatus).IsEqualTo(IndexStatus.Unknown);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_BackfillingIsTheDefault()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new GlobalSecondaryIndexDescription { IndexName = "ByStatus" };

        // Assert
        await Assert.That(value.Backfilling).IsFalse();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ProvisionedThroughputIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new GlobalSecondaryIndexDescription { IndexName = "ByStatus" };

        // Assert
        await Assert.That(value.ProvisionedThroughput).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_OnDemandThroughputIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new GlobalSecondaryIndexDescription { IndexName = "ByStatus" };

        // Assert
        await Assert.That(value.OnDemandThroughput).IsNull();
    }
}
