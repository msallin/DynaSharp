using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Streams;

public sealed class ShardTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_SequenceNumberRangeIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new Shard { ShardId = "shardId-1" };

        // Assert
        await Assert.That(value.SequenceNumberRange).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ParentShardIdIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new Shard { ShardId = "shardId-1" };

        // Assert
        await Assert.That(value.ParentShardId).IsNull();
    }
}
