using System.Text;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>
/// The schema of a mix built from the infos of its types, as hand-written types make one: how it tells the types of items apart and what it says when it cannot.
/// </summary>
public sealed class DiscriminatorSchemaTests
{
    [Test]
    public async Task Check_MixWithATypeWithoutADiscriminator_NamesItsPosition()
    {
        // Arrange
        var schema = new DiscriminatorSchema(Info("A"), Info(null));

        // Act
        var act = schema.Check;

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>()
            .WithMessage("The type at position 1 of this mix has no discriminator, so its items cannot be told from others.", StringComparison.Ordinal);
    }

    [Test]
    public async Task FindType_ItemWithoutTheDiscriminator_ReturnsMinusOne()
    {
        // Arrange
        var schema = new DiscriminatorSchema(Info("A"), Info("B"));

        // Act
        var index = schema.FindType(new Item { ["pk"] = "a" });

        // Assert
        await Assert.That(index).IsEqualTo(-1);
    }

    [Test]
    public async Task FindType_ReaderOnADiscriminatorThatIsNoString_ThrowsDynamoDbMappingExceptionNamingTheAttribute()
    {
        // Arrange: a number whose digits are the discriminator of the first type
        var schema = new DiscriminatorSchema(Info("5"), Info("B"));
        var json = Encoding.UTF8.GetBytes("""{"pk":{"S":"a"},"_type":{"N":"5"}}""");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => Task.FromResult(FindType(schema, json)));

        // Assert
        await Assert.That((failure.EntityType, failure.AttributeName, failure.Message)).IsEqualTo(("Mixed<5, B>", "_type", new DynamoDbMappingException("Mixed<5, B>", "_type").Message));
    }

    private static DynamoDbEntityInfo Info(string? discriminator) => new(discriminator, "pk", null, []);

    private static int FindType(DiscriminatorSchema schema, byte[] json)
    {
        var reader = new DynamoDbReader(json);
        reader.ReadStartItem();
        return schema.FindType(reader);
    }
}
