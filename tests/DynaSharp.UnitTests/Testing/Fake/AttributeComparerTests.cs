using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// The order is the one of the Developer Guide, "Comparison operator and function reference" and "Query": strings by their UTF-8 bytes, binaries by their bytes, both
// unsigned, numbers by value. Equality of a condition compares lists element by element, maps by entry, and sets without order.
public sealed class AttributeComparerTests
{
    public static IEnumerable<Func<(AttributeValue Left, AttributeValue Right)>> EqualPairs()
    {
        yield return () => ("a", "a");
        yield return () => (AttributeValue.FromNumber("1"), AttributeValue.FromNumber("1.0"));
        yield return () => (AttributeValue.FromBinary([1, 2]), AttributeValue.FromBinary([1, 2]));
        yield return () => (true, true);
        yield return () => (AttributeValue.Null, AttributeValue.Null);
        yield return () => (List("a", 1), List("a", AttributeValue.FromNumber("1.00")));
        yield return () => (Map(("k", 1), ("j", "x")), Map(("j", "x"), ("k", 1)));
        yield return () => (AttributeValue.FromStringSet(["a", "b"]), AttributeValue.FromStringSet(["b", "a"]));
        yield return () => (AttributeValue.FromNumberSet(["1", "2"]), AttributeValue.FromNumberSet(["2.0", "1"]));
        yield return () => (AttributeValue.FromBinarySet([[1], [2]]), AttributeValue.FromBinarySet([[2], [1]]));
    }

    public static IEnumerable<Func<(AttributeValue Left, AttributeValue Right)>> UnequalPairs()
    {
        yield return () => ("1", AttributeValue.FromNumber("1"));
        yield return () => ("a", "b");
        yield return () => (AttributeValue.FromNumber("1"), AttributeValue.FromNumber("2"));
        yield return () => (AttributeValue.FromBinary([1]), AttributeValue.FromBinary([2]));
        yield return () => (true, false);
        yield return () => (List("a"), List("a", "b"));
        yield return () => (List("a"), List("b"));
        yield return () => (Map(("k", 1)), Map(("k", 1), ("j", 2)));
        yield return () => (Map(("k", 1)), Map(("j", 1)));
        yield return () => (Map(("k", 1)), Map(("k", 2)));
        yield return () => (AttributeValue.FromStringSet(["a"]), AttributeValue.FromStringSet(["a", "b"]));
        yield return () => (AttributeValue.FromStringSet(["a", "b"]), AttributeValue.FromStringSet(["a", "c"]));
        yield return () => (AttributeValue.FromNumberSet(["1"]), AttributeValue.FromNumberSet(["2"]));
        yield return () => (AttributeValue.FromBinarySet([[1]]), AttributeValue.FromBinarySet([[2]]));
        yield return () => (default, default);
    }

    [Test]
    [Arguments("a", "b", -1)]
    [Arguments("a", "a", 0)]
    [Arguments("ab", "a", 1)]
    [Arguments("", "a", -1)]
    [Arguments("Z", "a", -1)]
    [Arguments("é", "z", 1)]
    [Arguments("", "😀", -1)]
    [Arguments("￿", "𐀀", -1)]
    [Arguments("😀", "", 1)]
    [Arguments("ÿ", "Ā", -1)]
    public async Task CompareStrings_TwoStrings_OrdersByUtf8Bytes(string left, string right, int expected)
    {
        // Arrange
        // Act
        var order = AttributeComparer.CompareStrings(left, right);

        // Assert
        await Assert.That(Math.Sign(order)).IsEqualTo(expected);
    }

    [Test]
    public async Task CompareStrings_AnyPair_AgreesWithTheBytesOfUtf8()
    {
        // Arrange: one string of each range a UTF-16 code unit can be in, against each other, and the order of their UTF-8 bytes.
        string[] texts = ["A", "\u007F", "\u0080", "߿", "ࠀ", "퟿", "", "￿", "𐀀", "􏿿"];

        // Act
        var disagreements = texts.SelectMany(left => texts, (left, right) => (left, right))
            .Count(pair => Math.Sign(AttributeComparer.CompareStrings(pair.left, pair.right))
                != Math.Sign(System.Text.Encoding.UTF8.GetBytes(pair.left).AsSpan().SequenceCompareTo(System.Text.Encoding.UTF8.GetBytes(pair.right))));

        // Assert
        await Assert.That(disagreements).IsEqualTo(0);
    }

