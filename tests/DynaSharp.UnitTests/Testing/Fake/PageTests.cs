using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

public sealed class PageTests
{
    [Test]
    public async Task Constructor_Counts_KeepsThem()
    {
        // Arrange
        Item[] items = [new() { ["pk"] = "a" }];

        // Act
        var page = new Page(items, 1, 3, null, 0.5, 0);

        // Assert
        await Assert.That((page.Items.Count, page.Count, page.ScannedCount, page.LastEvaluatedKey, page.Units, page.FetchUnits)).IsEqualTo((1, 1, 3, (Item?)null, 0.5, 0.0));
    }
}
