using System.Security.Cryptography;
using System.Text;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Mapping;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../docs/patterns.md">Sharded "latest across everything"</see>: "the newest n of the whole table", which no partition holds. A global index with a few write shards as partitions keeps it cheap, and a fan-out that
/// reads each shard backwards and stops at n gives the answer. The shard is an attribute and not a computed property, so that no update of what it would be made of is refused for it.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class ShardedLatestTests(Backend backend)
{
    private const int Shards = 3;

    private static readonly DateTimeOffset Noon = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static IEnumerable<PartitionKey<Activity>> AllShards => Enumerable.Range(0, Shards).Select(static shard => Activity.Keys.Gsi1Pk.Of(shard));

    [Test]
    public async Task PageAsync_Descending_GivesTheNewestAcrossAllUsers()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var activities = sandbox.Shop.Of<Activity>();
        await activities.BatchWrite().PutAll(Enumerable.Range(0, 12).Select(static minute => NewActivity($"user{minute % 5}", Noon.AddMinutes(minute)))).SendAsync();
        var byTime = activities.Index(Activity.Indexes.Gsi1);

        // Act
        var latest = await byTime.Query(AllShards).Descending().PageAsync(3);

        // Assert
        await Assert.That(string.Join(",", latest.Items.Select(static activity => activity.At.Minute))).IsEqualTo("11,10,9");
    }

    [Test]
    public async Task PageAsync_NextToken_GoesOnWithTheOlderOnes()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var activities = sandbox.Shop.Of<Activity>();
        await activities.BatchWrite().PutAll(Enumerable.Range(0, 7).Select(static minute => NewActivity($"user{minute}", Noon.AddMinutes(minute)))).SendAsync();
        var byTime = activities.Index(Activity.Indexes.Gsi1);
        var first = await byTime.Query(AllShards).Descending().PageAsync(4);

        // Act
        var second = await byTime.Query(AllShards).Descending().PageAsync(4, first.Next);

        // Assert
        await Assert.That((second.HasMore, string.Join(",", second.Items.Select(static activity => activity.At.Minute)))).IsEqualTo((false, "2,1,0"));
    }

    private static Activity NewActivity(string user, DateTimeOffset at) => new() { User = user, At = at, Shard = SHA256.HashData(Encoding.UTF8.GetBytes(user))[0] % Shards };
}

[DynamoDbEntity]
[Discriminator("ACTIVITY")]
[PartitionKey("USER#{User}")]
[SortKey("AT#{At:ticks}")]
[GlobalIndexKey("GSI1", PartitionKey = "LATEST#{Shard:d1}", SortKey = "{At}")]
internal sealed partial record Activity
{
    public required string User { get; init; }

    public required DateTimeOffset At { get; init; }

    public required int Shard { get; init; }
}
