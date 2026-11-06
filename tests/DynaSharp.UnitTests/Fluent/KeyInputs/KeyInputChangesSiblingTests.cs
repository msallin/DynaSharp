// The run-time check of a key left half done is under test here, so the updates do on purpose what DYNA226 reports.
#pragma warning disable DYNA226
using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping.KeyEntities;
using static DynaSharp.UnitTests.Fluent.KeyInputChangesFixture;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>The other key of a sparse index, without which an update of one key would leave the item half in the index, as bodies written by hand from <c>api.md</c> section 8 and the requirements.</summary>
public sealed class KeyInputChangesSiblingTests
{
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
        await Assert.That(failure.Message).IsEqualTo(
            "This update of DynaSharp.UnitTests.Fluent.Ticket sets Owner, of the key template OWNER#{Owner} of the sparse index ByOwner, and does not produce byownersk, the other key of that index. " +
            "An update does not read the item, so it cannot tell whether the item holds byownersk, and an item without it would stay out of the index without an error. " +
            "Add .Set(Ticket.Attrs.Due, value) with the value the item holds, so that it writes byownersk too.");
    }

    [Test]
    public async Task ToRequestJson_SetOfOneKeyOfASparseIndexWhoseOtherHasTwoInputs_ThrowsNamingBothSets()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var claims = client.Table<Claim>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => claims.Update(Claim.Keys.Of("c1")).Set(Claim.Attrs.Agent, "ann").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).EndsWith("Add .Set(Claim.Attrs.Floor, value) and .Set(Claim.Attrs.Desk, value) with the values the item holds, so that it writes byagentsk too.");
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
        var failure = Assert.Throws<InvalidOperationException>(() => _ = client.TransactWrite().Update(tickets, TicketKey).IgnoreVersion().Set(Ticket.Attrs.Owner, "ann").Delete(tickets, new StoredKey<Ticket>("a", "b")));

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
        var json = client.Table<Claim>("Shop").Update(Claim.Keys.Of("c1")).Set(Claim.Attrs.Handler, "h").ToRequestJson();

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
        var failure = Assert.Throws<InvalidOperationException>(() => claims.Update(new StoredKey<Claim>("OTHER#c1")).Set(Claim.Attrs.Handler, "h").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("does not produce bycasesk,").And.Contains("Use a Put of the changed entity, or a raw path").And.DoesNotContain("Set what");
    }

    [Test]
    public async Task ToRequestJson_SiblingOnAnIndexThatIsNotSparseOnAKeyThatFitsNoTemplate_LeavesItAlone()
    {
        // Arrange (the key is on every item a Put wrote, so this update has nothing to add)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<OtherSlot>("Shop").Update(new StoredKey<OtherSlot>("ROOM#r1", "DAY#2026-11-02")).Set(OtherSlot.Attrs.Owner, "ann").ToRequestJson();

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
        var failure = Assert.Throws<InvalidOperationException>(() => devices.Update(new StoredKey<PlainDevice>("d1")).Set(PlainDevice.Attrs.Rack, "r").Set(keyInput, "d2").ToRequestJson());

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
        var json = client.Table<PlainDevice>("Shop").Update(new StoredKey<PlainDevice>("d1")).Set(PlainDevice.Attrs.Rack, "r").ToRequestJson();

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
    public async Task ToRequestJson_SiblingTheMetadataDoesNotDescribe_Throws()
    {
        // Arrange (only a type written by hand can leave a key of an index without a template)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var table = client.Table<HandSparse>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => table.Update(new StoredKey<HandSparse>("x")).Set(HandSparse.Tag, "a").ToRequestJson());

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
        var json = client.Table<PlainDevice>("Shop").Update(new StoredKey<PlainDevice>("d1")).Set(PlainDevice.Attrs.Shelf, "s").ToRequestJson();

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
        var json = client.Table<NickName>("Shop").Update(new StoredKey<NickName>("x")).Set(NickName.Attrs.Nick, "n").Set(NickName.Attrs.Age, 30).ToRequestJson();

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
        var failure = Assert.Throws<InvalidOperationException>(() => sales.Update(new StoredKey<GatedSale>("x")).Set(GatedSale.Attrs.Tenant, "t").Set(GatedSale.Attrs.Seller, "s").ToRequestJson());

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
}
