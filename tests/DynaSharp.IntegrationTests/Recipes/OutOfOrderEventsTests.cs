using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../docs/patterns-writes.md#events-that-arrive-out-of-order">Events that arrive out of order</see>: one item per key, a time per group of fields, and an update that holds only where the
/// stored time is older or missing, so a late event never moves the item back. A failed condition is an answer of <c>TrySendAsync</c>, and the stored time it hands over tells a duplicate from old news.
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
        await Assert.That((applied, match!.HomeGoals, match.AwayGoals)).IsEqualTo((EventOutcome.Superseded, 2, 1));
    }

    [Test]
    public async Task ApplyAsync_SameEventTwice_ReportsItAsStored()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var matches = sandbox.Shop.Of<OutOfOrderMatch>();
        var first = await ApplyScoreAsync(matches, 1, 0, KickOff.AddMinutes(10));

        // Act
        var second = await ApplyScoreAsync(matches, 1, 0, KickOff.AddMinutes(10));

        // Assert
        await Assert.That((first, second)).IsEqualTo((EventOutcome.Applied, EventOutcome.AlreadyStored));
    }

    [Test]
    public async Task ApplyAsync_ResponseLost_ReportsItAsStored()
    {
        // Arrange (the first attempt is written and its response lost, so the retry of the client meets that write, as a second delivery would)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        using var flaky = new LostResponseClient(sandbox.Emulator);

        // Act
        var outcome = await ApplyScoreAsync(flaky.Client.Table<OutOfOrderMatch>(sandbox.Shop.Name), 1, 0, KickOff.AddMinutes(10));

        // Assert
        await Assert.That(outcome).IsEqualTo(EventOutcome.AlreadyStored);
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
        await Assert.That((applied, match!.HomeGoals, match.Status)).IsEqualTo((EventOutcome.Applied, 3, "FULL_TIME"));
    }

    private static async Task<EventOutcome> ApplyScoreAsync(Table<OutOfOrderMatch> matches, int home, int away, DateTimeOffset at)
    {
        var result = await matches.Update(OutOfOrderMatch.Keys.Of(Match))
            .Set(OutOfOrderMatch.Attrs.HomeGoals, home)
            .Set(OutOfOrderMatch.Attrs.AwayGoals, away)
            .Set(OutOfOrderMatch.Attrs.ScoreAt, at)
            .If(OutOfOrderMatch.Attrs.ScoreAt.NotExistsOrLt(at))
            .ReturnOldOnFailure()
            .TrySendAsync();
        if (result.ConditionMet)
        {
            return EventOutcome.Applied;
        }

        return result.Item!.ScoreAt == at ? EventOutcome.AlreadyStored : EventOutcome.Superseded;
    }

    private static async Task ApplyStatusAsync(Table<OutOfOrderMatch> matches, string status, DateTimeOffset at) =>
        await matches.Update(OutOfOrderMatch.Keys.Of(Match))
            .Set(OutOfOrderMatch.Attrs.Status, status)
            .Set(OutOfOrderMatch.Attrs.StatusAt, at)
            .If(OutOfOrderMatch.Attrs.StatusAt.NotExistsOrLt(at))
            .SendAsync();
}

/// <summary>What an event did: written now, stored before by an earlier delivery or the client's own retry, or older than what is stored.</summary>
internal enum EventOutcome
{
    Applied,
    AlreadyStored,
    Superseded,
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
