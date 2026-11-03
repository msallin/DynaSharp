using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

public sealed class ItemStoreTests
{
    private static readonly SlotKey One = new(AttributeValue.FromNumber("1"), AttributeValue.Null, AttributeValue.Null);
    private static readonly SlotKey Two = new(AttributeValue.FromNumber("2"), AttributeValue.Null, AttributeValue.Null);

    [Test]
    public async Task Set_SameSlotTwice_ReplacesAndCountsOnce()
    {
        // Arrange
        var store = new ItemStore();
        store.Set("p", One, new Item { ["v"] = 1 });

        // Act
        store.Set("p", One, new Item { ["v"] = 2 });

        // Assert
        await Assert.That((store.Count, store.Get("p", One)!["v"].AsNumber())).IsEqualTo((1, "2"));
    }

    [Test]
    public async Task Remove_MissingPartitionOrSlot_IsFalse()
    {
        // Arrange
        var store = new ItemStore();
        store.Set("p", One, []);

        // Act
        var removed = (store.Remove("q", One), store.Remove("p", Two));

        // Assert
        await Assert.That((removed, store.Count)).IsEqualTo(((false, false), 1));
    }

    [Test]
    public async Task Remove_LastItemOfAPartition_DropsThePartition()
    {
        // Arrange
        var store = new ItemStore();
        store.Set("p", One, []);
        store.Set("p", Two, []);

        // Act
        var removed = store.Remove("p", One) && store.Remove("p", Two);

        // Assert
        await Assert.That((removed, store.Partitions().Count(), store.Get("p", One))).IsEqualTo((true, 0, (Item?)null));
    }

    [Test]
    public async Task Partitions_SeveralInAnyOrder_AreInKeyOrder()
    {
        // Arrange
        var store = new ItemStore();
        foreach (var partition in new[] { "b", "a", "c" })
        {
            store.Set(partition, One, []);
        }

        // Act
        var partitions = store.Partitions().Select(entry => entry.Key.AsString());

        // Assert
        await Assert.That(string.Join(",", partitions)).IsEqualTo("a,b,c");
    }

    [Test]
    public async Task Slots_ThreeLevels_OrderByEachInTurn()
    {
        // Arrange: the slots of an index, by index sort key, then table partition, then table sort key.
        var store = new ItemStore();
        SlotKey[] slots =
        [
            new("x", "b", AttributeValue.FromNumber("1")),
            new("x", "a", AttributeValue.FromNumber("2")),
            new("x", "a", AttributeValue.FromNumber("1")),
            new("w", "z", AttributeValue.FromNumber("9")),
        ];
        foreach (var slot in slots)
        {
            store.Set("p", slot, new Item { ["at"] = $"{slot.Sort.AsString()}{slot.TablePartition.AsString()}{slot.TableSort.AsNumber()}" });
        }

        // Act
        var order = store.Partition("p", forward: true).Select(entry => entry.Value["at"].AsString());

        // Assert
        await Assert.That(string.Join(",", order)).IsEqualTo("wz9,xa1,xa2,xb1");
    }
}
