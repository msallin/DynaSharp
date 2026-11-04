using System.Net;
using DynaSharp.Coordination;
using DynaSharp.Testing;
using Microsoft.Extensions.Logging;

namespace DynaSharp.UnitTests.Coordination;

public sealed class LeaderElectionTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    [Test]
    public async Task RunAsync_NullLocks_ThrowsArgumentNullException()
    {
        // Arrange
        LockTable? locks = null;

        // Act
        var act = () => LeaderElection.RunAsync(locks!, "leader", Lease, static (_, _) => Task.CompletedTask, CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("locks");
    }

    [Test]
    public async Task RunAsync_NullWork_ThrowsArgumentNullException()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var act = () => LeaderElection.RunAsync(bench.Locks, "leader", Lease, null!, CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("work");
    }

    [Test]
    public async Task RunAsync_NoName_ThrowsArgumentException()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var act = () => LeaderElection.RunAsync(bench.Locks, string.Empty, Lease, static (_, _) => Task.CompletedTask, CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("name");
    }

    [Test]
    public async Task RunAsync_LeaseOutOfRange_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var act = () => LeaderElection.RunAsync(bench.Locks, "leader", TimeSpan.Zero, static (_, _) => Task.CompletedTask, CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("leaseDuration");
    }

    [Test]
    public async Task RunAsync_WorkCompletes_ReturnsAndHandsTheLockOn()
    {
        // Arrange
        using var bench = new LockHarness();
        long? token = null;

        Task Work(Lease lease, CancellationToken stop)
        {
            token = lease.FencingToken;
            return Task.CompletedTask;
        }

        // Act
        await LeaderElection.RunAsync(bench.Locks, "leader", Lease, Work, CancellationToken.None);

        // Assert
        await Assert.That((token, bench.ItemOf("leader")!.ContainsKey("owner"))).IsEqualTo(((long?)1L, false));
    }

    [Test]
    public async Task RunAsync_WorkCompletes_LogsTheTermWithoutItemData()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        await LeaderElection.RunAsync(bench.Locks, "leader", Lease, static (_, _) => Task.CompletedTask, CancellationToken.None);

        // Assert
        await Assert.That(string.Join("|", bench.LockLogs.Select(static entry => $"{entry.Level} {entry.EventId.Id} {entry.Message}"))).IsEqualTo(
            $"Information 45 This process leads on lock leader in table {bench.TableName}, with fencing token 1.|Information 46 This process no longer leads on lock leader in table {bench.TableName} (work_returned).");
    }

    [Test]
    public async Task RunAsync_LeaseLostAndWonAgain_LogsBothTerms()
    {
        // Arrange
        using var bench = new LockHarness();
        var leading = new TaskCompletionSource<Lease>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Work(Lease lease, CancellationToken stop)
        {
            if (lease.FencingToken == 1)
            {
                leading.SetResult(lease);
                await Task.Delay(Timeout.Infinite, stop);
            }
        }

        var election = LeaderElection.RunAsync(bench.Locks, "leader", Lease, Work, CancellationToken.None);
        var first = await leading.Task;
        var beat = first.NextBeat;
        bench.Steal("leader", "thief");
        bench.Clock.Advance(Lease / 3);
        await beat;

        // Act
        await bench.RunAsync(Completion(election), TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That(string.Join(",", bench.LockLogs.Select(static entry => entry.EventId.Id))).IsEqualTo("45,40,47,46,41,45,46");
    }

    [Test]
    public async Task RunAsync_WorkFails_LogsTheEndOfTheTerm()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        await Assert.That(() => LeaderElection.RunAsync(bench.Locks, "leader", Lease, static (_, _) => throw new InvalidOperationException("broken"), CancellationToken.None))
            .ThrowsExactly<InvalidOperationException>();

        // Assert
        await Assert.That(bench.LockLogs[^1].Message).IsEqualTo($"This process no longer leads on lock leader in table {bench.TableName} (work_failed).");
    }

    [Test]
    public async Task RunAsync_WorkFails_ReleasesTheLockAndThrows()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var act = () => LeaderElection.RunAsync(bench.Locks, "leader", Lease, static (_, _) => throw new InvalidOperationException("broken"), CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessage("broken", StringComparison.Ordinal);
        await Assert.That(bench.ItemOf("leader")!.ContainsKey("owner")).IsFalse();
    }

    [Test]
    public async Task RunAsync_LeaseLost_StopsTheWorkAndContendsAgain()
    {
        // Arrange
        using var bench = new LockHarness();
        var terms = new List<long>();
        var leading = new TaskCompletionSource<Lease>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Work(Lease lease, CancellationToken stop)
        {
            terms.Add(lease.FencingToken);
            if (terms.Count == 1)
            {
                leading.SetResult(lease);
                await Task.Delay(Timeout.Infinite, stop);
            }
        }

        var election = LeaderElection.RunAsync(bench.Locks, "leader", Lease, Work, CancellationToken.None);
        var first = await leading.Task;

        // Act (another process takes the lock, and stops renewing it at once)
        var beat = first.NextBeat;
        bench.Steal("leader", "thief");
        bench.Clock.Advance(Lease / 3);
        await beat;
        await bench.RunAsync(Completion(election), TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That((string.Join(",", terms), first.LossReason)).IsEqualTo(("1,2", "taken_over"));
    }

    [Test]
    public async Task RunAsync_WorkIgnoresTheLoss_ContendsAgainOnceItReturns()
    {
        // Arrange
        using var bench = new LockHarness();
        var terms = new List<long>();
        var leading = new TaskCompletionSource<Lease>(TaskCreationOptions.RunContinuationsAsynchronously);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Work(Lease lease, CancellationToken stop)
        {
            terms.Add(lease.FencingToken);
            if (terms.Count == 1)
            {
                leading.SetResult(lease);
                await done.Task;
            }
        }

        var election = LeaderElection.RunAsync(bench.Locks, "leader", Lease, Work, CancellationToken.None);
        var first = await leading.Task;
        bench.Steal("leader", "thief");
        await first.RenewAsync();

        // Act
        done.SetResult();
        await bench.RunAsync(Completion(election), TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That(string.Join(",", terms)).IsEqualTo("1,2");
    }

    [Test]
    public async Task RunAsync_CancelledWhileLeading_StopsTheWorkReleasesAndThrows()
    {
        // Arrange
        using var bench = new LockHarness();
        using var stop = new CancellationTokenSource();
        var leading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Work(Lease lease, CancellationToken token)
        {
            leading.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        }

        var election = LeaderElection.RunAsync(bench.Locks, "leader", Lease, Work, stop.Token);
        await leading.Task;

        // Act
        await stop.CancelAsync();

        // Assert
        await Assert.That(() => election).Throws<OperationCanceledException>();
        await Assert.That(bench.ItemOf("leader")!.ContainsKey("owner")).IsFalse();
    }

    [Test]
    public async Task RunAsync_CancelledAndTheWorkThrowsSomethingElse_ThrowsOperationCanceledAndLogsIt()
    {
        // Arrange (the work catches its cancellation and throws an exception of its own, as a wrapper might)
        using var bench = new LockHarness();
        using var stop = new CancellationTokenSource();
        var leading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Work(Lease lease, CancellationToken token)
        {
            leading.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("stopped");
            }
        }

        var election = LeaderElection.RunAsync(bench.Locks, "leader", Lease, Work, stop.Token);
        await leading.Task;

        // Act
        await stop.CancelAsync();

        // Assert
        await Assert.That(() => election).ThrowsExactly<OperationCanceledException>();
        await Assert.That((bench.ItemOf("leader")!.ContainsKey("owner"), bench.LockLogs.Single(static entry => entry.EventId.Id == 48).Message)).IsEqualTo(
            (false, $"The work of the leader on lock leader in table {bench.TableName} threw System.InvalidOperationException after the election was cancelled."));
    }

    [Test]
    public async Task RunAsync_CancelledWhileTheWorkReturns_Throws()
    {
        // Arrange
        using var bench = new LockHarness();
        using var stop = new CancellationTokenSource();

        // Act
        var act = () => LeaderElection.RunAsync(bench.Locks, "leader", Lease, (_, _) => stop.CancelAsync(), stop.Token);

        // Assert
        await Assert.That(act).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task RunAsync_CancelledWhileContending_Throws()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var holder = await bench.Locks.TryAcquireAsync("leader", Lease);
        using var stop = new CancellationTokenSource();
        var election = LeaderElection.RunAsync(new LockTable(bench.Table), "leader", Lease, static (_, _) => Task.CompletedTask, stop.Token);

        // Act
        await stop.CancelAsync();

        // Assert
        await Assert.That(() => election).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task RunAsync_TransientErrorWhileContending_LogsAndContendsAgain()
    {
        // Arrange
        using var bench = new LockHarness();
        bench.Handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Try again.", HttpStatusCode.InternalServerError));
        var ran = false;
        Task Work(Lease lease, CancellationToken stop)
        {
            ran = true;
            return Task.CompletedTask;
        }

        // Act
        await bench.RunAsync(Completion(LeaderElection.RunAsync(bench.Locks, "leader", Lease, Work, CancellationToken.None)), TimeSpan.FromSeconds(1));

        // Assert
        var entry = bench.LockLogs.Single(entry => entry.EventId.Id == 44);
        await Assert.That((ran, entry.Category, entry.Level, entry.Message)).IsEqualTo(
            (true, "DynaSharp.Coordination.LockTable", LogLevel.Warning, $"Contending for lock leader in table {bench.TableName} failed with InternalServerError. The election contends again in 00:00:10."));
    }

    [Test]
    [Arguments(typeof(TimeoutException))]
    [Arguments(typeof(HttpRequestException))]
    public async Task RunAsync_NetworkFailureWhileContending_ContendsAgain(Type failure)
    {
        // Arrange
        using var bench = new LockHarness();
        var failed = false;
        bench.Intercept = (_, _) =>
        {
            if (failed)
            {
                return Task.FromResult<StubResponse?>(null);
            }

            failed = true;
            throw failure == typeof(TimeoutException) ? new TimeoutException() : new HttpRequestException();
        };
        var ran = false;
        Task Work(Lease lease, CancellationToken stop)
        {
            ran = true;
            return Task.CompletedTask;
        }

        // Act
        await bench.RunAsync(Completion(LeaderElection.RunAsync(bench.Locks, "leader", Lease, Work, CancellationToken.None)), TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That(ran).IsTrue();
    }

    [Test]
    public async Task RunAsync_ItemAtTheKeyIsNoLock_EndsWithInvalidOperationException()
    {
        // Arrange
        using var bench = new LockHarness();
        bench.Fake.Put(bench.TableName, new Item(bench.KeyOf("leader")) { ["owner"] = "someone" });

        // Act
        var act = () => LeaderElection.RunAsync(bench.Locks, "leader", Lease, static (_, _) => Task.CompletedTask, CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("is not a lock item written by LockTable", StringComparison.Ordinal);
    }

    [Test]
    public async Task IsTransient_NoCredentialsYet_IsTransient()
    {
        // Arrange (the credentials of a pod can arrive after it starts, and an expired token is replaced)
        var exception = new CredentialsUnavailableException("No credentials yet.");

        // Act
        var transient = LeaderElection.IsTransient(exception);

        // Assert
        await Assert.That(transient).IsTrue();
    }

    [Test]
    public async Task RunAsync_AcquireAnsweredAfterTheLease_StartsNoTermAndContendsAgain()
    {
        // Arrange (the answer of the first acquire comes a whole lease after it was sent)
        using var bench = new LockHarness();
        var slow = true;
        bench.Intercept = (request, _) =>
        {
            if (slow && request.Operation == "UpdateItem")
            {
                slow = false;
                bench.Fake.Answer(request);
                bench.Clock.Advance(Lease);
                return Task.FromResult<StubResponse?>(StubResponse.Json("{}"));
            }

            return Task.FromResult<StubResponse?>(null);
        };
        var terms = new List<long>();
        Task Work(Lease lease, CancellationToken stop)
        {
            terms.Add(lease.FencingToken);
            return Task.CompletedTask;
        }

        // Act
        await LeaderElection.RunAsync(bench.Locks, "leader", Lease, Work, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        await Assert.That((string.Join(",", terms), bench.LockLogs.Count(static entry => entry.EventId.Id == 45))).IsEqualTo(("2", 1));
    }

    [Test]
    public async Task RunAsync_TableMissing_Throws()
    {
        // Arrange
        using var bench = new LockHarness();
        bench.Fake.DeleteTable(bench.TableName);

        // Act
        var act = () => LeaderElection.RunAsync(bench.Locks, "leader", Lease, static (_, _) => Task.CompletedTask, CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<ResourceNotFoundException>();
    }

    private static async Task<bool> Completion(Task task)
    {
        await task;
        return true;
    }
}
