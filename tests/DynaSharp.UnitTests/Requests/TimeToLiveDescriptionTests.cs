using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class TimeToLiveDescriptionTests
{
    [Test]
    public async Task Constructor_NothingSet_StatusIsUnknownAndAttributeNameIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new TimeToLiveDescription();

        // Assert
        await Assert.That((value.TimeToLiveStatus, value.AttributeName)).IsEqualTo((TimeToLiveStatus.Unknown, (string?)null));
    }
}
