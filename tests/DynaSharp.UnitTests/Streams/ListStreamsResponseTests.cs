using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Streams;

public sealed class ListStreamsResponseTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_StreamsIsEmpty()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ListStreamsResponse { };

        // Assert
        await Assert.That(value.Streams).IsEmpty();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_LastEvaluatedStreamArnIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ListStreamsResponse { };

        // Assert
        await Assert.That(value.LastEvaluatedStreamArn).IsNull();
    }
}
