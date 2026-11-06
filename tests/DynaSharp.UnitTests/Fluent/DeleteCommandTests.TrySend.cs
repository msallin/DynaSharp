using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

/// <summary><c>TrySendAsync</c> of a delete. <see cref="PutCommandTests"/> tests what the three writes share.</summary>
public sealed partial class DeleteCommandTests
{
    [Test]
    public async Task TrySendAsync_ConditionFailedWithTheItem_ReadsItAsTheEntity()
    {
        // Arrange
        var stored = new Customer("alice", "Alice", 2);
        using var handler = new StubHandler(_ => StubResponse.ConditionalCheckFailed(stored));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Customer>("Shop").Delete(Customer.Keys.Of("alice")).If(Customer.Attrs.OrderCount.Eq(0)).ReturnOldOnFailure().TrySendAsync();

        // Assert
        await Assert.That((result.ConditionMet, result.Item)).IsEqualTo((false, stored));
    }

    [Test]
    public async Task TrySendAsync_ConditionHeldWithReturnOld_ReadsTheDeletedItem()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Attributes":{"pk":{"S":"CUSTOMER#alice"},"sk":{"S":"PROFILE"},"_type":{"S":"CUSTOMER"},"fullName":{"S":"Alice"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Customer>("Shop").Delete(Customer.Keys.Of("alice")).IfExists().ReturnOld().TrySendAsync();

        // Assert
        await Assert.That((result.ConditionMet, result.Item!.FullName)).IsEqualTo((true, "Alice"));
    }
}
