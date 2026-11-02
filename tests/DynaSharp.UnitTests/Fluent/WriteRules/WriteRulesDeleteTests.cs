using DynaSharp.Mapping;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping.KeyEntities;
using static DynaSharp.UnitTests.Fluent.WriteRulesFixture;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>What <see cref="VersionAttribute"/> asks of a <c>Delete</c>, by key or by entity, as bodies written by hand from <c>api.md</c> section 8.</summary>
public sealed class WriteRulesDeleteTests
{
    [Test]
    public async Task SendAsync_DeleteIfVersion_ReturnsNoVersion()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("{}"));

        // Act
        var result = await client.Table<Ticket>("Shop").Delete(Ticket.Keys.Of("t1", Day)).IfVersion(3).SendAsync();

        // Assert
        await Assert.That(result.Version).IsNull();
    }

    [Test]
    public async Task ToRequestJson_DeleteIgnoreVersion_HasNoCondition()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Delete(Ticket.Keys.Of("t1", Day)).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY}"""));
    }

    [Test]
    public async Task SendAsync_DeleteThatSaysNothingAboutTheVersion_ThrowsAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await tickets.Delete(Ticket.Keys.Of("t1", Day)).SendAsync());

        // Assert
        await Assert.That((failure!.Message.Contains(nameof(Ticket), StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task ToRequestJson_DeleteByAttributeValuesThatSaysNothingAboutTheVersion_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => tickets.Delete("a", "b").ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ToRequestJson_DeleteOfAnEntity_ChecksTheVersionItHolds()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Delete(Held(3)).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"3"}}}"""));
    }

    [Test]
    public async Task ToRequestJson_DeleteOfAnEntityOfVersionZero_RequiresNoItemUnderTheKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Delete(Held(0)).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"}}"""));
    }

    [Test]
    public async Task ToRequestJson_DeleteOfAnEntityIgnoreVersion_HasNoCondition()
    {
        // Arrange (the verb comes after the entity gave its version, so the condition has to wait for the terminal)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Delete(Held(3)).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY}"""));
    }

    [Test]
    public async Task ToRequestJson_DeleteOfAnEntityIfVersion_ChecksThatOneInstead()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Delete(Held(3)).IfVersion(7).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"7"}}}"""));
    }

    [Test]
    public async Task ToRequestJson_DeleteOfAnEntityWithAnotherCondition_RequiresBoth()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Delete(Held(3)).IfExists().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ConditionExpression":"(attribute_exists(#n0)) AND (#n1 = :v0)","ExpressionAttributeNames":{"#n0":"pk","#n1":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"3"}}
            """);
    }

    [Test]
    public async Task ToRequestJson_DeleteOfAnEntityReturningOld_KeepsTheCheck()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Delete(Held(3)).ReturnOld().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ConditionExpression":"#n0 = :v0"
            """);
    }

    [Test]
    public async Task SendAsync_DeleteOfAnEntity_SendsTheCheckAndReturnsNoVersion()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("{}"));

        // Act
        var result = await client.Table<Ticket>("Shop").Delete(Held(3)).SendAsync();

        // Assert
        await Assert.That((result.Version, handler.Requests.Single().BodyText.Contains("ConditionExpression", StringComparison.Ordinal))).IsEqualTo(((long?)null, true));
    }

    [Test]
    public async Task ToRequestJson_DeleteOfAnEntityWithTemplatesAndNoVersion_HasNoCondition()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var order = new KeyedOrder { UserId = Guid.Empty, CreatedAt = DateTimeOffset.UnixEpoch, OrderId = Guid.Empty, Status = Plan.Free, Total = 1m };

        // Act
        var json = client.Table<KeyedOrder>("Shop").Delete(order).ToRequestJson();

        // Assert
        await Assert.That(json.Contains("Condition", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ToRequestJson_DeleteOfAStructEntity_ChecksTheVersionItHolds()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Gauge>("Shop").Delete(new Gauge { Id = "g", Revision = 5 }).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"GAUGE#g"}},"ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"5"}}}""");
    }

    [Test]
    [Arguments(long.MaxValue)]
    public async Task ToRequestJson_DeleteOfAnEntityAtTheLastVersion_ChecksIt(long version)
    {
        // Arrange (IfVersion refuses the last version because an update has no next one; a delete needs none)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Gauge>("Shop").Delete(new Gauge { Id = "g", Revision = (ulong)version }).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            {":v0":{"N":"9223372036854775807"}}
            """);
    }

    [Test]
    public async Task Delete_EntityWhoseVersionDoesNotFitALong_ThrowsOverflow()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var gauges = client.Table<Gauge>("Shop");
        var gauge = new Gauge { Id = "g", Revision = ulong.MaxValue };

        // Act
        var act = () => gauges.Delete(gauge).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<OverflowException>();
    }

    [Test]
    public async Task Delete_HandWrittenEntityThatNamesAVersion_ThrowsNamingIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = Assert.Throws<NotSupportedException>(() => client.Table<HandVersioned>("Shop").Delete(new HandVersioned("a")).ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains(nameof(HandVersioned));
    }

    [Test]
    public async Task ToRequestJson_DeleteByKeyAfterADeleteOfAnEntity_KnowsNothingOfItsVersion()
    {
        // Arrange (the state goes back to the pool, and a version left in it would stand in for the verb a key needs)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        _ = tickets.Delete(Held(3)).IgnoreVersion().ToRequestJson();

        // Act
        var act = () => tickets.Delete(Ticket.Keys.Of("t1", Day)).ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ToRequestJson_TransactionPut_ChecksTheVersionOfItsEntityAndStoresTheNext()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Put(client.Table<Ticket>("Shop"), Held(3)).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TransactItems":[{"Put":{"TableName":"Shop","Item":@ITEM,"ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"3"}}}}],"ClientRequestToken":"t"}""", 4));
    }

    [Test]
    [Arguments(-1L)]
    [Arguments(long.MaxValue)]
    public async Task IfVersion_DeleteWithNoVersionAnItemCanHold_Throws(long version)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => tickets.Delete(Ticket.Keys.Of("t1", Day)).IfVersion(version).ToRequestJson();

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>();
    }
}
