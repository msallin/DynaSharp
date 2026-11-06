using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

/// <summary><c>TrySendAsync</c> of an update. <see cref="PutCommandTests"/> tests what the three writes share.</summary>
public sealed partial class UpdateCommandTests
{
    [Test]
    public async Task TrySendAsync_ConditionFailedWithTheItem_ReadsItAsTheEntity()
    {
        // Arrange
        var stored = new Customer("alice", "Alice", 7);
        using var handler = new StubHandler(_ => StubResponse.ConditionalCheckFailed(stored));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Customer>("Shop").Update(Customer.Keys.Of("alice")).Set(Customer.Attrs.OrderCount, 8).If(Customer.Attrs.OrderCount.Lt(7)).ReturnOldOnFailure().TrySendAsync();

        // Assert
        await Assert.That((result.ConditionMet, result.Item)).IsEqualTo((false, stored));
    }

    [Test]
    public async Task TrySendAsync_ConditionFailedWithReturnNew_HasNoItem()
    {
        // Arrange (ReturnNew asks for what the write stored, and it stored nothing)
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Customer>("Shop").Update(Customer.Keys.Of("alice")).Set(Customer.Attrs.OrderCount, 8).IfExists().ReturnNew().TrySendAsync();

        // Assert
        await Assert.That((result.ConditionMet, result.HasItem)).IsEqualTo((false, false));
    }

    [Test]
    public async Task TrySendAsync_ConditionHeldWithReturnNew_ReadsTheNewItem()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Attributes":{"pk":{"S":"CUSTOMER#alice"},"sk":{"S":"PROFILE"},"_type":{"S":"CUSTOMER"},"fullName":{"S":"Alice"},"orderCount":{"N":"8"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Customer>("Shop").Update(Customer.Keys.Of("alice")).Set(Customer.Attrs.OrderCount, 8).IfExists().ReturnNew().TrySendAsync();

        // Assert
        await Assert.That((result.ConditionMet, result.Item!.OrderCount)).IsEqualTo((true, 8));
    }
}
