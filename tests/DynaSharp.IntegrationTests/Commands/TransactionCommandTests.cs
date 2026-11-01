using DynaSharp.Expressions;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using static DynaSharp.Expr;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>TransactWrite and TransactGet of the command API against the emulators, with what only a service can show: cancellation, its reasons and idempotency.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class TransactionCommandTests(Backend backend)
{
    [Test]
    public async Task TransactWrite_EveryKindOfAction_AppliesThemAll()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await sandbox.Shop.Put(new Item { ["pk"] = "counter", ["sk"] = "s", ["n"] = 1 }).SendAsync();
        await sandbox.Shop.Put(new Item { ["pk"] = "doomed", ["sk"] = "s" }).SendAsync();
        await sandbox.Shop.Put(new Item { ["pk"] = "guard", ["sk"] = "s", ["remaining"] = 1 }).SendAsync();

        // Act
        await sandbox.Client.TransactWrite()
            .Put(sandbox.Shop, new Item { ["pk"] = "created", ["sk"] = "s" }).IfNotExists()
            .Put(profiles, new Profile("alice", "Alice"))
            .Update(sandbox.Shop, "counter", "s").Add("n", 1).Set("status", "open").If(Attr("n").Lt(100))
            .Delete(sandbox.Shop, "doomed", "s").IfExists()
            .Check(sandbox.Shop, "guard", "s").If(Attr("remaining").Gt(0))
            .SendAsync();

        // Assert
        var counter = await sandbox.ReadAsync("counter");
        await Assert.That(((await sandbox.ReadAsync("created")) is not null, counter!.GetNumber<int>("n"), counter.GetString("status"), await sandbox.ReadAsync("doomed"), (await profiles.Get(Profile.Keys.Of("alice")).Consistent().SendAsync()).Found))
            .IsEqualTo((true, (int?)2, (string?)"open", (Item?)null, true));
    }

    [Test]
    public async Task TransactWrite_CommandOfALoop_AppliesEveryAction()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "counter", ["sk"] = "s", ["n"] = 1 }).SendAsync();
        await sandbox.Shop.Put(new Item { ["pk"] = "doomed", ["sk"] = "s" }).SendAsync();
        string[] created = ["a", "b", "c"];

        // Act
        await SendALoopAsync(sandbox, created, new TransactMarker[created.Length]);

        // Assert
        var counter = await sandbox.ReadAsync("counter");
        await Assert.That(((await sandbox.ReadAsync("a")) is not null, (await sandbox.ReadAsync("c")) is not null, counter!.GetNumber<int>("n"), await sandbox.ReadAsync("doomed")))
            .IsEqualTo((true, true, (int?)2, (Item?)null));
    }

    [Test]
    public async Task TransactWrite_CommandOfALoopThatFails_CancelsItAllAndTheMarkerTellsWhichRound()
    {
        // Arrange (the second round of the loop finds its key taken)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "counter", ["sk"] = "s", ["n"] = 1 }).SendAsync();
        await sandbox.Shop.Put(new Item { ["pk"] = "doomed", ["sk"] = "s" }).SendAsync();
        await sandbox.Shop.Put(new Item { ["pk"] = "b", ["sk"] = "s", ["holder"] = "bob" }).SendAsync();
        string[] created = ["a", "b", "c"];
        var markers = new TransactMarker[created.Length];

        // Act
        var failure = await Items.FailureOfAsync<TransactionCanceledException>(() => SendALoopAsync(sandbox, created, markers));

        // Assert
        await Assert.That((failure.ReasonFor(markers[0]), failure.ReasonFor(markers[1]), failure.ReasonFor(markers[2]), await sandbox.ReadAsync("a"), (await sandbox.ReadAsync("doomed")) is not null))
            .IsEqualTo((TransactionFailure.None, TransactionFailure.ConditionalCheckFailed, TransactionFailure.None, (Item?)null, true));
    }

    [Test]
    public async Task TransactWrite_ActionsOnTwoTables_AppliesBoth()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var other = await sandbox.CreateTableAsync();

        // Act
        var result = await sandbox.Client.TransactWrite()
            .Put(sandbox.Shop, new Item { ["pk"] = "a", ["sk"] = "s" })
            .Put(other, new Item { ["pk"] = "b", ["sk"] = "s" })
            .ReturnCapacity()
            .SendAsync();

        // Assert
        await Assert.That(((await sandbox.ReadAsync("a")) is not null, (await other.Get("b", "s").Consistent().SendAsync()).Found, result.ConsumedCapacity.Sum(static capacity => capacity.CapacityUnits))).IsEqualTo((true, true, 4.0));
    }

    [Test]
    public async Task TransactWrite_ConditionFails_MapsTheMarkersToTheReasonsInOrderWithTheItem()
    {
        // Arrange (the second of three actions fails on an item that exists)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "EMAIL#a@b.ch", ["sk"] = "MARKER", ["holder"] = "bob" }).SendAsync();
        var markers = new TransactMarker[3];

        // Act
        var failure = await Items.FailureOfAsync<TransactionCanceledException>(() => SendWithATakenMarkerAsync(sandbox, markers));

        // Assert
        await Assert.That((failure.ReasonFor(markers[0]), failure.ReasonFor(markers[1]), failure.ReasonFor(markers[2]), failure.ItemFor(markers[0]), failure.ItemFor(markers[1])?.GetString("holder")))
            .IsEqualTo((TransactionFailure.None, TransactionFailure.ConditionalCheckFailed, TransactionFailure.None, (Item?)null, (string?)"bob"));
    }

    [Test]
    public async Task TransactWrite_ConditionFails_ListsTheFailureAndAppliesNothing()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "EMAIL#a@b.ch", ["sk"] = "MARKER", ["holder"] = "bob" }).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<TransactionCanceledException>(() => SendWithATakenMarkerAsync(sandbox, new TransactMarker[3]));

        // Assert
        await Assert.That((failure.Failures.Single(), await sandbox.ReadAsync("customer"), failure.CancellationReasons.Count)).IsEqualTo((new TransactionActionFailure(1, TransactionFailure.ConditionalCheckFailed), (Item?)null, 3));
    }

    [Test]
    public async Task TransactWrite_Unique_AllowsTheValueOnce()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var marker = new Item { ["pk"] = "EMAIL#a@b.ch", ["sk"] = "MARKER" };

        // Act
        await sandbox.Client.TransactWrite().Unique(sandbox.Shop, marker, out _).SendAsync();
        var failure = await Items.FailureOfAsync<TransactionCanceledException>(async () => await sandbox.Client.TransactWrite().Unique(sandbox.Shop, marker, out _).SendAsync());

        // Assert
        await Assert.That(failure.Failures.Single().Reason).IsEqualTo(TransactionFailure.ConditionalCheckFailed);
    }

    [Test]
    public async Task TransactWrite_TypedItemOnFailure_ReadsAsTheEntity()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await profiles.Put(new Profile("alice", "Alice", 3)).SendAsync();
        var markers = new TransactMarker[1];

        // Act
        var failure = await Items.FailureOfAsync<TransactionCanceledException>(async () => await PutAgain(sandbox, profiles, markers).SendAsync());

        // Assert
        await Assert.That(failure.ItemFor(markers[0])!.To<Profile>()).IsEqualTo(new Profile("alice", "Alice", 3));
    }

    [Test]
    public async Task TransactWrite_SameTokenTwice_AppliesItOnce()
    {
        // Arrange (an ADD shows a second application; for 10 minutes DynamoDB answers a repeat with the first outcome)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var token = Guid.NewGuid().ToString();

        // Act
        await sandbox.Client.TransactWrite().Token(token).Update(sandbox.Shop, "counter", "s").Add("n", 1).SendAsync();
        await sandbox.Client.TransactWrite().Update(sandbox.Shop, "counter", "s").Add("n", 1).Token(token).SendAsync();

        // Assert
        await Assert.That((await sandbox.ReadAsync("counter"))!.GetNumber<int>("n")).IsEqualTo(1);
    }

    [Test]
    public async Task TransactWrite_NoToken_MakesEachSendATransactionOfItsOwn()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        await sandbox.Client.TransactWrite().Update(sandbox.Shop, "counter", "s").Add("n", 1).SendAsync();
        await sandbox.Client.TransactWrite().Update(sandbox.Shop, "counter", "s").Add("n", 1).SendAsync();

        // Assert
        await Assert.That((await sandbox.ReadAsync("counter"))!.GetNumber<int>("n")).IsEqualTo(2);
    }

    [Test]
    public async Task TransactWrite_SameTokenWithOtherActions_Throws()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var token = Guid.NewGuid().ToString();
        await sandbox.Client.TransactWrite().Put(sandbox.Shop, new Item { ["pk"] = "a", ["sk"] = "s" }).Token(token).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<IdempotentParameterMismatchException>(async () =>
            await sandbox.Client.TransactWrite().Put(sandbox.Shop, new Item { ["pk"] = "b", ["sk"] = "s" }).Token(token).SendAsync());

        // Assert
        await Assert.That(failure.OperationName).IsEqualTo("TransactWriteItems");
    }

    [Test]
    public async Task TransactWrite_TwoActionsOnOneItem_ThrowsWithTheErrorOfDynamoDB()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(async () =>
            await sandbox.Client.TransactWrite().Put(sandbox.Shop, new Item { ["pk"] = "a", ["sk"] = "s" }).Delete(sandbox.Shop, "a", "s").SendAsync());

        // Assert
        await Assert.That(failure.Message).Contains("multiple operations on one item", StringComparison.Ordinal);
    }

    [Test]
    public async Task TransactWrite_InvalidExpression_ExplainsThePlaceholdersOfTheAction()
    {
        // Arrange (a plain string in a hole is a value, so this holds no name at all, which DynamoDB refuses)
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(async () =>
            await DanglingAnd(sandbox.Client.TransactWrite().Delete(sandbox.Shop, "a", "s")).SendAsync());

        // Assert
        await Assert.That(failure.ExpressionNames!["TransactItems[0].#n0"]).IsEqualTo("x");
    }

    [Test]
    public async Task TransactGet_ItemsOfTwoTypes_ReadsEachThroughItsSlot()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await profiles.Put(new Profile("alice", "Alice")).SendAsync();
        await sandbox.Shop.Put(new Item { ["pk"] = "TENANT#1", ["sk"] = "QUOTA", ["remaining"] = 5 }).SendAsync();

        // Act
        var snapshot = await sandbox.Client.TransactGet()
            .Get(profiles, Profile.Keys.Of("alice"), out var profile)
            .Get(sandbox.Shop, "TENANT#1", "QUOTA", out var quota)
            .Get(sandbox.Shop, "missing", "s", out var missing)
            .ReturnCapacity()
            .SendAsync();

        // Assert
        await Assert.That((snapshot.Read(profile).Item, snapshot.Read(quota).Item!.GetNumber<int>("remaining"), snapshot.Read(missing).Found, snapshot.ConsumedCapacity.Sum(static capacity => capacity.CapacityUnits)))
            .IsEqualTo((new Profile("alice", "Alice"), (int?)5, false, 6.0));
    }

    [Test]
    public async Task TransactGet_TableWithoutASortKey_ReadsByThePartitionKey()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var sessions = sandbox.Client.Table(await sandbox.CreateTableAsync(TableSpec.PartitionOnly), "pk");
        await sessions.Put(new Item { ["pk"] = "a", ["v"] = 1 }).SendAsync();

        // Act
        var snapshot = await sandbox.Client.TransactGet().Get(sessions, "a", out var found).Get(sessions, "b", out var missing).SendAsync();

        // Assert
        await Assert.That((snapshot.Read(found).Item!.GetNumber<int>("v"), snapshot.Read(missing).Found)).IsEqualTo(((int?)1, false));
    }

    [Test]
    public async Task TransactGet_ReadAfterTheClientWentOn_StillHoldsItsSnapshot()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["v"] = 1 }).SendAsync();
        var snapshot = await sandbox.Client.TransactGet().Get(sandbox.Shop, "a", "s", out var slot).SendAsync();

        // Act
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["v"] = 2 }).SendAsync();
        await sandbox.ReadAsync("a");

        // Assert
        await Assert.That(snapshot.Read(slot).Item!.GetNumber<int>("v")).IsEqualTo(1);
    }

    [Test]
    public async Task TransactGet_MissingTable_Throws()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var missing = sandbox.Client.Table("table-that-does-not-exist", "pk");

        // Act
        var failure = await Items.FailureOfAsync<ResourceNotFoundException>(async () => await sandbox.Client.TransactGet().Get(missing, "a", default, out _).SendAsync());

        // Assert
        await Assert.That(failure.OperationName).IsEqualTo("TransactGetItems");
    }

    private static PutStep PutAgain(CommandSandbox sandbox, Table<Profile> profiles, TransactMarker[] markers) =>
        sandbox.Client.TransactWrite().Unique(profiles, new Profile("alice", "Other"), out markers[0]).ReturnOldOnFailure();

    private static async Task SendWithATakenMarkerAsync(CommandSandbox sandbox, TransactMarker[] markers) => await WithATakenMarker(sandbox, markers).SendAsync();

    private static async Task SendALoopAsync(CommandSandbox sandbox, string[] created, TransactMarker[] markers)
    {
        var transaction = sandbox.Client.TransactWrite();
        for (var i = 0; i < created.Length; i++)
        {
            transaction = transaction.Put(sandbox.Shop, new Item { ["pk"] = created[i], ["sk"] = "s" }, out markers[i]).IfNotExists();
        }

        transaction = transaction.Update(sandbox.Shop, "counter", "s").Add("n", 1);
        transaction = transaction.Delete(sandbox.Shop, "doomed", "s").IfExists();
        await transaction.SendAsync();
    }

    private static UpdateStep WithATakenMarker(CommandSandbox sandbox, TransactMarker[] markers) => sandbox.Client.TransactWrite()
        .Put(sandbox.Shop, new Item { ["pk"] = "customer", ["sk"] = "s" }, out markers[0])
        .Unique(sandbox.Shop, new Item { ["pk"] = "EMAIL#a@b.ch", ["sk"] = "MARKER", ["holder"] = "alice" }, out markers[1])
        .ReturnOldOnFailure()
        .Update(sandbox.Shop, "counter", "s", out markers[2])
        .Add("n", 1);

    // The analyzer refuses this text in an interpolated string, so the handler is driven by hand, as the compiler would.
    private static ConditionStep DanglingAnd(ConditionStep step)
    {
        var condition = new ExpressionHandler(0, 2, step);
        condition.AppendFormatted(Attr("x"));
        condition.AppendLiteral(" = ");
        condition.AppendFormatted((AttributeValue)"open");
        condition.AppendLiteral(" AND");
        return step.If(condition);
    }
}
