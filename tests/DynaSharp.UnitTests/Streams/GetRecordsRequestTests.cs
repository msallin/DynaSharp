using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Streams;

public sealed class GetRecordsRequestTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_LimitIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new GetRecordsRequest { ShardIterator = "iterator" };

        // Assert
        await Assert.That(value.Limit).IsNull();
    }
}
