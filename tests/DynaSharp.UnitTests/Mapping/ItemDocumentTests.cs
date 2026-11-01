using DynaSharp.UnitTests.Fluent;

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
