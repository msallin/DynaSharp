using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../docs/patterns-writes.md#events-that-arrive-out-of-order">Events that arrive out of order</see>: one item per key, a time per group of fields, and an update that holds only where the
/// stored time is older or missing, so a late event never moves the item back and a failed condition is a duplicate.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class OutOfOrderEventsTests(Backend backend)
{
    private const string Match = "m1";

    private static readonly DateTimeOffset KickOff = new(2026, 11, 1, 18, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ApplyAsync_LateScore_KeepsTheNewerOne()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var matches = sandbox.Shop.Of<OutOfOrderMatch>();
        await ApplyScoreAsync(matches, 2, 1, KickOff.AddMinutes(60));

        // Act
        var applied = await ApplyScoreAsync(matches, 1, 1, KickOff.AddMinutes(40));
        var match = await matches.Get(OutOfOrderMatch.Keys.Of(Match)).Consistent().ItemAsync();

        // Assert
        await Assert.That((applied, match!.HomeGoals, match.AwayGoals)).IsEqualTo((false, 2, 1));
    }

    [Test]
    public async Task ApplyAsync_SameEventTwice_ReportsADuplicate()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var matches = sandbox.Shop.Of<OutOfOrderMatch>();
        var first = await ApplyScoreAsync(matches, 1, 0, KickOff.AddMinutes(10));

        // Act
        var second = await ApplyScoreAsync(matches, 1, 0, KickOff.AddMinutes(10));

        // Assert
        await Assert.That((first, second)).IsEqualTo((true, false));
    }

    [Test]
    public async Task ApplyAsync_LateScoreAfterANewerStatus_KeepsBothGroups()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var matches = sandbox.Shop.Of<OutOfOrderMatch>();
        await ApplyStatusAsync(matches, "FULL_TIME", KickOff.AddMinutes(95));

        // Act
        var applied = await ApplyScoreAsync(matches, 3, 2, KickOff.AddMinutes(89));
        var match = await matches.Get(OutOfOrderMatch.Keys.Of(Match)).Consistent().ItemAsync();

        // Assert
        await Assert.That((applied, match!.HomeGoals, match.Status)).IsEqualTo((true, 3, "FULL_TIME"));
    }

    private static async Task<bool> ApplyScoreAsync(Table<OutOfOrderMatch> matches, int home, int away, DateTimeOffset at)
    {
        try
        {
            await matches.Update(OutOfOrderMatch.Keys.Of(Match))
                .Set(OutOfOrderMatch.Attrs.HomeGoals, home)
                .Set(OutOfOrderMatch.Attrs.AwayGoals, away)
                .Set(OutOfOrderMatch.Attrs.ScoreAt, at)
                .If(OutOfOrderMatch.Attrs.ScoreAt.NotExistsOrLt(at))
                .SendAsync();
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    private static async Task ApplyStatusAsync(Table<OutOfOrderMatch> matches, string status, DateTimeOffset at) =>
        await matches.Update(OutOfOrderMatch.Keys.Of(Match))
            .Set(OutOfOrderMatch.Attrs.Status, status)
            .Set(OutOfOrderMatch.Attrs.StatusAt, at)
            .If(OutOfOrderMatch.Attrs.StatusAt.NotExistsOrLt(at))
            .SendAsync();
}

/// <summary>Only updates write this item, and each group of fields has the time of the event that set it.</summary>
[DynamoDbEntity]
[Discriminator("OUT_OF_ORDER_MATCH")]
[PartitionKey("MATCH#{Id}")]
[SortKey("LIVE")]
internal sealed partial record OutOfOrderMatch
{
    public required string Id { get; init; }

    public int HomeGoals { get; init; }

    public int AwayGoals { get; init; }

    public DateTimeOffset? ScoreAt { get; init; }

    public string? Status { get; init; }

    public DateTimeOffset? StatusAt { get; init; }
}
