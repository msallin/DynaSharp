using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class BillingModeSummaryTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_BillingModeIsTheDefault()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new BillingModeSummary { };

        // Assert
        await Assert.That(value.BillingMode).IsEqualTo(BillingMode.Unspecified);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_LastUpdateToPayPerRequestDateTimeIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new BillingModeSummary { };

        // Assert
        await Assert.That(value.LastUpdateToPayPerRequestDateTime).IsNull();
    }
}
