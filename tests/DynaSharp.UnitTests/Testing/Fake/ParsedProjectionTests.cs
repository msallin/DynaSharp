using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// What the fake does not implement here answers UnsupportedByFakeException, which names it.
public sealed class ParsedProjectionTests
{
    [Test]
    public async Task Apply_AnyItem_AnswersUnsupported()
    {
        // Arrange
        // Act
        var act = () => new ParsedProjection("a").Apply([]);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessageContaining("does not support the projection expression a", StringComparison.Ordinal);
    }
}
