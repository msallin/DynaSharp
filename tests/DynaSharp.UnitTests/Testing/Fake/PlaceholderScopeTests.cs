using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// What the fake does not implement here answers UnsupportedByFakeException, which names it.
public sealed class PlaceholderScopeTests
{
    [Test]
    public async Task ThrowIfUnused_NoMaps_AnswersUnsupported()
    {
        // Arrange
        // Act
        var act = () => new PlaceholderScope(null, null).ThrowIfUnused();

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessageContaining("does not support 0 expression attribute names and 0 values", StringComparison.Ordinal);
    }

    [Test]
    public async Task ThrowIfUnused_Maps_AnswersUnsupported()
    {
        // Arrange
        // Act
        var act = () => new PlaceholderScope(new Dictionary<string, string> { ["#a"] = "a" }, new Item { [":v"] = 1 }).ThrowIfUnused();

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessageContaining("does not support 1 expression attribute names and 1 values", StringComparison.Ordinal);
    }
}
