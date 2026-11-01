using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class TransactGetItemsRequestTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_ConsumedCapacityIsNotRequested()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new TransactGetItemsRequest { TransactItems = [] };

        // Assert
        await Assert.That(request.ReturnConsumedCapacity).IsEqualTo(ReturnConsumedCapacity.None);
    }
}
