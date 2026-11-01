using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class StreamSpecificationTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_StreamViewTypeIsTheDefault()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new StreamSpecification { StreamEnabled = true };

        // Assert
        await Assert.That(value.StreamViewType).IsEqualTo(StreamViewType.Unspecified);
    }
}
