using DynaSharp.Coordination;

namespace DynaSharp.UnitTests.Coordination;

public sealed class LeaseTransactExtensionsTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    [Test]
    public async Task IsHeld_LeaseHeld_CommitsTheTransaction()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Act
        await bench.Client.TransactWrite().IsHeld(lease!).Put(bench.Table, Report(lease!)).SendAsync();

        // Assert
        await Assert.That(bench.Fake.Get(bench.TableName, new() { ["pk"] = "REPORT", ["sk"] = "1" })?.GetNumber<long>("fence")).IsEqualTo(1L);
    }

    [Test]
    public async Task IsHeld_TakenOver_CancelsTheTransactionAtTheCheck()
    {
        // Arrange (the holder's clock stands still, so it does not know that its lease ran out)
        using var bench = new LockHarness();
        var stale = bench.OtherProcess(new ManualTimeProvider());
        await using var holder = await stale.TryAcquireAsync("job", Lease);
        await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(Lease);
        await using var next = await bench.Locks.TryAcquireAsync("job", Lease);
        TransactMarker held = default;

        // Act
        var act = async () => await bench.Client.TransactWrite().IsHeld(holder!, out held).Put(bench.Table, Report(holder!)).SendAsync();

        // Assert
        var exception = await Assert.That(act).ThrowsExactly<TransactionCanceledException>();
        await Assert.That((exception!.ReasonFor(held), holder!.Ended.IsCancellationRequested, bench.Fake.Get(bench.TableName, new() { ["pk"] = "REPORT", ["sk"] = "1" })))
            .IsEqualTo((TransactionFailure.ConditionalCheckFailed, false, (Item?)null));
    }

    [Test]
    public async Task IsHeld_SameLeaseTwice_ThrowsInvalidOperationExceptionBeforeSending()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        var requests = bench.Handler.Requests.Count;

        // Act
        var failure = TestClient.Catch(() => _ = bench.Client.TransactWrite().IsHeld(lease!).Put(bench.Table, Report(lease!)).IsHeld(lease!));

        // Assert
        await Assert.That((failure is InvalidOperationException, failure.Message.Contains("as action 1 (ConditionCheck)", StringComparison.Ordinal), bench.Handler.Requests.Count))
            .IsEqualTo((true, true, requests));
    }

    [Test]
    public async Task IsHeld_Released_CancelsTheTransaction()
    {
        // Arrange
        using var bench = new LockHarness();
        var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        await lease!.DisposeAsync();

        // Act
        var act = async () => await bench.Client.TransactWrite().IsHeld(lease).Put(bench.Table, Report(lease)).SendAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<TransactionCanceledException>();
    }

    [Test]
    public async Task IsHeld_AnyLease_ChecksOwnerAndFencingTokenOfTheLockItem()
    {
        // Arrange
        using var bench = new LockHarness(sortKey: false);
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Act
        var json = bench.Client.TransactWrite().IsHeld(lease!).ToRequestJson();
        var items = json[..(json.IndexOf("}]", StringComparison.Ordinal) + 2)];

        // Assert
        await Assert.That(items).IsEqualTo(
            """{"TransactItems":[{"ConditionCheck":{"TableName":"TABLE","Key":{"pk":{"S":"LOCK#job"}},"ConditionExpression":"(#n0 = :v0 AND #n1 = :v1)","ExpressionAttributeNames":{"#n0":"owner","#n1":"fence"},"ExpressionAttributeValues":{":v0":{"S":"OWNER"},":v1":{"N":"1"}}}}]""".Replace("TABLE", bench.TableName, StringComparison.Ordinal).Replace("OWNER", lease!.Owner, StringComparison.Ordinal));
    }

    [Test]
    public async Task IsHeld_NullLease_ThrowsArgumentNullException()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var act = () => { _ = bench.Client.TransactWrite().IsHeld(null!); };

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("lease");
    }

    [Test]
    public async Task IsHeld_CurrentLease_CommitsTheTransaction()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        await bench.Locks.RunAsLeaderAsync("job", Lease, async (lease, stop) =>
            await bench.Client.TransactWrite().IsHeld().Put(bench.Table, Report(lease)).SendAsync(stop));

        // Assert
        await Assert.That(bench.Fake.Get(bench.TableName, new() { ["pk"] = "REPORT", ["sk"] = "1" })?.GetNumber<long>("fence")).IsEqualTo(1L);
    }

    [Test]
    public async Task IsHeld_CurrentLease_ChecksWhatTheExplicitLeaseChecks()
    {
        // Arrange
        using var bench = new LockHarness();
        string? implicitJson = null;
        string? explicitJson = null;

        // Act
        await bench.Locks.RunAsLeaderAsync("job", Lease, (lease, _) =>
        {
            implicitJson = bench.Client.TransactWrite().IsHeld().ToRequestJson();
            explicitJson = bench.Client.TransactWrite().IsHeld(lease).ToRequestJson();
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(Without(implicitJson!, "ClientRequestToken")).IsEqualTo(Without(explicitJson!, "ClientRequestToken"));
    }

    [Test]
    public async Task IsHeld_CurrentLeaseTakenOver_NamesTheCheckByItsMarker()
    {
        // Arrange
        using var bench = new LockHarness();
        TransactionFailure? reason = null;

        // Act
        await bench.Locks.RunAsLeaderAsync("job", Lease, async (lease, stop) =>
        {
            bench.Steal("job", "thief");
            TransactMarker held = default;
            try
            {
                await bench.Client.TransactWrite().IsHeld(out held).Put(bench.Table, Report(lease)).SendAsync(stop);
            }
            catch (TransactionCanceledException exception)
            {
                reason = exception.ReasonFor(held);
            }
        });

        // Assert
        await Assert.That(reason).IsEqualTo(TransactionFailure.ConditionalCheckFailed);
    }

    [Test]
    public async Task IsHeld_NoCurrentLease_ThrowsInvalidOperationException()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var act = () => { _ = bench.Client.TransactWrite().IsHeld(); };

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("No lease is current; pass the lease, or start this work inside RunAsLeaderAsync.", StringComparison.Ordinal);
    }

    [Test]
    public async Task IsHeld_NoCurrentLeaseWithAMarker_ThrowsInvalidOperationException()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var act = () => { _ = bench.Client.TransactWrite().IsHeld(out _); };

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    private static Item Report(Lease lease) => new() { ["pk"] = "REPORT", ["sk"] = "1", ["fence"] = lease.FencingToken };

    private static string Without(string json, string member)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        node.Remove(member);
        return node.ToJsonString();
    }
}
