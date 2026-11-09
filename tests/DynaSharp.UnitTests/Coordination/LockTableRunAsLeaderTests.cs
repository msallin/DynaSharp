using System.Net;
using DynaSharp.Coordination;
using DynaSharp.Coordination.Testing;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Telemetry;
using Microsoft.Extensions.Logging;

namespace DynaSharp.UnitTests.Coordination;

public sealed class LockTableRunAsLeaderTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    [Test]
    public async Task RunAsLeaderAsync_NullWork_ThrowsArgumentNullException()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var act = () => bench.Locks.RunAsLeaderAsync("leader", Lease, null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("work");
    }

    [Test]
    public async Task RunAsLeaderAsync_NoName_ThrowsArgumentException()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var act = () => bench.Locks.RunAsLeaderAsync(string.Empty, Lease, static (_, _) => Task.CompletedTask);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("name");
    }

    [Test]
    public async Task RunAsLeaderAsync_LeaseOutOfRange_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var act = () => bench.Locks.RunAsLeaderAsync("leader", TimeSpan.Zero, static (_, _) => Task.CompletedTask);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("leaseDuration");
    }

    [Test]
    public async Task RunAsLeaderAsync_WorkCompletes_ReturnsAndHandsTheLockOn()
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
        await bench.Locks.RunAsLeaderAsync("leader", Lease, Work);

        // Assert
        await Assert.That((token, bench.ItemOf("leader")!.ContainsKey("owner"))).IsEqualTo(((long?)1L, false));
    }

    [Test]
    public async Task RunAsLeaderAsync_WorkCompletes_LogsTheTermWithTheHolderLabelAndWithoutItemData()
    {
        // Arrange
        using var bench = new LockHarness(options: new() { HolderLabel = "pod-7" });

        // Act
        await bench.Locks.RunAsLeaderAsync("leader", Lease, static (_, _) => Task.CompletedTask);

        // Assert
        await Assert.That(string.Join("|", bench.LockLogs.Select(static entry => $"{entry.Level} {entry.EventId.Id} {entry.Message}"))).IsEqualTo(
            $"Information 45 This process leads on lock leader in table {bench.TableName}, with fencing token 1 (holder label pod-7).|Information 46 This process no longer leads on lock leader in table {bench.TableName} (work_returned, holder label pod-7).");
    }

    [Test]
    public async Task RunAsLeaderAsync_LeaseLostAndWonAgain_LogsBothTerms()
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

        var election = bench.Locks.RunAsLeaderAsync("leader", Lease, Work);
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
    public async Task RunAsLeaderAsync_WorkFails_LogsTheEndOfTheTerm()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        await Assert.That(() => bench.Locks.RunAsLeaderAsync("leader", Lease, static (_, _) => throw new InvalidOperationException("broken")))
            .ThrowsExactly<InvalidOperationException>();

        // Assert
        await Assert.That(bench.LockLogs[^1].Message).IsEqualTo($"This process no longer leads on lock leader in table {bench.TableName} (work_failed, holder label (null)).");
    }

    [Test]
    public async Task RunAsLeaderAsync_WorkFails_ReleasesTheLockAndThrows()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var act = () => bench.Locks.RunAsLeaderAsync("leader", Lease, static (_, _) => throw new InvalidOperationException("broken"));

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessage("broken", StringComparison.Ordinal);
        await Assert.That(bench.ItemOf("leader")!.ContainsKey("owner")).IsFalse();
    }

    [Test]
    public async Task RunAsLeaderAsync_LeaseLost_StopsTheWorkAndContendsAgain()
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

        var election = bench.Locks.RunAsLeaderAsync("leader", Lease, Work);
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
    public async Task RunAsLeaderAsync_WorkIgnoresTheLoss_ContendsAgainOnceItReturns()
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

        var election = bench.Locks.RunAsLeaderAsync("leader", Lease, Work);
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
    public async Task RunAsLeaderAsync_CancelledWhileLeading_StopsTheWorkReleasesAndThrows()
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

        var election = bench.Locks.RunAsLeaderAsync("leader", Lease, Work, stop.Token);
        await leading.Task;

        // Act
        await stop.CancelAsync();

        // Assert
        await Assert.That(() => election).Throws<OperationCanceledException>();
        await Assert.That(bench.ItemOf("leader")!.ContainsKey("owner")).IsFalse();
    }

    [Test]
    public async Task RunAsLeaderAsync_CancelledAndTheWorkThrowsSomethingElse_ThrowsOperationCanceledAndLogsIt()
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

        var election = bench.Locks.RunAsLeaderAsync("leader", Lease, Work, stop.Token);
        await leading.Task;

        // Act
        await stop.CancelAsync();

        // Assert
        await Assert.That(() => election).ThrowsExactly<OperationCanceledException>();
        await Assert.That((bench.ItemOf("leader")!.ContainsKey("owner"), bench.LockLogs.Single(static entry => entry.EventId.Id == 48).Message)).IsEqualTo(
            (false, $"The work of the leader on lock leader in table {bench.TableName} threw System.InvalidOperationException after the caller cancelled."));
    }

    [Test]
    public async Task RunAsLeaderAsync_CancelledWhileTheWorkReturns_Throws()
    {
        // Arrange
        using var bench = new LockHarness();
        using var stop = new CancellationTokenSource();

        // Act
        var act = () => bench.Locks.RunAsLeaderAsync("leader", Lease, (_, _) => stop.CancelAsync(), stop.Token);

        // Assert
        await Assert.That(act).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task RunAsLeaderAsync_CancelledWhileContending_Throws()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var holder = await bench.Locks.TryAcquireAsync("leader", Lease);
        using var stop = new CancellationTokenSource();
        var election = new LockTable(bench.Table).RunAsLeaderAsync("leader", Lease, static (_, _) => Task.CompletedTask, stop.Token);

        // Act
        await stop.CancelAsync();

        // Assert
        await Assert.That(() => election).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task RunAsLeaderAsync_TransientErrorWhileContending_LogsAndContendsAgain()
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
        await bench.RunAsync(Completion(bench.Locks.RunAsLeaderAsync("leader", Lease, Work)), TimeSpan.FromSeconds(1));

        // Assert
        var entry = bench.LockLogs.Single(entry => entry.EventId.Id == 44);
        await Assert.That((ran, entry.Category, entry.Level, entry.Message)).IsEqualTo(
            (true, "DynaSharp.Coordination.LockTable", LogLevel.Warning, $"Contending for lock leader in table {bench.TableName} failed with InternalServerError. The election contends again every 00:00:10, and logs again once DynamoDB answers."));
    }

    [Test]
    public async Task RunAsLeaderAsync_LongOutageWhileContending_WarnsOnceAndLogsTheEnd()
    {
        // Arrange (three failures in a row are one outage: one warning at its start, one information at the next answer)
        using var bench = new LockHarness();
        for (var i = 0; i < 3; i++)
        {
            bench.Handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Try again.", HttpStatusCode.InternalServerError));
        }

        // Act
        await bench.RunAsync(Completion(bench.Locks.RunAsLeaderAsync("leader", Lease, static (_, _) => Task.CompletedTask)), TimeSpan.FromSeconds(1));

        // Assert
        var outage = bench.LockLogs.Where(static entry => entry.EventId.Id is 44 or 49).Select(static entry => (entry.EventId.Id, entry.Level, entry.Message));
        (int, LogLevel, string)[] expected =
        [
            (44, LogLevel.Warning, $"Contending for lock leader in table {bench.TableName} failed with InternalServerError. The election contends again every 00:00:10, and logs again once DynamoDB answers."),
            (49, LogLevel.Information, $"Contending for lock leader in table {bench.TableName} works again, 00:00:30 after it failed and after 3 retries."),
        ];
        await Assert.That(outage).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task RunAsLeaderAsync_LongOutageWhileContending_CountsEveryRetry()
    {
        // Arrange
        using var bench = new LockHarness();
        using var metrics = MeasurementCapture<long>.ForTable(bench.TableName, "dynasharp.lock.contention_retries");
        for (var i = 0; i < 3; i++)
        {
            bench.Handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Try again.", HttpStatusCode.InternalServerError));
        }

        // Act
        await bench.RunAsync(Completion(bench.Locks.RunAsLeaderAsync("leader", Lease, static (_, _) => Task.CompletedTask)), TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That(metrics.All.Select(static measurement => (measurement.Unit, measurement.Value, measurement.TagText))).IsEquivalentTo(
            Enumerable.Repeat<(string?, long, string)>(("{retry}", 1L, $"db.collection.name={bench.TableName},error.type=InternalServerError"), 3));
    }

    [Test]
    public async Task RunAsLeaderAsync_OutageEndsWhileAnotherHolderLeads_LogsTheEndAtThePoll()
    {
        // Arrange (a follower ends its outage at the first answer, the lock held, not when it next leads)
        using var bench = new LockHarness();
        await bench.Locks.SimulateHolderAsync("leader", Lease, "another-pod");
        bench.Handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Try again.", HttpStatusCode.InternalServerError));
        var polled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bench.Intercept = (request, _) =>
        {
            if (request.Operation == "GetItem")
            {
                polled.TrySetResult();
            }

            return Task.FromResult<StubResponse?>(null);
        };
        using var stop = new CancellationTokenSource();
        var election = bench.Locks.RunAsLeaderAsync("leader", Lease, static (_, _) => Task.CompletedTask, stop.Token);

        // Act (the first poll by reading comes after the write that found the lock held)
        await bench.RunAsync(Completion(polled.Task), TimeSpan.FromSeconds(1));
        await stop.CancelAsync();
        await election.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        // Assert
        await Assert.That(bench.LockLogs.Where(static entry => entry.EventId.Id is 44 or 49).Select(static entry => entry.EventId.Id)).IsEquivalentTo([44, 49], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task RunAsLeaderAsync_SecondOutage_WarnsAgain()
    {
        // Arrange (an outage that ended is over: the next failure starts another, with its own warning)
        using var bench = new LockHarness();
        bench.Handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Try again.", HttpStatusCode.InternalServerError));
        var terms = 0;
        async Task Work(Lease lease, CancellationToken stop)
        {
            terms++;
            if (terms == 1)
            {
                // The lease is lost at the next renewal, and the release after it and the acquire after that fail.
                bench.Steal("leader", "thief");
                await Task.Delay(Timeout.Infinite, stop).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                bench.Handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Try again.", HttpStatusCode.InternalServerError));
                bench.Handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Try again.", HttpStatusCode.InternalServerError));
            }
        }

        // Act
        await bench.RunAsync(Completion(bench.Locks.RunAsLeaderAsync("leader", Lease, Work)), TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That(bench.LockLogs.Where(static entry => entry.EventId.Id is 44 or 49).Select(static entry => entry.EventId.Id)).IsEquivalentTo([44, 49, 44, 49], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    [Arguments(typeof(TimeoutException))]
    [Arguments(typeof(HttpRequestException))]
    [Arguments(typeof(TaskCanceledException))]
    public async Task RunAsLeaderAsync_NetworkFailureWhileContending_ContendsAgain(Type failure)
    {
        // Arrange (a TaskCanceledException that the caller did not ask for is what the connect timeout of SocketsHttpHandler throws)
        using var bench = new LockHarness();
        var failed = false;
        bench.Intercept = (_, _) =>
        {
            if (failed)
            {
                return Task.FromResult<StubResponse?>(null);
            }

            failed = true;
            Exception exception = failure == typeof(TimeoutException)
                ? new TimeoutException()
                : failure == typeof(HttpRequestException)
                    ? new HttpRequestException()
                    : new TaskCanceledException("The connection attempt timed out.", new TimeoutException());
            throw exception;
        };
        var ran = false;
        Task Work(Lease lease, CancellationToken stop)
        {
            ran = true;
            return Task.CompletedTask;
        }

        // Act
        await bench.RunAsync(Completion(bench.Locks.RunAsLeaderAsync("leader", Lease, Work)), TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That(ran).IsTrue();
    }

    [Test]
    public async Task RunAsLeaderAsync_ItemAtTheKeyIsNoLock_EndsWithInvalidOperationException()
    {
        // Arrange
        using var bench = new LockHarness();
        bench.Fake.Put(bench.TableName, new Item(bench.KeyOf("leader")) { ["owner"] = "someone" });

        // Act
        var act = () => bench.Locks.RunAsLeaderAsync("leader", Lease, static (_, _) => Task.CompletedTask);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("is not a lock item written by LockTable", StringComparison.Ordinal);
    }

    [Test]
    public async Task IsTransient_NoCredentialsYet_IsTransient()
    {
        // Arrange (the credentials of a pod can arrive after it starts, and an expired token is replaced)
        var exception = new CredentialsUnavailableException("No credentials yet.");

        // Act
        var transient = Leadership.IsTransient(exception);

        // Assert
        await Assert.That(transient).IsTrue();
    }

    [Test]
    public async Task RunAsLeaderAsync_AcquireAnsweredAfterTheLease_StartsNoTermAndContendsAgain()
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
        await bench.Locks.RunAsLeaderAsync("leader", Lease, Work).WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        await Assert.That((string.Join(",", terms), bench.LockLogs.Count(static entry => entry.EventId.Id == 45))).IsEqualTo(("2", 1));
    }

    [Test]
    public async Task RunAsLeaderAsync_TableMissing_Throws()
    {
        // Arrange
        using var bench = new LockHarness();
        bench.Fake.DeleteTable(bench.TableName);

        // Act
        var act = () => bench.Locks.RunAsLeaderAsync("leader", Lease, static (_, _) => Task.CompletedTask);

        // Assert
        await Assert.That(act).ThrowsExactly<ResourceNotFoundException>();
    }

    [Test]
    public async Task RunAsLeaderAsync_WorkThatDoesNotCompleteAtOnce_ResumesWithoutTheContextOfTheCaller()
    {
        // Arrange (the acquire is answered at once, so the work starts on the context of the caller)
        using var bench = new LockHarness();
        var context = new CountingSynchronizationContext();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        var election = context.Start(() => bench.Locks.RunAsLeaderAsync("leader", Lease, (_, _) => done.Task));
        done.SetResult();
        await election;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task RunAsLeaderAsync_LeaseLost_LogsTheEndOfTheTermAsLost()
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

        var election = bench.Locks.RunAsLeaderAsync("leader", Lease, Work);
        var first = await leading.Task;
        var beat = first.NextBeat;
        bench.Steal("leader", "thief");
        bench.Clock.Advance(Lease / 3);
        await beat;

        // Act
        await bench.RunAsync(Completion(election), TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That(bench.LockLogs.First(static entry => entry.EventId.Id == 46).Message).IsEqualTo(
            $"This process no longer leads on lock leader in table {bench.TableName} (lease_lost, holder label (null)).");
    }

    [Test]
    public async Task RunAsLeaderAsync_CancelledWhileLeading_LogsTheEndOfTheTermAsCancelled()
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

        var election = bench.Locks.RunAsLeaderAsync("leader", Lease, Work, stop.Token);
        await leading.Task;

        // Act
        await stop.CancelAsync();
        await election.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        // Assert
        await Assert.That(bench.LockLogs.Single(static entry => entry.EventId.Id == 46).Message).IsEqualTo(
            $"This process no longer leads on lock leader in table {bench.TableName} (cancelled, holder label (null)).");
    }

    private static async Task<bool> Completion(Task task)
    {
        await task;
        return true;
    }
}
