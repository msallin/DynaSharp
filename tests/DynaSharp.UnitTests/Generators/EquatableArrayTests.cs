using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators;

/// <summary>The array that keeps pipeline values comparable. A broken equality here shows up only as a slow compiler.</summary>
public sealed class EquatableArrayTests
{
    [Test]
    public async Task Equals_SameElementsInOtherArrays_IsTrueWithEqualHashes()
    {
        // Arrange
        var left = new EquatableArray<string>(["a", "b"]);
        var right = new EquatableArray<string>([.. "ab".Select(c => c.ToString())]);

        // Act
        var equal = (left == right, left != right, left.Equals((object)right), left.GetHashCode() == right.GetHashCode());

        // Assert
        await Assert.That(equal).IsEqualTo((true, false, true, true));
    }

    [Test]
    public async Task Equals_DifferentElementsOrOrder_IsFalse()
    {
        // Arrange
        var array = new EquatableArray<string>(["a", "b"]);

        // Act
        var equal = (array == new EquatableArray<string>(["b", "a"]), array == new EquatableArray<string>(["a"]), array.Equals("a"));

        // Assert
        await Assert.That(equal).IsEqualTo((false, false, false));
    }

    [Test]
    public async Task GetHashCode_OtherOrder_Differs()
    {
        // Arrange
        var left = new EquatableArray<string>(["a", "b"]);
        var right = new EquatableArray<string>(["b", "a"]);

        // Act, Assert
        await Assert.That(left.GetHashCode()).IsNotEqualTo(right.GetHashCode());
    }

    [Test]
    public async Task Default_BehavesAsEmpty()
    {
        // Arrange
        EquatableArray<string> array = default;

        // Act
        var seen = (array.Count, array.ToList().Count, array == new EquatableArray<string>([]), array.GetHashCode() == new EquatableArray<string>([]).GetHashCode());

        // Assert
        await Assert.That(seen).IsEqualTo((0, 0, true, true));
    }

    [Test]
    public async Task Enumerate_Elements_ComeInOrderAndByIndex()
    {
        // Arrange
        var array = new EquatableArray<string>(["a", "b"]);

        // Act
        var seen = (string.Concat(array), array[1], array.Count);
        var boxed = new List<object>();
        foreach (var item in (System.Collections.IEnumerable)array)
        {
            boxed.Add(item);
        }

        // Assert
        await Assert.That((seen, boxed.Count)).IsEqualTo((("ab", "b", 2), 2));
    }
}
