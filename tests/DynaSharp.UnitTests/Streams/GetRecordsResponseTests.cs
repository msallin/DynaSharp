using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Streams;

public sealed class GetRecordsResponseTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_RecordsIsEmpty()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new GetRecordsResponse { };

        // Assert
        await Assert.That(value.Records).IsEmpty();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_NextShardIteratorIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new GetRecordsResponse { };

        // Assert
        await Assert.That(value.NextShardIterator).IsNull();
    }
}
