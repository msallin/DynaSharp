using DynaSharp.Mapping;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping;
using DynaSharp.UnitTests.Mapping.KeyEntities;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>What <see cref="VersionAttribute"/> does to every write path, as bodies written by hand from <c>api.md</c> section 8.</summary>
public sealed class WriteRulesTests
{
    private const string Key = """{"pk":{"S":"TICKET#t1"},"sk":{"S":"AT#2026-10-31"}}""";

    private const string LoadedAndNew =
        "This write of DynaSharp.UnitTests.Fluent.Ticket holds a version above 0 and asks that no item has its key, which can never both hold: an item that does not exist has no version. For a first write use version 0, to replace an item leave out IfNotExists, and to write the entity as it is call IgnoreVersion().";

    private const string ExistsAtZero =
        "This write of DynaSharp.UnitTests.Fluent.Ticket holds version 0 and asks that an item has its key, which can never both hold: version 0 means that no item has the key. For a first write leave out IfExists, for an item that exists use the version last read, and to write without looking at the version call IgnoreVersion().";

    private static readonly DateOnly Day = new(2026, 10, 31);

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
    public async Task IgnoreVersion_InTheActionAfterOneWithIfVersion_IsLegal()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var json = client.TransactWrite().Update(tickets, Ticket.Keys.Of("t1", Day)).IfVersion(3).Update(tickets, Ticket.Keys.Of("t2", Day)).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("TICKET#t2");
    }

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
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Item":@ITEM,"ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"}}""", 3));
    }

    [Test]
    public async Task ToRequestJson_PutIgnoreVersionTwice_WritesTheItemOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Put(Held(3)).IgnoreVersion().IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Item":@ITEM}""", 3));
    }

    [Test]
    public async Task ToRequestJson_PutIgnoreVersionOfAnItemAboveTheBufferOfTheWriter_WritesItOnce()
    {
        // Arrange (the writer hands such an item to the body while it is being written)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var note = new string('x', 100_000);

        // Act
        var json = client.Table<Ticket>("Shop").Put(Held(3) with { Note = note }).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(client.Table<Ticket>("Shop").BatchWrite().Put(Held(3) with { Note = note }).IgnoreVersion().ToRequestJson()[..^4].Replace("""{"RequestItems":{"Shop":[{"PutRequest":{"Item":""", """{"TableName":"Shop","Item":""", StringComparison.Ordinal));
    }

    [Test]
    public async Task ToRequestJson_PutIgnoreVersionOfAStructEntity_WritesItsVersion()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Gauge>("Shop").Put(new Gauge { Id = "g", Revision = 3 }).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"GAUGE#g"},"_type":{"S":"GAUGE"},"Id":{"S":"g"},"Revision":{"N":"3"}}}""");
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
        await Assert.That((result.Version, ticket.Revision)).IsEqualTo(((long?)3L, 3));
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
        var json = client.Table<Customer>("Shop").Update(Customer.Keys.Of("a")).Set("x", 1).IgnoreVersion().ToRequestJson();

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
    public async Task ToRequestJson_TransactionPutIgnoreVersion_WritesTheEntityAsItIsWithoutACondition()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Put(client.Table<Ticket>("Shop"), Held(3)).IgnoreVersion().Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TransactItems":[{"Put":{"TableName":"Shop","Item":@ITEM}}],"ClientRequestToken":"t"}""", 3));
    }

    [Test]
    public async Task ToRequestJson_TransactionPutIgnoreVersion_HoldsForItsOwnStep()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var json = client.TransactWrite().Put(tickets, Held(3)).Put(tickets, Held(5)).IgnoreVersion().Put(tickets, Held(7)).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill(
            """{"TransactItems":[{"Put":{"TableName":"Shop","Item":@ITEM,"ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"3"}}}},""", 4)
            + Fill("""{"Put":{"TableName":"Shop","Item":@ITEM}},""", 5)
            + Fill("""{"Put":{"TableName":"Shop","Item":@ITEM,"ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"7"}}}}],"ClientRequestToken":"t"}""", 8));
    }

    [Test]
    public async Task ToRequestJson_TransactionIgnoreVersionOfAStepAfterAPut_LeavesThePutAlone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var json = client.TransactWrite().Put(tickets, Held(3)).Delete(tickets, Ticket.Keys.Of("t2", Day)).IgnoreVersion().Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).StartsWith(Fill("""{"TransactItems":[{"Put":{"TableName":"Shop","Item":@ITEM,"ConditionExpression":"#n0 = :v0",""", 4));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ToRequestJson_TransactionStepByKeyAfterAPut_StillOwesItsVersion(bool ignoreOnThePut)
    {
        // Arrange (the version of the entity of a put says nothing about the next action)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => ignoreOnThePut
            ? client.TransactWrite().Put(tickets, Held(3)).IgnoreVersion().Delete(tickets, Ticket.Keys.Of("t2", Day)).ToRequestJson()
            : client.TransactWrite().Put(tickets, Held(3)).Delete(tickets, Ticket.Keys.Of("t2", Day)).ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ToRequestJson_UniqueWithAnEntityAndIgnoreVersion_KeepsTheConditionOnTheKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Unique(client.Table<Ticket>("Shop"), Held(3), out _).IgnoreVersion().Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TransactItems":[{"Put":{"TableName":"Shop","Item":@ITEM,"ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"}}}],"ClientRequestToken":"t"}""", 3));
    }

    [Test]
    public async Task ToRequestJson_TransactionUpdateIfVersion_ChecksItAndStoresTheNext()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Update(client.Table<Ticket>("Shop"), Ticket.Keys.Of("t1", Day)).IfVersion(3).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TransactItems":[{"Update":{"TableName":"Shop","Key":@KEY,"UpdateExpression":"SET #n0 = :v1, #n1 = if_not_exists(#n1, :v2)","ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"Revision","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"N":"3"},":v1":{"N":"4"},":v2":{"S":"TICKET"}}}}],"ClientRequestToken":"t"}"""));
    }

    [Test]
    public async Task ToRequestJson_TransactionDeleteIfVersion_ChecksIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Delete(client.Table<Ticket>("Shop"), Ticket.Keys.Of("t1", Day)).IfVersion(3).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TransactItems":[{"Delete":{"TableName":"Shop","Key":@KEY,"ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"3"}}}}],"ClientRequestToken":"t"}"""));
    }

    [Test]
    public async Task ToRequestJson_TransactionCheckIfVersion_ChecksIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Check(client.Table<Ticket>("Shop"), Ticket.Keys.Of("t1", Day)).IfVersion(0).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TransactItems":[{"ConditionCheck":{"TableName":"Shop","Key":@KEY,"ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"}}}],"ClientRequestToken":"t"}"""));
    }

    [Test]
    public async Task ToRequestJson_TransactionCheckWithoutAVersion_NeedsNoVerb()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var json = client.TransactWrite().Check(tickets, Ticket.Keys.Of("t1", Day)).IfExists().Check(tickets, "a", "b").IfExists().Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("ConditionCheck");
    }

    [Test]
    public async Task ToRequestJson_TransactionUpdateIgnoreVersion_LeavesTheVersionAlone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Update(client.Table<Ticket>("Shop"), Ticket.Keys.Of("t1", Day)).Set("Note", "x").IgnoreVersion().Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TransactItems":[{"Update":{"TableName":"Shop","Key":@KEY,"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1)","ExpressionAttributeNames":{"#n0":"Note","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"S":"x"},":v1":{"S":"TICKET"}}}}],"ClientRequestToken":"t"}"""));
    }

    [Test]
    public async Task ToRequestJson_TransactionDeleteIgnoreVersion_HasNoCondition()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Delete(client.Table<Ticket>("Shop"), "a", "b").IgnoreVersion().Token("t").ToRequestJson();

        // Assert
        await Assert.That(json.Contains("Condition", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ToRequestJson_TransactionUpdateThatSaysNothingAboutTheVersion_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => client.TransactWrite().Update(tickets, "a", "b").Set("Note", "x").ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Put_TransactionActionBeforeItSaysNothingAboutTheVersion_ThrowsAtTheVerbThatEndsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => client.TransactWrite().Delete(tickets, Ticket.Keys.Of("t1", Day)).Put(tickets, Held(1)).ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Put_ConvertedActionThatSaysNothingAboutTheVersion_ThrowsAtTheVerbThatEndsIt()
    {
        // Arrange (the conversion ends what can be said, not what is owed)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            TransactWriteCommand command = client.TransactWrite().Delete(tickets, Ticket.Keys.Of("t1", Day));
            _ = command.Put(tickets, Held(1));
        });

        // Assert
        await Assert.That(failure.Message).Contains(nameof(Ticket));
    }

    [Test]
    public async Task SendAsync_ConvertedActionThatSaysNothingAboutTheVersion_ThrowsAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            TransactWriteCommand command = client.TransactWrite().Update(tickets, Ticket.Keys.Of("t1", Day)).Set("Note", "x");
            await command.SendAsync();
        });

        // Assert
        await Assert.That((failure!.Message.Contains(nameof(Ticket), StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task ToRequestJson_ConvertedActionWithItsVersion_WritesItAsAChainDoes()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        TransactWriteCommand command = client.TransactWrite().Delete(tickets, Ticket.Keys.Of("t1", Day)).IfVersion(3);
        var json = command.Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TransactItems":[{"Delete":{"TableName":"Shop","Key":@KEY,"ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"3"}}}}],"ClientRequestToken":"t"}"""));
    }

    [Test]
    public async Task SendAsync_TransactionDeleteThatSaysNothingAboutTheVersion_ThrowsAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.TransactWrite().Delete(tickets, Ticket.Keys.Of("t1", Day)).SendAsync());

        // Assert
        await Assert.That((failure!.Message.Contains(nameof(Ticket), StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task IfVersion_TransactionActionOnAnItem_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var act = () => client.TransactWrite().Update(shop, "a", "b").IfVersion(1).ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    [Arguments(-1L)]
    [Arguments(long.MaxValue)]
    public async Task IfVersion_TransactionUpdateWithNoVersionAnItemCanHold_Throws(long version)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => client.TransactWrite().Update(tickets, Ticket.Keys.Of("t1", Day)).IfVersion(version).ToRequestJson();

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(-1L)]
    [Arguments(long.MaxValue)]
    public async Task IfVersion_TransactionDeleteWithNoVersionAnItemCanHold_Throws(long version)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => client.TransactWrite().Delete(tickets, Ticket.Keys.Of("t1", Day)).IfVersion(version).ToRequestJson();

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>();
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

    [Test]
    public async Task ToRequestJson_BatchPutWithoutIgnoreVersion_ThrowsNamingTheEntity()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => tickets.BatchWrite().Put(Held(1)).ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains(nameof(Ticket));
    }

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

    private static string Stored(int revision) =>
        """{"pk":{"S":"TICKET#t1"},"sk":{"S":"AT#2026-10-31"},"bystatepk":{"S":"STATE#Open"},"bystatesk":{"S":"000-main"},"bytitlesk":{"S":"TITLE#untitled"},"_type":{"S":"TICKET"},"Id":{"S":"t1"},"OpenedAt":{"S":"2026-10-31"},"State":{"S":"Open"},"Queue":{"S":"main"},"Rank":{"N":"0"},"Title":{"S":"untitled"},"Votes":{"N":"0"},"Revision":{"N":"""
        + "\"" + revision.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"}}";

    private static Ticket Held(int revision) => new() { Id = "t1", OpenedAt = Day, Revision = revision };

    // A body with @KEY for the key and @ITEM for the stored item, whose braces would not survive an interpolated raw string.
    private static string Fill(string body, int revision = 0) => body.Replace("@KEY", Key, StringComparison.Ordinal).Replace("@ITEM", Stored(revision), StringComparison.Ordinal);

    private static void InvalidCall(UpdateCommand<Ticket> command, long version, out Exception? failure)
    {
        failure = null;
        try
        {
            _ = command.IfVersion(version);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            failure = exception;
        }
    }
}
