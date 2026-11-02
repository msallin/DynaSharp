using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping.KeyEntities;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>One attribute that several keys write, as a template and as a plain key, as bodies written by hand from <c>api.md</c> section 8 and the requirements.</summary>
public sealed class KeyInputChangesSharedAttributeTests
{
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
}
