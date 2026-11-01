using DynaSharp.Serialization;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping;
using DynaSharp.UnitTests.Mapping.KeyEntities;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// What a typed update of a property that feeds a key does to that key, as bodies written by hand from <c>api.md</c> section 8 and the requirements. The entities are
/// in <see cref="Ticket"/>, <see cref="WideKey"/>, <see cref="HalfPlain"/> and <c>Mapping/KeyModels.cs</c>.
/// </summary>
public sealed class KeyInputChangesTests
{
    private const string Key = """{"pk":{"S":"TICKET#t1"},"sk":{"S":"AT#2026-10-31"}}""";

    private static readonly DateOnly Day = new(2026, 10, 31);

    private static readonly Ticket.Keys.PrimaryKey TicketKey = Ticket.Keys.Of("t1", Day);

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
        await Assert.That(failure.Message).Contains("{Rank:d3}-{Queue}").And.Contains("sets Rank, and not Queue,").And.Contains("Set all of them");
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
    public async Task ToRequestJson_SetOfBothKeysOfASparseIndex_WritesThemBoth()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(TicketKey).IgnoreVersion().Set(Ticket.Attrs.Owner, "ann").Set(Ticket.Attrs.Due, Day).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = :v2, #n3 = :v3, #n4 = if_not_exists(#n4, :v4)","ExpressionAttributeNames":{"#n0":"Owner","#n1":"Due","#n2":"byownerpk","#n3":"byownersk","#n4":"_type"},"ExpressionAttributeValues":{":v0":{"S":"ann"},":v1":{"S":"2026-10-31"},":v2":{"S":"OWNER#ann"},":v3":{"S":"DUE#2026-10-31"},":v4":{"S":"TICKET"}}}"""));
    }

    [Test]
    public async Task ToRequestJson_SetOfThePartitionKeyOfASparseIndexAlone_ThrowsNamingTheSortKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => tickets.Update(TicketKey).IgnoreVersion().Set(Ticket.Attrs.Owner, "ann").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("sets Owner, of the key template OWNER#{Owner} of the sparse index ByOwner, and does not produce byownersk,").And.Contains("stay out").And.Contains("Set what that key is made of");
    }

    [Test]
    public async Task ToRequestJson_SetOfTheSortKeyOfASparseIndexAlone_ThrowsNamingThePartitionKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => tickets.Update(TicketKey).IgnoreVersion().Set(Ticket.Attrs.Due, Day).ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("sets Due, of the key template DUE#{Due} of the sparse index ByOwner, and does not produce byownerpk,");
    }

    [Test]
    public async Task SendAsync_SetOfOneKeyOfASparseIndex_ThrowsAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await tickets.Update(TicketKey).IgnoreVersion().Set(Ticket.Attrs.Owner, "ann").SendAsync());

        // Assert
        await Assert.That((failure is not null, handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task Update_TransactionActionWithOneKeyOfASparseIndex_ThrowsAtTheVerbThatEndsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => _ = client.TransactWrite().Update(tickets, TicketKey).IgnoreVersion().Set(Ticket.Attrs.Owner, "ann").Delete(tickets, "a", "b"));

        // Assert
        await Assert.That(failure.Message).Contains("byownersk");
    }

    [Test]
    public async Task ToRequestJson_OneKeyOfASparseIndexByRawPath_IsNotChecked()
    {
        // Arrange (an update of an item that is in the index already, which is the escape hatch for it)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(TicketKey).IgnoreVersion().Set("Owner", "ann").Set("byownerpk", "OWNER#ann").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = if_not_exists(#n2, :v2)"
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfOneKeyWhileAnInputOfTheOtherIsRemoved_LeavesTheIndex()
    {
        // Arrange (the other way round than the remove of the owner: the update says that the item leaves)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(TicketKey).IgnoreVersion().Set(Ticket.Attrs.Owner, "ann").Remove(Ticket.Attrs.Due).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0, #n4 = if_not_exists(#n4, :v1) REMOVE #n1, #n2, #n3"
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfOneKeyWhileAnInputOfTheOtherIsSetToNull_LeavesTheIndex()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(TicketKey).IgnoreVersion().Set(Ticket.Attrs.Due, Day).Set(Ticket.Attrs.Owner, null!).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0, #n4 = if_not_exists(#n4, :v1) REMOVE #n1, #n2, #n3"
            """);
    }

    [Test]
    public async Task ToRequestJson_SiblingWithSomeInputs_NamesWhatIsMissingOfIt()
    {
        // Arrange (the rule of a template comes first, because it says more)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var claims = client.Table<Claim>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => claims.Update(Claim.Keys.Of("c1")).Set(Claim.Attrs.Agent, "a").Set(Claim.Attrs.Floor, 3).ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("sets Floor, and not Desk,");
    }

    [Test]
    public async Task ToRequestJson_SiblingWithAllInputs_WritesBothKeys()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Claim>("Shop").Update(Claim.Keys.Of("c1")).Set(Claim.Attrs.Agent, "a").Set(Claim.Attrs.Floor, 3).Set(Claim.Attrs.Desk, "d").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            {":v0":{"S":"a"},":v1":{"N":"3"},":v2":{"S":"d"},":v3":{"S":"AGENT#a"},":v4":{"S":"003-d"},":v5":{"S":"CLAIM"}}
            """);
    }

    [Test]
    public async Task ToRequestJson_SiblingMadeOfATableKeyInput_WritesItFromTheKey()
    {
        // Arrange (the sort key of ByCase repeats what the table key holds, which the update was given)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Claim>("Shop").Update(Claim.Keys.Of("c1")).Set(Claim.Attrs.Handler, "h").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"CLAIM#c1"}},"UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = :v2, #n3 = if_not_exists(#n3, :v3)","ExpressionAttributeNames":{"#n0":"Handler","#n1":"bycasepk","#n2":"bycasesk","#n3":"_type"},"ExpressionAttributeValues":{":v0":{"S":"h"},":v1":{"S":"HANDLER#h"},":v2":{"S":"CASE#c1"},":v3":{"S":"CLAIM"}}}""");
    }

    [Test]
    public async Task ToRequestJson_SiblingMadeOfATableKeyInputOnARawKey_WritesItFromTheParsedKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Claim>("Shop").Update("CLAIM#c1").Set(Claim.Attrs.Handler, "h").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            ":v2":{"S":"CASE#c1"}
            """);
    }

    [Test]
    public async Task ToRequestJson_SiblingOnAKeyThatFitsNoTemplate_ThrowsNamingThePutAndARawPath()
    {
        // Arrange (a sparse index would silently miss the item, and nothing can set an input of the table key)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var claims = client.Table<Claim>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => claims.Update("OTHER#c1").Set(Claim.Attrs.Handler, "h").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("does not produce bycasesk,").And.Contains("Use a Put of the changed entity, or a raw path").And.DoesNotContain("Set what");
    }

    [Test]
    public async Task ToRequestJson_SetBesideAnInputOfTheTableKey_DerivesTheIndexKeyFromTheGeneratedKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<TenantInvoice>("Billing").Update(TenantInvoice.Keys.Of("acme", "2026-09", 2)).Set(TenantInvoice.Attrs.Status, InvoiceState.Paid).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Billing","Key":{"pk":{"S":"TENANT#acme"},"sk":{"S":"INV#2026-09#000002"}},"UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = if_not_exists(#n2, :v2)","ExpressionAttributeNames":{"#n0":"Status","#n1":"bystatuspk","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"S":"Paid"},":v1":{"S":"TENANT#acme#STATUS#Paid"},":v2":{"S":"INVOICE"}}}""");
    }

    [Test]
    public async Task ToRequestJson_SetBesideAnInputOfTheTableKeyOnARawKey_DerivesTheIndexKeyFromTheParsedKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<TenantInvoice>("Billing").Update("TENANT#acme", "INV#2026-09#000002").Set(TenantInvoice.Attrs.Status, InvoiceState.Paid).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            ":v1":{"S":"TENANT#acme#STATUS#Paid"}
            """);
    }

    [Test]
    public async Task ToRequestJson_InputsOfBothKeysOfTheTable_ReadsEachFromItsKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<BothKeysInput>("Shop").Update("A#a", "B#b").Set(BothKeysInput.Attrs.Mark, "m").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            ":v1":{"S":"m-a-b"}
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfTheInputOfTheTableKey_ThrowsTheRuleOfTheTableKey()
    {
        // Arrange (what the update was given is no reason to change it)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var invoices = client.Table<TenantInvoice>("Billing");
        var keyInput = TenantInvoice.Attrs.TenantId;

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => invoices.Update(TenantInvoice.Keys.Of("acme", "2026-09", 2)).Set(TenantInvoice.Attrs.Status, InvoiceState.Paid).Set(keyInput, "acme").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("cannot update a key");
    }

    [Test]
    public async Task ToRequestJson_RawKeyThatFitsNoTemplate_ThrowsNamingThePutAndARawPath()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var invoices = client.Table<TenantInvoice>("Billing");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => invoices.Update("OTHER#acme", "INV#2026-09#000002").Set(TenantInvoice.Attrs.Status, InvoiceState.Paid).ToRequestJson());

        // Assert (setting the tenant would be the error of the table key, so the message cannot say that)
        await Assert.That(failure.Message).Contains("and not TenantId,").And.Contains("Use a Put of the changed entity, or a raw path").And.DoesNotContain("Set all");
    }

    [Test]
    public async Task ToRequestJson_RawKeyWithoutTheSortKeyThatHoldsTheInput_ThrowsNamingThePutAndARawPath()
    {
        // Arrange (DynamoDB rejects such a key, and it has nothing to say about the input)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var slots = client.Table<DaySlot>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => slots.Update("ROOM#r1").Set(DaySlot.Attrs.Owner, "ann").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("Use a Put of the changed entity, or a raw path");
    }

    [Test]
    public async Task ToRequestJson_RawKeyThatIsNoString_ThrowsNamingThePutAndARawPath()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var invoices = client.Table<TenantInvoice>("Billing");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => invoices.Update(1, "INV#2026-09#000002").Set(TenantInvoice.Attrs.Status, InvoiceState.Paid).ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("Use a Put of the changed entity, or a raw path");
    }

    [Test]
    public async Task ToRequestJson_TransactionStep_DerivesTheIndexKeyPerAction()
    {
        // Arrange (each action has its own key)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var invoices = client.Table<TenantInvoice>("Billing");

        // Act
        var json = client.TransactWrite()
            .Update(invoices, TenantInvoice.Keys.Of("acme", "2026-09", 2)).Set(TenantInvoice.Attrs.Status, InvoiceState.Paid)
            .Update(invoices, "TENANT#globex", "INV#2026-10#000003").Set(TenantInvoice.Attrs.Status, InvoiceState.Open)
            .Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TransactItems":[{"Update":{"TableName":"Billing","Key":{"pk":{"S":"TENANT#acme"},"sk":{"S":"INV#2026-09#000002"}},"UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = if_not_exists(#n2, :v2)","ExpressionAttributeNames":{"#n0":"Status","#n1":"bystatuspk","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"S":"Paid"},":v1":{"S":"TENANT#acme#STATUS#Paid"},":v2":{"S":"INVOICE"}}}},{"Update":{"TableName":"Billing","Key":{"pk":{"S":"TENANT#globex"},"sk":{"S":"INV#2026-10#000003"}},"UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = if_not_exists(#n2, :v2)","ExpressionAttributeNames":{"#n0":"Status","#n1":"bystatuspk","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"S":"Open"},":v1":{"S":"TENANT#globex#STATUS#Open"},":v2":{"S":"INVOICE"}}}}],"ClientRequestToken":"t"}""");
    }

    [Test]
    public async Task ToRequestJson_PlainTableKeyInATemplateOnARawKey_ReadsItThroughItsConverter()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<NumberedCounter>("Shop").Update(7).Set(NumberedCounter.Attrs.Kind, "hits").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            ":v1":{"S":"KIND#hits#0007"}
            """);
    }

    [Test]
    public async Task ToRequestJson_PlainTableKeyInATemplateOnAGeneratedKey_ReadsItFromTheKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<NumberedCounter>("Shop").Update(NumberedCounter.Keys.Of(7)).Set(NumberedCounter.Attrs.Kind, "hits").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            ":v1":{"S":"KIND#hits#0007"}
            """);
    }

    [Test]
    public async Task ToRequestJson_PlainTableKeyOfAnotherType_ThrowsNamingThePutAndARawPath()
    {
        // Arrange (a string where the key holds a number, which DynamoDB rejects, and which no converter reads)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var counters = client.Table<NumberedCounter>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => counters.Update("seven").Set(NumberedCounter.Attrs.Kind, "hits").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("Use a Put of the changed entity, or a raw path");
    }

    [Test]
    public async Task ToRequestJson_SetOfAPlainTableKey_ThrowsTheRuleOfTheTableKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var counters = client.Table<NumberedCounter>("Shop");
        var keyInput = NumberedCounter.Attrs.Id;

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => counters.Update(NumberedCounter.Keys.Of(7)).Set(keyInput, 8).ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("cannot update a key");
    }

    [Test]
    public async Task ToRequestJson_TimestampInTheSameFormatOnARawKey_DerivesTheIndexKeyFromTheParsedKey()
    {
        // Arrange (the key holds the minute, and the index key has no use for more)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<RoomSlot>("Shop").Update("ROOM#r1", "AT#2026-11-02T10:00").Set(RoomSlot.Attrs.Owner, "ann").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ExpressionAttributeValues":{":v0":{"S":"ann"},":v1":{"S":"OWNER#ann"},":v2":{"S":"2026-11-02T10:00"},":v3":{"S":"SLOT"}}
            """);
    }

    [Test]
    public async Task ToRequestJson_TimestampAKeyHoldsInPartOnAGeneratedKey_TakesItFromTheKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var start = new DateTimeOffset(2026, 11, 2, 10, 30, 0, TimeSpan.Zero);

        // Act
        var json = client.Table<DaySlot>("Shop").Update(DaySlot.Keys.Of("r1", start)).Set(DaySlot.Attrs.Owner, "ann").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            ":v1":{"S":"OWNER#ann#AT#2026-11-02T10:30:00.0000000Z"}
            """);
    }

    [Test]
    public async Task ToRequestJson_TimestampAKeyHoldsInPartInATransactionStep_TakesItFromTheKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var slots = client.Table<DaySlot>("Shop");
        var start = new DateTimeOffset(2026, 11, 2, 10, 30, 0, TimeSpan.Zero);

        // Act
        var json = client.TransactWrite().Update(slots, DaySlot.Keys.Of("r1", start), out _).Set(DaySlot.Attrs.Owner, "ann").Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            ":v1":{"S":"OWNER#ann#AT#2026-11-02T10:30:00.0000000Z"}
            """);
    }

    [Test]
    public async Task ToRequestJson_TimestampAKeyHoldsInPartOnARawKey_ThrowsNamingThePutAndARawPath()
    {
        // Arrange (the day does not say what the index wants)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var slots = client.Table<DaySlot>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => slots.Update("ROOM#r1", "DAY#2026-11-02").Set(DaySlot.Attrs.Owner, "ann").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("Use a Put of the changed entity, or a raw path");
    }

    [Test]
    public async Task ToRequestJson_RawKeyAfterAGeneratedOneInATransaction_KnowsNothingOfTheKeyBefore()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var slots = client.Table<DaySlot>("Shop");
        var start = new DateTimeOffset(2026, 11, 2, 10, 30, 0, TimeSpan.Zero);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => client.TransactWrite()
            .Update(slots, DaySlot.Keys.Of("r1", start)).Set(DaySlot.Attrs.Owner, "ann")
            .Update(slots, "ROOM#r2", "DAY#2026-11-02").Set(DaySlot.Attrs.Owner, "bob")
            .Token("t").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("Use a Put of the changed entity, or a raw path");
    }

    [Test]
    public async Task ToRequestJson_KeyWrittenByHand_ThrowsNamingThePutAndARawPath()
    {
        // Arrange (a key of this kind writes itself and holds no value it could give)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var slots = client.Table<DaySlot>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => slots.Update(default(HandKey)).Set(DaySlot.Attrs.Owner, "ann").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("Use a Put of the changed entity, or a raw path");
    }

    [Test]
    public async Task ToRequestJson_SiblingOnAnIndexThatIsNotSparseOnAKeyThatFitsNoTemplate_LeavesItAlone()
    {
        // Arrange (the key is on every item a Put wrote, so this update has nothing to add)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<OtherSlot>("Shop").Update("ROOM#r1", "DAY#2026-11-02").Set(OtherSlot.Attrs.Owner, "ann").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ExpressionAttributeNames":{"#n0":"Owner","#n1":"byownerpk","#n2":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_SiblingOfASetKeyWithATableKeyInputOnAGeneratedKey_WritesIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var start = new DateTimeOffset(2026, 11, 2, 10, 30, 0, TimeSpan.Zero);

        // Act
        var json = client.Table<OtherSlot>("Shop").Update(OtherSlot.Keys.Of("r1", start)).Set(OtherSlot.Attrs.Owner, "ann").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ExpressionAttributeNames":{"#n0":"Owner","#n1":"byownerpk","#n2":"byownersk","#n3":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_SiblingOfASetOfAnInputOfATableKey_ThrowsTheRuleOfTheTableKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var claims = client.Table<Claim>("Shop");
        var keyInput = Claim.Attrs.Id;

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => claims.Update(Claim.Keys.Of("c1")).Set(Claim.Attrs.Handler, "h").Set(keyInput, "c2").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("cannot update a key");
    }

    [Test]
    public async Task ToRequestJson_SiblingMadeOfAPlainTableKeyThatIsSet_ThrowsTheRuleOfTheTableKey()
    {
        // Arrange (DynamoDB would reject the SET, and the index repeats that key)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var devices = client.Table<PlainDevice>("Shop");
        var keyInput = PlainDevice.Attrs.Serial;

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => devices.Update("d1").Set(PlainDevice.Attrs.Rack, "r").Set(keyInput, "d2").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("cannot update a key");
    }

    [Test]
    public async Task ToRequestJson_SiblingMadeOfAPlainTableKey_WritesItFromTheKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<PlainDevice>("Shop").Update("d1").Set(PlainDevice.Attrs.Rack, "r").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ExpressionAttributeNames":{"#n0":"Rack","#n1":"byrackpk","#n2":"byracksk","#n3":"_type"},"ExpressionAttributeValues":{":v0":{"S":"r"},":v1":{"S":"RACK#r"},":v2":{"S":"SERIAL#d1"},":v3":{"S":"PLAIN"}}
            """);
    }

    [Test]
    public async Task ToRequestJson_SiblingWithAnInputOnlyAKeyHolds_ThrowsNamingThePutAndARawPath()
    {
        // Arrange (no handle can set it, so the message cannot ask for that)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var claims = client.Table<Claim>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => claims.Update(Claim.Keys.Of("c1")).Set(Claim.Attrs.Auditor, "a").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("does not produce byauditsk,").And.Contains("Use a Put of the changed entity, or a raw path").And.DoesNotContain("Set what");
    }

    [Test]
    public async Task ToRequestJson_SiblingOfLiterals_WritesItInTheSameSet()
    {
        // Arrange (the attribute of the literals sorts before the key that was set, as the metadata lists them)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Claim>("Shop").Update(Claim.Keys.Of("c1")).Set(Claim.Attrs.ClosedOn, Day).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"CLAIM#c1"}},"UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = :v0, #n3 = if_not_exists(#n3, :v2)","ExpressionAttributeNames":{"#n0":"ClosedOn","#n1":"byclosedpk","#n2":"byclosedsk","#n3":"_type"},"ExpressionAttributeValues":{":v0":{"S":"2026-10-31"},":v1":{"S":"CLOSED"},":v2":{"S":"CLAIM"}}}""");
    }

    [Test]
    public async Task ToRequestJson_RemoveBesideALiteralKey_RemovesTheLiteralKeyToo()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Claim>("Shop").Update(Claim.Keys.Of("c1")).Remove(Claim.Attrs.ClosedOn).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n3 = if_not_exists(#n3, :v0) REMOVE #n0, #n1, #n2","ExpressionAttributeNames":{"#n0":"ClosedOn","#n1":"byclosedpk","#n2":"byclosedsk","#n3":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_NullableInputWithAValue_WritesTheKeysAsASetDoes()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        DateOnly? closedOn = Day;

        // Act
        var json = client.Table<Claim>("Shop").Update(Claim.Keys.Of("c1")).Set(Claim.Attrs.ClosedOn, closedOn).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"CLAIM#c1"}},"UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = :v0, #n3 = if_not_exists(#n3, :v2)","ExpressionAttributeNames":{"#n0":"ClosedOn","#n1":"byclosedpk","#n2":"byclosedsk","#n3":"_type"},"ExpressionAttributeValues":{":v0":{"S":"2026-10-31"},":v1":{"S":"CLOSED"},":v2":{"S":"CLAIM"}}}""");
    }

    [Test]
    public async Task ToRequestJson_NullableInputWithoutAValue_RemovesTheKeysAsARemoveDoes()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        DateOnly? closedOn = null;

        // Act
        var json = client.Table<Claim>("Shop").Update(Claim.Keys.Of("c1")).Set(Claim.Attrs.ClosedOn, closedOn).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n3 = if_not_exists(#n3, :v0) REMOVE #n0, #n1, #n2","ExpressionAttributeNames":{"#n0":"ClosedOn","#n1":"byclosedpk","#n2":"byclosedsk","#n3":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_NullableInputOfAnIndexThatIsNotSparseWithoutAValue_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        int? rank = null;

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => tickets.Update(TicketKey).IgnoreVersion().Set(Ticket.Attrs.Rank, rank).ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("removes Rank,").And.Contains("is not sparse");
    }

    [Test]
    public async Task ToRequestJson_NullableInputOfATableKey_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        DateOnly? openedAt = Day;
        var keyInput = Ticket.Attrs.OpenedAt;

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => tickets.Update(TicketKey).IgnoreVersion().Set(keyInput, openedAt).ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("DynamoDB cannot update a key");
    }

    [Test]
    public async Task ToRequestJson_InputOfAnotherIndex_LeavesTheLiteralKeyAlone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Claim>("Shop").Update(Claim.Keys.Of("c1")).Set(Claim.Attrs.Agent, "a").Set(Claim.Attrs.Floor, 1).Set(Claim.Attrs.Desk, "d").ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("byclosedpk");
    }

    [Test]
    public async Task ToRequestJson_NoInputAtAll_LeavesTheLiteralKeyAlone()
    {
        // Arrange (the rule of keys runs only for an update of an input, so a raw path is not checked)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Claim>("Shop").Update(Claim.Keys.Of("c1")).Set("ClosedOn", "2026-10-31").ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("byclosedpk");
    }

    [Test]
    public async Task ToRequestJson_SiblingOfLiteralsOnAnIndexThatIsNotSparse_WritesItInTheSameSet()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<FeaturedPost>("Shop").Update(FeaturedPost.Keys.Of("p1")).Set(FeaturedPost.Attrs.Title, "Hello").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"POST#p1"}},"UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = :v0, #n3 = if_not_exists(#n3, :v2)","ExpressionAttributeNames":{"#n0":"Title","#n1":"everythingpk","#n2":"everythingsk","#n3":"_type"},"ExpressionAttributeValues":{":v0":{"S":"Hello"},":v1":{"S":"ALL"},":v2":{"S":"POST"}}}""");
    }

    [Test]
    public async Task ToRequestJson_InputOfAnotherIndex_LeavesALiteralKeyOfAnIndexThatIsNotSparseAlone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<FeaturedPost>("Shop").Update(FeaturedPost.Keys.Of("p1")).Set(FeaturedPost.Attrs.FeaturedOn, Day).ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("everything");
    }

    [Test]
    public async Task ToRequestJson_NoInputOfAnIndexWithAKeyLeft_LeavesItsLiteralKeyAlone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var outsideTheEntity = "Views";

        // Act
        var json = client.Table<FeaturedPost>("Shop").Update(FeaturedPost.Keys.Of("p1")).Set(outsideTheEntity, 1).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"POST#p1"}},"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1)","ExpressionAttributeNames":{"#n0":"Views","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"N":"1"},":v1":{"S":"POST"}}}""");
    }

    [Test]
    public async Task ToRequestJson_IndexOfLiterals_WritesItOnAnUpdateThatDoesNotTouchIt()
    {
        // Arrange (the discriminator stays last)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Notice>("Shop").Update(Notice.Keys.Of("n1")).Set(Notice.Attrs.Note, "x").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"NOTICE#n1"}},"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1), #n2 = if_not_exists(#n2, :v2), #n3 = if_not_exists(#n3, :v3), #n4 = if_not_exists(#n4, :v4)","ExpressionAttributeNames":{"#n0":"Note","#n1":"boardpk","#n2":"boardsk","#n3":"everyonepk","#n4":"_type"},"ExpressionAttributeValues":{":v0":{"S":"x"},":v1":{"S":"NOTICES"},":v2":{"S":"ANY"},":v3":{"S":"EVERYONE"},":v4":{"S":"NOTICE"}}}""");
    }

    [Test]
    public async Task ToRequestJson_LiteralBesideAKeyOfTheTable_CountsAsAnIndexOfLiterals()
    {
        // Arrange (a key of the table is on every item, so no property can bring an item into such an index)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<LabeledNote>("Shop").Update(LabeledNote.Keys.Of("n1")).Set(LabeledNote.Attrs.Note, "x").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"LABEL#n1"},"sk":{"S":"DATA"}},"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1), #n2 = if_not_exists(#n2, :v2), #n3 = if_not_exists(#n3, :v3), #n4 = if_not_exists(#n4, :v4)","ExpressionAttributeNames":{"#n0":"Note","#n1":"onpartitionsk","#n2":"onsortpk","#n3":"bylabelsk","#n4":"_type"},"ExpressionAttributeValues":{":v0":{"S":"x"},":v1":{"S":"ANY"},":v2":{"S":"EVERY"},":v3":{"S":"ALL"},":v4":{"S":"LABELED"}}}""");
    }

    [Test]
    public async Task ToRequestJson_IndexOfLiteralsAndAnUpdateWithoutActions_WritesIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Notice>("Shop").Update(Notice.Keys.Of("n1")).IfExists().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n1 = if_not_exists(#n1, :v0), #n2 = if_not_exists(#n2, :v1), #n3 = if_not_exists(#n3, :v2), #n4 = if_not_exists(#n4, :v3)"
            """);
    }

    [Test]
    public async Task ToRequestJson_IndexOfLiteralsBesideAKeyThatFollowsAnInput_WritesEach()
    {
        // Arrange (the sibling of an input is a plain SET, and only the index of literals goes by if_not_exists)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Notice>("Shop").Update(Notice.Keys.Of("n1")).Set(Notice.Attrs.Text, "x").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = :v0, #n3 = if_not_exists(#n3, :v2), #n4 = if_not_exists(#n4, :v3), #n5 = if_not_exists(#n5, :v4), #n6 = if_not_exists(#n6, :v5)","ExpressionAttributeNames":{"#n0":"Text","#n1":"bytextpk","#n2":"bytextsk","#n3":"boardpk","#n4":"boardsk","#n5":"everyonepk","#n6":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_ActionOnAKeyOfLiterals_TakesPrecedence()
    {
        // Arrange (DynamoDB rejects two actions on one path)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Notice>("Shop").Update(Notice.Keys.Of("n1")).Remove("boardpk").Set("everyonepk", "OTHER").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"NOTICE#n1"}},"UpdateExpression":"SET #n1 = :v0, #n2 = if_not_exists(#n2, :v1), #n3 = if_not_exists(#n3, :v2) REMOVE #n0","ExpressionAttributeNames":{"#n0":"boardpk","#n1":"everyonepk","#n2":"boardsk","#n3":"_type"},"ExpressionAttributeValues":{":v0":{"S":"OTHER"},":v1":{"S":"ANY"},":v2":{"S":"NOTICE"}}}""");
    }

    [Test]
    public async Task ToRequestJson_IndexOfLiteralsInATransaction_WritesItForThatActionOnly()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var notices = client.Table<Notice>("Shop");
        var shop = client.Table("Shop", "pk");

        // Act
        var json = client.TransactWrite().Update(notices, Notice.Keys.Of("n1")).Set(Notice.Attrs.Note, "x").Update(shop, "a").Set("b", 1).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TransactItems":[{"Update":{"TableName":"Shop","Key":{"pk":{"S":"NOTICE#n1"}},"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1), #n2 = if_not_exists(#n2, :v2), #n3 = if_not_exists(#n3, :v3), #n4 = if_not_exists(#n4, :v4)","ExpressionAttributeNames":{"#n0":"Note","#n1":"boardpk","#n2":"boardsk","#n3":"everyonepk","#n4":"_type"},"ExpressionAttributeValues":{":v0":{"S":"x"},":v1":{"S":"NOTICES"},":v2":{"S":"ANY"},":v3":{"S":"EVERYONE"},":v4":{"S":"NOTICE"}}}},{"Update":{"TableName":"Shop","Key":{"pk":{"S":"a"}},"UpdateExpression":"SET #n0 = :v0","ExpressionAttributeNames":{"#n0":"b"},"ExpressionAttributeValues":{":v0":{"N":"1"}}}}],"ClientRequestToken":"t"}""");
    }

    [Test]
    public async Task ToRequestJson_HandlesOnATableWithoutASchema_LeaveTheIndexOfLiteralsAlone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Update("a").Set(Notice.Attrs.Note, "x").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"a"}},"UpdateExpression":"SET #n0 = :v0","ExpressionAttributeNames":{"#n0":"Note"},"ExpressionAttributeValues":{":v0":{"S":"x"}}}""");
    }

    [Test]
    public async Task ToRequestJson_KeyWithoutInputsOrAFormat_IsNoLiteralOfTheMetadata()
    {
        // Arrange (only a type written by hand can say that a key has no inputs and that nothing formats it)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<HandLiteral>("Shop").Update("x").Set("a", 1).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"x"}},"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1)","ExpressionAttributeNames":{"#n0":"a","#n1":"written"},"ExpressionAttributeValues":{":v0":{"N":"1"},":v1":{"S":"FIXED"}}}""");
    }

    [Test]
    public async Task ToRequestJson_SiblingTheMetadataDoesNotDescribe_Throws()
    {
        // Arrange (only a type written by hand can leave a key of an index without a template)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var table = client.Table<HandSparse>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => table.Update("x").Set(HandSparse.Tag, "a").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("does not produce other,");
    }

    [Test]
    public async Task ToRequestJson_SiblingThatIsATemplatedKeyOfTheTable_IsOnEveryItem()
    {
        // Arrange (ByMark sorts by the sort key of the table, in the same attribute)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<TableSibling>("Shop").Update(TableSibling.Keys.Of("a", "b")).Set(TableSibling.Attrs.Mark, "m").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ExpressionAttributeNames":{"#n0":"Mark","#n1":"bymarkpk","#n2":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_SiblingThatIsAPlainKeyOfTheTable_IsOnEveryItem()
    {
        // Arrange (ByRack shares the plain partition key of the table)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<PlainDevice>("Shop").Update("d1").Set(PlainDevice.Attrs.Shelf, "s").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ExpressionAttributeNames":{"#n0":"Shelf","#n1":"byshelfpk","#n2":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfTheSortKeyOfASparseLocalIndex_NeedsNothingOfThePartitionKey()
    {
        // Arrange (the partition key of a local index is the one of the table)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<TableSibling>("Shop").Update(TableSibling.Keys.Of("a", "b")).Set(TableSibling.Attrs.Nick, "n").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ExpressionAttributeNames":{"#n0":"Nick","#n1":"bynicksk","#n2":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfAPartitionKeyOfASparseIndexWithoutASortKey_NeedsNoSibling()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<NickName>("Shop").Update("x").Set(NickName.Attrs.Nick, "n").Set(NickName.Attrs.Age, 30).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ExpressionAttributeNames":{"#n0":"Nick","#n1":"Age","#n2":"shared","#n3":"bynickandagesk","#n4":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfAnAttributeTwoSparseIndexesShare_NeedsTheOtherKeyOfEach()
    {
        // Arrange (the shared key alone brings the item into neither index)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var sales = client.Table<GatedSale>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => sales.Update("x").Set(GatedSale.Attrs.Tenant, "t").Set(GatedSale.Attrs.Seller, "s").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("sparse index ByBuyer").And.Contains("does not produce bybuyersk,");
    }

    [Test]
    public async Task ToRequestJson_SetOfANonSparseIndexKeyAlone_NeedsNoSibling()
    {
        // Arrange (the other key is on every item already, and it is a template of its own)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(TicketKey).IgnoreVersion().Set(Ticket.Attrs.State, TicketState.Closed).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("bystatepk");
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
    public async Task ToRequestJson_RemoveOfAnInputOfASparseIndex_RemovesTheDerivedAttributesOfTheIndex()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(TicketKey).IgnoreVersion().Remove(Ticket.Attrs.Owner).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"UpdateExpression":"SET #n3 = if_not_exists(#n3, :v0) REMOVE #n0, #n1, #n2","ExpressionAttributeNames":{"#n0":"Owner","#n1":"byownerpk","#n2":"byownersk","#n3":"_type"},"ExpressionAttributeValues":{":v0":{"S":"TICKET"}}}"""));
    }

    [Test]
    public async Task ToRequestJson_SetToNullOfAnInputOfASparseIndex_IsARemove()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(TicketKey).IgnoreVersion().Set(Ticket.Attrs.Owner, null!).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"UpdateExpression":"SET #n3 = if_not_exists(#n3, :v0) REMOVE #n0, #n1, #n2","ExpressionAttributeNames":{"#n0":"Owner","#n1":"byownerpk","#n2":"byownersk","#n3":"_type"},"ExpressionAttributeValues":{":v0":{"S":"TICKET"}}}"""));
    }

    [Test]
    public async Task ToRequestJson_RemoveOfOneInputAndSetOfTheOther_LeavesTheIndexAndWritesNoKey()
    {
        // Arrange (a key written and a key removed in one update would overlap)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(TicketKey).IgnoreVersion().Remove(Ticket.Attrs.Owner).Set(Ticket.Attrs.Due, Day).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n1 = :v0, #n4 = if_not_exists(#n4, :v1) REMOVE #n0, #n2, #n3","ExpressionAttributeNames":{"#n0":"Owner","#n1":"Due","#n2":"byownerpk","#n3":"byownersk","#n4":"_type"},"ExpressionAttributeValues":{":v0":{"S":"2026-10-31"},":v1":{"S":"TICKET"}}
            """);
    }

    [Test]
    public async Task ToRequestJson_RemoveOfAnInputOfAnIndexThatIsNotSparse_ThrowsNamingTheIndex()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => tickets.Update(TicketKey).IgnoreVersion().Remove(Ticket.Attrs.State).ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("ByState").And.Contains("STATE#{State}");
    }

    [Test]
    public async Task ToRequestJson_RemoveOfAnInputOfAPlainKeyOfAnIndexThatIsNotSparse_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var purchases = client.Table<Purchase>("Shop");

        // Act
        var act = () => purchases.Update("a", 1).IgnoreVersion().Remove(Purchase.Attrs.Status).ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ToRequestJson_SetOfAPlainKeyOfAnIndex_WritesNothingElse()
    {
        // Arrange (the attribute is the key)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Purchase>("Shop").Update("a", 1).IgnoreVersion().Set(Purchase.Attrs.Status, Status.Closed).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1)","ExpressionAttributeNames":{"#n0":"Status","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"S":"Closed"},":v1":{"S":"PURCHASE"}}
            """);
    }

    [Test]
    [Arguments("Id")]
    [Arguments("OpenedAt")]
    public async Task ToRequestJson_SetOfAnInputOfATableKey_ThrowsNamingTheTemplate(string input)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        var (id, openedAt) = (Ticket.Attrs.Id, Ticket.Attrs.OpenedAt);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => _ = input == "Id"
            ? tickets.Update(TicketKey).IgnoreVersion().Set(id, "other").ToRequestJson()
            : tickets.Update(TicketKey).IgnoreVersion().Set(openedAt, Day).ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains(input == "Id" ? "TICKET#{Id}" : "AT#{OpenedAt}").And.Contains("cannot update a key");
    }

    [Test]
    public async Task ToRequestJson_RemoveOfAnInputOfATableKey_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Shop");
        var keyInput = Ticket.Attrs.Id;

        // Act
        var act = () => tickets.Update(TicketKey).IgnoreVersion().Remove(keyInput).ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
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
        var json = client.Table<WideKey>("Shop").Update("x")
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
        var failure = Assert.Throws<InvalidOperationException>(() => wide.Update("x").Set(WideKey.Attrs.G, "7").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("and not Secret,").And.Contains("Use a Put of the changed entity, or a raw path").And.DoesNotContain("Set all");
    }

    [Test]
    public async Task ToRequestJson_SetOfAnAttributeTwoIndexesShare_WritesItOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<GatedSale>("Shop").Update("x").Set(GatedSale.Attrs.Tenant, "t").Set(GatedSale.Attrs.Seller, "s").Set(GatedSale.Attrs.Buyer, "b").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = :v2, #n3 = :v3, #n4 = :v4, #n5 = :v5, #n6 = if_not_exists(#n6, :v6)","ExpressionAttributeNames":{"#n0":"Tenant","#n1":"Seller","#n2":"Buyer","#n3":"shared","#n4":"bysellersk","#n5":"bybuyersk","#n6":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_RemoveOfTheGateOfOneOfTwoIndexes_KeepsTheAttributeTheOtherNeeds()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<GatedSale>("Shop").Update("x").Remove(GatedSale.Attrs.Seller).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n2 = if_not_exists(#n2, :v0) REMOVE #n0, #n1","ExpressionAttributeNames":{"#n0":"Seller","#n1":"bysellersk","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"S":"GATED"}}
            """);
    }

    [Test]
    public async Task ToRequestJson_RemoveOfTheGatesOfBothIndexes_RemovesTheSharedAttribute()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<GatedSale>("Shop").Update("x").Remove(GatedSale.Attrs.Seller).Remove(GatedSale.Attrs.Buyer).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ExpressionAttributeNames":{"#n0":"Seller","#n1":"Buyer","#n2":"shared","#n3":"bysellersk","#n4":"bybuyersk","#n5":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_RemoveOfAGateBesideAnIndexWithoutOne_KeepsTheSharedAttribute()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<LooseGate>("Shop").Update("x").Remove(LooseGate.Attrs.Note).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n2 = if_not_exists(#n2, :v0) REMOVE #n0, #n1","ExpressionAttributeNames":{"#n0":"Note","#n1":"gatedpk","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"S":"LOOSE"}}
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfAPlainKeyBesideATemplate_WritesOnlyTheAttribute()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<HalfPlain>("Shop").Update("x").Set(HalfPlain.Attrs.Mark, "m").Set(HalfPlain.Attrs.Tag, "t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = :v2, #n3 = if_not_exists(#n3, :v3)","ExpressionAttributeNames":{"#n0":"Mark","#n1":"Tag","#n2":"halfpk","#n3":"_type"},"ExpressionAttributeValues":{":v0":{"S":"m"},":v1":{"S":"t"},":v2":{"S":"H#t"},":v3":{"S":"HALF"}}
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfAPlainKeyOfASparseIndexAlone_ThrowsNamingTheTemplate()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var halves = client.Table<HalfPlain>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => halves.Update("x").Set(HalfPlain.Attrs.Mark, "m").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("does not produce halfpk,");
    }

    [Test]
    public async Task ToRequestJson_SetOfATemplateBesideAPlainKeyAlone_ThrowsNamingThePlainKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var halves = client.Table<HalfPlain>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => halves.Update("x").Set(HalfPlain.Attrs.Tag, "t").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("does not produce Mark,");
    }

    [Test]
    public async Task ToRequestJson_RemoveOfAPlainKeyBesideATemplate_RemovesTheTemplateToo()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<HalfPlain>("Shop").Update("x").Remove(HalfPlain.Attrs.Mark).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n2 = if_not_exists(#n2, :v0) REMOVE #n0, #n1","ExpressionAttributeNames":{"#n0":"Mark","#n1":"halfpk","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"S":"HALF"}}
            """);
    }

    [Test]
    public async Task ToRequestJson_RemoveOfATemplateInputBesideAPlainKey_LeavesThePlainKeyAlone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<HalfPlain>("Shop").Update("x").Remove(HalfPlain.Attrs.Tag).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n2 = if_not_exists(#n2, :v0) REMOVE #n0, #n1","ExpressionAttributeNames":{"#n0":"Tag","#n1":"halfpk","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"S":"HALF"}}
            """);
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
        var json = client.Table<HandTemplated>("Shop").Update("x").Set(HandTemplated.Tag, "a").ToRequestJson();

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

    private static string Fill(string body) => body.Replace("@KEY", Key, StringComparison.Ordinal);

    private readonly struct HandKey : DynaSharp.Mapping.IKey<DaySlot>
    {
        public void WriteKey(DynamoDbWriter writer)
        {
            writer.WriteAttributeName("pk"u8);
            writer.WriteString("ROOM#r1"u8);
            writer.WriteAttributeName("sk"u8);
            writer.WriteString("DAY#2026-11-02"u8);
        }
    }
}
