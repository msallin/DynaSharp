using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class AttributeDefinitionTests
{
    [Test]
    public async Task Constructor_Arguments_SetsAttributeName()
    {
        // Arrange (nothing to arrange)

        // Act
        var definition = new AttributeDefinition("pk", ScalarAttributeType.String);

        // Assert
        await Assert.That(definition.AttributeName).IsEqualTo("pk");
    }

    [Test]
    public async Task Constructor_Arguments_SetsAttributeType()
    {
        // Arrange (nothing to arrange)

        // Act
        var definition = new AttributeDefinition("pk", ScalarAttributeType.Number);

        // Assert
        await Assert.That(definition.AttributeType).IsEqualTo(ScalarAttributeType.Number);
    }
}
