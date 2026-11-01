using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Streams;

public sealed class DescribeStreamRequestTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_LimitIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new DescribeStreamRequest { StreamArn = "arn" };

        // Assert
        await Assert.That(value.Limit).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ExclusiveStartShardIdIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new DescribeStreamRequest { StreamArn = "arn" };

        // Assert
        await Assert.That(value.ExclusiveStartShardId).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ShardFilterIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new DescribeStreamRequest { StreamArn = "arn" };

        // Assert
        await Assert.That(value.ShardFilter).IsNull();
    }
}
