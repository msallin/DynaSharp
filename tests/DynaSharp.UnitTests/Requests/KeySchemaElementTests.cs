using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class KeySchemaElementTests
{
    [Test]
    public async Task Constructor_Arguments_SetsAttributeName()
    {
        // Arrange (nothing to arrange)

        // Act
        var element = new KeySchemaElement("pk", KeyType.Hash);

        // Assert
        await Assert.That(element.AttributeName).IsEqualTo("pk");
    }

    [Test]
    public async Task Constructor_Arguments_SetsKeyType()
    {
        // Arrange (nothing to arrange)

        // Act
        var element = new KeySchemaElement("sk", KeyType.Range);

        // Assert
        await Assert.That(element.KeyType).IsEqualTo(KeyType.Range);
    }
}
