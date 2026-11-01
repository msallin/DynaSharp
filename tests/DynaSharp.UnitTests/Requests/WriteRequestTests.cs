using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class WriteRequestTests
{
    [Test]
    public async Task Constructor_NothingSet_PutAndDeleteAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var write = new WriteRequest();

        // Assert
        await Assert.That((write.PutRequest, write.DeleteRequest)).IsEqualTo(((PutRequest?)null, (DeleteRequest?)null));
    }

    [Test]
    public async Task Constructor_Put_ExposesTheItem()
    {
        // Arrange (nothing to arrange)

        // Act
        var write = new WriteRequest { PutRequest = new PutRequest { Item = new Dictionary<string, AttributeValue> { ["pk"] = "a" } } };

        // Assert
        await Assert.That(write.PutRequest!.Item["pk"].AsString()).IsEqualTo("a");
    }

    [Test]
    public async Task Constructor_Delete_ExposesTheKey()
    {
        // Arrange (nothing to arrange)

        // Act
        var write = new WriteRequest { DeleteRequest = new DeleteRequest { Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" } } };

        // Assert
        await Assert.That(write.DeleteRequest!.Key["pk"].AsString()).IsEqualTo("a");
    }
}
