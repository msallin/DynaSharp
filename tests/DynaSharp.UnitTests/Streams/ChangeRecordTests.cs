using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Streams;

public sealed class ChangeRecordTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_EventIdIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ChangeRecord { };

        // Assert
        await Assert.That(value.EventId).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_EventNameIsTheDefault()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ChangeRecord { };

        // Assert
        await Assert.That(value.EventName).IsEqualTo(OperationType.Unknown);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_EventVersionIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ChangeRecord { };

        // Assert
        await Assert.That(value.EventVersion).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_EventSourceIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ChangeRecord { };

        // Assert
        await Assert.That(value.EventSource).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_RegionIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ChangeRecord { };

        // Assert
        await Assert.That(value.Region).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_DynamoDbIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ChangeRecord { };

        // Assert
        await Assert.That(value.DynamoDb).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_UserIdentityIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ChangeRecord { };

        // Assert
        await Assert.That(value.UserIdentity).IsNull();
    }
}
