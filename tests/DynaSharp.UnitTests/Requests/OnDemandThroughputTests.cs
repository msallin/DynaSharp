using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class OnDemandThroughputTests
{
    [Test]
    public async Task Constructor_NoMembers_BothMaximumsAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new OnDemandThroughput();

        // Assert
        await Assert.That((value.MaxReadRequestUnits, value.MaxWriteRequestUnits)).IsEqualTo(((long?)null, (long?)null));
    }
}
