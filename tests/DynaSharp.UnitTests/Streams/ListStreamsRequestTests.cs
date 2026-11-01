using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Streams;

public sealed class ListStreamsRequestTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_TableNameIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ListStreamsRequest { };

        // Assert
        await Assert.That(value.TableName).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_LimitIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ListStreamsRequest { };

        // Assert
        await Assert.That(value.Limit).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ExclusiveStartStreamArnIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ListStreamsRequest { };

        // Assert
        await Assert.That(value.ExclusiveStartStreamArn).IsNull();
    }
}
