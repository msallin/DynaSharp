using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Idempotent counter</see>: a counter that a retry cannot count twice, because the item remembers the ids it has counted, and the write holds only for a new id. Every write pays for the whole
/// item, and the set of ids grows, so the set is windowed: an item per day starts empty.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class IdempotentCounterTests(Backend backend)
{
    private const string Signups = "signups";

    private static readonly DateOnly Today = new(2026, 11, 1);

    [Test]
    public async Task CountAsync_SameIdConcurrently_CountsOnce()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var tallies = sandbox.Shop.Of<DailyTally>();

        // Act
        var counted = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => CountAsync(tallies, Today, "request-1")));

        // Assert
        await Assert.That((counted.Count(static won => won), (await ReadAsync(tallies, Today)).Count)).IsEqualTo((1, 1L));
    }

    [Test]
    public async Task CountAsync_RetryAfterALostResponse_CountsOnce()
    {
        // Arrange (where the atomic counter counts twice, the retry here fails its own condition)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        using var flaky = new LostResponseClient(sandbox.Emulator);

        // Act
        await CountAsync(flaky.Client.Table<DailyTally>(sandbox.Shop.Name), Today, "request-1");

        // Assert
        await Assert.That((await ReadAsync(sandbox.Shop.Of<DailyTally>(), Today)).Count).IsEqualTo(1L);
    }

    [Test]
    public async Task CountAsync_SetOfIdsGrows_PaysForTheWholeItem()
    {
        // Arrange (each id is 300 bytes, so ten of them are more than 2 KB)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var tallies = sandbox.Shop.Of<DailyTally>();
        var first = await AddAsync(tallies, Today, new string('0', 300));
        for (var i = 1; i < 10; i++)
        {
            await CountAsync(tallies, Today, new string((char)('0' + i), 300));
        }

        // Act
        var tenth = await AddAsync(tallies, Today, new string('a', 300));

        // Assert
        await Assert.That((first.ConsumedCapacity!.CapacityUnits, tenth.ConsumedCapacity!.CapacityUnits)).IsEqualTo((1.0, 4.0));
    }

    [Test]
    public async Task CountAsync_NextWindow_StartsAnEmptySet()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var tallies = sandbox.Shop.Of<DailyTally>();
        var tomorrow = Today.AddDays(1);
        await CountAsync(tallies, Today, "request-1");

        // Act
        await CountAsync(tallies, tomorrow, "request-2");
        var windows = await tallies.BatchGet().Keys([DailyTally.Keys.Of(Signups, Today), DailyTally.Keys.Of(Signups, tomorrow)]).SendAsync();

        // Assert
        await Assert.That((windows.Items.Sum(static tally => tally.Count), windows.Items.Max(static tally => tally.Seen.Count))).IsEqualTo((2L, 1));
    }

    private static async Task<bool> CountAsync(Table<DailyTally> tallies, DateOnly day, string requestId)
    {
        try
        {
            await AddAsync(tallies, day, requestId);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    private static async Task<WriteResult<DailyTally>> AddAsync(Table<DailyTally> tallies, DateOnly day, string requestId) =>
        await tallies.Update(DailyTally.Keys.Of(Signups, day))
            .Add(DailyTally.Attrs.Count, 1L)
            .Add(DailyTally.Attrs.Seen, [requestId])
            .If(DailyTally.Attrs.Seen.Contains(requestId).Negate())
            .ReturnCapacity()
            .SendAsync();

    private static async Task<DailyTally> ReadAsync(Table<DailyTally> tallies, DateOnly day) => (await tallies.Get(DailyTally.Keys.Of(Signups, day)).Consistent().ItemAsync())!;
}

/// <summary>Only updates write this item, so a read takes what the key holds from the key.</summary>
[DynamoDbEntity]
[Discriminator("TALLY")]
[PartitionKey("TALLY#{Name}")]
[SortKey("DAY#{Day}")]
internal sealed partial record DailyTally
{
    public required string Name { get; init; }

    public required DateOnly Day { get; init; }

    public long Count { get; init; }

    public HashSet<string> Seen { get; init; } = [];
}
