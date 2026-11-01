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
