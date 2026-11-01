using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class DescribeTableResponseTests
{
    [Test]
    public async Task Constructor_Table_IsKept()
    {
        // Arrange
        var table = new TableDescription { TableName = "Orders" };

        // Act
        var response = new DescribeTableResponse { Table = table };

        // Assert
        await Assert.That(response.Table).IsSameReferenceAs(table);
    }
}
