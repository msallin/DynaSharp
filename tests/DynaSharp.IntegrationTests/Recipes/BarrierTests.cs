using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Barrier / rendezvous</see>: n parties wait for each other, and the last to arrive starts the next step. Each adds its name to a set in one item. A set makes a repeated arrival harmless, and the
/// new item comes back with the write, so exactly one party sees the set complete.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class BarrierTests(Backend backend)
{
    private const int Parties = 6;
    private const string Nightly = "nightly";

    [Test]
    public async Task ArriveAsync_ConcurrentParties_LetsExactlyOneSeeTheBarrierComplete()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var barriers = sandbox.Shop.Of<Rendezvous>();
        await barriers.Put(new Rendezvous { Name = Nightly, Parties = Parties }).SendAsync();

        // Act
        var complete = await Task.WhenAll(Enumerable.Range(0, Parties).Select(i => ArriveAsync(barriers, $"party{i}")));

        // Assert
        await Assert.That(complete.Count(static last => last)).IsEqualTo(1);
    }

    [Test]
    public async Task ArriveAsync_SamePartyTwice_CountsOnce()
    {
        // Arrange (a party that retries after a timeout must not stand in for one that never came)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var barriers = sandbox.Shop.Of<Rendezvous>();
        await barriers.Put(new Rendezvous { Name = Nightly, Parties = 2 }).SendAsync();

        // Act
        var first = await ArriveAsync(barriers, "party0");
        var again = await ArriveAsync(barriers, "party0");
        var other = await ArriveAsync(barriers, "party1");

        // Assert
        await Assert.That((first, again, other)).IsEqualTo((false, false, true));
    }

    private static async Task<bool> ArriveAsync(Table<Rendezvous> barriers, string party)
    {
        var barrier = (await barriers.Update(Rendezvous.Keys.Of(Nightly)).Add(Rendezvous.Attrs.Arrived, [party]).IfExists().ReturnNew().SendAsync()).Item!;
        return barrier.Arrived.Count == barrier.Parties;
    }
}

[DynamoDbEntity]
[Discriminator("BARRIER")]
[PartitionKey("BARRIER#{Name}")]
[SortKey("STATE")]
internal sealed partial record Rendezvous
{
    public required string Name { get; init; }

    public required int Parties { get; init; }

    public HashSet<string> Arrived { get; init; } = [];
}
