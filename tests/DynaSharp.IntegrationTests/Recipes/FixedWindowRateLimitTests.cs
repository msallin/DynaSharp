using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Fixed-window rate limit</see>: a counter per caller and minute, raised with a condition on the limit, so concurrent calls cannot pass it. The next minute is another item, and TTL takes the old ones away.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class FixedWindowRateLimitTests(Backend backend)
{
    private const int Limit = 5;
    private const string Caller = "api-key-1";

    private static readonly DateTimeOffset Noon = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task TryAdmitAsync_ConcurrentCalls_AdmitsUpToTheLimit()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var windows = sandbox.Shop.Of<RateWindow>();
        var clock = new ManualClock(Noon);

        // Act
        var admitted = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => TryAdmitAsync(windows, clock)));

        // Assert
        await Assert.That(admitted.Count(static passed => passed)).IsEqualTo(Limit);
    }

    [Test]
    public async Task TryAdmitAsync_NextWindow_StartsOver()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var windows = sandbox.Shop.Of<RateWindow>();
        var clock = new ManualClock(Noon);
        for (var call = 0; call < Limit; call++)
        {
            await TryAdmitAsync(windows, clock);
        }

        // Act
        var refused = await TryAdmitAsync(windows, clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        var admitted = await TryAdmitAsync(windows, clock);

        // Assert
        await Assert.That((refused, admitted)).IsEqualTo((false, true));
    }

    [Test]
    public async Task TryAdmitAsync_FirstCall_SetsTheExpiryAndLaterCallsKeepIt()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var windows = sandbox.Shop.Of<RateWindow>();
        var clock = new ManualClock(Noon);
        await TryAdmitAsync(windows, clock);

        // Act
        clock.Advance(TimeSpan.FromSeconds(30));
        await TryAdmitAsync(windows, clock);
        var window = await windows.Get(RateWindow.Keys.Of(Caller, Noon)).Consistent().ItemAsync();

        // Assert
        await Assert.That((window!.Hits, window.ExpiresAt)).IsEqualTo((2, (DateTimeOffset?)Noon.AddMinutes(2)));
    }

    private static async Task<bool> TryAdmitAsync(Table<RateWindow> windows, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        try
        {
            await windows.Update(RateWindow.Keys.Of(Caller, now))
                .Add(RateWindow.Attrs.Hits, 1)
                .SetIfNotExists(RateWindow.Attrs.ExpiresAt, now.AddMinutes(2))
                .If(RateWindow.Attrs.Hits.NotExists().Or(RateWindow.Attrs.Hits.Lt(Limit)))
                .SendAsync();
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }
}

/// <summary>Only updates write this item, so a read takes the caller from the key. The window cannot come from there, because its format drops the seconds. So it is not required, and an item reads with the default.</summary>
[DynamoDbEntity]
[Discriminator("RATE_WINDOW")]
[PartitionKey("RATE#{Caller}")]
[SortKey("WINDOW#{Window:yyyy-MM-ddTHH:mm}")]
internal sealed partial record RateWindow
{
    public required string Caller { get; init; }

    public DateTimeOffset Window { get; init; }

    public int Hits { get; init; }

    [DynamoDbAttribute("ttl")]
    [DynamoDbConverter(typeof(EpochSecondsConverter))]
    public DateTimeOffset? ExpiresAt { get; init; }
}
