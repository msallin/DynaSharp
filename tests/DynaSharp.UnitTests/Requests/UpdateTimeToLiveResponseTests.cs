using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class UpdateTimeToLiveResponseTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_TimeToLiveSpecificationIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new UpdateTimeToLiveResponse { };

        // Assert
        await Assert.That(value.TimeToLiveSpecification).IsNull();
    }
}
