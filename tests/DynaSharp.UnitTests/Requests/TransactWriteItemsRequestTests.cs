using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class TransactWriteItemsRequestTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_ConsumedCapacityIsNotRequested()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new TransactWriteItemsRequest { TransactItems = [] };

        // Assert
        await Assert.That(request.ReturnConsumedCapacity).IsEqualTo(ReturnConsumedCapacity.None);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ItemCollectionMetricsAreNotRequested()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new TransactWriteItemsRequest { TransactItems = [] };

        // Assert
        await Assert.That(request.ReturnItemCollectionMetrics).IsEqualTo(ReturnItemCollectionMetrics.None);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ClientRequestTokenIsNullSoTheLibraryMakesOne()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new TransactWriteItemsRequest { TransactItems = [] };

        // Assert
        await Assert.That(request.ClientRequestToken).IsNull();
    }
}
