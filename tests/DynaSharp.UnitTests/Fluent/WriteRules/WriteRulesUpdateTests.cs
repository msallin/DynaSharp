using DynaSharp.Mapping;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping.KeyEntities;
using static DynaSharp.UnitTests.Fluent.WriteRulesFixture;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>What <see cref="VersionAttribute"/> asks of an <c>Update</c>, which holds no entity and has to say how it treats the version, as bodies written by hand from <c>api.md</c> section 8.</summary>
public sealed class WriteRulesUpdateTests
{
    [Test]
    public async Task IgnoreVersion_AfterIfVersionOnAnUpdate_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => tickets.Update(Ticket.Keys.Of("t1", Day)).IfVersion(3).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ToRequestJson_UpdateIfVersionZero_RequiresNoItemUnderTheKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(Ticket.Keys.Of("t1", Day)).IfVersion(0).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"UpdateExpression":"SET #n1 = :v0, #n2 = if_not_exists(#n2, :v1)","ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk","#n1":"Revision","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"N":"1"},":v1":{"S":"TICKET"}}}"""));
    }

    [Test]
    public async Task SendAsync_UpdateIfVersion_ReturnsTheStoredVersion()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("{}"));

        // Act
        var result = await client.Table<Ticket>("Shop").Update(Ticket.Keys.Of("t1", Day)).IfVersion(3).SendAsync();

        // Assert
        await Assert.That(result.Version).IsEqualTo(4L);
    }

    [Test]
    public async Task StoredVersion_UpdateIfVersion_IsTheStoredVersion()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("{}"));

        // Act
        var result = await client.Table<Ticket>("Shop").Update(Ticket.Keys.Of("t1", Day)).IfVersion(3).SendAsync();

        // Assert
        await Assert.That(result.StoredVersion).IsEqualTo(4L);
    }

    [Test]
    public async Task StoredVersion_UpdateIgnoreVersion_ThrowsSayingWhichWritesStoreNone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("{}"));
        var result = await client.Table<Ticket>("Shop").Update(Ticket.Keys.Of("t1", Day)).Set("Note", "x").IgnoreVersion().SendAsync();

        // Act
        var act = () => result.StoredVersion;

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("IgnoreVersion()", StringComparison.Ordinal);
    }

    [Test]
    public async Task ToRequestJson_UpdateIgnoreVersion_LeavesTheVersionAlone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(Ticket.Keys.Of("t1", Day)).Set("Note", "x").IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1)","ExpressionAttributeNames":{"#n0":"Note","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"S":"x"},":v1":{"S":"TICKET"}}}"""));
    }

    [Test]
    public async Task SendAsync_UpdateIgnoreVersion_ReturnsNoVersion()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("{}"));

        // Act
        var result = await client.Table<Ticket>("Shop").Update(Ticket.Keys.Of("t1", Day)).Set("Note", "x").IgnoreVersion().SendAsync();

        // Assert
        await Assert.That(result.Version).IsNull();
    }

    [Test]
    public async Task SendAsync_UpdateThatSaysNothingAboutTheVersion_ThrowsNamingTheEntityAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await tickets.Update(Ticket.Keys.Of("t1", Day)).Set("Note", "x").SendAsync());

        // Assert
        await Assert.That((failure!.Message.Contains(nameof(Ticket), StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task SendAsync_UpdateByAttributeValuesThatSaysNothingAboutTheVersion_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => tickets.Update("a", "b").Set("Note", "x").ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ToRequestJson_UpdateThatReturnsAnAttributeAndSaysNothingAboutTheVersion_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => tickets.Update(Ticket.Keys.Of("t1", Day)).Add(Ticket.Attrs.Votes, 1).Return(Ticket.Attrs.Votes).ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task SendAsync_UpdateThatReturnsAnAttributeAndSaysNothingAboutTheVersion_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = async () => await tickets.Update(Ticket.Keys.Of("t1", Day)).Add(Ticket.Attrs.Votes, 1).Return(Ticket.Attrs.Votes).SendAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ToRequestJson_UpdateThatReturnsAnAttribute_KeepsTheVersion()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(Ticket.Keys.Of("t1", Day)).Add(Ticket.Attrs.Votes, 1).IfVersion(3).Return(Ticket.Attrs.Votes).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n1 = :v2, #n2 = if_not_exists(#n2, :v3) ADD #n0 :v0","ConditionExpression":"#n1 = :v1"
            """);
    }

    [Test]
    [Arguments(-1L)]
    [Arguments(long.MinValue)]
    [Arguments(long.MaxValue)]
    public async Task IfVersion_NoVersionAnItemCanHold_ThrowsAndLeavesTheCommandUsable(long version)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table<Ticket>("Shop").Update(Ticket.Keys.Of("t1", Day));
        InvalidCall(command, version, out var failure);

        // Act
        var json = command.IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That((failure is ArgumentOutOfRangeException, json.Length > 0)).IsEqualTo((true, true));
    }

    [Test]
    public async Task IfVersion_EntityWithoutAVersion_ThrowsInsteadOfDroppingTheCheck()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => _ = customers.Update(Customer.Keys.Of("a")).IfVersion(1));

        // Assert
        await Assert.That(failure.Message).Contains("Shop");
    }

    [Test]
    public async Task IfVersion_EntityWithKeyTemplatesAndNoVersion_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var orders = client.Table<KeyedOrder>("Shop");

        // Act
        var act = () => orders.Delete("a", "b").IfVersion(1).ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ToRequestJson_IgnoreVersionOfAnEntityWithoutOne_ChangesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Update(Customer.Keys.Of("a")).Set(Customer.Attrs.OrderCount, 1).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json.Contains("Condition", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ToRequestJson_UpdateOfAnEntityWithoutAVersion_NeedsNoVerb()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var outsideTheEntity = "x";

        // Act
        var json = client.Table<KeyedOrder>("Shop").Update("a", "b").Set(outsideTheEntity, 1).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("UpdateExpression");
    }

    [Test]
    public async Task ToRequestJson_UpdateOfAnItem_NeedsNoVerb()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Update("a", "b").Set("Revision", 5).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("UpdateExpression");
    }

    [Test]
    public async Task ToRequestJson_DeleteIfVersion_ChecksIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Delete(Ticket.Keys.Of("t1", Day)).IfVersion(3).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"3"}}}"""));
    }
}
