using DynaSharp.Expressions;
using DynaSharp.Mapping;
using DynaSharp.Serialization;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// What <c>StoreKeyInputs()</c> adds to an update, as bodies written by hand from <c>api.md</c> sections 7 and 8. A timestamp in the key is cut at the day, and the number of seconds of the attribute comes from
/// <c>date -u -d 2026-11-02T13:45:30Z +%s</c>, not from this code.
/// </summary>
public sealed class StoreKeyInputsTests
{
    private const string OrderId = "0190a5b2-7c3e-7f00-8a11-1234567890ab";

    private const string PurchaseBody = """{"TableName":"Shop","Key":{"pk":{"S":"USER#alice"},"sk":{"S":"ORDER#0190a5b2-7c3e-7f00-8a11-1234567890ab"}},"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1), #n2 = if_not_exists(#n2, :v2), #n3 = if_not_exists(#n3, :v3), #n4 = if_not_exists(#n4, :v4) + :v5","ExpressionAttributeNames":{"#n0":"Lines","#n1":"userId","#n2":"orderId","#n3":"_type","#n4":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"3"},":v1":{"S":"alice"},":v2":{"S":"0190a5b2-7c3e-7f00-8a11-1234567890ab"},":v3":{"S":"PURCHASE"},":v4":{"N":"0"},":v5":{"N":"1"}}}""";

    private static readonly DateTimeOffset At = new(2026, 11, 2, 13, 45, 30, TimeSpan.Zero);

    private static readonly Guid Id = Guid.Parse(OrderId);

