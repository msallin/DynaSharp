using System.Text;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// An iterator of the fake carries its position and its time, and a text that is no iterator of the fake, such as "not-an-iterator" of the integration tests, decodes to nothing.
public sealed class ShardIteratorTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Decode_EncodedIterator_GivesItBack()
    {
        // Arrange
        var iterator = new ShardIterator(FakeStreams.UsersArn, FakeStreams.FirstShard, 3, At.AddMilliseconds(250));

        // Act
        var decoded = ShardIterator.Decode(iterator.Encode());

        // Assert
        await Assert.That(decoded).IsEqualTo(iterator);
    }

    [Test]
    [Arguments("not-an-iterator")]
    [Arguments("FakeShardIterator/***")]
    [Arguments("FakeShardIterator/")]
    public async Task Decode_TextThatIsNoIterator_GivesNull(string text)
    {
        // Arrange: no prefix, no Base64, and no parts at all.

        // Act
        var decoded = ShardIterator.Decode(text);

        // Assert
        await Assert.That(decoded).IsNull();
    }

    [Test]
    [Arguments("arn|shard|1")]
    [Arguments("arn|shard|-1|0")]
    [Arguments("arn|shard|1|x")]
    [Arguments("arn|shard|1|253402300800000")]
    public async Task Decode_PartsThatAreNoPosition_GivesNull(string payload)
    {
        // Arrange: three parts, a negative position, a time that is no number, and one past the end of DateTimeOffset.
        var text = "FakeShardIterator/" + Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));

        // Act
        var decoded = ShardIterator.Decode(text);

        // Assert
        await Assert.That(decoded).IsNull();
    }

    [Test]
    public async Task Decode_Null_ThrowsArgumentNullException()
    {
        // Arrange
        string text = null!;

        // Act
        var act = () => ShardIterator.Decode(text);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }
}
