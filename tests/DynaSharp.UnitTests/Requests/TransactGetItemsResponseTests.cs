using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class TransactGetItemsResponseTests
{
    [Test]
    public async Task Constructor_NothingSet_ResponsesIsEmpty()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new TransactGetItemsResponse();

        // Assert
        await Assert.That(response.Responses).IsEmpty();
    }

    [Test]
    public async Task Constructor_NothingSet_ConsumedCapacityIsEmpty()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new TransactGetItemsResponse();

        // Assert
        await Assert.That(response.ConsumedCapacity).IsEmpty();
    }
}
