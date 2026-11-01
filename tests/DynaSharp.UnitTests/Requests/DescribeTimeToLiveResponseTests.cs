using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class DescribeTimeToLiveResponseTests
{
    [Test]
    public async Task Constructor_NothingSet_DescriptionIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new DescribeTimeToLiveResponse();

        // Assert
        await Assert.That(value.TimeToLiveDescription).IsNull();
    }
}
