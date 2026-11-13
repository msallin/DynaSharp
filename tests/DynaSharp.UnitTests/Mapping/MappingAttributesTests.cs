using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>The attributes hold what the source says. The generator reads the syntax, so these values matter to reflection-free tools only, but they are public API.</summary>
public sealed class MappingAttributesTests
{
    [Test]
    public async Task PartitionKeyAttribute_Template_KeepsTheValues()
    {
        // Arrange, Act
        var attribute = new PartitionKeyAttribute("USER#{Id}") { AttributeName = "id" };

        // Assert
        await Assert.That((attribute.Template, attribute.AttributeName)).IsEqualTo(("USER#{Id}", "id"));
    }

    [Test]
    public async Task PartitionKeyAttribute_Plain_HasNoTemplate()
    {
        // Arrange, Act
        var attribute = new PartitionKeyAttribute();

        // Assert
        await Assert.That((attribute.Template, attribute.AttributeName)).IsEqualTo(((string?)null, (string?)null));
    }

    [Test]
    public async Task SortKeyAttribute_Template_KeepsTheValues()
    {
        // Arrange, Act
        var attribute = new SortKeyAttribute("ORDER#{Id}") { AttributeName = "id" };

        // Assert
        await Assert.That((attribute.Template, attribute.AttributeName)).IsEqualTo(("ORDER#{Id}", "id"));
    }

    [Test]
    public async Task SortKeyAttribute_Plain_HasNoTemplate()
    {
        // Arrange, Act
        var attribute = new SortKeyAttribute();

        // Assert
        await Assert.That((attribute.Template, attribute.AttributeName)).IsEqualTo(((string?)null, (string?)null));
    }

    [Test]
    public async Task GlobalIndexKeyAttribute_AllArguments_KeepsTheValues()
    {
        // Arrange, Act
        var attribute = new GlobalIndexKeyAttribute("GSI1")
        {
            PartitionKey = "A#{X}",
            SortKey = "{Y}",
            Sparse = true,
            PartitionKeyAttributeName = "a",
            SortKeyAttributeName = "b",
        };

        // Assert
        await Assert.That((attribute.IndexName, attribute.PartitionKey, attribute.SortKey, attribute.Sparse, attribute.PartitionKeyAttributeName, attribute.SortKeyAttributeName))
            .IsEqualTo(("GSI1", "A#{X}", "{Y}", true, "a", "b"));
    }

    [Test]
    public async Task LocalIndexKeyAttribute_AllArguments_KeepsTheValues()
    {
        // Arrange, Act
        var attribute = new LocalIndexKeyAttribute("LSI1", "{Y}") { Sparse = true, SortKeyAttributeName = "b" };

        // Assert
        await Assert.That((attribute.IndexName, attribute.SortKey, attribute.Sparse, attribute.SortKeyAttributeName)).IsEqualTo(("LSI1", "{Y}", true, "b"));
    }

    [Test]
    public async Task DynamoDbAttributeAttribute_Name_KeepsTheValue()
    {
        // Arrange, Act
        var attribute = new DynamoDbAttributeAttribute("ttl");

        // Assert
        await Assert.That(attribute.Name).IsEqualTo("ttl");
    }

    [Test]
    public async Task DynamoDbEnumNameAttribute_Name_KeepsTheValue()
    {
        // Arrange, Act
        var attribute = new DynamoDbEnumNameAttribute("APPOINTMENT");

        // Assert
        await Assert.That(attribute.Name).IsEqualTo("APPOINTMENT");
    }

    [Test]
    public async Task DynamoDbConverterAttribute_Type_KeepsTheValue()
    {
        // Arrange, Act
        var attribute = new DynamoDbConverterAttribute(typeof(string));

        // Assert
        await Assert.That(attribute.ConverterType).IsEqualTo(typeof(string));
    }

    [Test]
    public async Task DiscriminatorAttribute_Value_KeepsTheValues()
    {
        // Arrange, Act
        var attribute = new DiscriminatorAttribute("ORDER") { AttributeName = "kind" };

        // Assert
        await Assert.That((attribute.Value, attribute.AttributeName, attribute.OptOut)).IsEqualTo(("ORDER", "kind", false));
    }

    [Test]
    public async Task DiscriminatorAttribute_OptOut_HasNoValue()
    {
        // Arrange, Act
        var attribute = new DiscriminatorAttribute { OptOut = true };

        // Assert
        await Assert.That((attribute.Value, attribute.OptOut)).IsEqualTo(((string?)null, true));
    }

    [Test]
    public async Task ComputedFromAttribute_Names_KeepsThem()
    {
        // Act
        var attribute = new ComputedFromAttribute("Arrival", "Slot");

        // Assert
        await Assert.That(attribute.Properties).IsEquivalentTo(["Arrival", "Slot"]);
    }

    [Test]
    public async Task MarkerAttributes_Constructed_AreAttributes()
    {
        // Arrange, Act
        Attribute[] attributes =
        [
            new DynamoDbEntityAttribute(), new DynamoDbDocumentAttribute(), new DynamoDbIgnoreAttribute(), new KeyOnlyAttribute(),
            new DynamoDbConstructorAttribute(), new VersionAttribute(),
        ];

        // Assert
        await Assert.That(attributes.Length).IsEqualTo(6);
    }
}
