using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class UpdateTableResponseTests
{
    [Test]
    public async Task Constructor_Description_KeepsIt()
    {
        // Arrange
        var table = new TableDescription { TableName = "Orders" };

        // Act
        var value = new UpdateTableResponse { TableDescription = table };

        // Assert
        await Assert.That(value.TableDescription).IsSameReferenceAs(table);
    }
}
