using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class ItemResponseTests
{
    [Test]
    public async Task Constructor_NothingSet_ItemIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new ItemResponse();

        // Assert
        await Assert.That(response.Item).IsNull();
    }
}
