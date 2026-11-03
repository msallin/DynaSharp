using DynaSharp.Mapping;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Fluent.WriteRulesFixture;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>What <see cref="VersionAttribute"/> asks of each action of a transaction, which settles action by action, as bodies written by hand from <c>api.md</c> section 8.</summary>
public sealed class WriteRulesTransactionTests
{
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
    public async Task ToRequestJson_BatchPutWithoutIgnoreVersion_ThrowsNamingTheEntity()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
#pragma warning disable DYNA236 // The batch throws on purpose.
        var failure = Assert.Throws<InvalidOperationException>(() => tickets.BatchWrite().Put(Held(1)).ToRequestJson());
#pragma warning restore DYNA236

        // Assert
        await Assert.That(failure.Message).Contains(nameof(Ticket));
    }
}
