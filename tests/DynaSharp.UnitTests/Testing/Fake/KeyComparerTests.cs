using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

public sealed class KeyComparerTests
{
    [Test]
    [Arguments("a", "1", "b", "0", -1)]
    [Arguments("a", "2", "a", "10", -1)]
    [Arguments("a", "1", "a", "1.0", 0)]
    [Arguments("b", null, "a", null, 1)]
    [Arguments("a", null, "a", null, 0)]
    public async Task Compare_Keys_OrdersByPartitionThenSort(string leftPartition, string? leftSort, string rightPartition, string? rightSort, int expected)
    {
        // Arrange
        var left = new ItemKey(leftPartition, leftSort is null ? (AttributeValue?)null : AttributeValue.FromNumber(leftSort));
        var right = new ItemKey(rightPartition, rightSort is null ? (AttributeValue?)null : AttributeValue.FromNumber(rightSort));

        // Act
        var order = KeyComparer.Instance.Compare(left, right);

        // Assert
        await Assert.That(Math.Sign(order)).IsEqualTo(expected);
    }
}
