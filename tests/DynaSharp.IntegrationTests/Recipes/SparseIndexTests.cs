using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Mapping;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../docs/patterns.md">Sparse index</see>: an index holds only the items that have its key attributes, so a rare state of a large table can be read at the cost of what is in that state. Membership is the
/// presence of those attributes, and it needs no primitive of its own: <c>SET</c> takes an item in, <c>REMOVE</c> takes it out.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class SparseIndexTests(Backend backend)
{
    private const string Pro = "pro";

    private static readonly DateOnly Day = new(2026, 11, 1);

    [Test]
    public async Task Update_SetAndRemove_ToggleTheMembership()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var subscribers = sandbox.Shop.Of<Subscriber>();
        var paying = subscribers.Index(Subscriber.Indexes.Gsi2);
        await subscribers.Put(new Subscriber { Id = "s1" }).SendAsync();
        await subscribers.Put(new Subscriber { Id = "s2" }).SendAsync();
        var before = await paying.Scan().CountAsync();

        // Act
        await subscribers.Update(Subscriber.Keys.Of("s1")).Set(Subscriber.Attrs.Plan, Pro).Set(Subscriber.Attrs.Since, Day).SendAsync();
        var during = await paying.Query(Subscriber.Keys.Gsi2Pk.Of(Pro)).ItemsAsync().ToListAsync();
        await subscribers.Update(Subscriber.Keys.Of("s1")).Remove(Subscriber.Attrs.Plan).SendAsync();
        var after = await paying.Scan().CountAsync();

        // Assert
        await Assert.That((before, during.Single().Id, after)).IsEqualTo((0, "s1", 0));
    }

    [Test]
    public async Task Update_NullableValue_TogglesTheMembership()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var subscribers = sandbox.Shop.Of<Subscriber>();
        var paying = subscribers.Index(Subscriber.Indexes.Gsi2);
        DateOnly? since = Day;
        DateOnly? none = null;

        // Act
        await subscribers.Update(Subscriber.Keys.Of("s1")).Set(Subscriber.Attrs.Plan, Pro).Set(Subscriber.Attrs.Since, since).SendAsync();
        var during = await paying.Scan().CountAsync();
        await subscribers.Update(Subscriber.Keys.Of("s1")).Set(Subscriber.Attrs.Since, none).SendAsync();
        var after = await paying.Scan().CountAsync();
        var stored = await subscribers.Get(Subscriber.Keys.Of("s1")).Consistent().ItemAsync();

        // Assert
        await Assert.That((during, after, stored!.Since, stored.Plan)).IsEqualTo((1, 0, (DateOnly?)null, (string?)Pro));
    }

    [Test]
    public async Task Update_Remove_TakesTheKeyAttributesOffTheItemAndKeepsTheItem()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var subscribers = sandbox.Shop.Of<Subscriber>();
        await subscribers.Put(new Subscriber { Id = "s1", Plan = Pro, Since = Day }).SendAsync();

        // Act
        await subscribers.Update(Subscriber.Keys.Of("s1")).Remove(Subscriber.Attrs.Plan).SendAsync();
        var stored = await sandbox.Shop.Get(Subscriber.Keys.Pk.Format("s1"), Subscriber.Keys.Sk.Format()).Consistent().ItemAsync();

        // Assert
        await Assert.That((stored!.ContainsKey("gsi2pk"), stored.ContainsKey("gsi2sk"), stored.GetString("Id"))).IsEqualTo((false, false, "s1"));
    }

    [Test]
    public async Task Update_LiteralPartitionOfTheIndex_TakesAnItemIn()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var articles = sandbox.Shop.Of<Article>();
        var featured = articles.Index(Article.Indexes.Gsi2);
        await articles.Put(new Article { Id = "a1" }).SendAsync();

        // Act
        await articles.Update(Article.Keys.Of("a1")).Set(Article.Attrs.FeaturedOn, Day).SendAsync();
        var found = await featured.Query(Article.Keys.Gsi2Pk.Of()).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(found.Single().Id).IsEqualTo("a1");
    }

    [Test]
    public async Task Update_RemoveWithALiteralPartitionOfTheIndex_TakesTheItemOut()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var articles = sandbox.Shop.Of<Article>();
        await articles.Update(Article.Keys.Of("a1")).Set(Article.Attrs.FeaturedOn, Day).SendAsync();

        // Act
        await articles.Update(Article.Keys.Of("a1")).Remove(Article.Attrs.FeaturedOn).SendAsync();

        // Assert
        await Assert.That(await articles.Index(Article.Indexes.Gsi2).Scan().CountAsync()).IsEqualTo(0);
    }
}

/// <summary>Only paying subscribers have a plan, so only they are in the index.</summary>
[DynamoDbEntity]
[Discriminator("SUBSCRIBER")]
[PartitionKey("SUB#{Id}")]
[SortKey("PROFILE")]
[GlobalIndexKey("GSI2", PartitionKey = "PLAN#{Plan}", SortKey = "SINCE#{Since}", Sparse = true)]
internal sealed partial record Subscriber
{
    public required string Id { get; init; }

    public string? Plan { get; init; }

    public DateOnly? Since { get; init; }
}

[DynamoDbEntity]
[Discriminator("ARTICLE")]
[PartitionKey("ARTICLE#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("GSI2", PartitionKey = "FEATURED", SortKey = "ON#{FeaturedOn}", Sparse = true)]
internal sealed partial record Article
{
    public required string Id { get; init; }

    public DateOnly? FeaturedOn { get; init; }
}
