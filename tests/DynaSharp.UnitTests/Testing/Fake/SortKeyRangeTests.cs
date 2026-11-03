using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// begins_with in a key condition works on strings and binaries, Developer Guide, "Key condition expressions for query".
public sealed class SortKeyRangeTests
{
    [Test]
    [Arguments("ORDER#2026", true)]
    [Arguments("ORDER", false)]
    [Arguments("INVOICE#2026", false)]
    public async Task Admits_BeginsWithOnAString_IsAPrefixTest(string sortKey, bool expected)
    {
        // Arrange
        var range = new SortKeyRange(SortKeyOperator.BeginsWith, "ORDER#");

        // Act
        var admits = range.Admits(sortKey);

        // Assert
        await Assert.That(admits).IsEqualTo(expected);
    }

    [Test]
    [Arguments(new byte[] { 1, 2, 3 }, true)]
    [Arguments(new byte[] { 1 }, false)]
    [Arguments(new byte[] { 2, 2 }, false)]
    public async Task Admits_BeginsWithOnABinary_IsAPrefixTest(byte[] sortKey, bool expected)
    {
        // Arrange
        var range = new SortKeyRange(SortKeyOperator.BeginsWith, AttributeValue.FromBinary([1, 2]));

        // Act
        var admits = range.Admits(AttributeValue.FromBinary(sortKey));

        // Assert
        await Assert.That(admits).IsEqualTo(expected);
    }
}
