using DynaSharp.Serialization;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Fluent.KeyInputChangesFixture;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Inputs that only the key of the table holds, read back from a generated key or a raw one, and the rule that a key of the table does not change, as bodies written by hand from <c>api.md</c> section 8 and the requirements.</summary>
public sealed class KeyInputChangesTableKeyTests
{
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
