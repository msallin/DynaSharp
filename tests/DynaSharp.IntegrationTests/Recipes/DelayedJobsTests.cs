using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Delayed / scheduled jobs</see>: a job waits in the partition of the hour it is due, sorted by time, and a poller reads the hours that have come and claims each job with a delete that only one
/// poller can win. TTL is never the timer: it fires days late, and nothing says when.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class DelayedJobsTests(Backend backend)
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task PollAsync_JobsAcrossAnHour_ClaimsWhatIsDueAndLeavesTheFuture()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var jobs = sandbox.Shop.Of<ScheduledJob>();
        var clock = new ManualClock(Noon);
        await ScheduleAsync(jobs, Noon.AddMinutes(-5), "before noon");
        await ScheduleAsync(jobs, Noon.AddMinutes(5), "after noon");
        await ScheduleAsync(jobs, Noon.AddMinutes(50), "later");
        clock.Advance(TimeSpan.FromMinutes(10));

        // Act
        var claimed = await PollAsync(jobs, clock);

        // Assert (oldest first, and the job of 12:50 waits)
        await Assert.That(string.Join(",", claimed)).IsEqualTo("before noon,after noon");
    }

    [Test]
    public async Task PollAsync_LaterPoll_FindsTheNextJob()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var jobs = sandbox.Shop.Of<ScheduledJob>();
        var clock = new ManualClock(Noon);
        await ScheduleAsync(jobs, Noon.AddMinutes(30), "half past");
        var early = await PollAsync(jobs, clock);

        // Act
        clock.Advance(TimeSpan.FromMinutes(30));
        var onTime = await PollAsync(jobs, clock);

        // Assert
        await Assert.That((early.Count, onTime.Single())).IsEqualTo((0, "half past"));
    }

    [Test]
    public async Task PollAsync_ConcurrentPollers_RunEachJobOnce()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var jobs = sandbox.Shop.Of<ScheduledJob>();
        var clock = new ManualClock(Noon);
        for (var i = 0; i < 6; i++)
        {
            await ScheduleAsync(jobs, Noon.AddMinutes(-i - 1), $"job{i}");
        }

        // Act
        var claimed = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PollAsync(jobs, clock)));

        // Assert
        await Assert.That((claimed.Sum(static poller => poller.Count), claimed.SelectMany(static poller => poller).Distinct().Count())).IsEqualTo((6, 6));
    }

    private static async Task ScheduleAsync(Table<ScheduledJob> jobs, DateTimeOffset dueAt, string payload) =>
        await jobs.Put(new ScheduledJob { DueAt = dueAt, Id = Guid.NewGuid(), Payload = payload }).SendAsync();

    private static async Task<List<string>> PollAsync(Table<ScheduledJob> jobs, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        PartitionKey<ScheduledJob>[] hours = [ScheduledJob.Keys.Pk.Of(now.AddHours(-1)), ScheduledJob.Keys.Pk.Of(now)];
        var claimed = new List<string>();
        await foreach (var job in jobs.Query(hours, ScheduledJob.Keys.Sk.Le(now)).Consistent().ItemsAsync())
        {
            try
            {
                await jobs.Delete(ScheduledJob.Keys.Of(job.DueAt, job.Id)).IfExists().SendAsync();
                claimed.Add(job.Payload);
            }
            catch (ConditionalCheckFailedException)
            {
                // Another poller got it.
            }
        }

        return claimed;
    }
}

[DynamoDbEntity]
[Discriminator("JOB")]
[PartitionKey("JOBS#{DueAt:yyyy-MM-ddTHH}")]
[SortKey("{DueAt}#{Id}")]
internal sealed partial record ScheduledJob
{
    public required DateTimeOffset DueAt { get; init; }

    public required Guid Id { get; init; }

    public required string Payload { get; init; }
}
