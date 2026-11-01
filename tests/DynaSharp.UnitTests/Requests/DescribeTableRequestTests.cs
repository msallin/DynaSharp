using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class DescribeTableRequestTests
{
    [Test]
    public async Task Constructor_TableName_IsKept()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new DescribeTableRequest { TableName = "Orders" };

        // Assert
        await Assert.That(request.TableName).IsEqualTo("Orders");
    }
}
