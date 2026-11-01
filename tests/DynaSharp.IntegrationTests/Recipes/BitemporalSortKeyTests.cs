using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Bitemporal sort key</see>: two times, when a fact holds and when it was recorded, both in the key, so a correction is a new item and nothing is overwritten. "What did we believe on that day about that day?" is one
/// backward read: the newest valid time up to the day, and a filter on the recording time.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class BitemporalSortKeyTests(Backend backend)
{
    private const string Keyboard = "KB-01";

    private static readonly DateTimeOffset January = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset February = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset MidFebruary = February.AddDays(14);

    private static readonly DateTimeOffset March = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AsOfAsync_AfterACorrection_TellsWhatHoldsNowAndWhatWasBelievedThen()
    {
        // Arrange (the price of February was recorded wrongly and corrected in March)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var prices = sandbox.Shop.Of<PriceFact>();
        var clock = new ManualClock(January);
        await RecordAsync(prices, clock, January, 10m);
        clock.Advance(February - January);
        await RecordAsync(prices, clock, February, 12m);
        clock.Advance(March - February);
        await RecordAsync(prices, clock, February, 11m);

        // Act
        var now = await AsOfAsync(prices, MidFebruary, March);
        var believedThen = await AsOfAsync(prices, MidFebruary, MidFebruary);
        var beforeFebruaryWasKnown = await AsOfAsync(prices, MidFebruary, January);

        // Assert
        await Assert.That((now?.Price, believedThen?.Price, beforeFebruaryWasKnown?.Price)).IsEqualTo(((decimal?)11m, (decimal?)12m, (decimal?)10m));
    }

    [Test]
    public async Task AsOfAsync_BeforeTheFirstFact_FindsNothing()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var prices = sandbox.Shop.Of<PriceFact>();
        await RecordAsync(prices, new ManualClock(February), February, 12m);

        // Act
        var before = await AsOfAsync(prices, January, March);

        // Assert
        await Assert.That(before).IsNull();
    }

    private static async Task RecordAsync(Table<PriceFact> prices, TimeProvider clock, DateTimeOffset validFrom, decimal price) =>
        await prices.Put(new PriceFact { Sku = Keyboard, ValidFrom = validFrom, RecordedAt = clock.GetUtcNow(), Price = price }).IfNotExists().SendAsync();

    private static async Task<PriceFact?> AsOfAsync(Table<PriceFact> prices, DateTimeOffset validAt, DateTimeOffset knownAt) =>
        await prices.Query(PriceFact.Keys.Pk.Of(Keyboard), SortKey.Le(PriceFact.Keys.Sk.UpperBound(validAt)))
            .Filter(PriceFact.Attrs.RecordedAt.Le(knownAt))
            .Descending()
            .ItemsAsync()
            .FirstOrDefaultAsync();
}

[DynamoDbEntity]
[Discriminator("PRICE")]
[PartitionKey("PRICE#{Sku}")]
[SortKey("VALID#{ValidFrom}#TX#{RecordedAt:ticks}")]
internal sealed partial record PriceFact
{
    public required string Sku { get; init; }

    public required DateTimeOffset ValidFrom { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    public required decimal Price { get; init; }
}
