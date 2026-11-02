using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Recipes;
using DynaSharp.IntegrationTests.Support;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>
/// <c>RetryOnConflictAsync</c> against a service that really refuses a stale write: writers that meet on one item, and what a lost response does to a run. Neither emulator produces
/// <c>TransactionConflict</c>, so that reason is tested with stubs. A failed condition means that another writer got through, so no writer needs more runs than there are writers.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class ConflictRetryTests(Backend backend)
{
    private const int Writers = 8;
    private const string Shared = "shared";

    private static readonly string OnePerWriter = string.Join(",", Enumerable.Range(1, Writers));

    [Test]
    public async Task RetryOnConflictAsync_ConcurrentPuts_GiveEachWriterOneResult()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var tallies = await StartAsync(sandbox);

        // Act
        var results = await RunWritersAsync(() => sandbox.Client.RetryOnConflictAsync(
            async ct =>
            {
                var tally = await tallies.Get(Tally.Keys.Of(Shared)).Consistent().ItemAsync(ct);
                await tallies.Put(tally! with { Count = tally.Count + 1 }).SendAsync(ct);
                return tally.Count + 1;
            },
            maxAttempts: Writers));

        // Assert
        await Assert.That((results, await StoredAsync(tallies))).IsEqualTo((OnePerWriter, (Writers, Writers + 1L)));
    }

    [Test]
    public async Task RetryOnConflictAsync_ConcurrentPutsOnATable_GiveEachWriterOneResult()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var tallies = await StartAsync(sandbox);

        // Act
        var results = await RunWritersAsync(() => tallies.RetryOnConflictAsync(
            async ct =>
            {
                var tally = await tallies.Get(Tally.Keys.Of(Shared)).Consistent().ItemAsync(ct);
                await tallies.Put(tally! with { Count = tally.Count + 1 }).SendAsync(ct);
                return tally.Count + 1;
            },
            maxAttempts: Writers));

        // Assert
        await Assert.That((results, await StoredAsync(tallies))).IsEqualTo((OnePerWriter, (Writers, Writers + 1L)));
    }

    [Test]
    public async Task RetryOnConflictAsync_ConcurrentUpdatesWithAVersion_GiveEachWriterOneResult()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var tallies = await StartAsync(sandbox);

        // Act
        var results = await RunWritersAsync(() => sandbox.Client.RetryOnConflictAsync(
            async ct =>
            {
                var tally = await tallies.Get(Tally.Keys.Of(Shared)).Consistent().ItemAsync(ct);
                await tallies.Update(Tally.Keys.Of(Shared)).Set(Tally.Attrs.Count, tally!.Count + 1).IfVersion(tally.Revision).SendAsync(ct);
                return tally.Count + 1;
            },
            maxAttempts: Writers));

        // Assert
        await Assert.That((results, await StoredAsync(tallies))).IsEqualTo((OnePerWriter, (Writers, Writers + 1L)));
    }

    [Test]
    public async Task RetryOnConflictAsync_ConcurrentTransactions_GiveEachWriterOneResult()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var tallies = await StartAsync(sandbox);
        var entries = sandbox.Shop.Of<TallyEntry>();

        // Act (a transaction fails as a whole, so an entry exists for exactly the runs that got through)
        var results = await RunWritersAsync(() => sandbox.Client.RetryOnConflictAsync(
            async ct =>
            {
                var tally = await tallies.Get(Tally.Keys.Of(Shared)).Consistent().ItemAsync(ct);
                var count = tally!.Count + 1;
                await sandbox.Client.TransactWrite()
                    .Update(tallies, Tally.Keys.Of(Shared)).Set(Tally.Attrs.Count, count).IfVersion(tally.Revision)
                    .Put(entries, new TallyEntry { Number = count }).IfNotExists()
                    .SendAsync(ct);
                return count;
            },
            maxAttempts: Writers));
        var stored = await entries.Query(TallyEntry.Keys.Pk.Format()).Consistent().CountAsync();

        // Assert
        await Assert.That((results, stored)).IsEqualTo((OnePerWriter, Writers));
    }

    [Test]
    public async Task RetryOnConflictAsync_TooFewRuns_SurfacesTheConflictAndLeavesOneWriterStanding()
    {
        // Arrange (everybody read before anybody writes, and one run allows no second look)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var tallies = await StartAsync(sandbox);
        var held = await tallies.Get(Tally.Keys.Of(Shared)).Consistent().ItemAsync();

        // Act
        var outcomes = await Task.WhenAll(Enumerable.Range(0, Writers).Select(async _ =>
        {
            try
            {
                await sandbox.Client.RetryOnConflictAsync(async ct => await tallies.Put(held! with { Count = 1 }).SendAsync(ct), maxAttempts: 1);
                return true;
            }
            catch (ConditionalCheckFailedException)
            {
                return false;
            }
        }));

        // Assert
        await Assert.That(outcomes.Count(static won => won)).IsEqualTo(1);
    }

    [Test]
    public async Task RetryOnConflictAsync_VersionReadOutsideTheDelegate_FailsInEveryRun()
    {
        // Arrange (the mistake the docs warn of: the retry changes nothing, because each run writes the same stale version. In a parameter it looks like a new item, which DYNA218 leaves alone)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var tallies = await StartAsync(sandbox);
        var stale = await tallies.Get(Tally.Keys.Of(Shared)).Consistent().ItemAsync();
        await tallies.Put(stale! with { Count = 1 }).SendAsync();
        var runs = 0;
        Task WriteAsync(Tally held) => sandbox.Client.RetryOnConflictAsync(
            async ct =>
            {
                runs++;
                await tallies.Put(held with { Count = 2 }).SendAsync(ct);
            });

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(() => WriteAsync(stale));

        // Assert
        await Assert.That((failure is not null, runs)).IsEqualTo((true, 5));
    }

    [Test]
    public async Task RetryOnConflictAsync_LostResponseOfAConditionalWrite_AppliesTheChangeTwice()
    {
        // Arrange (only the writes go through the client that loses a response: the first attempt takes effect, the second fails on it, and the delegate runs on top of its own write)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var tallies = await StartAsync(sandbox);
        using var flaky = new LostResponseClient(sandbox.Emulator);
        var writes = flaky.Client.Table<Tally>(sandbox.Shop.Name);

        // Act
        await flaky.Client.RetryOnConflictAsync(
            async ct =>
            {
                var tally = await tallies.Get(Tally.Keys.Of(Shared)).Consistent().ItemAsync(ct);
                await writes.Put(tally! with { Count = tally.Count + 1 }).SendAsync(ct);
            });

        // Assert
        await Assert.That((await StoredAsync(tallies)).Count).IsEqualTo(2);
    }

    [Test]
    public async Task RetryOnConflictAsync_LostResponseOfAWriteWithOneAttempt_FailsAndAppliesItOnce()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var tallies = await StartAsync(sandbox);
        using var flaky = new LostResponseClient(sandbox.Emulator);
        var writes = flaky.Client.Table<Tally>(sandbox.Shop.Name);

        // Act
        var failure = await Items.FailureOfAsync<HttpRequestException>(() => flaky.Client.RetryOnConflictAsync(
            async ct =>
            {
                var tally = await tallies.Get(Tally.Keys.Of(Shared)).Consistent().ItemAsync(ct);
                await writes.Put(tally! with { Count = tally.Count + 1 }).MaxAttempts(1).SendAsync(ct);
            }));

        // Assert (the caller cannot tell whether it counted, which is the price of exactly-once)
        await Assert.That((failure is not null, (await StoredAsync(tallies)).Count)).IsEqualTo((true, 1));
    }

    private static async Task<Table<Tally>> StartAsync(CommandSandbox sandbox)
    {
        var tallies = sandbox.Shop.Of<Tally>();
        await tallies.Put(new Tally { Id = Shared }).SendAsync();
        return tallies;
    }

    // Every writer ends with one number, so sorted they are 1, 2, 3 and so on, with nothing missing and nothing twice.
    private static async Task<string> RunWritersAsync(Func<Task<int>> writer)
    {
        var results = await Task.WhenAll(Enumerable.Range(0, Writers).Select(_ => writer()));
        return string.Join(",", results.Order());
    }

    private static async Task<(int Count, long Revision)> StoredAsync(Table<Tally> tallies)
    {
        var stored = await tallies.Get(Tally.Keys.Of(Shared)).Consistent().ItemAsync();
        return (stored!.Count, stored.Revision);
    }
}
