using DynaSharp.Mapping;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping;
using static DynaSharp.UnitTests.Fluent.WriteRulesFixture;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>What <see cref="VersionAttribute"/> does to a <c>Put</c>: the condition on the version an entity holds, the next version it stores, and <c>IgnoreVersion</c>, as bodies written by hand from <c>api.md</c> section 8.</summary>
public sealed class WriteRulesPutTests
{
    [Test]
    public async Task ToRequestJson_PutOfANewEntity_RequiresNoItemUnderTheKeyAndStoresTheFirst()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Put(Held(0)).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Item":@ITEM,"ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"}}""", 1));
    }

    [Test]
    public async Task ToRequestJson_PutOfALoadedEntity_RequiresItsVersionAndStoresTheNext()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Put(Held(3)).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Item":@ITEM,"ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"3"}}}""", 4));
    }

    [Test]
    public async Task ToRequestJson_PutWithAnotherCondition_RequiresBoth()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Put(Held(3)).IfExists().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ConditionExpression":"(attribute_exists(#n0)) AND (#n1 = :v0)","ExpressionAttributeNames":{"#n0":"pk","#n1":"Revision"}
            """);
    }

    [Test]
    public async Task ToRequestJson_PutIfNotExistsOfALoadedEntity_ThrowsSayingWhatToDo()
    {
        // Arrange (no item can lack its key and hold version 3, so this could only ever fail at DynamoDB)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => tickets.Put(Held(3)).IfNotExists().ToRequestJson();

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>().WithMessage(LoadedAndNew, StringComparison.Ordinal);
    }

    [Test]
    public async Task ToRequestJson_PutIgnoreVersion_WritesWhatABatchThatIgnoresVersionsWrites()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var put = tickets.Put(Held(3)).IgnoreVersion().ToRequestJson();
        var batch = tickets.BatchWrite().Put(Held(3)).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(put.Replace("""{"TableName":"Shop","Item":""", """{"RequestItems":{"Shop":[{"PutRequest":{"Item":""", StringComparison.Ordinal)).IsEqualTo(batch[..^4]);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ToRequestJson_PutIgnoreVersionBesideAnotherCondition_KeepsThatCondition(bool ignoreFirst)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var json = ignoreFirst ? tickets.Put(Held(3)).IgnoreVersion().IfNotExists().ToRequestJson() : tickets.Put(Held(3)).IfNotExists().IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Item":@ITEM,"ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"}}""", 4));
    }

    [Test]
    public async Task ToRequestJson_PutIgnoreVersionTwice_IsOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Put(Held(3)).IgnoreVersion().IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Item":@ITEM}""", 4));
    }

    [Test]
    public async Task ToRequestJson_PutIgnoreVersionOfAStructEntity_WritesItsNextVersion()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Gauge>("Shop").Put(new Gauge { Id = "g", Revision = 3 }).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"GAUGE#g"},"_type":{"S":"GAUGE"},"Id":{"S":"g"},"Revision":{"N":"4"}}}""");
    }

    [Test]
    public async Task ToRequestJson_PutIgnoreVersionOfAnEntityWithoutAVersion_ChangesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var json = customers.Put(new Customer("alice", "Alice")).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(customers.Put(new Customer("alice", "Alice")).ToRequestJson());
    }

    [Test]
    public async Task ToRequestJson_TransactionPutIgnoreVersionOnATableWithoutASchema_ChangesNothing()
    {
        // Arrange (a step does not know its type, so this compiles)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Put(client.Table("Shop", "pk"), new Item { ["pk"] = "a" }).IgnoreVersion().Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TransactItems":[{"Put":{"TableName":"Shop","Item":{"pk":{"S":"a"}}}}],"ClientRequestToken":"t"}""");
    }

    [Test]
    public async Task SendAsync_PutIgnoreVersion_ReturnsTheVersionItStoredAndLeavesTheEntityAlone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("{}"));
        var ticket = Held(3);

        // Act
        var result = await client.Table<Ticket>("Shop").Put(ticket).IgnoreVersion().SendAsync();

        // Assert
        await Assert.That((result.Version, ticket.Revision)).IsEqualTo(((long?)4L, 3));
    }

    [Test]
    public async Task SendAsync_PutIgnoreVersionOfAnEntityWithoutAVersion_ReturnsNoVersion()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("{}"));

        // Act
        var result = await client.Table<Customer>("Shop").Put(new Customer("alice", "Alice")).IgnoreVersion().SendAsync();

        // Assert
        await Assert.That(result.Version).IsNull();
    }

    [Test]
    public async Task ToRequestJson_NextCommandAfterAPutThatIgnoresTheVersion_StartsClean()
    {
        // Arrange (the state goes back to the pool, and the next put takes it)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        tickets.Put(Held(3)).IgnoreVersion().ToRequestJson();

        // Act
        var json = tickets.Put(Held(3)).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Item":@ITEM,"ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"3"}}}""", 4));
    }

    [Test]
    public async Task SendAsync_Put_ReturnsTheStoredVersionAndLeavesTheEntityAlone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("{}"));
        var ticket = Held(3);

        // Act
        var result = await client.Table<Ticket>("Shop").Put(ticket).SendAsync();

        // Assert
        await Assert.That((result.Version, ticket.Revision)).IsEqualTo(((long?)4L, 3));
    }

    [Test]
    public async Task SendAsync_PutThatIsTheLastThePropertyHolds_ThrowsAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var ticket = Held(int.MaxValue);

        // Act
        var act = () => client.Table<Ticket>("Shop").Put(ticket).ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<OverflowException>();
    }

    [Test]
    public async Task ToRequestJson_PutOfAnEntityWithoutAVersion_HasNoCondition()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Put(new Customer("alice", "Alice")).ToRequestJson();

        // Assert
        await Assert.That(json.Contains("ConditionExpression", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task SendAsync_PutOfAnEntityWithoutAVersion_ReturnsNoVersion()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("{}"));

        // Act
        var result = await client.Table<Customer>("Shop").Put(new Customer("alice", "Alice")).SendAsync();

        // Assert
        await Assert.That(result.Version).IsNull();
    }

    [Test]
    public async Task ToRequestJson_PutOfAHandWrittenEntityThatNamesAVersion_ThrowsNamingIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = Assert.Throws<NotSupportedException>(() => client.Table<HandVersioned>("Shop").Put(new HandVersioned("a")).ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains(nameof(HandVersioned));
    }

    [Test]
    public async Task WriteAttributes_EntityWithAVersion_IsAPlainCopy()
    {
        // Arrange, Act
        var item = EntityJson.Write(Held(3));

        // Assert
        await Assert.That(item).IsEqualTo(Stored(3));
    }

    [Test]
    public async Task ToRequestJson_UpdateIfVersion_ChecksItAndStoresTheNext()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(Ticket.Keys.Of("t1", Day)).Set("Note", "x").IfVersion(3).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"UpdateExpression":"SET #n0 = :v0, #n1 = :v2, #n2 = if_not_exists(#n2, :v3)","ConditionExpression":"#n1 = :v1","ExpressionAttributeNames":{"#n0":"Note","#n1":"Revision","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"S":"x"},":v1":{"N":"3"},":v2":{"N":"4"},":v3":{"S":"TICKET"}}}"""));
    }

    [Test]
    public async Task ToRequestJson_PutAfterAFailedUpdateOnTheSameState_StartsClean()
    {
        // Arrange (the state of a sent command goes back to the pool, and what it knew about versions must not stay)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("{}"));
        var tickets = client.Table<Ticket>("Shop");
        await tickets.Put(Held(1)).SendAsync();
        handler.Enqueue(StubResponse.Json("{}"));

        // Act
        var result = await client.Table<Customer>("Shop").Put(new Customer("a", "A")).SendAsync();

        // Assert
        await Assert.That(result.Version).IsNull();
    }
}
