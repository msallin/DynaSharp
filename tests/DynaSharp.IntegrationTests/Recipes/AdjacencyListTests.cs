using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Mapping;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Adjacency list (+ inverted GSI)</see>: a graph in one table. A node and its outgoing edges share a partition, so one query walks them, and a global index with the key the other way
/// round walks the edges backwards. A query then holds several types, which is what <c>Of&lt;...&gt;()</c> reads.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class AdjacencyListTests(Backend backend)
{
    [Test]
    public async Task Query_APartition_ReadsTheNodeAndItsEdgesEachAsItsType()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await PutGraphAsync(sandbox);

        // Act
        var items = await sandbox.Shop.Query(Member.Keys.Pk.Of("alice")).Of<Member, Membership>().ItemsAsync().ToListAsync();

        // Assert (GROUP# sorts before PROFILE)
        await Assert.That(string.Join(",", items.Select(static item => item.Match(static member => member.Name, static edge => edge.GroupId)))).IsEqualTo("admins,staff,Alice");
    }

    [Test]
    public async Task Query_InvertedIndex_WalksTheEdgesBackwards()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await PutGraphAsync(sandbox);

        // Act
        var byTarget = sandbox.Shop.Index<Membership>(Membership.Indexes.Gsi1);
        var items = await byTarget.Query(Team.Keys.Gsi1Pk.Of("staff")).Of<Team, Membership>().ItemsAsync().ToListAsync();

        // Assert (the team sorts first, under a key of its own, and then the people on it)
        await Assert.That(string.Join(",", items.Select(static item => item.Match(static team => team.Title, static edge => edge.UserId)))).IsEqualTo("Everyone,alice,bob");
    }

    [Test]
    public async Task Query_ATableOfOneType_DropsTheOtherTypesOfThePartition()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await PutGraphAsync(sandbox);

        // Act
        var page = await sandbox.Shop.Of<Membership>().Query(Membership.Keys.Pk.Of("alice")).PageAsync();

        // Assert (the profile was read and paid for)
        await Assert.That((page.Count, page.ScannedCount)).IsEqualTo((2, 3));
    }

    private static async Task PutGraphAsync(ShopSandbox sandbox)
    {
        await sandbox.Shop.Of<Team>().Put(new Team { GroupId = "staff", Title = "Everyone" }).SendAsync();
        await sandbox.Shop.Of<Member>().BatchWrite()
            .Put(new Member { UserId = "alice", Name = "Alice" })
            .Put(new Member { UserId = "bob", Name = "Bob" })
            .SendAsync();
        await sandbox.Shop.Of<Membership>().BatchWrite()
            .Put(new Membership { UserId = "alice", GroupId = "admins" })
            .Put(new Membership { UserId = "alice", GroupId = "staff" })
            .Put(new Membership { UserId = "bob", GroupId = "staff" })
            .SendAsync();
    }
}

[DynamoDbEntity]
[Discriminator("MEMBER")]
[PartitionKey("USER#{UserId}")]
[SortKey("PROFILE")]
internal sealed partial record Member
{
    public required string UserId { get; init; }

    public required string Name { get; init; }
}

/// <summary>An edge, under the user that it leaves, and in the index under the team that it enters.</summary>
[DynamoDbEntity]
[Discriminator("MEMBERSHIP")]
[PartitionKey("USER#{UserId}")]
[SortKey("GROUP#{GroupId}")]
[GlobalIndexKey("GSI1", PartitionKey = "GROUP#{GroupId}", SortKey = "USER#{UserId}")]
internal sealed partial record Membership
{
    public required string UserId { get; init; }

    public required string GroupId { get; init; }
}

[DynamoDbEntity]
[Discriminator("TEAM")]
[PartitionKey("GROUP#{GroupId}")]
[SortKey("INFO")]
[GlobalIndexKey("GSI1", PartitionKey = "GROUP#{GroupId}", SortKey = "INFO")]
internal sealed partial record Team
{
    public required string GroupId { get; init; }

    public required string Title { get; init; }
}
