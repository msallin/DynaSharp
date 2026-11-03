using DynaSharp.UnitTests.Fluent;
using DynaSharp.UnitTests.Fluent.Mix;

namespace DynaSharp.UnitTests.Mapping;

public sealed class ItemDocumentTests
{
    [Test]
    public async Task From_Entity_HoldsTheAttributesItIsStoredWith()
    {
        // Arrange
        var customer = new Customer("alice", "Alice", 2);

        // Act
        var item = Item.From(customer);

        // Assert
        await Assert.That((item.GetString("pk"), item.GetString("_type"), item.GetNumber<int>("orderCount"))).IsEqualTo(("CUSTOMER#alice", "CUSTOMER", (int?)2));
    }

    [Test]
    public async Task To_ItemOfAnEntity_ReadsTheEntity()
    {
        // Arrange
        var customer = new Customer("alice", "Alice", 2, "a@example.com");

        // Act
        var copy = Item.From(customer).To<Customer>();

        // Assert
        await Assert.That(copy).IsEqualTo(customer);
    }

    [Test]
    public async Task To_MissingAttribute_ThrowsTheMappingException()
    {
        // Arrange
        var item = new Item { ["pk"] = "CUSTOMER#alice" };

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => Task.FromResult(item.To<Customer>()));

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("fullName");
    }

    [Test]
    public async Task To_NestedDocument_ReadsIt()
    {
        // Arrange
        var item = new Item { ["city"] = "Bern" };

        // Act
        var address = item.To<Address>();

        // Assert
        await Assert.That(address).IsEqualTo(new Address("Bern"));
    }

    [Test]
    public async Task From_Item_Copies()
    {
        // Arrange
        var item = new Item { ["a"] = 1 };

        // Act
        var copy = Item.From(item);

        // Assert
        await Assert.That((copy.Equals(item), ReferenceEquals(copy, item))).IsEqualTo((true, false));
    }

    [Test]
    public async Task From_Null_Throws()
    {
        // Arrange
        Customer customer = null!;

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentNullException>(() => Task.FromResult(Item.From(customer)));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("value");
    }

    [Test]
    public async Task Is_ItemOfTheType_IsTrue()
    {
        // Arrange
        var item = Item.From(new Alpha { Pk = "a", Sk = "1" });

        // Act
        var isAlpha = item.Is<Alpha>();

        // Assert
        await Assert.That(isAlpha).IsTrue();
    }

    [Test]
    public async Task Is_ItemOfAnotherType_IsFalse()
    {
        // Arrange
        var item = Item.From(new Beta { Pk = "a", Sk = "1", Amount = 5 });

        // Act
        var isAlpha = item.Is<Alpha>();

        // Assert
        await Assert.That(isAlpha).IsFalse();
    }

    [Test]
    public async Task Is_ImagesOfASingleTable_TellsTheEntitiesApart()
    {
        // Arrange: the new image of a stream record from a table that holds customers and orders, which a reader of orders skips
        var image = Item.From(new Customer("alice", "Alice", 2));

        // Act
        var kinds = (image.Is<Customer>(), image.Is<SchemaOrder>());

        // Assert
        await Assert.That(kinds).IsEqualTo((true, false));
    }

    [Test]
    public async Task Is_TypeWithItsOwnAttributeName_ReadsThatAttribute()
    {
        // Arrange
        var inKind = new Item { ["pk"] = "a", ["kind"] = "KINDED" };
        var inType = new Item { ["pk"] = "a", ["_type"] = "KINDED" };

        // Act
        var answers = (inKind.Is<Kinded>(), inType.Is<Kinded>());

        // Assert
        await Assert.That(answers).IsEqualTo((true, false));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task Is_NoDiscriminatorAsAString_IsFalse(int kind)
    {
        // Arrange
        var item = new Item { ["pk"] = "a" };
        switch (kind)
        {
            case 1:
                item["_type"] = AttributeValue.Null;
                break;
            case 2:
                item["_type"] = 1;
                break;
            case 3:
                item["_type"] = "alpha";
                break;
        }

        // Act
        var isAlpha = item.Is<Alpha>();

        // Assert
        await Assert.That(isAlpha).IsFalse();
    }

    [Test]
    public async Task Is_ItemThatSaysItIsTheTypeAndLacksAnAttribute_IsTrueWhileToThrows()
    {
        // Arrange
        var item = new Item { ["pk"] = "a", ["sk"] = "1", ["_type"] = "BETA" };

        // Act
        var isBeta = item.Is<Beta>();
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => Task.FromResult(item.To<Beta>()));

        // Assert
        await Assert.That((isBeta, failure.AttributeName)).IsEqualTo((true, "Amount"));
    }

    [Test]
    public async Task Is_HandWrittenTypeWithoutADiscriminator_Throws()
    {
        // Arrange
        var item = new Item { ["pk"] = "a", ["_type"] = string.Empty };

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(item.Is<Undeclared>()));

        // Assert
        await Assert.That(failure.Message).IsEqualTo("This type has no discriminator, so its items cannot be told from others.");
    }

    [Test]
    public async Task From_StructDocument_Works()
    {
        // Arrange
        var session = new Session("s");

        // Act
        var item = Item.From(session);

        // Assert
        await Assert.That(item.GetString("id")).IsEqualTo("s");
    }
}