    [Test]
    public async Task TryCompare_Numbers_OrdersByValue()
    {
        // Arrange
        var nine = AttributeValue.FromNumber("9");
        var ten = AttributeValue.FromNumber("10");

        // Act
        var ordered = AttributeComparer.TryCompare(nine, ten, out var order);

        // Assert
        await Assert.That(ordered && order < 0).IsTrue();
    }

    [Test]
    public async Task TryCompare_Binaries_OrdersByUnsignedBytes()
    {
        // Arrange
        var high = AttributeValue.FromBinary([0x80]);
        var low = AttributeValue.FromBinary([0x7F, 0xFF]);

        // Act
        var ordered = AttributeComparer.TryCompare(high, low, out var order);

        // Assert
        await Assert.That(ordered && order > 0).IsTrue();
    }

    [Test]
    public async Task TryCompare_Strings_OrdersThem()
    {
        // Arrange
        var a = AttributeValue.FromString("a");
        var b = AttributeValue.FromString("b");

        // Act
        var ordered = AttributeComparer.TryCompare(a, b, out var order);

        // Assert
        await Assert.That(ordered && order < 0).IsTrue();
    }

    [Test]
    public async Task TryCompare_DifferentTypes_HasNoOrder()
    {
        // Arrange
        var text = AttributeValue.FromString("1");
        var number = AttributeValue.FromNumber("1");

        // Act
        var ordered = AttributeComparer.TryCompare(text, number, out var order);

        // Assert
        await Assert.That((ordered, order)).IsEqualTo((false, 0));
    }

    [Test]
    public async Task TryCompare_Booleans_HaveNoOrder()
    {
        // Arrange
        var yes = AttributeValue.FromBool(true);

        // Act
        var ordered = AttributeComparer.TryCompare(yes, yes, out _);

        // Assert
        await Assert.That(ordered).IsFalse();
    }

    [Test]
    public async Task Compare_TwoNulls_IsLevel()
    {
        // Arrange
        // Act
        var order = AttributeComparer.Instance.Compare(AttributeValue.Null, AttributeValue.Null);

        // Assert
        await Assert.That(order).IsEqualTo(0);
    }

    [Test]
    public async Task Compare_EqualNumbersInOtherText_IsLevel()
    {
        // Arrange
        var one = AttributeValue.FromNumber("1");
        var also = AttributeValue.FromNumber("1.0");

        // Act
        var order = AttributeComparer.Instance.Compare(one, also);

        // Assert
        await Assert.That(order).IsEqualTo(0);
    }

    [Test]
    public async Task Compare_StringAndNumber_ThrowsNamingTheTypes()
    {
        // Arrange
        var text = AttributeValue.FromString("1");
        var number = AttributeValue.FromNumber("1");

        // Act
        var act = () => AttributeComparer.Instance.Compare(text, number);

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>().WithMessageContaining("compared a S with a N", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(EqualPairs))]
    public async Task AreEqual_EqualValues_IsTrue(AttributeValue left, AttributeValue right)
    {
        // Arrange
        // Act
        var equal = AttributeComparer.AreEqual(left, right);

        // Assert
        await Assert.That(equal).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(UnequalPairs))]
    public async Task AreEqual_DifferentValues_IsFalse(AttributeValue left, AttributeValue right)
    {
        // Arrange
        // Act
        var equal = AttributeComparer.AreEqual(left, right);

        // Assert
        await Assert.That(equal).IsFalse();
    }

    private static AttributeValue List(params AttributeValue[] elements) => AttributeValue.FromList(elements);

    private static AttributeValue Map(params (string Name, AttributeValue Value)[] entries) =>
        AttributeValue.FromMap(entries.Select(static entry => KeyValuePair.Create(entry.Name, entry.Value)));
}
