using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Token bucket</see>: a rate with bursts. The refill is computed on the client from the time since <c>lastRefill</c>, and the write holds only if that is still what the item says, so two callers cannot
/// both refill. Two callers can read one instant, though, and then <c>lastRefill</c> does not tell them apart, so the condition also holds the tokens they saw.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class TokenBucketTests(Backend backend)
{
    private const decimal Capacity = 3m;
    private const string Caller = "api-key-1";

    private static readonly DateTimeOffset Noon = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task TryTakeAsync_ConcurrentCallersOnOneInstant_TakeNoMoreThanTheBucketHolds()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var buckets = sandbox.Shop.Of<RateBucket>();
        var clock = new ManualClock(Noon);
        await buckets.Put(new RateBucket { Caller = Caller, Tokens = Capacity, LastRefill = Noon }).SendAsync();

        // Act
        var taken = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => TryTakeAsync(sandbox.Client, buckets, clock)));

        // Assert
        await Assert.That(taken.Count(static got => got)).IsEqualTo(3);
    }

    [Test]
    public async Task TryTakeAsync_TimePassed_RefillsAtTheRate()
    {
        // Arrange (one token a second)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var buckets = sandbox.Shop.Of<RateBucket>();
        var clock = new ManualClock(Noon);
        await buckets.Put(new RateBucket { Caller = Caller, Tokens = 0m, LastRefill = Noon }).SendAsync();
        var empty = await TryTakeAsync(sandbox.Client, buckets, clock);

        // Act
        clock.Advance(TimeSpan.FromSeconds(2));
        var first = await TryTakeAsync(sandbox.Client, buckets, clock);
        var second = await TryTakeAsync(sandbox.Client, buckets, clock);
        var third = await TryTakeAsync(sandbox.Client, buckets, clock);

        // Assert
        await Assert.That((empty, first, second, third)).IsEqualTo((false, true, true, false));
    }

    [Test]
    public async Task TryTakeAsync_LongIdle_StopsAtTheCapacity()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var buckets = sandbox.Shop.Of<RateBucket>();
        var clock = new ManualClock(Noon);
        await buckets.Put(new RateBucket { Caller = Caller, Tokens = 0m, LastRefill = Noon }).SendAsync();
        clock.Advance(TimeSpan.FromHours(1));

        // Act
        await TryTakeAsync(sandbox.Client, buckets, clock);

        // Assert
        await Assert.That((await buckets.Get(RateBucket.Keys.Of(Caller)).Consistent().ItemAsync())!.Tokens).IsEqualTo(Capacity - 1);
    }

    // Someone who takes a token since the read makes this count again from what is there now, and an empty bucket is then the answer. Each failure is a token gone, so three
    // tokens on a frozen clock take four runs at most, which the default allows.
    private static Task<bool> TryTakeAsync(DynamoDbClient client, Table<RateBucket> buckets, TimeProvider clock) =>
        client.RetryOnConflictAsync(async ct =>
        {
            var bucket = (await buckets.Get(RateBucket.Keys.Of(Caller)).Consistent().ItemAsync(ct))!;
            var now = clock.GetUtcNow();
            var tokens = Math.Min(Capacity, bucket.Tokens + (decimal)(now - bucket.LastRefill).TotalSeconds);
            if (tokens < 1)
            {
                return false;
            }

            await buckets.Update(RateBucket.Keys.Of(Caller))
                .Set(RateBucket.Attrs.Tokens, tokens - 1)
                .Set(RateBucket.Attrs.LastRefill, now)
                .If(RateBucket.Attrs.LastRefill.Eq(bucket.LastRefill).And(RateBucket.Attrs.Tokens.Eq(bucket.Tokens)))
                .SendAsync(ct);
            return true;
        });
}

[DynamoDbEntity]
[Discriminator("BUCKET")]
[PartitionKey("BUCKET#{Caller}")]
[SortKey("STATE")]
internal sealed partial record RateBucket
{
    public required string Caller { get; init; }

    public required decimal Tokens { get; init; }

    public required DateTimeOffset LastRefill { get; init; }
}
