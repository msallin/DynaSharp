using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class UpdateGlobalSecondaryIndexActionTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_BothThroughputsAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new UpdateGlobalSecondaryIndexAction { IndexName = "ByStatus" };

        // Assert
        await Assert.That((value.ProvisionedThroughput, value.OnDemandThroughput)).IsEqualTo(((ProvisionedThroughput?)null, (OnDemandThroughput?)null));
    }
}
