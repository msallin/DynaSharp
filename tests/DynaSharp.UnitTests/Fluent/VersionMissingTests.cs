using DynaSharp.Mapping;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>What <see cref="VersionMissing.Allow"/> changes on every write path, as bodies written by hand from <c>api.md</c> section 8.</summary>
public sealed class VersionMissingTests
{
    private const string Key = """{"pk":{"S":"PARCEL#p1"}}""";

    private const string VersionWrittenWithoutOne = "The item has no version attribute \"Revision\", so Select cannot read the version of Ticket: the item was written without one. A table that keeps such items declares [Version(Missing = VersionMissing.Allow)], which reads them as version 0.";

    private const string ReadsAsZero = """
        "ConditionExpression":"(attribute_not_exists(#n0) OR #n0 = :v0)","ExpressionAttributeNames":{"#n0":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"0"}}
        """;

    private const string KeyIsMissing = """
        "ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"
        """;

    private const string ItemExistsAtZero = """
        "ConditionExpression":"(attribute_exists(#n0)) AND (attribute_not_exists(#n1) OR #n1 = :v0)","ExpressionAttributeNames":{"#n0":"pk","#n1":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"0"}}
        """;

    private static Parcel New => new() { Id = "p1" };

    private static Parcel.Keys.PrimaryKey ParcelKey => Parcel.Keys.Of("p1");

    [Test]
    public async Task Missing_NotSet_IsReject()
    {
        // Arrange, Act
        var attribute = new VersionAttribute();

        // Assert
        await Assert.That(attribute.Missing).IsEqualTo(VersionMissing.Reject);
    }

    [Test]
    public async Task Info_GeneratedEntityThatAllowsIt_SaysSo()
    {
        // Arrange, Act
        var (allowed, rejected) = (InfoOf<Parcel>(), InfoOf<Ticket>());

        // Assert
        await Assert.That((allowed.VersionMissing, allowed.VersionAttributeName, rejected.VersionMissing)).IsEqualTo((VersionMissing.Allow, (string?)"Revision", VersionMissing.Reject));
    }

