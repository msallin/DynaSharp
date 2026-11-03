using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// The other key of a sparse index that is the stored text of an attribute, which an update copies, <c>SET key = attribute</c>, instead of asking for the value, as bodies written by hand from
/// <c>api.md</c> section 8. DynamoDB evaluates the copy on the item, so an item without the attribute fails the update, see the integration tests.
/// </summary>
public sealed class KeyInputChangesCopyTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ToRequestJson_SetOfOneKeyOfASparseIndexWhoseOtherIsAString_CopiesTheString()
    {
        // Arrange (a restore of a soft delete with the owner outside the key of the table)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Copier>("Shop").Update(Copier.Keys.Of("c1")).Set(Copier.Attrs.LiveSince, Noon).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"COPIER#c1"}},"UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = #n3, #n4 = if_not_exists(#n4, :v2)","ExpressionAttributeNames":{"#n0":"LiveSince","#n1":"byownersk","#n2":"byownerpk","#n3":"owner","#n4":"_type"},"ExpressionAttributeValues":{":v0":{"S":"2026-11-01T12:00:00.0000000Z"},":v1":{"S":"LIVE#2026-11-01T12:00:00.0000000Z"},":v2":{"S":"COPIER"}}}""");
    }

    [Test]
    public async Task ToRequestJson_SetOfOneKeyOfASparseIndexWhoseOtherIsAnEnum_CopiesTheName()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Copier>("Shop").Update(Copier.Keys.Of("c1")).Set(Copier.Attrs.Mark, "m").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = #n3, #n4 = if_not_exists(#n4, :v2)","ExpressionAttributeNames":{"#n0":"Mark","#n1":"bylevelpk","#n2":"bylevelsk","#n3":"Level","#n4":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfOneKeyOfASparseIndexWhoseOtherIsATimestamp_CopiesTheTimestamp()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Copier>("Shop").Update(Copier.Keys.Of("c1")).Set(Copier.Attrs.Tag, "t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            #n2 = #n3, #n4 = if_not_exists(#n4, :v2)","ExpressionAttributeNames":{"#n0":"Tag","#n1":"byatpk","#n2":"byatsk","#n3":"At","#n4":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfOneKeyOfASparseIndexWhoseOtherIsAGuid_CopiesTheGuid()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Copier>("Shop").Update(Copier.Keys.Of("c1")).Set(Copier.Attrs.RefMark, "r").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            #n2 = #n3, #n4 = if_not_exists(#n4, :v2)","ExpressionAttributeNames":{"#n0":"RefMark","#n1":"byrefsk","#n2":"byrefpk","#n3":"Ref","#n4":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfOneKeyOfASparseIndexWhoseOtherIsADate_CopiesTheDate()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Copier>("Shop").Update(Copier.Keys.Of("c1")).Set(Copier.Attrs.DayMark, "d").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            #n2 = #n3, #n4 = if_not_exists(#n4, :v2)","ExpressionAttributeNames":{"#n0":"DayMark","#n1":"bydaysk","#n2":"bydaypk","#n3":"Day","#n4":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfOneKeyOfASparseIndexWhoseOtherPadsANumber_ThrowsNamingTheSet()
    {
        // Arrange (a number is stored as N and the key holds six digits of text, so nothing can be copied)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var copiers = client.Table<Copier>("Shop");

        // Act
#pragma warning disable DYNA226 // The other key of the index is left out on purpose.
        var failure = Assert.Throws<InvalidOperationException>(() => copiers.Update(Copier.Keys.Of("c1")).Set(Copier.Attrs.CountMark, "c").ToRequestJson());
#pragma warning restore DYNA226

        // Assert
        await Assert.That(failure.Message).EndsWith("Add .Set(Copier.Attrs.Count, value) with the value the item holds, so that it writes bycountpk too.");
    }

    [Test]
    public async Task ToRequestJson_SetOfTheInputOfTheCopiedKey_WritesItFromTheValue()
    {
        // Arrange (the copy is only for an input the update does not set)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Copier>("Shop").Update(Copier.Keys.Of("c1")).Set(Copier.Attrs.LiveSince, Noon).Set(Copier.Attrs.Owner, "ann").Set(Copier.Attrs.Due, new DateOnly(2026, 11, 2)).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = :v2, #n3 = :v1, #n4 = :v3, #n5 = :v4, #n6 = if_not_exists(#n6, :v5)","ExpressionAttributeNames":{"#n0":"LiveSince","#n1":"owner","#n2":"Due","#n3":"byownerpk","#n4":"byownersk","#n5":"byduesk","#n6":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfOneKeyWhileTheCopiedInputIsRemoved_LeavesTheIndex()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Copier>("Shop").Update(Copier.Keys.Of("c1")).Set(Copier.Attrs.LiveSince, Noon).Remove(Copier.Attrs.Owner).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0, #n5 = if_not_exists(#n5, :v1) REMOVE #n1, #n2, #n3, #n4"
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfOneKeyOfEachOfTwoIndexesThatShareTheCopiedKey_CopiesItOnce()
    {
        // Arrange (DynamoDB rejects two actions on one path)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Copier>("Shop").Update(Copier.Keys.Of("c1")).Set(Copier.Attrs.LiveSince, Noon).Set(Copier.Attrs.Due, new DateOnly(2026, 11, 2)).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = :v2, #n3 = :v3, #n4 = #n5, #n6 = if_not_exists(#n6, :v4)","ExpressionAttributeNames":{"#n0":"LiveSince","#n1":"Due","#n2":"byownersk","#n3":"byduesk","#n4":"byownerpk","#n5":"owner","#n6":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_CopiedKeyThatARawPathWrites_LeavesItToTheRawPath()
    {
        // Arrange (an action of the caller on the attribute takes precedence, as for the discriminator)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Copier>("Shop").Update(Copier.Keys.Of("c1")).Set(Copier.Attrs.LiveSince, Noon).Set("byownerpk", "ann").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = :v2, #n3 = if_not_exists(#n3, :v3)","ExpressionAttributeNames":{"#n0":"LiveSince","#n1":"byownerpk","#n2":"byownersk","#n3":"_type"}
            """);
    }

    [Test]
    public async Task Update_TransactionActionWithOneKeyOfASparseIndex_CopiesTheOther()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var copiers = client.Table<Copier>("Shop");

        // Act
        var json = client.TransactWrite().Update(copiers, Copier.Keys.Of("c1")).Set(Copier.Attrs.LiveSince, Noon).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            #n2 = #n3, #n4 = if_not_exists(#n4, :v2)"
            """);
    }
}
