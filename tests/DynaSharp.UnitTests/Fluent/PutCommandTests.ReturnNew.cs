using DynaSharp.Testing;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

// ReturnNew of a put, which reads the item back from what the put sent.
public sealed partial class PutCommandTests
{
    private static readonly Ticket ReadTicket = new() { Id = "t1", OpenedAt = new DateOnly(2026, 10, 31), Revision = 3 };

    [Test]
    public async Task ReturnNew_EntityWithAVersion_ReturnsItAtTheNewVersion()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var changed = ReadTicket with { Note = "moved" };

        // Act
        var result = await client.Table<Ticket>("Shop").Put(changed).ReturnNew().SendAsync();

        // Assert
        await Assert.That(result.Item).IsEqualTo(changed with { Revision = 4 });
    }

    [Test]
    public async Task ReturnNew_EntityWithAVersion_LeavesTheEntityOfTheCallAsItWas()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Ticket>("Shop").Put(ReadTicket).ReturnNew().SendAsync();

        // Assert
        await Assert.That((ReadTicket.Revision, result.Item!.Revision, result.Version)).IsEqualTo((3, 4, (long?)4));
    }

    [Test]
    public async Task ReturnNew_Request_AsksDynamoDBForNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Put(new Item { ["pk"] = "a" }).ReturnOld().ReturnNew().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"a"}}}""");
    }

    [Test]
    public async Task ReturnNew_ItemBuiltAttributeByAttribute_ReturnsWhatWasWritten()
    {
        // Arrange (the response holds attributes that are not the item, which the put must not take)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"pk":{"S":"old"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").Put().With("pk", "a").With("n", 2).ReturnNew().SendAsync();

        // Assert
        await Assert.That((result.HasItem, result.Item!.GetString("pk"), result.Item.GetNumber<int>("n"))).IsEqualTo((true, (string?)"a", (int?)2));
    }

    [Test]
    public async Task ReturnOld_AfterReturnNew_ReturnsTheItemOfTheResponse()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"pk":{"S":"a"},"n":{"N":"1"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").Put(new Item { ["pk"] = "a", ["n"] = 2 }).ReturnNew().ReturnOld().SendAsync();

        // Assert
        await Assert.That(result.Item!.GetNumber<int>("n")).IsEqualTo(1);
    }

    [Test]
    public async Task ReturnNew_ConditionFailedOfTrySendAsync_HasNoItem()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").Put(new Item { ["pk"] = "a" }).If(Attr("n").NotExists()).ReturnNew().TrySendAsync();

        // Assert
        await Assert.That((result.ConditionMet, result.HasItem)).IsEqualTo((false, false));
    }

    [Test]
    public async Task ReturnNew_NextCommandOfTheClient_ReturnsNothing()
    {
        // Arrange (the state goes back to the client, and the next put must not read back an item)
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        await shop.Put(new Item { ["pk"] = "a" }).ReturnNew().SendAsync();

        // Act
        var result = await shop.Put(new Item { ["pk"] = "b" }).SendAsync();

        // Assert
        await Assert.That(result.HasItem).IsFalse();
    }
}
