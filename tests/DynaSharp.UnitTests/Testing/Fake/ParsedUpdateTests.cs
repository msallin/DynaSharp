using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// What the fake does not implement here answers UnsupportedByFakeException, which names it.
public sealed class ParsedUpdateTests
{
    [Test]
    public async Task Apply_AnyItem_AnswersUnsupported()
    {
        // Arrange
        // Act
        var act = () => new ParsedUpdate("SET a = :v").Apply(null, []);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessageContaining("does not support the update expression SET a = :v", StringComparison.Ordinal);
    }
}
