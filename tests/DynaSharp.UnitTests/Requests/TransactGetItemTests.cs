using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class TransactGetItemTests
{
    [Test]
    public async Task Constructor_TransactGetItem_ExposesTheGet()
    {
        // Arrange (nothing to arrange)

        // Act
        var item = new TransactGetItem { Get = new TransactGet { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" } } };

        // Assert
        await Assert.That(item.Get.TableName).IsEqualTo("Users");
    }
}
