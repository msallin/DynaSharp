using System.Security.Cryptography;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Sharded counter</see>: a counter hotter than one item can take is spread over several, each with a random increment, and a read sums them. Writes get cheap and scale, and a read costs one query
/// per shard.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class ShardedCounterTests(Backend backend)
{
    private const int Shards = 4;
    private const string Post = "post-1";

    private static IEnumerable<string> AllShards => Enumerable.Range(0, Shards).Select(static shard => LikeShard.Keys.Pk.Format(Post, shard));

    [Test]
    public async Task TotalAsync_ConcurrentIncrementsOnRandomShards_SumsThemAll()
    {
        // Arrange
        const int Likes = 40;
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var shards = sandbox.Shop.Of<LikeShard>();

        // Act
        await Task.WhenAll(Enumerable.Range(0, Likes).Select(async _ =>
            await shards.Update(LikeShard.Keys.Of(Post, RandomNumberGenerator.GetInt32(Shards))).Add(LikeShard.Attrs.Count, 1L).MaxAttempts(1).SendAsync()));

        // Assert
        await Assert.That(await TotalAsync(shards)).IsEqualTo(Likes);
    }

    [Test]
    public async Task TotalAsync_ShardsNothingWroteTo_CountAsNothing()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var shards = sandbox.Shop.Of<LikeShard>();
        await shards.Update(LikeShard.Keys.Of(Post, 2)).Add(LikeShard.Attrs.Count, 5L).SendAsync();

        // Act
        var total = await TotalAsync(shards);

        // Assert
        await Assert.That(total).IsEqualTo(5);
    }

    private static async Task<long> TotalAsync(Table<LikeShard> shards) => await shards.Query(AllShards).ItemsAsync().Select(static shard => shard.Count).SumAsync();
}

[DynamoDbEntity]
[Discriminator("LIKES")]
[PartitionKey("LIKES#{PostId}#{Shard:d2}")]
[SortKey("COUNT")]
internal sealed partial record LikeShard
{
    public required string PostId { get; init; }

    public required int Shard { get; init; }

    public long Count { get; init; }
}
