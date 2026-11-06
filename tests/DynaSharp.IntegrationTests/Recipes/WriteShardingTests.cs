using System.Security.Cryptography;
using System.Text;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Write sharding</see>: a partition takes about 1000 writes a second, so a hot key is spread over several by a suffix. A random suffix spreads best, and a read has to ask every shard; a
/// suffix hashed from an id lets a read find its one shard. The hash must be stable: <see cref="string.GetHashCode()"/> differs per process.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class WriteShardingTests(Backend backend)
{
    private const int Shards = 4;
    private const string Poll = "best-pet";

    private static readonly DateTimeOffset Noon = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static IEnumerable<PartitionKey<Vote>> AllShards => Enumerable.Range(0, Shards).Select(static shard => Vote.Keys.Pk.Of(Poll, shard));

    [Test]
    public async Task Query_FanOutOverRandomShards_MergesConcurrentWritesInTimeOrder()
    {
        // Arrange
        const int Votes = 24;
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var votes = sandbox.Shop.Of<Vote>();

        // Act
        await Task.WhenAll(Enumerable.Range(0, Votes).Select(async second =>
            await votes.Put(new Vote { Poll = Poll, Shard = RandomNumberGenerator.GetInt32(Shards), At = Noon.AddSeconds(second), VoteId = Guid.NewGuid() }).SendAsync()));
        var merged = await votes.Query(AllShards).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((merged.Count, merged.Select(static vote => vote.At).Order().SequenceEqual(merged.Select(static vote => vote.At)), merged.Select(static vote => vote.Shard).Distinct().Count() > 1))
            .IsEqualTo((Votes, true, true));
    }

    [Test]
    public async Task CountAsync_FanOut_SumsTheShards()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var votes = sandbox.Shop.Of<Vote>();
        await votes.BatchWrite().PutAll(Enumerable.Range(0, 10).Select(static i => new Vote { Poll = Poll, Shard = i % Shards, At = Noon, VoteId = Guid.NewGuid() })).SendAsync();

        // Act
        var count = await votes.Query(AllShards).CountAsync();

        // Assert
        await Assert.That(count).IsEqualTo(10);
    }

    [Test]
    public async Task Get_HashedSuffix_FindsTheItemInItsOneShard()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var sessions = sandbox.Shop.Of<UserSession>();
        const string Id = "session-1";
        await sessions.Put(new UserSession { Shard = ShardOf(Id), Id = Id, User = "alice" }).SendAsync();

        // Act
        var found = await sessions.Get(UserSession.Keys.Of(ShardOf(Id), Id)).Consistent().ItemAsync();

        // Assert
        await Assert.That(found?.User).IsEqualTo("alice");
    }

    // The first byte of a digest does not change between processes, and it is spread evenly.
    private static int ShardOf(string id) => SHA256.HashData(Encoding.UTF8.GetBytes(id))[0] % Shards;
}

[DynamoDbEntity]
[Discriminator("VOTE")]
[PartitionKey("VOTES#{Poll}#{Shard:d2}")]
[SortKey("{At}#{VoteId}")]
internal sealed partial record Vote
{
    public required string Poll { get; init; }

    public required int Shard { get; init; }

    public required DateTimeOffset At { get; init; }

    public required Guid VoteId { get; init; }
}

[DynamoDbEntity]
[Discriminator("SESSION")]
[PartitionKey("SESSIONS#{Shard:d2}")]
[SortKey("{Id}")]
internal sealed partial record UserSession
{
    public required int Shard { get; init; }

    public required string Id { get; init; }

    public required string User { get; init; }
}
