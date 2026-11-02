using DynaSharp.Testing;
using static DynaSharp.UnitTests.Fluent.WriteRulesFixture;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Where a version and <c>IfNotExists</c> or <c>IfExists</c> say the same or the opposite, on every write path, as bodies written by hand from <c>api.md</c> section 8.</summary>
public sealed class WriteRulesExistenceTests
{
    [Test]
    public async Task SendAsync_PutIfNotExistsOfALoadedEntity_SendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(async () => await tickets.Put(Held(1)).IfNotExists().SendAsync());

        // Assert
        await Assert.That((failure.Message, handler.Requests.Count)).IsEqualTo((LoadedAndNew, 0));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ToRequestJson_UpdateIfVersionAboveZeroAndIfNotExists_ThrowsInEitherOrder(bool versionFirst)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        var key = Ticket.Keys.Of("t1", new DateOnly(2026, 10, 31));

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => _ = versionFirst ? tickets.Update(key).IfVersion(3).IfNotExists().ToRequestJson() : tickets.Update(key).IfNotExists().IfVersion(3).ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("version above 0").And.Contains("Ticket");
    }

    [Test]
    public async Task SendAsync_UpdateIfVersionAboveZeroAndIfNotExists_SendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(async () => await tickets.Update(Ticket.Keys.Of("t1", new DateOnly(2026, 10, 31))).IfVersion(1).IfNotExists().SendAsync());

        // Assert
        await Assert.That((failure is not null, handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ToRequestJson_UpdateIfVersionZeroAndIfNotExists_HasTheClauseOnceInEitherOrder(bool versionFirst)
    {
        // Arrange (version 0 stands for an item that does not exist, so the two are one condition)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        var key = Ticket.Keys.Of("t1", new DateOnly(2026, 10, 31));

        // Act
        var json = versionFirst ? tickets.Update(key).IfVersion(0).IfNotExists().ToRequestJson() : tickets.Update(key).IfNotExists().IfVersion(0).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"UpdateExpression":"SET #n1 = :v0, #n2 = if_not_exists(#n2, :v1)","ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk","#n1":"Revision","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"N":"1"},":v1":{"S":"TICKET"}}}"""));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ToRequestJson_TransactionUpdateIfVersionZeroAndIfNotExists_HasTheClauseOnceInEitherOrder(bool versionFirst)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        var key = Ticket.Keys.Of("t1", new DateOnly(2026, 10, 31));

        // Act
        var json = versionFirst
            ? client.TransactWrite().Update(tickets, key).IfVersion(0).IfNotExists().Token("t").ToRequestJson()
            : client.TransactWrite().Update(tickets, key).IfNotExists().IfVersion(0).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"
            """);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ToRequestJson_TransactionCheckIfVersionZeroAndIfNotExists_HasTheClauseOnceInEitherOrder(bool versionFirst)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        var key = Ticket.Keys.Of("t1", Day);

        // Act
        var json = versionFirst
            ? client.TransactWrite().Check(tickets, key).IfVersion(0).IfNotExists().Token("t").ToRequestJson()
            : client.TransactWrite().Check(tickets, key).IfNotExists().IfVersion(0).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TransactItems":[{"ConditionCheck":{"TableName":"Shop","Key":@KEY,"ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"}}}],"ClientRequestToken":"t"}"""));
    }

    [Test]
    public async Task ToRequestJson_UpdateIfNotExistsTwice_HasTheClauseOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(Ticket.Keys.Of("t1", Day)).IfVersion(0).IfNotExists().IfNotExists().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ConditionExpression":"attribute_not_exists(#n0)"
            """);
    }

    [Test]
    public async Task ToRequestJson_UpdateIfVersionZeroThenAboveZero_Throws()
    {
        // Arrange (the first says that no item exists, and the second asks for one that holds a version)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        var key = Ticket.Keys.Of("t1", Day);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => _ = tickets.Update(key).IfVersion(0).IfVersion(3).ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("version above 0");
    }

    [Test]
    public async Task ToRequestJson_TransactionUpdateIfVersionAboveZeroAndIfNotExists_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        var key = Ticket.Keys.Of("t1", new DateOnly(2026, 10, 31));

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => client.TransactWrite().Update(tickets, key).IfVersion(3).IfNotExists().Token("t").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("version above 0");
    }

    [Test]
    public async Task ToRequestJson_TransactionCheckIfVersionAboveZeroAndIfNotExists_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        var key = Ticket.Keys.Of("t1", new DateOnly(2026, 10, 31));

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => client.TransactWrite().Check(tickets, key).IfVersion(3).IfNotExists().Token("t").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("version above 0");
    }

    [Test]
    public async Task ToRequestJson_PutIfExistsOfANewEntity_ThrowsSayingWhatToDo()
    {
        // Arrange (version 0 means that no item has the key, so this could only ever fail at DynamoDB)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => tickets.Put(Held(0)).IfExists().ToRequestJson();

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>().WithMessage(ExistsAtZero, StringComparison.Ordinal);
    }

    [Test]
    public async Task SendAsync_PutIfExistsOfANewEntity_SendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(async () => await tickets.Put(Held(0)).IfExists().SendAsync());

        // Assert
        await Assert.That((failure.Message, handler.Requests.Count)).IsEqualTo((ExistsAtZero, 0));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ToRequestJson_PutIfExistsOfANewEntityWithIgnoreVersion_IsLegalInEitherOrder(bool ignoreFirst)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var json = ignoreFirst ? tickets.Put(Held(0)).IgnoreVersion().IfExists().ToRequestJson() : tickets.Put(Held(0)).IfExists().IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Item":@ITEM,"ConditionExpression":"attribute_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"}}""", 0));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ToRequestJson_UpdateIfVersionZeroAndIfExists_ThrowsInEitherOrder(bool versionFirst)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        var key = Ticket.Keys.Of("t1", Day);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => _ = versionFirst ? tickets.Update(key).IfVersion(0).IfExists().ToRequestJson() : tickets.Update(key).IfExists().IfVersion(0).ToRequestJson());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(ExistsAtZero);
    }

    [Test]
    public async Task ToRequestJson_UpdateIfVersionAboveZeroAndIfExists_IsLegal()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(Ticket.Keys.Of("t1", Day)).IfVersion(3).IfExists().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("attribute_exists");
    }

    [Test]
    public async Task ToRequestJson_UpdateIgnoreVersionAndIfExists_IsLegal()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(Ticket.Keys.Of("t1", Day)).IgnoreVersion().IfExists().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("attribute_exists");
    }

    [Test]
    public async Task ToRequestJson_DeleteOfANewEntityIfExists_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => tickets.Delete(Held(0)).IfExists().ToRequestJson();

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>().WithMessage(ExistsAtZero, StringComparison.Ordinal);
    }

    [Test]
    public async Task ToRequestJson_DeleteOfANewEntityIfExistsWithIgnoreVersion_IsLegal()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Delete(Held(0)).IfExists().IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"ConditionExpression":"attribute_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"}}"""));
    }

    [Test]
    public async Task ToRequestJson_DeleteOfANewEntityWithAnotherVersionIfExists_IsLegal()
    {
        // Arrange (IfVersion replaces the version the entity gave)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Delete(Held(0)).IfVersion(3).IfExists().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("attribute_exists");
    }

    [Test]
    public async Task ToRequestJson_DeleteOfALoadedEntityIfExists_IsLegal()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Delete(Held(3)).IfExists().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("attribute_exists");
    }

    [Test]
    public async Task ToRequestJson_DeleteByKeyIfVersionZeroAndIfExists_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => tickets.Delete(Ticket.Keys.Of("t1", Day)).IfVersion(0).IfExists().ToRequestJson();

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>().WithMessage(ExistsAtZero, StringComparison.Ordinal);
    }

    [Test]
    public async Task ToRequestJson_TransactionPutIfExistsOfANewEntity_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => client.TransactWrite().Put(tickets, Held(0)).IfExists().Token("t").ToRequestJson();

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>().WithMessage(ExistsAtZero, StringComparison.Ordinal);
    }

    [Test]
    public async Task ToRequestJson_TransactionPutIfExistsOfANewEntityWithIgnoreVersion_IsLegal()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Put(client.Table<Ticket>("Shop"), Held(0)).IfExists().IgnoreVersion().Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("attribute_exists");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ToRequestJson_TransactionUpdateIfVersionZeroAndIfExists_ThrowsInEitherOrder(bool versionFirst)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        var key = Ticket.Keys.Of("t1", Day);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => _ = versionFirst
            ? client.TransactWrite().Update(tickets, key).IfVersion(0).IfExists().Token("t").ToRequestJson()
            : client.TransactWrite().Update(tickets, key).IfExists().IfVersion(0).Token("t").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(ExistsAtZero);
    }

    [Test]
    public async Task ToRequestJson_TransactionDeleteIfVersionZeroAndIfExists_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        var key = Ticket.Keys.Of("t1", Day);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => client.TransactWrite().Delete(tickets, key).IfVersion(0).IfExists().Token("t").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(ExistsAtZero);
    }

    [Test]
    public async Task ToRequestJson_TransactionCheckIfVersionZeroAndIfExists_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        var key = Ticket.Keys.Of("t1", Day);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => client.TransactWrite().Check(tickets, key).IfVersion(0).IfExists().Token("t").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(ExistsAtZero);
    }

    [Test]
    public async Task ToRequestJson_NewEntityInTheActionAfterAnIfExists_IsLegal()
    {
        // Arrange (what one action says of its item says nothing of the next)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var json = client.TransactWrite().Put(tickets, Held(3)).IfExists().Put(tickets, Held(0)).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("attribute_not_exists");
    }

    [Test]
    public async Task ToRequestJson_IfExistsOfAnEntityWithoutAVersion_IsLegal()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Put(new Customer("a", "Alice")).IfExists().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("attribute_exists");
    }

    [Test]
    public async Task ToRequestJson_ActionAfterAnUpdateIfVersion_KnowsNothingOfItsVersion()
    {
        // Arrange (a transaction goes on with the same state)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var json = client.TransactWrite()
            .Update(tickets, Ticket.Keys.Of("t1", new DateOnly(2026, 10, 31))).IfVersion(3)
            .Update(tickets, Ticket.Keys.Of("t2", new DateOnly(2026, 10, 31))).IfVersion(0).IfNotExists()
            .Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("attribute_not_exists");
    }

    [Test]
    public async Task ToRequestJson_PutIfNotExistsOfANewEntity_HasTheClauseOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Put(Held(0)).IfNotExists().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Item":@ITEM,"ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"}}""", 1));
    }

    [Test]
    public async Task ToRequestJson_TransactionPutIfNotExistsOfANewEntity_HasTheClauseOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Put(client.Table<Ticket>("Shop"), Held(0)).IfNotExists().Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TransactItems":[{"Put":{"TableName":"Shop","Item":@ITEM,"ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"}}}],"ClientRequestToken":"t"}""", 1));
    }

    [Test]
    public async Task ToRequestJson_TransactionUniqueOfANewEntity_HasTheClauseOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Unique(client.Table<Ticket>("Shop"), Held(0), out _).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TransactItems":[{"Put":{"TableName":"Shop","Item":@ITEM,"ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"}}}],"ClientRequestToken":"t"}""", 1));
    }

    [Test]
    public async Task ToRequestJson_TransactionPutOfANewEntity_RequiresNoItemUnderTheKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Put(client.Table<Ticket>("Shop"), Held(0)).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TransactItems":[{"Put":{"TableName":"Shop","Item":@ITEM,"ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"}}}],"ClientRequestToken":"t"}""", 1));
    }

    [Test]
    public async Task ToRequestJson_PutIgnoreVersionOfANewEntity_HasNoCondition()
    {
        // Arrange (this is how an item without a version attribute is taken over)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Put(Held(0)).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Item":@ITEM}""", 0));
    }

    [Test]
    public async Task ToRequestJson_TransactionPutIfNotExistsOfALoadedEntity_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => client.TransactWrite().Put(tickets, Held(3)).IfNotExists().ToRequestJson();

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>().WithMessage(LoadedAndNew, StringComparison.Ordinal);
    }

    [Test]
    public async Task Put_ActionAfterAPutIfNotExistsOfALoadedEntity_ThrowsAtTheEndOfThatAction()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => client.TransactWrite().Put(tickets, Held(3)).IfNotExists().Put(tickets, Held(0)).ToRequestJson();

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>().WithMessage(LoadedAndNew, StringComparison.Ordinal);
    }

    [Test]
    public async Task ToRequestJson_TransactionUniqueOfALoadedEntity_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => client.TransactWrite().Unique(tickets, Held(3), out _).ToRequestJson();

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>().WithMessage(LoadedAndNew, StringComparison.Ordinal);
    }

    [Test]
    public async Task ToRequestJson_LoadedEntityInTheActionAfterAnIfNotExists_IsLegal()
    {
        // Arrange (what one action says of its item says nothing of the next)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var json = client.TransactWrite().Put(tickets, Held(0)).IfNotExists().Put(tickets, Held(3)).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ConditionExpression":"#n0 = :v0"
            """);
    }

    [Test]
    public async Task ToRequestJson_PutIgnoreVersion_WritesTheEntityAsItIsWithoutACondition()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Put(Held(3)).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Item":@ITEM}""", 3));
    }
}
