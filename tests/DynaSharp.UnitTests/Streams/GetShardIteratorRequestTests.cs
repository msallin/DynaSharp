using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Streams;

public sealed class GetShardIteratorRequestTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_SequenceNumberIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new GetShardIteratorRequest { StreamArn = "arn", ShardId = "shardId-1", ShardIteratorType = ShardIteratorType.Latest };

        // Assert
        await Assert.That(value.SequenceNumber).IsNull();
    }
}
