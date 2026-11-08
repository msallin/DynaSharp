using System.Security.Cryptography;
using System.Text;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Mapping;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../docs/patterns.md">Top-N / leaderboard</see>: a global index keeps scores in order, with a few write shards so that a popular game does not throttle the index, and a fan-out reads the top. A change of a score moves the item
/// in the index, a delete and an insert for DynamoDB, so the new value must be known here, and an <c>ADD</c> by handle is refused.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class LeaderboardTests(Backend backend)
{
    private const int Shards = 3;
    private const string Chess = "chess";

    private static IEnumerable<PartitionKey<PlayerScore>> Board => Enumerable.Range(0, Shards).Select(static shard => PlayerScore.Keys.Gsi1Pk.Of(Chess, shard));

    [Test]
    public async Task PageAsync_Descending_GivesTheTopAcrossTheShards()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var scores = sandbox.Shop.Of<PlayerScore>();
        int[] points = [300, 1200, 50, 900, 700];
        await scores.BatchWrite().PutAll(points.Select(static score => NewScore($"player{score}", score))).SendAsync();

        // Act
        var top = await scores.Index(PlayerScore.Indexes.Gsi1).Query(Board).Descending().PageAsync(3);

        // Assert
        await Assert.That(string.Join(",", top.Items.Select(static score => score.Points))).IsEqualTo("1200,900,700");
    }

    [Test]
    public async Task Update_NewScore_MovesThePlayerAndLeavesNoOldEntry()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var scores = sandbox.Shop.Of<PlayerScore>();
        await scores.Put(NewScore("ann", 100)).SendAsync();
        await scores.Put(NewScore("ben", 200)).SendAsync();

        // Act
        await scores.Update(PlayerScore.Keys.Of("ann", Chess)).Set(PlayerScore.Attrs.Points, 300).SendAsync();
        var board = await scores.Index(PlayerScore.Indexes.Gsi1).Query(Board).Descending().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", board.Select(static score => $"{score.Player}:{score.Points}"))).IsEqualTo("ann:300,ben:200");
    }

    [Test]
    public async Task Update_AddToAScoreInAnIndexKey_ThrowsBeforeItIsSent()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var scores = sandbox.Shop.Of<PlayerScore>();

        // Act
        var failure = await Items.FailureOfAsync<InvalidOperationException>(async () => await scores.Update(PlayerScore.Keys.Of("ann", Chess)).Add(PlayerScore.Attrs.Points, 10).SendAsync());

        // Assert
        await Assert.That(failure.Message).Contains("Points", StringComparison.Ordinal);
    }

    private static PlayerScore NewScore(string player, int points) => new() { Player = player, Game = Chess, Shard = SHA256.HashData(Encoding.UTF8.GetBytes(player))[0] % Shards, Points = points };
}

[DynamoDbEntity]
[Discriminator("SCORE")]
[PartitionKey("PLAYER#{Player}")]
[SortKey("GAME#{Game}")]
[GlobalIndexKey("GSI1", PartitionKey = "BOARD#{Game}#{Shard:d1}", SortKey = "{Points:d10}")]
internal sealed partial record PlayerScore
{
    public required string Player { get; init; }

    public required string Game { get; init; }

    public required int Shard { get; init; }

    public required int Points { get; init; }
}
