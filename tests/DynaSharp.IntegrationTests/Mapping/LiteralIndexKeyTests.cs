using DynaSharp.IntegrationTests.Backends;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>
/// Keys of an index that hold only literals, such as <c>PartitionKey = "ALL"</c>. DynamoDB takes an item into an index only with all its keys, so what an update does about such a key decides
/// whether an item it creates is in the index at all.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class LiteralIndexKeyTests(Backend backend)
{
    [Test]
    public async Task Update_SetOfTheOtherKey_TakesTheItemItCreatesIntoTheIndex()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var listed = sandbox.Shop.Of<ListedPost>();

        // Act
        await listed.Update(ListedPost.Keys.Of("a")).Set(ListedPost.Attrs.Title, "Hello").SendAsync();
        var found = await listed.Index(ListedPost.Indexes.Gsi1).Query(ListedPost.Keys.Gsi1Pk.Of()).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((found.Single().Id, found.Single().Title)).IsEqualTo(("a", "Hello"));
    }

    [Test]
    public async Task Update_SetOfTheOtherKeyInATransaction_TakesTheItemItCreatesIntoTheIndex()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var listed = sandbox.Shop.Of<ListedPost>();

        // Act
        await sandbox.Client.TransactWrite().Update(listed, ListedPost.Keys.Of("a")).Set(ListedPost.Attrs.Title, "Hello").SendAsync();
        var found = await listed.Index(ListedPost.Indexes.Gsi1).Query(ListedPost.Keys.Gsi1Pk.Of()).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(found.Single().Id).IsEqualTo("a");
    }

    [Test]
    public async Task Update_NotTouchingTheIndex_AddsNothingToIt()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var listed = sandbox.Shop.Of<ListedPost>();

        // Act
        await listed.Update(ListedPost.Keys.Of("a")).Set(ListedPost.Attrs.Note, "x").SendAsync();
        var stored = await sandbox.Shop.Get(ListedPost.Keys.Pk.Format("a"), ListedPost.Keys.Sk.Format()).Consistent().ItemAsync();

        // Assert
        await Assert.That((stored!.ContainsKey("gsi1pk"), stored.ContainsKey("gsi1sk"), await sandbox.Shop.Index<ListedPost>(ListedPost.Indexes.Gsi1).Scan().CountAsync())).IsEqualTo((false, false, 0));
    }

    [Test]
    public async Task Update_NotTouchingAnIndexOfLiterals_TakesTheItemItCreatesIntoTheIndex()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var pinned = sandbox.Shop.Of<PinnedPost>();

        // Act
        await pinned.Update(PinnedPost.Keys.Of("a")).Set(PinnedPost.Attrs.Note, "x").SendAsync();
        var found = await pinned.Index(PinnedPost.Indexes.Gsi1).Query(PinnedPost.Keys.Gsi1Pk.Of(), SortKeyCondition.Eq(PinnedPost.Keys.Gsi1Sk.Format())).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((found.Single().Id, found.Single().Note)).IsEqualTo(("a", (string?)"x"));
    }

    [Test]
    public async Task Update_NotTouchingALocalIndexWithALiteralSortKey_TakesTheItemItCreatesIntoTheIndex()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var labeled = sandbox.Shop.Of<LabeledPost>();

        // Act
        await labeled.Update(LabeledPost.Keys.Of("a")).Set(LabeledPost.Attrs.Note, "x").SendAsync();
        var found = await labeled.Index(LabeledPost.Indexes.Lsi1).Query(LabeledPost.Keys.Pk.Of("a")).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((found.Single().Id, found.Single().Note)).IsEqualTo(("a", (string?)"x"));
    }

    [Test]
    public async Task Query_ConsistentReadOfALocalIndex_SeesTheWriteBeforeIt()
    {
        // Arrange (a local index takes what a global one refuses)
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var labeled = sandbox.Shop.Of<LabeledPost>();
        await labeled.Put(new LabeledPost { Id = "a" }).SendAsync();

        // Act
        var found = await labeled.Index(LabeledPost.Indexes.Lsi1).Query(LabeledPost.Keys.Pk.Of("a")).Consistent().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(found.Single().Id).IsEqualTo("a");
    }

    [Test]
    public async Task Update_NotTouchingAnIndexOnATableKeyWithALiteralSortKey_TakesTheItemItCreatesIntoTheIndex()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var tabled = sandbox.Shop.Of<TabledPost>();

        // Act
        await tabled.Update(TabledPost.Keys.Of("a")).Set(TabledPost.Attrs.Note, "x").SendAsync();
        var found = await tabled.Index(ShopSandbox.ByPartitionAndLabel).Query(TabledPost.Keys.Pk.Of("a")).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((found.Single().Id, found.Single().Note)).IsEqualTo(("a", (string?)"x"));
    }

    [Test]
    public async Task Update_OfAnItemInAnIndexOfLiterals_KeepsItThere()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var pinned = sandbox.Shop.Of<PinnedPost>();
        await pinned.Put(new PinnedPost { Id = "a" }).SendAsync();

        // Act
        await pinned.Update(PinnedPost.Keys.Of("a")).Set(PinnedPost.Attrs.Note, "x").SendAsync();

        // Assert
        await Assert.That(await pinned.Index(PinnedPost.Indexes.Gsi1).Scan().CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task Update_OfWhatAnotherTypeHolds_LeavesItsKeysOfTheIndex()
    {
        // Arrange (another type stored its own keys under the attributes of the index)
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var key = PinnedPost.Keys.Pk.Format("a");
        await sandbox.Shop.Put(new Item { ["pk"] = key, ["sk"] = "DATA", ["gsi1pk"] = "OTHER", ["gsi1sk"] = "HELD", ["_type"] = "OTHER" }).SendAsync();

        // Act
        await sandbox.Shop.Of<PinnedPost>().Update(PinnedPost.Keys.Of("a")).Set(PinnedPost.Attrs.Note, "x").SendAsync();
        var stored = await sandbox.Shop.Get(key, "DATA").Consistent().ItemAsync();

        // Assert
        await Assert.That((stored!.GetString("gsi1pk"), stored.GetString("gsi1sk"), stored.GetString("_type"))).IsEqualTo(("OTHER", "HELD", "OTHER"));
    }

    [Test]
    public async Task Update_RemoveOfAnInputBesideALiteralKeyOfAnIndexThatIsNotSparse_ThrowsAndSendsNothing()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var listed = sandbox.Shop.Of<ListedPost>();

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await listed.Update(ListedPost.Keys.Of("a")).Remove(ListedPost.Attrs.Title).SendAsync());

        // Assert
        await Assert.That((failure!.Message.Contains("is not sparse", StringComparison.Ordinal), await listed.Scan().CountAsync())).IsEqualTo((true, 0));
    }
}
