using DynaSharp.IntegrationTests.Backends;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary><see cref="DynaSharp.Mapping.OmitEmptyAttribute"/> against what a service stores: an empty string goes like null, and what other code stored reads as it is.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class OmitEmptyWriteTests(Backend backend)
{
    private static Memo.Keys.PrimaryKey Key => Memo.Keys.Of("m1");

    [Test]
    public async Task Put_EmptyString_StoresNoAttribute()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var memos = shop.Shop.Of<Memo>();

        // Act
        await memos.Put(new Memo { Id = "m1", Text = string.Empty, Place = new MemoPlace { City = string.Empty } }).SendAsync();
        var stored = await ReadRawAsync(shop);

        // Assert
        await Assert.That((stored!.ContainsKey("Text"), stored["Place"].AsMap().Count)).IsEqualTo((false, 0));
    }

    [Test]
    public async Task Put_EmptyCollectionsAndString_StoresNoAttributesAndReadsThemBack()
    {
        // Arrange (a non-nullable string keeps its initial value on a read)
        await using var shop = await ShopSandbox.StartAsync(backend);
        var memos = shop.Shop.Of<Memo>();

        // Act
        await memos.Put(new Memo { Id = "m1", Title = string.Empty }).SendAsync();
        var stored = await ReadRawAsync(shop);
        var memo = await memos.Get(Key).Consistent().ItemAsync();

        // Assert
        await Assert.That((string.Join(",", stored!.Keys.Order(StringComparer.Ordinal)), memo!.Title, memo.Lines.Count, memo.Labels.Count, memo.Counts.Count, memo.Names.Length)).IsEqualTo(("Id,_type,pk,sk", "untitled", 0, 0, 0, 0));
    }

    [Test]
    public async Task Put_FilledCollections_RoundTrip()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var memos = shop.Shop.Of<Memo>();
        var written = new Memo { Id = "m1", Title = "t", Lines = ["a"], Labels = ["b"], Counts = new Dictionary<string, int> { ["c"] = 1 }, Names = ["d"] };

        // Act
        await memos.Put(written).SendAsync();
        var memo = await memos.Get(Key).Consistent().ItemAsync();

        // Assert
        await Assert.That((memo!.Title, memo.Lines[0], memo.Labels.Single(), memo.Counts["c"], memo.Names[0])).IsEqualTo(("t", "a", "b", 1, "d"));
    }

    [Test]
    public async Task Update_SetOfEmptyCollections_RemovesTheAttributes()
    {
        // Arrange (an empty set on its way to DynamoDB would be a validation error)
        await using var shop = await ShopSandbox.StartAsync(backend);
        var memos = shop.Shop.Of<Memo>();
        await memos.Put(new Memo { Id = "m1", Lines = ["a"], Labels = ["b"] }).SendAsync();

        // Act
        await memos.Update(Key).Set(Memo.Attrs.Lines, []).Set(Memo.Attrs.Labels, []).SendAsync();
        var stored = await ReadRawAsync(shop);

        // Assert
        await Assert.That((stored!.ContainsKey("Lines"), stored.ContainsKey("Labels"))).IsEqualTo((false, false));
    }

    [Test]
    public async Task Update_AddToASet_StillAdds()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var memos = shop.Shop.Of<Memo>();
        await memos.Put(new Memo { Id = "m1", Labels = ["a"] }).SendAsync();

        // Act
        await memos.Update(Key).Add(Memo.Attrs.Labels, ["b"]).SendAsync();
        var memo = await memos.Get(Key).Consistent().ItemAsync();

        // Assert
        await Assert.That(memo!.Labels.Order(StringComparer.Ordinal)).IsEquivalentTo(["a", "b"]);
    }

    [Test]
    public async Task Transaction_PutOfEmptyCollections_StoresNoAttributes()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var memos = shop.Shop.Of<Memo>();

        // Act
        await shop.Client.TransactWrite().Put(memos, new Memo { Id = "m1", Title = string.Empty }).SendAsync();
        var stored = await ReadRawAsync(shop);

        // Assert
        await Assert.That((stored!.ContainsKey("Title"), stored.ContainsKey("Lines"))).IsEqualTo((false, false));
    }

    [Test]
    public async Task Update_SetOfAnEmptyString_RemovesTheAttribute()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var memos = shop.Shop.Of<Memo>();
        await memos.Put(new Memo { Id = "m1", Text = "old" }).SendAsync();

        // Act
        await memos.Update(Key).Set(Memo.Attrs.Text, string.Empty).SendAsync();
        var stored = await ReadRawAsync(shop);

        // Assert
        await Assert.That(stored!.ContainsKey("Text")).IsFalse();
    }

    [Test]
    public async Task Update_SetIfNotNullOfAnEmptyString_LeavesTheAttribute()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var memos = shop.Shop.Of<Memo>();
        await memos.Put(new Memo { Id = "m1", Text = "old" }).SendAsync();

        // Act
        await memos.Update(Key).SetIfNotNull(Memo.Attrs.Text, string.Empty).SendAsync();
        var stored = await memos.Get(Key).Consistent().ItemAsync();

        // Assert
        await Assert.That(stored!.Text).IsEqualTo("old");
    }

    [Test]
    public async Task Get_EmptyStringThatOtherCodeStored_ReadsAsEmpty()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Shop.Put(new Item { ["pk"] = "MEMO#m1", ["sk"] = "MEMO", ["_type"] = "MEMO", ["Id"] = "m1", ["Text"] = string.Empty }).SendAsync();

        // Act
        var stored = await shop.Shop.Of<Memo>().Get(Key).Consistent().ItemAsync();

        // Assert
        await Assert.That(stored!.Text).IsEqualTo(string.Empty);
    }

    private static async Task<Item?> ReadRawAsync(ShopSandbox shop) => await shop.Shop.Get("MEMO#m1", "MEMO").Consistent().ItemAsync();
}
