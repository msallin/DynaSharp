using DynaSharp.IntegrationTests.Backends;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>An enum with <c>[DynamoDbEnumName]</c> in a key template and an attribute: the item is stored under <c>APPOINTMENT#...</c>, and reads, queries and conditions find it there.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class EnumStoredNameTests(Backend backend)
{
    private static readonly YardMessage Message = new() { Owner = MessageOwner.Appointment, OwnerKey = "A-17#3", Id = "m1", ForwardedTo = MessageOwner.LoadingUnit };

    [Test]
    public async Task Put_EnumWithStoredNames_StoresThemInTheKeyAndTheAttribute()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var messages = shop.Client.Table<YardMessage>(shop.Shop.Name);

        // Act
        await messages.Put(Message).SendAsync();
        var stored = (await shop.Shop.Get("APPOINTMENT#A-17#3", "MSG#m1").Consistent().SendAsync()).Item!;

        // Assert
        await Assert.That((stored.GetString("Owner"), stored.GetString("ForwardedTo"))).IsEqualTo(("APPOINTMENT", "LOADING_UNIT"));
    }

    [Test]
    public async Task Get_KeyOfTheEnum_ReadsTheItemBack()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var messages = shop.Client.Table<YardMessage>(shop.Shop.Name);
        await messages.Put(Message).SendAsync();

        // Act
        var read = (await messages.Get(YardMessage.Keys.Of(MessageOwner.Appointment, "A-17#3", "m1")).Consistent().SendAsync()).Item!;

        // Assert
        await Assert.That(read).IsEqualTo(Message);
    }

    [Test]
    public async Task Query_PartitionAndCondition_FindTheItemByItsStoredNames()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var messages = shop.Client.Table<YardMessage>(shop.Shop.Name);
        await messages.Put(Message).SendAsync();

        // Act
        var found = await messages.Query(YardMessage.Keys.Pk.Of(MessageOwner.Appointment, "A-17#3")).Filter(YardMessage.Attrs.ForwardedTo.Eq(MessageOwner.LoadingUnit)).Consistent().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(found.Single().Id).IsEqualTo("m1");
    }

    [Test]
    public async Task Parse_StoredKey_GivesTheMemberBack()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Client.Table<YardMessage>(shop.Shop.Name).Put(Message).SendAsync();
        var stored = (await shop.Shop.Get("APPOINTMENT#A-17#3", "MSG#m1").Consistent().SendAsync()).Item!;

        // Act
        var parts = YardMessage.Keys.Pk.Parse(stored.GetString("pk")!);

        // Assert
        await Assert.That((parts.Owner, parts.OwnerKey)).IsEqualTo((MessageOwner.Appointment, "A-17#3"));
    }
}