    [Test]
    public async Task ToRequestJson_PutAtZero_LooksAtTheVersionAttributeAndStoresTheFirst()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Parcel>("Shop").Put(New).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"PARCEL#p1"},"_type":{"S":"PARCEL"},"Id":{"S":"p1"},"Revision":{"N":"1"}},"ConditionExpression":"(attribute_not_exists(#n0) OR #n0 = :v0)","ExpressionAttributeNames":{"#n0":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"0"}}}""");
    }

    [Test]
    public async Task ToRequestJson_PutAboveZero_IsTheOrdinaryCheck()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Parcel>("Shop").Put(New with { Revision = 3 }).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"3"}}
            """);
    }

    [Test]
    public async Task ToRequestJson_UpdateIfVersionZero_LooksAtTheVersionAttributeAndStoresTheFirst()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Parcel>("Shop").Update(ParcelKey).IfVersion(0).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"PARCEL#p1"}},"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v2)","ConditionExpression":"(attribute_not_exists(#n0) OR #n0 = :v1)","ExpressionAttributeNames":{"#n0":"Revision","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"N":"1"},":v1":{"N":"0"},":v2":{"S":"PARCEL"}}}""");
    }

    [Test]
    public async Task ToRequestJson_DeleteByKeyIfVersionZero_LooksAtTheVersionAttribute()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Parcel>("Shop").Delete(ParcelKey).IfVersion(0).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo($$$"""{"TableName":"Shop","Key":{{{Key}}},{{{ReadsAsZero}}}}""");
    }

    [Test]
    public async Task ToRequestJson_DeleteOfAnEntityAtZero_LooksAtTheVersionAttribute()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Parcel>("Shop").Delete(New).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo($$$"""{"TableName":"Shop","Key":{{{Key}}},{{{ReadsAsZero}}}}""");
    }

    [Test]
    public async Task ToRequestJson_TransactionPutAtZero_LooksAtTheVersionAttribute()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Put(client.Table<Parcel>("Shop"), New).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains(ReadsAsZero);
    }

    [Test]
    public async Task ToRequestJson_TransactionUpdateIfVersionZero_LooksAtTheVersionAttribute()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Update(client.Table<Parcel>("Shop"), ParcelKey).IfVersion(0).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ConditionExpression":"(attribute_not_exists(#n0) OR #n0 = :v1)","ExpressionAttributeNames":{"#n0":"Revision"
            """);
    }

    [Test]
    public async Task ToRequestJson_TransactionDeleteIfVersionZero_LooksAtTheVersionAttribute()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Delete(client.Table<Parcel>("Shop"), ParcelKey).IfVersion(0).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains(ReadsAsZero);
    }

    [Test]
    public async Task ToRequestJson_TransactionCheckIfVersionZero_LooksAtTheVersionAttribute()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Check(client.Table<Parcel>("Shop"), ParcelKey).IfVersion(0).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo($$$"""{"TransactItems":[{"ConditionCheck":{"TableName":"Shop","Key":{{{Key}}},{{{ReadsAsZero}}}}}],"ClientRequestToken":"t"}""");
    }

    [Test]
    public async Task ToRequestJson_PutIfNotExistsAtZero_AsksForTheKeyAlone()
    {
        // Arrange (the caller asked for a free key, which is more than an item without a version)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Parcel>("Shop").Put(New).IfNotExists().ToRequestJson();

        // Assert
        await Assert.That(json).Contains(KeyIsMissing + "}");
    }

    [Test]
    public async Task ToRequestJson_TransactionPutIfNotExistsAtZero_AsksForTheKeyAlone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Put(client.Table<Parcel>("Shop"), New).IfNotExists().Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains(KeyIsMissing + "}");
    }

    [Test]
    public async Task ToRequestJson_UniqueAtZero_AsksForTheKeyAlone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Unique(client.Table<Parcel>("Shop"), New, out _).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains(KeyIsMissing + "}");
    }

    [Test]
    [Arguments(true, "#n1")]
    [Arguments(false, "#n0")]
    public async Task ToRequestJson_UpdateIfVersionZeroAndIfNotExists_AsksForTheKeyAloneOnceInEitherOrder(bool versionFirst, string key)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var parcels = client.Table<Parcel>("Shop");

        // Act
        var json = versionFirst ? parcels.Update(ParcelKey).IfVersion(0).IfNotExists().ToRequestJson() : parcels.Update(ParcelKey).IfNotExists().IfVersion(0).ToRequestJson();

        // Assert
        await Assert.That(json).Contains($"\"ConditionExpression\":\"attribute_not_exists({key})\"");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ToRequestJson_TransactionCheckIfVersionZeroAndIfNotExists_AsksForTheKeyAloneOnceInEitherOrder(bool versionFirst)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var parcels = client.Table<Parcel>("Shop");

        // Act
        var json = versionFirst
            ? client.TransactWrite().Check(parcels, ParcelKey).IfVersion(0).IfNotExists().Token("t").ToRequestJson()
            : client.TransactWrite().Check(parcels, ParcelKey).IfNotExists().IfVersion(0).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains(KeyIsMissing + "}");
    }

    [Test]
    public async Task ToRequestJson_PutIfExistsAtZero_AsksForAnItemAtZero()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Parcel>("Shop").Put(New).IfExists().ToRequestJson();

        // Assert
        await Assert.That(json).Contains(ItemExistsAtZero);
    }

    [Test]
    public async Task ToRequestJson_DeleteOfAnEntityIfExistsAtZero_AsksForAnItemAtZero()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Parcel>("Shop").Delete(New).IfExists().ToRequestJson();

        // Assert
        await Assert.That(json).Contains(ItemExistsAtZero);
    }

    [Test]
    public async Task ToRequestJson_DeleteIfVersionZeroAndIfExists_AsksForAnItemAtZero()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Parcel>("Shop").Delete(ParcelKey).IfVersion(0).IfExists().ToRequestJson();

        // Assert
        await Assert.That(json).Contains(ItemExistsAtZero);
    }

    [Test]
    public async Task ToRequestJson_TransactionPutIfExistsAtZero_AsksForAnItemAtZero()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Put(client.Table<Parcel>("Shop"), New).IfExists().Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains(ItemExistsAtZero);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ToRequestJson_TransactionCheckIfVersionZeroAndIfExists_AsksForAnItemAtZeroInEitherOrder(bool versionFirst)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var parcels = client.Table<Parcel>("Shop");

        // Act
        var json = versionFirst
            ? client.TransactWrite().Check(parcels, ParcelKey).IfVersion(0).IfExists().Token("t").ToRequestJson()
            : client.TransactWrite().Check(parcels, ParcelKey).IfExists().IfVersion(0).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains(ItemExistsAtZero);
    }

    [Test]
    [Arguments(true, "(attribute_exists(#n1)) AND (attribute_not_exists(#n0) OR #n0 = :v1)")]
    [Arguments(false, "(attribute_exists(#n0)) AND (attribute_not_exists(#n1) OR #n1 = :v1)")]
    public async Task ToRequestJson_UpdateIfVersionZeroAndIfExists_AsksForAnItemAtZeroInEitherOrder(bool versionFirst, string condition)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var parcels = client.Table<Parcel>("Shop");

        // Act
        var json = versionFirst ? parcels.Update(ParcelKey).IfVersion(0).IfExists().ToRequestJson() : parcels.Update(ParcelKey).IfExists().IfVersion(0).ToRequestJson();

        // Assert (a name gets its number when it first shows up, and the action on the version writes one before the condition does)
        await Assert.That(json).Contains($"\"ConditionExpression\":\"{condition}\"");
    }

    [Test]
    public async Task ToRequestJson_DeleteIfVersionZeroBesideAnotherCondition_RequiresBoth()
    {
        // Arrange (the version comes last, where every clause of the chain has been written)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Parcel>("Shop").Delete(ParcelKey).IfVersion(0).If(Parcel.Attrs.Note.Exists()).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ConditionExpression":"(attribute_exists(#n0)) AND (attribute_not_exists(#n1) OR #n1 = :v0)","ExpressionAttributeNames":{"#n0":"Note","#n1":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"0"}}
            """);
    }

    [Test]
    public async Task ToRequestJson_DeleteIfVersionZeroThenAboveZero_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var parcels = client.Table<Parcel>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => _ = parcels.Delete(ParcelKey).IfVersion(0).IfVersion(3).ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("Keep one IfVersion");
    }

    [Test]
    public async Task IgnoreVersion_AfterIfVersionZero_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var parcels = client.Table<Parcel>("Shop");

        // Act
        var act = () => parcels.Update(ParcelKey).IfVersion(0).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ToRequestJson_PutIgnoreVersionAtZero_StoresVersion1WithoutACondition()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Parcel>("Shop").Put(New).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"PARCEL#p1"},"_type":{"S":"PARCEL"},"Id":{"S":"p1"},"Revision":{"N":"1"}}}""");
    }

    [Test]
    public async Task ToRequestJson_PutAboveZeroAndIfNotExists_Throws()
    {
        // Arrange (an item that lacks its key holds no version, whatever this setting says)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var parcels = client.Table<Parcel>("Shop");
        var held = New with { Revision = 3 };

        // Act
        var act = () => parcels.Put(held).IfNotExists().ToRequestJson();

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ToRequestJson_ActionAfterAnIfVersionZero_KnowsNothingOfIt()
    {
        // Arrange (a transaction goes on with the same state)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var parcels = client.Table<Parcel>("Shop");

        // Act
        var json = client.TransactWrite().Check(parcels, ParcelKey).IfVersion(0).Check(parcels, Parcel.Keys.Of("p2")).IfVersion(3).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"3"}}
            """);
    }

    [Test]
    public async Task ToRequestJson_EntityThatRejectsItAfterOneThatAllowsIt_KeepsTheConditionOnTheKey()
    {
        // Arrange (the state goes back to the pool, and the next command takes it)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        client.Table<Parcel>("Shop").Delete(ParcelKey).IfVersion(0).ToRequestJson();

        // Act
        var json = client.Table<Ticket>("Shop").Delete(Ticket.Keys.Of("t1", new DateOnly(2026, 10, 31))).IfVersion(0).ToRequestJson();

        // Assert
        await Assert.That(json).Contains(KeyIsMissing);
    }

    [Test]
    public async Task SendAsync_PutAtZero_ReturnsTheFirstVersion()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("{}"));

        // Act
        var result = await client.Table<Parcel>("Shop").Put(New).SendAsync();

        // Assert
        await Assert.That(result.Version).IsEqualTo(1L);
    }

    [Test]
    public async Task SendAsync_SelectOfAVersionTheItemDoesNotHave_GivesZero()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("""{"Item":{"_type":{"S":"PARCEL"}}}"""));

        // Act
        var selected = await client.Table<Parcel>("Shop").Get(ParcelKey).Select(Parcel.Attrs.Revision).SendAsync();

        // Assert
        await Assert.That((selected.Found, selected.Value)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task SendAsync_SelectOfAVersionTheItemHas_GivesIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("""{"Item":{"_type":{"S":"PARCEL"},"Revision":{"N":"7"}}}"""));

        // Act
        var selected = await client.Table<Parcel>("Shop").Get(ParcelKey).Select(Parcel.Attrs.Revision).SendAsync();

        // Assert
        await Assert.That(selected.Value).IsEqualTo(7);
    }

    [Test]
    public async Task SendAsync_SelectOfAVersionThatRejectsIt_ThrowsThatTheItemWasWrittenWithoutOne()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("""{"Item":{"_type":{"S":"TICKET"}}}"""));
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await tickets.Get(Ticket.Keys.Of("t1", new DateOnly(2026, 10, 31))).Select(Ticket.Attrs.Revision).SendAsync());

        // Assert
        await Assert.That((failure.Message, failure.AttributeName)).IsEqualTo((VersionWrittenWithoutOne, "Revision"));
    }

    [Test]
    public async Task ValuesAsync_SelectOfAVersionThatRejectsItFromAnIndex_ThrowsThatTheItemWasWrittenWithoutOne()
    {
        // Arrange (an index is not written, and still knows the version of its table)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("""{"Items":[{"Id":{"S":"t1"}}],"Count":1,"ScannedCount":1}"""));
        var byState = client.Table<Ticket>("Shop").Index(Ticket.Indexes.ByState);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await byState.Query(Ticket.Keys.ByStatePk.Of(TicketState.Open)).Select(Ticket.Attrs.Revision).ValuesAsync().ToListAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(VersionWrittenWithoutOne);
    }

    [Test]
    public async Task SendAsync_SelectOfAnotherAttributeOfAVersionedEntity_ThrowsThatItIsMissing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("""{"Item":{"_type":{"S":"TICKET"}}}"""));
        var tickets = client.Table<Ticket>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await tickets.Get(Ticket.Keys.Of("t1", new DateOnly(2026, 10, 31))).Select(Ticket.Attrs.Votes).SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo("The attribute \"Votes\" is missing, so the item cannot be read as Ticket.");
    }

    [Test]
    public async Task ToRequestJson_AddByTheHandleOfTheVersion_StillCompiles()
    {
        // Arrange (this handle has a converter of its own, and an Add wants one that says it is a number)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Parcel>("Shop").Update(ParcelKey).Add(Parcel.Attrs.Revision, 1).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("ADD #n0 :v0");
    }

    [Test]
    public async Task ItemAsync_RequiredVersionTheItemDoesNotHave_ReadsAsZero()
    {
        // Arrange (a read that threw here would be no way to adopt a table)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("""{"Item":{"pk":{"S":"LABEL#a"},"_type":{"S":"LABEL"},"Id":{"S":"a"}}}"""));

        // Act
        var label = await client.Table<RequiredLabel>("Shop").Get(RequiredLabel.Keys.Of("a")).ItemAsync();

        // Assert
        await Assert.That(label!.Revision).IsEqualTo(0L);
    }

    [Test]
    public async Task ItemAsync_VersionOfAConstructorTheItemDoesNotHave_ReadsAsZero()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("""{"Item":{"pk":{"S":"TAG#a"},"_type":{"S":"TAG"},"Id":{"S":"a"}}}"""));

        // Act
        var tag = await client.Table<PositionalTag>("Shop").Get(PositionalTag.Keys.Of("a")).ItemAsync();

        // Assert
        await Assert.That(tag!.Revision).IsEqualTo(0L);
    }

    private static DynamoDbEntityInfo InfoOf<T>()
        where T : IDynamoDbEntity<T> => T.Info;
}
