using DynaSharp.Testing;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

/// <content>
/// What the message of a failed condition says. DynamoDB says "The conditional request failed" and no more, so the client adds which condition the write sent, by kind and by the name of the version attribute,
/// and never a value: <see cref="Gauge"/> and <see cref="Ticket"/> have a version called <c>Revision</c>, <see cref="Parcel"/> one that may be missing.
/// </content>
public sealed partial class FailedConditionTests
{
    private const string ServiceMessage = "The conditional request failed";

    private const string NewVersionAdvice = " The version this write expects marks a new item, so the check asks that no item has this key. To write over the stored item, use the version last read from it, or call IgnoreVersion() to write without the check.";

    private static readonly Ticket.Keys.PrimaryKey TicketKey = Ticket.Keys.Of("t1", new DateOnly(2026, 11, 3));

    [Test]
    public async Task SendAsync_UpdateWithAVersion_NamesTheVersionCheck()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Users");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await tickets.Update(TicketKey).Set(Ticket.Attrs.Note, "n").IfVersion(3).SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(Explained("The condition of this Update on Users did not hold: the version check on Revision."));
    }

    [Test]
    public async Task SendAsync_PlainPutOfANewEntityOverAStoredOne_PointsAtTheVersionAndAtIgnoreVersion()
    {
        // Arrange (a fresh entity holds version 0, which asks that no item has the key, and the stored item is at a later version: the trap of a plain put)
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var gauges = client.Table<Gauge>("Users");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await gauges.Put(new Gauge { Id = "g1" }).SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(Explained("The condition of this Put on Users did not hold: the version check on Revision." + NewVersionAdvice));
    }

    [Test]
    public async Task SendAsync_PutOfAStoredVersion_NamesTheVersionCheck()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var gauges = client.Table<Gauge>("Users");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await gauges.Put(new Gauge { Id = "g1", Revision = 2 }).SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(Explained("The condition of this Put on Users did not hold: the version check on Revision."));
    }

    [Test]
    public async Task SendAsync_DeleteOfANewEntity_PointsAtTheVersion()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var gauges = client.Table<Gauge>("Users");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await gauges.Delete(new Gauge { Id = "g1" }).SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(Explained("The condition of this Delete on Users did not hold: the version check on Revision." + NewVersionAdvice));
    }

    [Test]
    public async Task SendAsync_UpdateAtVersionZero_PointsAtTheVersion()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var gauges = client.Table<Gauge>("Users");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await gauges.Update(Gauge.Keys.Of("g1")).IfVersion(0).SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(Explained("The condition of this Update on Users did not hold: the version check on Revision." + NewVersionAdvice));
    }

    [Test]
    public async Task SendAsync_PutAtZeroOfAnEntityThatAllowsNoVersion_NamesTheVersionCheck()
    {
        // Arrange (version 0 reads as a missing version too, so it is a check on the attribute and not one on the key)
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var parcels = client.Table<Parcel>("Users");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await parcels.Put(new Parcel { Id = "p1" }).SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(Explained("The condition of this Put on Users did not hold: the version check on Revision."));
    }

    [Test]
    public async Task SendAsync_UpdateAtZeroOfAnEntityThatAllowsNoVersion_NamesTheVersionCheck()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var parcels = client.Table<Parcel>("Users");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await parcels.Update(Parcel.Keys.Of("p1")).IfVersion(0).SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(Explained("The condition of this Update on Users did not hold: the version check on Revision."));
    }

    [Test]
    public async Task SendAsync_PutIfNotExists_NamesTheCheckOnTheKey()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await client.Table("Users", "pk").Put(new Item { ["pk"] = "a" }).IfNotExists().SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(Explained("The condition of this Put on Users did not hold: the check that no item has this key."));
    }

    [Test]
    public async Task SendAsync_NewEntityIfNotExists_NamesTheCheckOnTheKeyAlone()
    {
        // Arrange (the version and IfNotExists send one condition, and the caller asked for it by name, so there is no advice about versions)
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var gauges = client.Table<Gauge>("Users");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await gauges.Put(new Gauge { Id = "g1" }).IfNotExists().SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(Explained("The condition of this Put on Users did not hold: the check that no item has this key."));
    }

    [Test]
    public async Task SendAsync_VersionZeroAndIfNotExists_NamesTheCheckOnTheKeyAlone()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var gauges = client.Table<Gauge>("Users");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await gauges.Update(Gauge.Keys.Of("g1")).IfVersion(0).IfNotExists().SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(Explained("The condition of this Update on Users did not hold: the check that no item has this key."));
    }

    [Test]
    public async Task SendAsync_DeleteIfExists_NamesTheCheckOnTheKey()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await client.Table("Users", "pk").Delete("a").IfExists().SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(Explained("The condition of this Delete on Users did not hold: the check that an item has this key."));
    }

    [Test]
    [Arguments("Put", "Put")]
    [Arguments("PutInterpolated", "Put")]
    [Arguments("Update", "Update")]
    [Arguments("UpdateInterpolated", "Update")]
    [Arguments("Delete", "Delete")]
    [Arguments("DeleteInterpolated", "Delete")]
    public async Task SendAsync_ConditionOfTheCaller_NamesItWithoutItsText(string command, string verb)
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var users = client.Table("Users", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await SendWithConditionAsync(command, users));

        // Assert
        await Assert.That(failure.Message).IsEqualTo(Explained($"The condition of this {verb} on Users did not hold: your own condition."));
    }

    [Test]
    public async Task SendAsync_VersionAndACondition_NamesBothAndSaysThatDynamoDbDoesNotTellWhich()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Users");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await tickets.Update(TicketKey).Set(Ticket.Attrs.Note, "n").IfVersion(3).If(Attr("Votes").Lt(10)).SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(Explained("The condition of this Update on Users did not hold: the version check on Revision or your own condition, and DynamoDB does not say which."));
    }

    [Test]
    public async Task SendAsync_ThreeParts_ListsThemAll()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var gauges = client.Table<Gauge>("Users");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await gauges.Put(new Gauge { Id = "g1", Revision = 2 }).IfExists().If(Attr("Id").Exists()).SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(Explained(
            "The condition of this Put on Users did not hold: the version check on Revision, the check that an item has this key or your own condition, and DynamoDB does not say which."));
    }

    [Test]
    public async Task SendAsync_NewEntityAndACondition_ListsBothAndPointsAtTheVersion()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var gauges = client.Table<Gauge>("Users");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await gauges.Put(new Gauge { Id = "g1" }).If(Attr("Id").Exists()).SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(Explained(
            "The condition of this Put on Users did not hold: the version check on Revision or your own condition, and DynamoDB does not say which." + NewVersionAdvice));
    }

    [Test]
    public async Task Message_ReadTwice_SaysTheSame()
    {
        // Arrange (the line is made when the message is first read, and kept)
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await client.Table("Users", "pk").Delete("a").IfExists().SendAsync());
        var first = failure.Message;

        // Act
        var second = failure.Message;

        // Assert
        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    public async Task Explain_RoutineFailure_AllocatesNothingUntilTheMessageIsRead()
    {
        // Arrange (a create-if-absent that fails is routine, and code that catches it never reads the text)
        var failure = new ConditionalCheckFailedException(ServiceMessage);
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var table = client.Table("Users", "pk").Model;

        // Act
        var allocated = MeasureExplain(failure, table);

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task SendAsync_WriteWithoutACondition_KeepsTheMessageOfTheService()
    {
        // Arrange (DynamoDB never fails such a write on a condition, but a stub can)
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await client.Table("Users", "pk").Put(new Item { ["pk"] = "a" }).SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(ServiceMessage);
    }

    [Test]
    public async Task SendAsync_ValuesOfTheKeyTheItemTheConditionAndTheVersion_StayOutOfTheText()
    {
        // Arrange (every value of the write is a marker that must not appear: the key, an attribute, the value of the condition and the version)
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var tickets = client.Table<Ticket>("Users");
        var key = Ticket.Keys.Of("secret-id", new DateOnly(2026, 11, 3));

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await tickets.Update(key).Set(Ticket.Attrs.Note, "secret-note").IfVersion(987654321).If(Attr("Title").Eq("secret-title")).SendAsync());

        // Assert
        var text = failure.ToString();
        await Assert.That((failure.Message.Contains("did not hold", StringComparison.Ordinal), text.Contains("secret", StringComparison.Ordinal), text.Contains("987654321", StringComparison.Ordinal), text.Contains("2026", StringComparison.Ordinal)))
            .IsEqualTo((true, false, false, false));
    }

    private static string Explained(string line) => ServiceMessage + Environment.NewLine + line;

    private static long MeasureExplain(ConditionalCheckFailedException failure, DynaSharp.Fluent.TableModel table)
    {
        for (var i = 0; i < 64; i++)
        {
            DynaSharp.Fluent.FailedCondition.Explain(failure, "PutItem", table, DynaSharp.Fluent.ConditionParts.NoItem);
        }

        var smallest = long.MaxValue;
        for (var i = 0; i < 16; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            DynaSharp.Fluent.FailedCondition.Explain(failure, "PutItem", table, DynaSharp.Fluent.ConditionParts.NoItem);
            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return smallest;
    }

    private static async Task SendWithConditionAsync(string command, Table<Item> users)
    {
        switch (command)
        {
            case "Put":
                await users.Put(new Item { ["pk"] = "a" }).If(Attr("n").Eq(1)).SendAsync();
                break;
            case "PutInterpolated":
                await users.Put(new Item { ["pk"] = "a" }).If($"{Attr("n")} = {1}").SendAsync();
                break;
            case "Update":
                await users.Update("a").Set("n", 2).If(Attr("n").Eq(1)).SendAsync();
                break;
            case "UpdateInterpolated":
                await users.Update("a").Set("n", 2).If($"{Attr("n")} = {1}").SendAsync();
                break;
            case "Delete":
                await users.Delete("a").If(Attr("n").Eq(1)).SendAsync();
                break;
            default:
                await users.Delete("a").If($"{Attr("n")} = {1}").SendAsync();
                break;
        }
    }
}
