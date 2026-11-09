using DynaSharp.Mapping;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping.KeyEntities;
using static DynaSharp.UnitTests.Fluent.WriteRulesFixture;

// Some tests write by key without IfVersion or IgnoreVersion on purpose, to see the write throw at run time.
#pragma warning disable DYNA239

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
        await Assert.That(act).Throws<InvalidOperationException>().WithMessage("This write already has IfVersion, which IgnoreVersion cannot take back. Use one of them.", StringComparison.Ordinal);
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
    public async Task ToRequestJson_UpdateIgnoreVersion_AdvancesTheVersionWithoutACondition()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(Ticket.Keys.Of("t1", Day)).Set("Note", "x").IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1), #n2 = if_not_exists(#n2, :v2) + :v3","ExpressionAttributeNames":{"#n0":"Note","#n1":"_type","#n2":"Revision"},"ExpressionAttributeValues":{":v0":{"S":"x"},":v1":{"S":"TICKET"},":v2":{"N":"0"},":v3":{"N":"1"}}}"""));
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
    public async Task ToRequestJson_UpdateIgnoreVersionThatSetsTheVersion_KeepsTheValueOfTheCaller()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(Ticket.Keys.Of("t1", Day)).Set(Ticket.Attrs.Revision, 9).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1)","ExpressionAttributeNames":{"#n0":"Revision","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"N":"9"},":v1":{"S":"TICKET"}}}"""));
    }

    [Test]
    [Arguments("raw path after")]
    [Arguments("handle after")]
    public async Task ToRequestJson_UpdateIfVersionThatSetsTheVersion_ChecksAndKeepsTheValueOfTheCaller(string shape)
    {
        // Arrange (DynamoDB refuses two actions on one path, so the action of the caller takes the place of the next version, as with IgnoreVersion)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var json = shape switch
        {
            "raw path after" => tickets.Update(Ticket.Keys.Of("t1", Day)).IfVersion(3).Set("Revision", 9).ToRequestJson(),
            _ => tickets.Update(Ticket.Keys.Of("t1", Day)).IfVersion(3).Set(Ticket.Attrs.Revision, 9).ToRequestJson(),
        };

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"UpdateExpression":"SET #n0 = :v1, #n1 = if_not_exists(#n1, :v2)","ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"Revision","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"N":"3"},":v1":{"N":"9"},":v2":{"S":"TICKET"}}}"""));
    }

    [Test]
    public async Task ToRequestJson_UpdateThatSetsTheVersionBeforeIfVersion_ChecksAndKeepsTheValueOfTheCaller()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(Ticket.Keys.Of("t1", Day)).Set("Revision", 9).IfVersion(3).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v2)","ConditionExpression":"#n0 = :v1","ExpressionAttributeNames":{"#n0":"Revision","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"N":"9"},":v1":{"N":"3"},":v2":{"S":"TICKET"}}}"""));
    }

    [Test]
    public async Task SendAsync_UpdateIfVersionThatSetsTheVersion_ReturnsNoVersion()
    {
        // Arrange (the update does not know what the action of the caller stores)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("{}"));

        // Act
        var result = await client.Table<Ticket>("Shop").Update(Ticket.Keys.Of("t1", Day)).IfVersion(3).Add("Revision", 5).SendAsync();

        // Assert
        await Assert.That(result.Version).IsNull();
    }

    [Test]
    public async Task ToRequestJson_UpdateIfVersionAfterIgnoreVersion_AdvancesFromThatVersionOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var json = tickets.Update(Ticket.Keys.Of("t1", Day)).IgnoreVersion().IfVersion(3).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(tickets.Update(Ticket.Keys.Of("t1", Day)).IfVersion(3).ToRequestJson());
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
        var act = () => tickets.Update(Ticket.Keys.Of("a", Day)).Set("Note", "x").ToRequestJson();

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
        var act = () => orders.Delete(KeyedOrder.Keys.Of(Guid.Empty, DateTimeOffset.UnixEpoch, Guid.Empty)).IfVersion(1).ToRequestJson();

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
        var json = client.Table<KeyedOrder>("Shop").Update(KeyedOrder.Keys.Of(Guid.Empty, DateTimeOffset.UnixEpoch, Guid.Empty)).Set(outsideTheEntity, 1).ToRequestJson();

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
