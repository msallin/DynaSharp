using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping.KeyEntities;
using static DynaSharp.UnitTests.Fluent.KeyInputChangesFixture;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Keys of literals, which an update writes beside an input or for an index of literals, as bodies written by hand from <c>api.md</c> section 8 and the requirements.</summary>
public sealed class KeyInputChangesLiteralTests
{
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
}
