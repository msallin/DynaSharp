using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class GetItemResponseTests
{
    [Test]
    public async Task Constructor_NothingSet_ItemIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new GetItemResponse();

        // Assert
        await Assert.That(response.Item).IsNull();
    }

    [Test]
    public async Task Constructor_NothingSet_ConsumedCapacityIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new GetItemResponse();

        // Assert
        await Assert.That(response.ConsumedCapacity).IsNull();
    }
}
