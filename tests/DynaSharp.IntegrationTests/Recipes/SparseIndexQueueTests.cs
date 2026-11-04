using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Mapping;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Queue via sparse GSI</see>: a pending job is in a sparse index under one of a few shards, so the queue is no hot partition. A worker claims a
/// job with a conditional update that takes it out of that index and into a second one of claims, keyed by the end of the claim; completing deletes it, and a
/// reaper puts a job whose claim ran out back. The index is eventually consistent, so a worker can read a job that is gone: the condition of the claim is what
/// makes it correct. A queue that needs visibility timeouts per message, delays, a dead-letter queue or FIFO groups is SQS, not a table.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class SparseIndexQueueTests(Backend backend)
{
    private const int Shards = 2;

    private static readonly TimeSpan Claim = TimeSpan.FromMinutes(5);

    private static readonly DateTimeOffset Noon = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ClaimAsync_ConcurrentWorkers_ClaimEachJobOnce()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var jobs = sandbox.Shop.Of<QueuedJob>();
        var clock = new ManualClock(Noon);
        for (var i = 0; i < 6; i++)
        {
            await EnqueueAsync(jobs, $"job{i}");
        }

        // Act
        var claimed = await Task.WhenAll(Enumerable.Range(0, 3).Select(worker => DrainAsync(jobs, clock, $"worker{worker}")));

        // Assert
        await Assert.That(string.Join(",", claimed.SelectMany(static jobsOfAWorker => jobsOfAWorker).Order())).IsEqualTo("job0,job1,job2,job3,job4,job5");
    }

    [Test]
    public async Task CompleteAsync_ByTheWorkerThatClaimed_DeletesTheJob()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var jobs = sandbox.Shop.Of<QueuedJob>();
        var clock = new ManualClock(Noon);
        var id = await EnqueueAsync(jobs, "invoice");
        var job = await ClaimNextAsync(jobs, clock, "worker1");

        // Act
        await CompleteAsync(jobs, job!, "worker1");

        // Assert
        await Assert.That(await jobs.Get(QueuedJob.Keys.Of(id)).Consistent().ItemAsync()).IsNull();
    }

    [Test]
    public async Task ReapAsync_ClaimRanOut_PutsTheJobBackForAnotherWorker()
    {
        // Arrange (worker1 claims and stalls past its claim)
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var jobs = sandbox.Shop.Of<QueuedJob>();
        var clock = new ManualClock(Noon);
        await EnqueueAsync(jobs, "invoice");
        var stalled = await ClaimNextAsync(jobs, clock, "worker1");
        clock.Advance(Claim + TimeSpan.FromSeconds(1));

        // Act
        var reaped = await ReapAsync(jobs, clock);
        var again = await ClaimNextAsync(jobs, clock, "worker2");
        var late = await Items.FailureOfAsync<ConditionalCheckFailedException>(() => CompleteAsync(jobs, stalled!, "worker1"));

        // Assert (the stalled worker can no longer complete what another holds now)
        await Assert.That((reaped, again!.Payload, late is not null)).IsEqualTo((1, "invoice", true));
    }

    [Test]
    public async Task ReapAsync_ClaimStillRunning_LeavesIt()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var jobs = sandbox.Shop.Of<QueuedJob>();
        var clock = new ManualClock(Noon);
        await EnqueueAsync(jobs, "invoice");
        await ClaimNextAsync(jobs, clock, "worker1");
        clock.Advance(Claim - TimeSpan.FromSeconds(1));

        // Act
        var reaped = await ReapAsync(jobs, clock);

        // Assert
        await Assert.That((reaped, await ClaimNextAsync(jobs, clock, "worker2"))).IsEqualTo((0, (QueuedJob?)null));
    }

    [Test]
    public async Task Enqueue_InTheTransactionOfTheBusinessWrite_FailsWithIt()
    {
        // Arrange (the order exists, so its put fails, and the job must not be enqueued without it)
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var jobs = sandbox.Shop.Of<QueuedJob>();
        var order = new Item { ["pk"] = "ORDER#1", ["sk"] = "ORDER" };
        await sandbox.Shop.Put(order).SendAsync();
        var job = NewJob("ship order 1");

        // Act
        await Items.FailureOfAsync<TransactionCanceledException>(async () => await sandbox.Client.TransactWrite()
            .Put(sandbox.Shop, order).IfNotExists()
            .Put(jobs, job)
            .SendAsync());

        // Assert
        await Assert.That(await jobs.Get(QueuedJob.Keys.Of(job.Id)).Consistent().ItemAsync()).IsNull();
    }

    private static QueuedJob NewJob(string payload)
    {
        // A version-7 GUID orders the jobs of a shard by the time they were enqueued.
        var id = Guid.CreateVersion7();
        return new QueuedJob { Id = id, Shard = ShardOf(id), Payload = payload };
    }

    private static int ShardOf(Guid id) => (int)((uint)id.GetHashCode() % Shards);

    private static async Task<Guid> EnqueueAsync(Table<QueuedJob> jobs, string payload)
    {
        var job = NewJob(payload);
        await jobs.Put(job).SendAsync();
        return job.Id;
    }

    // Reads the pending jobs of every shard, oldest first, and claims the first one this worker wins.
    private static async Task<QueuedJob?> ClaimNextAsync(Table<QueuedJob> jobs, TimeProvider clock, string worker)
    {
        var pending = jobs.Index(QueuedJob.Indexes.Gsi2);
        for (var shard = 0; shard < Shards; shard++)
        {
            await foreach (var job in pending.Query(QueuedJob.Keys.Gsi2Pk.Of(shard)).ItemsAsync())
            {
                try
                {
                    var claimed = await jobs.Update(QueuedJob.Keys.Of(job.Id))
                        .Remove(QueuedJob.Attrs.Shard)
                        .Set(QueuedJob.Attrs.Owner, worker)
                        .Set(QueuedJob.Attrs.ClaimedUntil, clock.GetUtcNow() + Claim)
                        .If(QueuedJob.Attrs.Shard.Exists())
                        .ReturnNew()
                        .SendAsync();
                    return claimed.Item;
                }
                catch (ConditionalCheckFailedException)
                {
                    // Another worker claimed it, or the index still showed a job that is gone.
                }
            }
        }

        return null;
    }

    private static async Task<List<string>> DrainAsync(Table<QueuedJob> jobs, TimeProvider clock, string worker)
    {
        var done = new List<string>();
        while (await ClaimNextAsync(jobs, clock, worker) is { } job)
        {
            await CompleteAsync(jobs, job, worker);
            done.Add(job.Payload);
        }

        return done;
    }

    private static async Task CompleteAsync(Table<QueuedJob> jobs, QueuedJob job, string worker) =>
        await jobs.Delete(QueuedJob.Keys.Of(job.Id)).If(QueuedJob.Attrs.Owner.Eq(worker)).SendAsync();

    // Puts every job whose claim ran out back into its shard, unless its worker renewed or completed it since the read.
    private static async Task<int> ReapAsync(Table<QueuedJob> jobs, TimeProvider clock)
    {
        var reaped = 0;
        await foreach (var job in jobs.Index(QueuedJob.Indexes.Gsi1).Query(QueuedJob.Keys.Gsi1Pk.Of(), QueuedJob.Keys.Gsi1Sk.Lt(clock.GetUtcNow())).ItemsAsync())
        {
            try
            {
                await jobs.Update(QueuedJob.Keys.Of(job.Id))
                    .Remove(QueuedJob.Attrs.Owner)
                    .Remove(QueuedJob.Attrs.ClaimedUntil)
                    .Set(QueuedJob.Attrs.Shard, ShardOf(job.Id))
                    .If(QueuedJob.Attrs.ClaimedUntil.Eq(job.ClaimedUntil!.Value))
                    .SendAsync();
                reaped++;
            }
            catch (ConditionalCheckFailedException)
            {
                // Completed or reaped in the meantime.
            }
        }

        return reaped;
    }
}

/// <summary>A job of the queue: in <c>GSI2</c> while it is pending, in <c>GSI1</c> while it is claimed, and in neither once it is done.</summary>
[DynamoDbEntity]
[Discriminator("QUEUED_JOB")]
[PartitionKey("JOB#{Id}")]
[SortKey("JOB")]
[GlobalIndexKey("GSI2", PartitionKey = "PENDING#{Shard}", SortKey = "{Id}", Sparse = true)]
[GlobalIndexKey("GSI1", PartitionKey = "CLAIMED", SortKey = "{ClaimedUntil}", Sparse = true)]
internal sealed partial record QueuedJob
{
    public required Guid Id { get; init; }

    public int? Shard { get; init; }

    public string? Owner { get; init; }

    public DateTimeOffset? ClaimedUntil { get; init; }

    public required string Payload { get; init; }
}
