using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

public sealed class WriteResultTests
{
    [Test]
    public async Task ConditionMet_DefaultValue_IsTrue()
    {
        // Arrange
        var result = default(WriteResult<Item>);

        // Act
        var met = result.ConditionMet;

        // Assert
        await Assert.That(met).IsTrue();
    }

    [Test]
    public async Task Version_ConditionFailed_IsNullBecauseNothingWasWritten()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var result = await client.Table<Ticket>("Shop").Put(new Ticket { Id = "t1", OpenedAt = new DateOnly(2026, 10, 31), Revision = 3 }).TrySendAsync();

        // Act
        var version = result.Version;

        // Assert
        await Assert.That(version).IsNull();
    }

    [Test]
    public async Task Version_DeleteOfAnEntityWithoutAVersion_IsNull()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var result = await client.Table<Customer>("Shop").Delete(Customer.Keys.Of("alice")).TrySendAsync();

        // Act
        var version = result.Version;

        // Assert
        await Assert.That(version).IsNull();
    }
}
