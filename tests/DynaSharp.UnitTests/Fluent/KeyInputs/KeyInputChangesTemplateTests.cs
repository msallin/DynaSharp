// The run-time check of a key left half done is under test here, so the updates do on purpose what DYNA226 reports.
#pragma warning disable DYNA226
using DynaSharp.Serialization;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping;
using static DynaSharp.UnitTests.Fluent.KeyInputChangesFixture;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>What a typed update of a property that feeds a key does to the template of that key, as bodies written by hand from <c>api.md</c> section 8 and the requirements. The entities are in <see cref="Ticket"/>, <see cref="WideKey"/>, <see cref="HalfPlain"/> and <c>Mapping/KeyModels.cs</c>.</summary>
public sealed class KeyInputChangesTemplateTests
{
    [Test]
    public async Task ToRequestJson_SetOfTheOnlyInputOfATemplate_WritesTheDerivedAttributeInTheSameSet()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(TicketKey).IgnoreVersion().Set(Ticket.Attrs.State, TicketState.Closed).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = if_not_exists(#n2, :v2)","ExpressionAttributeNames":{"#n0":"State","#n1":"bystatepk","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"S":"Closed"},":v1":{"S":"STATE#Closed"},":v2":{"S":"TICKET"}}}"""));
    }

    [Test]
    public async Task ToRequestJson_SetOfEveryInputOfATemplate_FormatsThemInTheOrderOfTheTemplate()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(TicketKey).IgnoreVersion().Set(Ticket.Attrs.Queue, "vip").Set(Ticket.Attrs.Rank, 7).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = :v2, #n3 = if_not_exists(#n3, :v3)","ExpressionAttributeNames":{"#n0":"Queue","#n1":"Rank","#n2":"bystatesk","#n3":"_type"},"ExpressionAttributeValues":{":v0":{"S":"vip"},":v1":{"N":"7"},":v2":{"S":"007-vip"},":v3":{"S":"TICKET"}}}"""));
    }

    [Test]
    public async Task ToRequestJson_SetOfSomeInputsOfATemplate_ThrowsNamingItAndWhatIsMissing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => tickets.Update(TicketKey).IgnoreVersion().Set(Ticket.Attrs.Rank, 7).ToRequestJson());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(
            "This update of DynaSharp.UnitTests.Fluent.Ticket sets Rank, and not Queue, of the key template {Rank:d3}-{Queue}, so the attribute bystatesk cannot be written and the index would go stale. " +
            "DynamoDB cannot build a key from what the item holds: add .Set(Ticket.Attrs.Queue, value) with the value the item holds, or use a raw path, which is not checked.");
    }

    [Test]
    public async Task SendAsync_SetOfSomeInputsOfATemplate_ThrowsAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await tickets.Update(TicketKey).IgnoreVersion().Set(Ticket.Attrs.Queue, "vip").SendAsync());

        // Assert
        await Assert.That((failure is not null, handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task ToRequestJson_SetOfTheInputOfALocalIndex_WritesItsSortKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(TicketKey).IgnoreVersion().Set(Ticket.Attrs.Title, "new").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ExpressionAttributeNames":{"#n0":"Title","#n1":"bytitlesk","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"S":"new"},":v1":{"S":"TITLE#new"},":v2":{"S":"TICKET"}}
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfAPlainKeyOfAnIndex_WritesNothingElse()
    {
        // Arrange (the attribute is the key)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Purchase>("Shop").Update(new StoredKey<Purchase>("a", 1)).IgnoreVersion().Set(Purchase.Attrs.Status, Status.Closed).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1)","ExpressionAttributeNames":{"#n0":"Status","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"S":"Closed"},":v1":{"S":"PURCHASE"}}
            """);
    }

    [Test]
    public async Task ToRequestJson_AddToAnInput_ThrowsBecauseTheValueIsNotKnown()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => tickets.Update(TicketKey).IgnoreVersion().Add(Ticket.Attrs.Rank, 1).ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("{Rank:d3}-{Queue}").And.Contains("Add or SetIfNotExists");
    }

    [Test]
    public async Task ToRequestJson_SetIfNotExistsOfAnInput_ThrowsBecauseTheValueIsNotKnown()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => tickets.Update(TicketKey).IgnoreVersion().SetIfNotExists(Ticket.Attrs.State, TicketState.Open).ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ToRequestJson_HandlesOfOtherAttributes_LeaveTheKeysAlone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(TicketKey).IgnoreVersion().Add(Ticket.Attrs.Votes, 1).SetIfNotExists(Ticket.Attrs.Note, "n").Remove(Ticket.Attrs.Place).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n1 = if_not_exists(#n1, :v1), #n3 = if_not_exists(#n3, :v2) REMOVE #n2 ADD #n0 :v0"
            """);
    }

    [Test]
    public async Task ToRequestJson_RawPathOfAnInput_IsTheEscapeHatch()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(TicketKey).IgnoreVersion().Set("Rank", 7).Remove("Owner").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0, #n2 = if_not_exists(#n2, :v1) REMOVE #n1","ExpressionAttributeNames":{"#n0":"Rank","#n1":"Owner","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"N":"7"},":v1":{"S":"TICKET"}}
            """);
    }

    [Test]
    public async Task ToRequestJson_NestedPathThatEndsLikeAnInput_IsNoInput()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var nested = Ticket.Attrs.Place.At(new Attr<string, StringConverter>("Owner"));

        // Act
        var json = client.Table<Ticket>("Shop").Update(TicketKey).IgnoreVersion().Set(nested, "x").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0.#n1 = :v0, #n2 = if_not_exists(#n2, :v1)"
            """);
    }

    [Test]
    public async Task ToRequestJson_UpdateThatReturnsAnAttribute_WritesTheDerivedAttribute()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(TicketKey).IgnoreVersion().Set(Ticket.Attrs.Title, "new").Return(Ticket.Attrs.Title).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("bytitlesk");
    }

    [Test]
    public async Task ToRequestJson_TransactionUpdate_WritesTheDerivedAttributeInItsOwnAction()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var json = client.TransactWrite()
            .Update(tickets, TicketKey).IgnoreVersion().Set(Ticket.Attrs.State, TicketState.Closed)
            .Update(tickets, Ticket.Keys.Of("t2", Day)).IgnoreVersion().Add(Ticket.Attrs.Votes, 1)
            .Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TransactItems":[{"Update":{"TableName":"Shop","Key":@KEY,"UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = if_not_exists(#n2, :v2)","ExpressionAttributeNames":{"#n0":"State","#n1":"bystatepk","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"S":"Closed"},":v1":{"S":"STATE#Closed"},":v2":{"S":"TICKET"}}}},{"Update":{"TableName":"Shop","Key":{"pk":{"S":"TICKET#t2"},"sk":{"S":"AT#2026-10-31"}},"UpdateExpression":"SET #n1 = if_not_exists(#n1, :v1) ADD #n0 :v0","ExpressionAttributeNames":{"#n0":"Votes","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"N":"1"},":v1":{"S":"TICKET"}}}}],"ClientRequestToken":"t"}"""));
    }

    [Test]
    public async Task ToRequestJson_TransactionRemoveOfAnInput_RemovesTheDerivedAttributes()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var json = client.TransactWrite().Update(tickets, TicketKey).IfVersion(2).Remove(Ticket.Attrs.Owner).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v1, #n4 = if_not_exists(#n4, :v2) REMOVE #n1, #n2, #n3"
            """);
    }

    [Test]
    public async Task Update_TransactionActionWithSomeInputs_ThrowsAtTheVerbThatEndsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => client.TransactWrite().Update(tickets, TicketKey).IgnoreVersion().Set(Ticket.Attrs.Rank, 1).Update(tickets, Ticket.Keys.Of("t2", Day)).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ToRequestJson_InvalidValueOfAnInput_ThrowsKeyFormatException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var act = () => tickets.Update(TicketKey).IgnoreVersion().Set(Ticket.Attrs.Title, string.Empty).ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<KeyFormatException>();
    }

    [Test]
    public async Task ToRequestJson_ManyInputs_FormatsAllOfThem()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<WideKey>("Shop").Update(new StoredKey<WideKey>("x"))
            .Set(WideKey.Attrs.F, "6").Set(WideKey.Attrs.E, "5").Set(WideKey.Attrs.D, "4").Set(WideKey.Attrs.C, "3").Set(WideKey.Attrs.B, "2").Set(WideKey.Attrs.A, "1").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            {"S":"1-2-3-4-5-6"}
            """);
    }

    [Test]
    public async Task ToRequestJson_TemplateWithAnInputThatOnlyAKeyHolds_ThrowsNamingIt()
    {
        // Arrange (no handle can set it, so a raw path is the way)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var wide = client.Table<WideKey>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => wide.Update(new StoredKey<WideKey>("x")).Set(WideKey.Attrs.G, "7").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("and not Secret,").And.Contains("Use a Put of the changed entity, or a raw path").And.DoesNotContain("Set all");
    }

    [Test]
    public async Task ToRequestJson_HandleOfAnotherEntityOnATableWithoutTemplates_IsNoInput()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Update("a", "b").Set(Ticket.Attrs.Rank, 7).Remove(Ticket.Attrs.Owner).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0 REMOVE #n1","ExpressionAttributeNames":{"#n0":"Rank","#n1":"Owner"}
            """);
    }

    [Test]
    public async Task ToRequestJson_HandWrittenTemplateOfNoIndex_WritesItAndNeverRemovesIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<HandTemplated>("Shop").Update(new StoredKey<HandTemplated>("x")).Set(HandTemplated.Tag, "a").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = if_not_exists(#n2, :v2)","ExpressionAttributeNames":{"#n0":"tag","#n1":"derived","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"S":"a"},":v1":{"S":"T#a"},":v2":{"S":"HAND"}}
            """);
    }

    [Test]
    public async Task SendAsync_NextCommandOnTheState_KnowsNothingOfTheInputsBefore()
    {
        // Arrange (the state goes back to the pool)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        handler.Enqueue(StubResponse.Json("{}"));
        await tickets.Update(TicketKey).IgnoreVersion().Set(Ticket.Attrs.State, TicketState.Closed).SendAsync();

        // Act
        var json = tickets.Update(TicketKey).IgnoreVersion().Set(Ticket.Attrs.Note, "n").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ExpressionAttributeNames":{"#n0":"Note","#n1":"_type"}
            """);
    }
}
