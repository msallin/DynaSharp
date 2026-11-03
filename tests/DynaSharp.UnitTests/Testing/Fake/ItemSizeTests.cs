using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// Developer Guide, "Item sizes and formats": name bytes plus value bytes; a number takes 1 byte per two significant digits plus 1; booleans and nulls 1 byte; lists and maps
// 3 bytes plus 1 per element; sets the sum of their elements.
public sealed class ItemSizeTests
{
    public static IEnumerable<Func<(Item Item, long Expected)>> Sizes()
    {
        yield return () => (new Item { ["name"] = "Alice" }, 9);
        yield return () => (new Item { ["n"] = AttributeValue.FromNumber("123") }, 4);
        yield return () => (new Item { ["n"] = AttributeValue.FromNumber("1234") }, 4);
        yield return () => (new Item { ["é"] = "é" }, 4);
        yield return () => (new Item { ["b"] = AttributeValue.FromBinary([1, 2, 3]) }, 4);
        yield return () => (new Item { ["t"] = true, ["z"] = AttributeValue.Null }, 4);
        yield return () => (new Item { ["l"] = AttributeValue.FromList(["ab", 1]) }, 1 + 3 + (1 + 2) + (1 + 2));
        yield return () => (new Item { ["m"] = AttributeValue.FromMap([KeyValuePair.Create("k", AttributeValue.FromString("v"))]) }, 1 + 3 + 1 + 1 + 1);
        yield return () => (new Item { ["ss"] = AttributeValue.FromStringSet(["a", "bc"]) }, 2 + 3);
        yield return () => (new Item { ["ns"] = AttributeValue.FromNumberSet(["1", "22"]) }, 2 + 2 + 2);
        yield return () => (new Item { ["bs"] = AttributeValue.FromBinarySet([[1], [2, 3]]) }, 2 + 3);
        yield return () => ([], 0);
    }

    [Test]
    [MethodDataSource(nameof(Sizes))]
    public async Task Of_Item_CountsAsTheDeveloperGuide(Item item, long expected)
    {
        // Arrange
        // Act
        var size = ItemSize.Of(item);

        // Assert
        await Assert.That(size).IsEqualTo(expected);
    }

    [Test]
    public async Task Of_ValueWithoutAType_CountsAsOneByte()
    {
        // Arrange
        var value = default(AttributeValue);

        // Act
        var size = ItemSize.Of(value);

        // Assert
        await Assert.That(size).IsEqualTo(1L);
    }
}
