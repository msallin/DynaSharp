using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// What the fake does not implement here answers UnsupportedByFakeException, which names it.
public sealed class ParsedConditionTests
{
    [Test]
    public async Task Evaluate_AnyItem_AnswersUnsupported()
    {
        // Arrange
        // Act
        var act = () => new ParsedCondition("a = :v").Evaluate(null);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessageContaining("does not support the condition expression a = :v", StringComparison.Ordinal);
    }
}
