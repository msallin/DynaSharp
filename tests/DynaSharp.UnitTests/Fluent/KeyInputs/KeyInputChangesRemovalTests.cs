using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping;
using static DynaSharp.UnitTests.Fluent.KeyInputChangesFixture;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>What the removal of an input does to the keys derived from it, and a null, which is a removal, as bodies written by hand from <c>api.md</c> section 8 and the requirements.</summary>
public sealed class KeyInputChangesRemovalTests
{
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
    public async Task ToRequestJson_RemoveOfAnInputOfASparseIndex_RemovesTheDerivedAttributesOfTheIndex()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Ticket>("Shop").Update(TicketKey).IgnoreVersion().Remove(Ticket.Attrs.Owner).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"UpdateExpression":"SET #n3 = if_not_exists(#n3, :v0), #n4 = if_not_exists(#n4, :v1) + :v2 REMOVE #n0, #n1, #n2","ExpressionAttributeNames":{"#n0":"Owner","#n1":"byownerpk","#n2":"byownersk","#n3":"_type","#n4":"Revision"},"ExpressionAttributeValues":{":v0":{"S":"TICKET"},":v1":{"N":"0"},":v2":{"N":"1"}}}"""));
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
        await Assert.That(json).IsEqualTo(Fill("""{"TableName":"Shop","Key":@KEY,"UpdateExpression":"SET #n3 = if_not_exists(#n3, :v0), #n4 = if_not_exists(#n4, :v1) + :v2 REMOVE #n0, #n1, #n2","ExpressionAttributeNames":{"#n0":"Owner","#n1":"byownerpk","#n2":"byownersk","#n3":"_type","#n4":"Revision"},"ExpressionAttributeValues":{":v0":{"S":"TICKET"},":v1":{"N":"0"},":v2":{"N":"1"}}}"""));
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
            "UpdateExpression":"SET #n1 = :v0, #n4 = if_not_exists(#n4, :v1), #n5 = if_not_exists(#n5, :v2) + :v3 REMOVE #n0, #n2, #n3","ExpressionAttributeNames":{"#n0":"Owner","#n1":"Due","#n2":"byownerpk","#n3":"byownersk","#n4":"_type","#n5":"Revision"},"ExpressionAttributeValues":{":v0":{"S":"2026-10-31"},":v1":{"S":"TICKET"},":v2":{"N":"0"},":v3":{"N":"1"}}
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
        var act = () => purchases.Update(new StoredKey<Purchase>("a", 1)).IgnoreVersion().Remove(Purchase.Attrs.Status).ToRequestJson();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }
}
