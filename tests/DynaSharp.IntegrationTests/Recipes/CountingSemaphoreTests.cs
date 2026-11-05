using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Counting semaphore</see>: at most n holders of something across processes. A counter with a condition is the short form, and a holder that crashes leaks its permit for good. One item per
/// permit with a lease survives that: a permit whose lease ran out is free for the next conditional write.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class CountingSemaphoreTests(Backend backend)
{
    private const int Permits = 3;
    private const string Printer = "printer";

    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    private static readonly DateTimeOffset Noon = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AcquireAsync_MoreContendersThanPermits_AdmitsOnePerPermit()
    {
        // Arrange
        const int Contenders = 8;
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var permits = sandbox.Shop.Of<LeasedPermit>();
        var clock = new ManualClock(Noon);

        // Act
        var slots = await Task.WhenAll(Enumerable.Range(0, Contenders).Select(i => AcquireAsync(permits, clock, $"worker{i}")));

        // Assert
        await Assert.That(string.Join(",", slots.OfType<int>().Order())).IsEqualTo("0,1,2");
    }

    [Test]
    public async Task AcquireAsync_AfterARelease_GetsThePermit()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var permits = sandbox.Shop.Of<LeasedPermit>();
        var clock = new ManualClock(Noon);
        await FillAsync(permits, clock);

        // Act
        await permits.Delete(LeasedPermit.Keys.Of(Printer, 1)).If(LeasedPermit.Attrs.Holder.Eq("worker1")).SendAsync();
        var slot = await AcquireAsync(permits, clock, "next");

        // Assert
        await Assert.That(slot).IsEqualTo(1);
    }

    [Test]
    public async Task AcquireAsync_HolderCrashed_GetsThePermitOnceTheLeaseRanOut()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var permits = sandbox.Shop.Of<LeasedPermit>();
        var clock = new ManualClock(Noon);
        await FillAsync(permits, clock);

        // Act
        var whileHeld = await AcquireAsync(permits, clock, "next");
        clock.Advance(Lease + TimeSpan.FromSeconds(1));
        var afterTheLease = await AcquireAsync(permits, clock, "next");

        // Assert
        await Assert.That((whileHeld, afterTheLease)).IsEqualTo(((int?)null, (int?)0));
    }

    [Test]
    public async Task TryCountAsync_HolderCrashed_LeaksThePermitForGood()
    {
        // Arrange (nobody gives back what a crashed process took, and no time passing helps)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var counter = sandbox.Shop.Of<PermitCounter>();
        var admitted = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => TryCountAsync(counter)));

        // Act
        var next = await TryCountAsync(counter);

        // Assert
        await Assert.That((admitted.Count(static won => won), next)).IsEqualTo((Permits, false));
    }

    private static async Task FillAsync(Table<LeasedPermit> permits, ManualClock clock)
    {
        for (var worker = 0; worker < Permits; worker++)
        {
            await AcquireAsync(permits, clock, $"worker{worker}");
        }
    }

    private static async Task<int?> AcquireAsync(Table<LeasedPermit> permits, TimeProvider clock, string holder)
    {
        var now = clock.GetUtcNow();
        for (var slot = 0; slot < Permits; slot++)
        {
            try
            {
                await permits.Put(new LeasedPermit { Name = Printer, Slot = slot, Holder = holder, LeaseUntil = now + Lease })
                    .If(LeasedPermit.Attrs.LeaseUntil.NotExistsOrLe(now))
                    .SendAsync();
                return slot;
            }
            catch (ConditionalCheckFailedException)
            {
                // Held. The next one may be free.
            }
        }

        return null;
    }

    private static async Task<bool> TryCountAsync(Table<PermitCounter> counter)
    {
        try
        {
            await counter.Update(PermitCounter.Keys.Of(Printer))
                .Add(PermitCounter.Attrs.Held, 1)
                .If(PermitCounter.Attrs.Held.NotExistsOrLt(Permits))
                .SendAsync();
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }
}

[DynamoDbEntity]
[Discriminator("PERMIT")]
[PartitionKey("SEMAPHORE#{Name}")]
[SortKey("SLOT#{Slot:d2}")]
internal sealed partial record LeasedPermit
{
    public required string Name { get; init; }

    public required int Slot { get; init; }

    public required string Holder { get; init; }

    [DynamoDbConverter(typeof(EpochSecondsConverter))]
    public required DateTimeOffset LeaseUntil { get; init; }
}

[DynamoDbEntity]
[Discriminator("PERMIT_COUNTER")]
[PartitionKey("SEMAPHORE#{Name}")]
[SortKey("COUNTER")]
internal sealed partial record PermitCounter
{
    public required string Name { get; init; }

    public int Held { get; init; }
}
