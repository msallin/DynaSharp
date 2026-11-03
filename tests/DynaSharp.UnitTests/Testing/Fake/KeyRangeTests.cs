using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

public sealed class KeyRangeTests
{
    [Test]
    public async Task Constructor_PartitionAndSort_KeepsBoth()
    {
        // Arrange
        var sort = new SortKeyRange(SortKeyOperator.BeginsWith, "ORDER#");

        // Act
        var range = new KeyRange("c#1", sort);

        // Assert
        await Assert.That((range.Partition.AsString(), range.Sort)).IsEqualTo(("c#1", sort));
    }
}
