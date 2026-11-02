using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>
/// A value that a converter counts as missing, in an item another writer stored, against what a service gives back. A stored <c>""</c> is what the service keeps of an attribute and a stub only claims, which is why these run here too.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class CountsAsMissingTests(Backend backend)
{
    [Test]
    public async Task Get_BlankString_ReadsNullAndTheRestOfTheItem()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await StoreAsync(shop, new Item { ["Note"] = string.Empty, ["Deck"] = "Upper" });

        // Act
        var cabin = await shop.Shop.Of<Cabin>().Get(Cabin.Keys.Of("c1")).Consistent().ItemAsync();

        // Assert
        await Assert.That((cabin!.Note, cabin.Deck, cabin.Berths)).IsEqualTo(((CabinNote?)null, Deck.Upper, 4));
    }

    [Test]
    public async Task Get_NameTheEnumLacks_KeepsTheInitialValue()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await StoreAsync(shop, new Item { ["Note"] = "kept", ["Deck"] = "TELEPORTED" });

        // Act
        var cabin = await shop.Shop.Of<Cabin>().Get(Cabin.Keys.Of("c1")).Consistent().ItemAsync();

        // Assert
        await Assert.That((cabin!.Note, cabin.Deck, cabin.Berths)).IsEqualTo((new CabinNote("kept"), Deck.Unknown, 4));
    }

    [Test]
    public async Task Select_BlankString_IsFoundWithNull()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await StoreAsync(shop, new Item { ["Note"] = string.Empty });

        // Act
        var note = await shop.Shop.Of<Cabin>().Get(Cabin.Keys.Of("c1")).Consistent().Select(Cabin.Attrs.Note).SendAsync();

        // Assert
        await Assert.That((note.Found, note.Value)).IsEqualTo((true, (CabinNote?)null));
    }

    [Test]
    public async Task Select_NameTheEnumLacks_ThrowsNamingTheAttribute()
    {
        // Arrange (DYNA209 reports this where it sees a handle, and a variable is no such place)
        await using var shop = await ShopSandbox.StartAsync(backend);
        await StoreAsync(shop, new Item { ["Deck"] = "TELEPORTED" });
        var deck = Cabin.Attrs.Deck;

        // Act
        var failure = await Items.FailureOfAsync<DynamoDbMappingException>(async () => await shop.Shop.Of<Cabin>().Get(Cabin.Keys.Of("c1")).Consistent().Select(deck).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Deck");
    }

    private static async Task StoreAsync(ShopSandbox shop, Item attributes)
    {
        var item = new Item(attributes)
        {
            ["pk"] = Cabin.Keys.Pk.Format("c1"),
            ["sk"] = "CABIN",
            ["_type"] = "CABIN",
            ["Id"] = "c1",
            ["Berths"] = AttributeValue.FromNumber(4),
        };
        await shop.Shop.Put(item).SendAsync();
    }
}