    [Test]
    public async Task ToRequestJson_GeneratedKey_StoresEachInputAfterTheActionsAndBeforeTheDiscriminator()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<UserOrder>("Shop").Update(UserOrder.Keys.Of("alice", Id)).Set(UserOrder.Attrs.Lines, 3).IgnoreVersion().StoreKeyInputs().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(PurchaseBody);
    }

    [Test]
    public async Task ToRequestJson_KeyGivenAsStrings_GivesTheSameBody()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<UserOrder>("Shop").Update(new StoredKey<UserOrder>("USER#alice", "ORDER#" + OrderId)).Set(UserOrder.Attrs.Lines, 3).IgnoreVersion().StoreKeyInputs().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(PurchaseBody);
    }

    [Test]
    public async Task ToRequestJson_ClauseFirst_GivesTheSameBody()
    {
        // Arrange (it waits for the end, so where it stands in the chain says nothing)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<UserOrder>("Shop").Update(UserOrder.Keys.Of("alice", Id)).StoreKeyInputs().Set(UserOrder.Attrs.Lines, 3).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(PurchaseBody);
    }

    [Test]
    public async Task ToRequestJson_EveryActionAnUpdateOwes_StoresAfterTheDerivedKeyAndBeforeTheLiterals()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Beacon>("Shop").Update(Beacon.Keys.Of("b1")).Set(Beacon.Attrs.State, "off").StoreKeyInputs().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"B#b1"}},"UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = if_not_exists(#n2, :v2), #n3 = if_not_exists(#n3, :v3), #n4 = if_not_exists(#n4, :v4)","ExpressionAttributeNames":{"#n0":"State","#n1":"bystatepk","#n2":"Id","#n3":"everythingpk","#n4":"_type"},"ExpressionAttributeValues":{":v0":{"S":"off"},":v1":{"S":"STATE#off"},":v2":{"S":"b1"},":v3":{"S":"ALL"},":v4":{"S":"BEACON"}}}""");
    }

    [Test]
    public async Task ToRequestJson_InputThatTheKeyCutsOffAndAGeneratedKey_StoresTheValueThroughItsConverter()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<MeterDay>("Shop").Update(MeterDay.Keys.Of("m1", At, 7)).Set(MeterDay.Attrs.Note, "n").StoreKeyInputs().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"METER#m1"},"sk":{"S":"2026-11-02#0007"}},"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1), #n2 = if_not_exists(#n2, :v2), #n3 = if_not_exists(#n3, :v3), #n4 = if_not_exists(#n4, :v4)","ExpressionAttributeNames":{"#n0":"Note","#n1":"Meter","#n2":"At","#n3":"Seq","#n4":"_type"},"ExpressionAttributeValues":{":v0":{"S":"n"},":v1":{"S":"m1"},":v2":{"N":"1793627130"},":v3":{"N":"7"},":v4":{"S":"METERDAY"}}}""");
    }

    [Test]
    public async Task SendAsync_InputThatTheKeyCutsOffAndStrings_ThrowsNamingItAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var days = client.Table<MeterDay>("Shop");

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await days.Update(new StoredKey<MeterDay>("METER#m1", "2026-11-02#0007")).Set(MeterDay.Attrs.Note, "n").StoreKeyInputs().SendAsync());

        // Assert
        await Assert.That((failure!.Message, handler.Requests.Count)).IsEqualTo(("StoreKeyInputs cannot restore At from the text of the key template {At:yyyy-MM-dd}#{Seq:d4}, which cuts it off. Pass a generated key (Keys.Of), or put the entity.", 0));
    }

    [Test]
    public async Task ToRequestJson_InputCutOffWithAnIndexOfTheSameFormat_ThrowsAndDoesNotStoreTheDay()
    {
        // Arrange (the text is enough for the index key, and a timestamp at midnight in the attribute would be wrong in silence)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var summaries = client.Table<DaySummary>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => summaries.Update(new StoredKey<DaySummary>("S#s1", "2026-11-02#0007")).Set(DaySummary.Attrs.Note, "n").StoreKeyInputs().ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("cannot restore At from the text of the key template {At:yyyy-MM-dd}#{Seq:d4}");
    }

    [Test]
    public async Task ToRequestJson_InputCutOffWithAnIndexOfTheSameFormatAndAGeneratedKey_StoresTheWholeValue()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<DaySummary>("Shop").Update(DaySummary.Keys.Of("s1", At, 7)).Set(DaySummary.Attrs.Note, "n").StoreKeyInputs().ToRequestJson();

        // Assert
        await Assert.That(json).Contains(""":v2":{"S":"2026-11-02T13:45:30.0000000Z"}""");
    }

    [Test]
    public async Task ToRequestJson_InputCutOffInOneKeyAndWholeInTheOther_ReadsItFromTheWholeOne()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<TrackPoint>("Shop").Update(new StoredKey<TrackPoint>("D#2026-11-02", TrackPoint.Keys.Sk.Format(At, "p1"))).Set(TrackPoint.Attrs.Note, "n").StoreKeyInputs().ToRequestJson();

        // Assert
        await Assert.That(json).Contains(""""#n1":"At","#n2":"Id"""").And.Contains(""":v1":{"S":"2026-11-02T13:45:30.0000000Z"},":v2":{"S":"p1"}""");
    }

    [Test]
    public async Task ToRequestJson_InputCutOffInOneKeyAndWholeInTheOther_HoldsNoKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var holds = ((CommandHandle)client.Table<TrackPoint>("Shop").Update(TrackPoint.Keys.Of(At, "p1"))).State!.Write.HeldKey;

        // Assert
        await Assert.That(holds).IsNull();
    }

    [Test]
    public async Task ToRequestJson_KeyMadeByHand_GivesOnlyWhatItsTextHolds()
    {
        // Arrange (such a key says nothing of its values, so it is as good as the strings it writes)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var days = client.Table<MeterDay>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => days.Update(default(HandMadeKey)).Set(MeterDay.Attrs.Note, "n").StoreKeyInputs().ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("cannot restore At");
    }

    [Test]
    [Arguments("OTHER#m1", "2026-11-02#0007", "METER#{Meter}")]
    [Arguments("METER#m1", "not a key", "{At:yyyy-MM-dd}#{Seq:d4}")]
    public async Task ToRequestJson_KeyThatDoesNotFit_ThrowsNamingTheTemplate(string partitionKey, string sortKey, string template)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var days = client.Table<MeterDay>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => days.Update(new StoredKey<MeterDay>(partitionKey, sortKey)).Set(MeterDay.Attrs.Note, "n").StoreKeyInputs().ToRequestJson());

        // Assert
        await Assert.That(failure.Message).IsEqualTo($"The key does not fit the key template {template} of DynaSharp.UnitTests.Fluent.MeterDay, so StoreKeyInputs cannot read its inputs.");
    }

    [Test]
    public async Task ToRequestJson_KeyThatLacksASortKey_ThrowsNamingTheTemplate()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var days = client.Table<MeterDay>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => days.Update(new StoredKey<MeterDay>("METER#m1")).Set(MeterDay.Attrs.Note, "n").StoreKeyInputs().ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("The key does not fit the key template {At:yyyy-MM-dd}#{Seq:d4}");
    }

    [Test]
    public async Task ToRequestJson_KeyThatIsNoString_ThrowsNamingTheTemplate()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var purchases = client.Table<UserOrder>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => purchases.Update(new StoredKey<UserOrder>(1, "ORDER#" + OrderId)).IgnoreVersion().StoreKeyInputs().ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("The key does not fit the key template USER#{UserId}");
    }

    [Test]
    public async Task ToRequestJson_ConverterThatStoresBinary_StoresBinaryAndNotTheTextOfTheKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var bytes = Convert.ToBase64String(Id.ToByteArray());

        // Act
        var json = client.Table<BinaryInput>("Shop").Update(BinaryInput.Keys.Of(Id)).Set(BinaryInput.Attrs.Note, "n").StoreKeyInputs().ToRequestJson();

        // Assert
        await Assert.That(json).Contains($$""":v1":{"B":"{{bytes}}"}""");
    }

    [Test]
    public async Task ToRequestJson_RawValue_StoresItWithItsDelimiter()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<RawSlot>("Shop").Update(new StoredKey<RawSlot>("slot#a#b")).Set(RawSlot.Attrs.Note, "n").StoreKeyInputs().ToRequestJson();

        // Assert
        await Assert.That(json).Contains(""":v1":{"S":"a#b"}""");
    }

    [Test]
    public async Task ToRequestJson_PlainKeyThatATemplateRepeats_StoresOnlyTheOtherInput()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<RepeatedPlain>("Shop").Update(RepeatedPlain.Keys.Of("r1", new DateOnly(2026, 11, 2))).Set(RepeatedPlain.Attrs.Note, "n").StoreKeyInputs().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1), #n2 = if_not_exists(#n2, :v2)","ExpressionAttributeNames":{"#n0":"Note","#n1":"Day","#n2":"_type"}""");
    }

    [Test]
    public async Task ToRequestJson_PlainKey_LeavesTheBodyAsItIs()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var things = client.Table<PlainThing>("Shop");

        // Act
        var with = things.Update(PlainThing.Keys.Of("p1")).Set(PlainThing.Attrs.Note, "n").StoreKeyInputs().ToRequestJson();
        var without = things.Update(PlainThing.Keys.Of("p1")).Set(PlainThing.Attrs.Note, "n").ToRequestJson();

        // Assert
        await Assert.That(with).IsEqualTo(without);
    }

    [Test]
    public async Task ToRequestJson_TemplateOverTheAttributeOfItsProperty_LeavesTheBodyAsItIs()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var things = client.Table<BareThing>("Shop");

        // Act
        var with = things.Update(BareThing.Keys.Of("p1")).Set(BareThing.Attrs.Note, "n").StoreKeyInputs().ToRequestJson();
        var without = things.Update(BareThing.Keys.Of("p1")).Set(BareThing.Attrs.Note, "n").ToRequestJson();

        // Assert
        await Assert.That(with).IsEqualTo(without);
    }

    [Test]
    public async Task ToRequestJson_InputOnlyTheKeyHolds_LeavesTheBodyAsItIs()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var things = client.Table<HeldThing>("Shop");

        // Act
        var with = things.Update(HeldThing.Keys.Of("p1")).Set(HeldThing.Attrs.Note, "n").StoreKeyInputs().ToRequestJson();
        var without = things.Update(HeldThing.Keys.Of("p1")).Set(HeldThing.Attrs.Note, "n").ToRequestJson();

        // Assert
        await Assert.That(with).IsEqualTo(without);
    }

    [Test]
    public async Task ToRequestJson_RawActionOnTheAttribute_KeepsThatActionAlone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<UserOrder>("Shop").Update(UserOrder.Keys.Of("alice", Id)).Set("userId", "x").IgnoreVersion().StoreKeyInputs().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1), #n2 = if_not_exists(#n2, :v2), #n3 = if_not_exists(#n3, :v3) + :v4","ExpressionAttributeNames":{"#n0":"userId","#n1":"orderId","#n2":"_type","#n3":"Revision"}""");
    }

    [Test]
    public async Task ToRequestJson_WriteOfTheInputByHandle_StillThrows()
    {
        // Arrange (a handle in a variable, which the analyzer does not follow)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var purchases = client.Table<UserOrder>("Shop");
        var handle = UserOrder.Attrs.UserId;

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => purchases.Update(UserOrder.Keys.Of("alice", Id)).Set(handle, "x").IgnoreVersion().StoreKeyInputs().ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("DynamoDB cannot update a key of a table");
    }

    [Test]
    public async Task ToRequestJson_IfExists_KeepsTheConditionBesideTheActions()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<UserOrder>("Shop").Update(UserOrder.Keys.Of("alice", Id)).IfExists().IgnoreVersion().StoreKeyInputs().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""UpdateExpression":"SET #n1 = if_not_exists(#n1, :v0), #n2 = if_not_exists(#n2, :v1), #n3 = if_not_exists(#n3, :v2), #n4 = if_not_exists(#n4, :v3) + :v4","ConditionExpression":"attribute_exists(#n0)""");
    }

    [Test]
    public async Task ToRequestJson_IfNotExistsAndAVersion_ReadTogether()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<UserOrder>("Shop").Update(UserOrder.Keys.Of("alice", Id)).IfVersion(0).IfNotExists().StoreKeyInputs().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""UpdateExpression":"SET #n1 = :v0, #n2 = if_not_exists(#n2, :v1), #n3 = if_not_exists(#n3, :v2), #n4 = if_not_exists(#n4, :v3)","ConditionExpression":"attribute_not_exists(#n0)""");
    }

    [Test]
    public async Task ToRequestJson_UpdateWithoutTheClause_WritesNoInput()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<MeterDay>("Shop").Update(MeterDay.Keys.Of("m1", At, 7)).Set(MeterDay.Attrs.Note, "n").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"METER#m1"},"sk":{"S":"2026-11-02#0007"}},"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1)","ExpressionAttributeNames":{"#n0":"Note","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"S":"n"},":v1":{"S":"METERDAY"}}}""");
    }

    [Test]
    public async Task ToRequestJson_StepsOfATransaction_RenderIt()
    {
        // Arrange (each action has placeholders of its own, and the clause of the first says nothing about the second)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var purchases = client.Table<UserOrder>("Shop");

        // Act
        var json = client.TransactWrite()
            .Update(purchases, UserOrder.Keys.Of("alice", Id)).Set(UserOrder.Attrs.Lines, 3).IgnoreVersion().StoreKeyInputs()
            .Update(purchases, UserOrder.Keys.Of("bob", Id)).Set(UserOrder.Attrs.Lines, 4).IgnoreVersion()
            .Token("t")
            .ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TransactItems":[{"Update":{"TableName":"Shop","Key":{"pk":{"S":"USER#alice"},"sk":{"S":"ORDER#0190a5b2-7c3e-7f00-8a11-1234567890ab"}},"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1), #n2 = if_not_exists(#n2, :v2), #n3 = if_not_exists(#n3, :v3), #n4 = if_not_exists(#n4, :v4) + :v5","ExpressionAttributeNames":{"#n0":"Lines","#n1":"userId","#n2":"orderId","#n3":"_type","#n4":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"3"},":v1":{"S":"alice"},":v2":{"S":"0190a5b2-7c3e-7f00-8a11-1234567890ab"},":v3":{"S":"PURCHASE"},":v4":{"N":"0"},":v5":{"N":"1"}}}},{"Update":{"TableName":"Shop","Key":{"pk":{"S":"USER#bob"},"sk":{"S":"ORDER#0190a5b2-7c3e-7f00-8a11-1234567890ab"}},"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1), #n2 = if_not_exists(#n2, :v2) + :v3","ExpressionAttributeNames":{"#n0":"Lines","#n1":"_type","#n2":"Revision"},"ExpressionAttributeValues":{":v0":{"N":"4"},":v1":{"S":"PURCHASE"},":v2":{"N":"0"},":v3":{"N":"1"}}}}],"ClientRequestToken":"t"}""");
    }

    [Test]
    public async Task ToRequestJson_StepWithAKeyThatDoesNotGiveTheValueBack_ThrowsAtTheNextVerb()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var days = client.Table<MeterDay>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => _ = client.TransactWrite().Update(days, new StoredKey<MeterDay>("METER#m1", "2026-11-02#0007")).StoreKeyInputs().Put(days, new MeterDay { Meter = "m", At = At, Seq = 1 }));

        // Assert
        await Assert.That(failure.Message).Contains("cannot restore At");
    }

    [Test]
    public async Task ToRequestJson_StepOfATableWithoutAnEntity_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => _ = client.TransactWrite().Update(shop, "a").StoreKeyInputs());

        // Assert
        await Assert.That(failure.Message).IsEqualTo("This view of the table Shop has no entity, so there are no key inputs to store.");
    }

    private readonly struct HandMadeKey : IKey<MeterDay>
    {
        public void WriteKey(DynamoDbWriter writer)
        {
            writer.WriteAttributeName("pk"u8);
            writer.WriteString("METER#m1");
            writer.WriteAttributeName("sk"u8);
            writer.WriteString("2026-11-02#0007");
        }
    }
}
