using DynaSharp.Mapping;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Fluent.WriteRulesFixture;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>What <see cref="VersionAttribute"/> asks of a batch, which cannot carry a condition, as bodies written by hand from <c>api.md</c> section 8.</summary>
public sealed class WriteRulesBatchTests
{
    [Test]
    public async Task SendAsync_BatchPutWithoutIgnoreVersion_ThrowsAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await tickets.BatchWrite().Put(Held(1)).SendAsync());

        // Assert
        await Assert.That((failure is not null, handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task ToRequestJson_BatchDeleteByKeyWithoutIgnoreVersion_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => tickets.BatchWrite().Delete(Ticket.Keys.Of("t1", Day)).ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ToRequestJson_BatchDeleteOfAttributeValuesWithoutIgnoreVersion_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => tickets.BatchWrite().Delete("a", "b").ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ToRequestJson_BatchOfItemsWithAPutOfAnotherTable_ThrowsForThatTable()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        var tickets = shop.Of<Ticket>();

        // Act
        var act = () => shop.BatchWrite().Put(new Item { ["pk"] = "a", ["sk"] = "b" }).Put(tickets, Held(1)).ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ToRequestJson_BatchIgnoreVersionAfterTheWrites_WritesThemAsTheyAre()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").BatchWrite().Put(Held(3)).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"RequestItems":{"Shop":[{"PutRequest":{"Item":@ITEM}}]}}""", 3));
    }

    [Test]
    public async Task ToRequestJson_BatchIgnoreVersionBeforeTheWrites_HoldsForThemToo()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").BatchWrite().IgnoreVersion().Put(Held(3)).Delete("a", "b").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("DeleteRequest");
    }

    [Test]
    public async Task ToRequestJson_BatchOfAnEntityWithoutAVersion_NeedsNoVerb()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").BatchWrite().Put(new Customer("a", "A")).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("PutRequest");
    }
}
