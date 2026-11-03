// The run-time check of a key left half done is under test here, so some updates do on purpose what DYNA226 reports.
#pragma warning disable DYNA226
using DynaSharp.Fluent;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// What a computed input of a key does to writes: a <c>Put</c> runs the getter, and an update, which cannot, refuses what would leave the index behind. The bodies are written by hand from
/// <c>api.md</c> section 8, and a throw is only a throw if nothing went out, so those look at what the handler saw. A handle in a variable is what the analyzers do not follow, which is how a test gets to the
/// run-time rule that <c>DYNA205</c> and <c>DYNA219</c> report in a build.
/// </summary>
public sealed class ComputedFromAttributeTests
{
    private const string Sender = "desk";

    private static readonly DateTimeOffset Arrival = new(2026, 11, 2, 8, 30, 0, 250, TimeSpan.Zero);

    private static readonly Visit.Keys.PrimaryKey VisitKey = Visit.Keys.Of("v1");

    private static readonly VisitMessage Message = new() { Arrival = Arrival, Sender = Sender };

    private static readonly Visit AVisit = new() { Id = "v1", Site = "bern", AppointmentKey = "a1", Message = Message, Guests = 2, Tags = ["x"] };

    [Test]
    public async Task ToRequestJson_PutOfAnEntity_WritesTheKeysFromTheGetters()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Visit>("Shop").Put(AVisit).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"VISIT#v1"},"byarrivalpk":{"S":"SITE#bern"},"byarrivalsk":{"S":"2026-11-02T08:30:00.2500000Z#a1"},"byloadpk":{"S":"LOAD#3"},"_type":{"S":"VISIT"},"Id":{"S":"v1"},"Site":{"S":"bern"},"AppointmentKey":{"S":"a1"},"Message":{"M":{"Arrival":{"S":"2026-11-02T08:30:00.2500000Z"},"Sender":{"S":"desk"}}},"Guests":{"N":"2"},"Tags":{"SS":["x"]}}}""");
    }

    [Test]
    public async Task Format_ComputedInput_IsAParameterOfTheBuilderLikeAnyOther()
    {
        // Arrange, Act
        var key = Visit.Keys.ByArrivalSk.Format(Arrival, "a1");

        // Assert
        await Assert.That(key).IsEqualTo("2026-11-02T08:30:00.2500000Z#a1");
    }

    [Test]
    public async Task ToRequestJson_PutWithoutTheNullableComputedInput_LeavesTheSparseIndexOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Reminder>("Shop").Put(new Reminder { Id = "r1", Queue = "q" }).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"REMINDER#r1"},"_type":{"S":"REMINDER"},"Id":{"S":"r1"},"Queue":{"S":"q"}}}""");
    }

    [Test]
    public async Task ToRequestJson_PutWithTheNullableComputedInput_WritesBothKeysOfTheSparseIndex()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Reminder>("Shop").Put(new Reminder { Id = "r1", Queue = "q", Due = Arrival }).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"REMINDER#r1"},"byduepk":{"S":"QUEUE#q"},"byduesk":{"S":"2026-11-02"},"_type":{"S":"REMINDER"},"Id":{"S":"r1"},"Queue":{"S":"q"},"Due":{"S":"2026-11-02T08:30:00.2500000Z"}}}""");
    }

    [Test]
    public async Task SendAsync_SetOfAnotherInputOfTheTemplate_ThrowsPointingAtAPutAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var visits = client.Table<Visit>("Shop");

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await visits.Update(VisitKey).Set(Visit.Attrs.AppointmentKey, "a2").SendAsync());

        // Assert
        await Assert.That((failure!.Message, handler.Requests.Count)).IsEqualTo((
            "This update of DynaSharp.UnitTests.Fluent.Visit sets AppointmentKey, and not Arrival, of the key template {Arrival}#{AppointmentKey}, so the attribute byarrivalsk cannot be written and the index would go stale. Use a Put of the changed entity, or a raw path, which is not checked.",
            0));
    }

    [Test]
    public async Task SendAsync_SetOfADependency_ThrowsNamingItAndTheTemplateAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var visits = client.Table<Visit>("Shop");
        var handle = Visit.Attrs.Message;

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await visits.Update(VisitKey).Set(handle, Message).SendAsync());

        // Assert
        await Assert.That((failure!.Message, handler.Requests.Count)).IsEqualTo((
            "This update of DynaSharp.UnitTests.Fluent.Visit changes Message, which Arrival of the key template {Arrival}#{AppointmentKey} is computed from, so the key cannot follow. Use a Put of the changed entity, or a raw path, which is not checked.",
            0));
    }

    [Test]
    public async Task SendAsync_SetOfAPathInsideADependency_ThrowsAndSendsNothing()
    {
        // Arrange (the hole the rule is there for: a nested path is no key input, and without it the index keeps the old day in silence)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var visits = client.Table<Visit>("Shop");
        var handle = Visit.Attrs.Message.At(VisitMessage.Attrs.Arrival);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await visits.Update(VisitKey).Set(handle, Arrival).SendAsync());

        // Assert
        await Assert.That((failure!.Message, handler.Requests.Count)).IsEqualTo((
            "This update of DynaSharp.UnitTests.Fluent.Visit changes Message, which Arrival of the key template {Arrival}#{AppointmentKey} is computed from, so the key cannot follow. Use a Put of the changed entity, or a raw path, which is not checked.",
            0));
    }

    [Test]
    public async Task SendAsync_RemoveOfADependency_ThrowsAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var visits = client.Table<Visit>("Shop");
        var handle = Visit.Attrs.Message;

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await visits.Update(VisitKey).Remove(handle).SendAsync());

        // Assert
        await Assert.That((failure!.Message.Contains("changes Message,", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task SendAsync_RemoveOfAPathInsideADependency_ThrowsAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var visits = client.Table<Visit>("Shop");
        var handle = Visit.Attrs.Message.At(VisitMessage.Attrs.Sender);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await visits.Update(VisitKey).Remove(handle).SendAsync());

        // Assert
        await Assert.That((failure!.Message.Contains("changes Message,", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task SendAsync_SetOfADependencyToNull_IsARemovalAndThrows()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var visits = client.Table<Visit>("Shop");
        var handle = Visit.Attrs.Message;

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await visits.Update(VisitKey).Set(handle, null).SendAsync());

        // Assert
        await Assert.That((failure!.Message.Contains("changes Message,", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task SendAsync_AddToADependency_ThrowsAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var visits = client.Table<Visit>("Shop");
        var handle = Visit.Attrs.Guests;

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await visits.Update(VisitKey).Add(handle, 1).SendAsync());

        // Assert
        await Assert.That((failure!.Message.Contains("changes Guests, which Load of the key template LOAD#{Load}", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task SendAsync_SetIfNotExistsOfADependency_ThrowsAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var visits = client.Table<Visit>("Shop");
        var handle = Visit.Attrs.Guests;

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await visits.Update(VisitKey).SetIfNotExists(handle, 1).SendAsync());

        // Assert
        await Assert.That((failure!.Message.Contains("changes Guests,", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task SendAsync_DeleteFromADependency_ThrowsAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var visits = client.Table<Visit>("Shop");
        var handle = Visit.Attrs.Tags;

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await visits.Update(VisitKey).Delete(handle, ["x"]).SendAsync());

        // Assert
        await Assert.That((failure!.Message.Contains("changes Tags, which Load", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task SendAsync_SetIfNotNullOfNullOnADependency_AddsNothingAndSends()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var visits = client.Table<Visit>("Shop");
        var handle = Visit.Attrs.Message;

        // Act
        await visits.Update(VisitKey).SetIfNotNull(handle, null).Set(Visit.Attrs.Note, "n").SendAsync();

        // Assert
        await Assert.That(handler.Requests[0].BodyText).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"VISIT#v1"}},"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1)","ExpressionAttributeNames":{"#n0":"Note","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"S":"n"},":v1":{"S":"VISIT"}}}""");
    }

    [Test]
    public async Task SendAsync_RawPathOfADependency_SendsAndLeavesTheKeyAlone()
    {
        // Arrange (the hole the documentation names: the derived key stays out of the request)
        using var handler = new StubHandler(static _ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var visits = client.Table<Visit>("Shop");

        // Act
        await visits.Update(VisitKey).Set("Message", AttributeValue.FromString("raw")).SendAsync();

        // Assert
        await Assert.That(handler.Requests[0].BodyText).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"VISIT#v1"}},"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1)","ExpressionAttributeNames":{"#n0":"Message","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"S":"raw"},":v1":{"S":"VISIT"}}}""");
    }

    [Test]
    public async Task ToRequestJson_UpdateThatTouchesNoInput_WritesNoKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Visit>("Shop").Update(VisitKey).Set(Visit.Attrs.Note, "n").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"VISIT#v1"}},"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1)","ExpressionAttributeNames":{"#n0":"Note","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"S":"n"},":v1":{"S":"VISIT"}}}""");
    }

    [Test]
    public async Task ToRequestJson_SetOfADependencyOnAStep_ThrowsAtTheNextVerb()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var visits = client.Table<Visit>("Shop");
        var handle = Visit.Attrs.Message.At(VisitMessage.Attrs.Arrival);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => _ = client.TransactWrite().Update(visits, VisitKey).Set(handle, Arrival).Put(visits, AVisit));

        // Assert
        await Assert.That((failure.Message.Contains("changes Message, which Arrival", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task ToRequestJson_DeleteFromADependencyOnAStep_ThrowsAtTheEnd()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var visits = client.Table<Visit>("Shop");
        var handle = Visit.Attrs.Tags;

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => client.TransactWrite().Update(visits, VisitKey).Delete(handle, ["x"]).Token("t").ToRequestJson());

        // Assert
        await Assert.That((failure.Message.Contains("changes Tags, which Load", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task ToRequestJson_SetOfAnInputWithTheComputedKeyBesideItInASparseIndex_ThrowsPointingAtAPut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var reminders = client.Table<Reminder>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => reminders.Update(Reminder.Keys.Of("r1")).Set(Reminder.Attrs.Queue, "q").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).IsEqualTo("This update of DynaSharp.UnitTests.Fluent.Reminder sets Queue, of the key template QUEUE#{Queue} of the sparse index ByDue, and does not produce byduesk, the other key of that index, so the item would silently stay out of it. Use a Put of the changed entity, or a raw path, which is not checked.");
    }

    [Test]
    public async Task ToRequestJson_SetOfANullableDependency_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var reminders = client.Table<Reminder>("Shop");
        var handle = Reminder.Attrs.Due;

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => reminders.Update(Reminder.Keys.Of("r1")).Set(handle, Arrival).ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("changes Due, which DueDay of the key template {DueDay} is computed from");
    }

    [Test]
    public async Task ToRequestJson_SetOfAnotherInputBesideAStoredComputedInput_PointsAtAPutAndNotAtTheHandle()
    {
        // Arrange (the computed input has an attribute and a handle, and a set of it would be undone by the next Put)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var badges = client.Table<Badge>("Shop");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => badges.Update(Badge.Keys.Of("b1")).Set(Badge.Attrs.Kind, "staff").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("sets Kind, and not Label,").And.Contains("Use a Put of the changed entity");
    }

    [Test]
    public async Task SendAsync_SetOfAStoredComputedInput_ThrowsNamingItAndTheTemplateAndSendsNothing()
    {
        // Arrange (the other input of the template is set, so the key could be written, from the value of the update and not from the getter)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var badges = client.Table<Badge>("Shop");
        var handle = Badge.Attrs.Label;

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await badges.Update(Badge.Keys.Of("b1")).Set(handle, "x").Set(Badge.Attrs.Kind, "staff").SendAsync());

        // Assert
        await Assert.That((failure!.Message, handler.Requests.Count)).IsEqualTo((
            "This update of DynaSharp.UnitTests.Fluent.Badge changes Label, which its getter computes for the key template LABEL#{Label}#{Kind}, so the key would follow the update and not the getter. Use a Put of the changed entity, or a raw path, which is not checked.",
            0));
    }

    [Test]
    public async Task SendAsync_SetOfAComputedPlainKeyOfAnIndex_ThrowsAndSendsNothing()
    {
        // Arrange (the attribute is the key, so nothing else would ever notice)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var badges = client.Table<Badge>("Shop");
        var handle = Badge.Attrs.Code;

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await badges.Update(Badge.Keys.Of("b1")).Set(handle, "X").SendAsync());

        // Assert
        await Assert.That((failure!.Message.Contains("changes Code, which its getter computes for the key template {Code}", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task SendAsync_RemoveOfAStoredComputedInput_ThrowsAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var badges = client.Table<Badge>("Shop");
        var handle = Badge.Attrs.Code;

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await badges.Update(Badge.Keys.Of("b1")).Remove(handle).SendAsync());

        // Assert
        await Assert.That((failure!.Message.Contains("changes Code,", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task ToRequestJson_SetOfAStoredComputedInputOnAStep_ThrowsAtTheNextVerb()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var badges = client.Table<Badge>("Shop");
        var handle = Badge.Attrs.Code;

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => _ = client.TransactWrite().Update(badges, Badge.Keys.Of("b1")).Set(handle, "X").Put(badges, new Badge { Id = "b2", Name = "n" }));

        // Assert
        await Assert.That((failure.Message.Contains("changes Code,", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task SendAsync_SetIfNotNullOfNullOnAStoredComputedInput_AddsNothingAndSends()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var badges = client.Table<Badge>("Shop");
        var handle = Badge.Attrs.Code;

        // Act
        await badges.Update(Badge.Keys.Of("b1")).SetIfNotNull(handle, null).SendAsync();

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_RawPathOfAStoredComputedInput_SendsAndLeavesTheKeyAlone()
    {
        // Arrange (a raw path is not checked, as everywhere)
        using var handler = new StubHandler(static _ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var badges = client.Table<Badge>("Shop");

        // Act
        await badges.Update(Badge.Keys.Of("b1")).Set("label", AttributeValue.FromString("x")).SendAsync();

        // Assert
        await Assert.That(handler.Requests[0].BodyText).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"BADGE#b1"}},"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1)","ExpressionAttributeNames":{"#n0":"label","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"S":"x"},":v1":{"S":"BADGE"}}}""");
    }

    [Test]
    public async Task ToRequestJson_SetOfTheDependencyOfAPlainKeyOfAnIndex_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var badges = client.Table<Badge>("Shop");
        var handle = Badge.Attrs.Name;

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => badges.Update(Badge.Keys.Of("b1")).Set(handle, "x").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("changes Name, which Label of the key template LABEL#{Label}#{Kind}");
    }

    [Test]
    public async Task ToRequestJson_SetOfTheDependencyOfTheKeyOfTheTable_ThrowsThatNoKeyCanChange()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shifts = client.Table<Shift>("Shop");
        var handle = Shift.Attrs.Name;

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => shifts.Update(Shift.Keys.Of("NIGHT")).Set(handle, "x").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).IsEqualTo("This update of DynaSharp.UnitTests.Fluent.Shift changes Code, which the key template SHIFT#{Code} is made of. DynamoDB cannot update a key of a table: delete the item and put a new one.");
    }

    [Test]
    public async Task ToRequestJson_UpdateBesideAComputedInputOfTheKeyOfTheTable_GoesOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Shift>("Shop").Update(Shift.Keys.Of("NIGHT")).Set(Shift.Attrs.Note, "n").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"SHIFT#NIGHT"}},"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1)","ExpressionAttributeNames":{"#n0":"Note","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"S":"n"},":v1":{"S":"SHIFT"}}}""");
    }

    [Test]
    public async Task ToRequestJson_SetOfADependencyThatFeedsATemplateItself_ThrowsForTheComputedInput()
    {
        // Arrange (the template of the property could follow, the one derived from it cannot, and a half of it would be the stale index)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shelves = client.Table<Shelf>("Shop");
        var handle = Shelf.Attrs.Site;

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => shelves.Update(Shelf.Keys.Of("s1")).Set(handle, "bern").ToRequestJson());

        // Assert
        await Assert.That(failure.Message).Contains("changes Site, which Region of the key template REGION#{Region}");
    }

    [Test]
    [Arguments("message", "message", true)]
    [Arguments("message.arrival", "message", true)]
    [Arguments("lines[2].sku", "lines", true)]
    [Arguments("messages", "message", false)]
    [Arguments("mess", "message", false)]
    [Arguments("other.message", "message", false)]
    public async Task IsAtOrInside_PathAndAttribute_FollowsTheNames(string path, string attribute, bool expected)
    {
        // Arrange, Act
        var inside = EntityWrites.IsAtOrInside(path, attribute);

        // Assert
        await Assert.That(inside).IsEqualTo(expected);
    }
}
