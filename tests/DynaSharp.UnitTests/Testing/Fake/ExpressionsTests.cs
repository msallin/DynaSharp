using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// What the fake does not implement here answers UnsupportedByFakeException, which names it.
public sealed class ExpressionsTests
{
    [Test]
    public async Task ParseCondition_AnyText_AnswersUnsupported()
    {
        // Arrange
        // Act
        var act = () => global::DynaSharp.Testing.Fake.Expressions.ParseCondition("a = :v", Scope());

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessageContaining("does not support a condition expression", StringComparison.Ordinal);
    }

    [Test]
    public async Task ParseUpdate_AnyText_AnswersUnsupported()
    {
        // Arrange
        // Act
        var act = () => global::DynaSharp.Testing.Fake.Expressions.ParseUpdate("SET a = :v", Scope());

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessageContaining("does not support an update expression", StringComparison.Ordinal);
    }

    [Test]
    public async Task ParseKeyCondition_AnyText_AnswersUnsupported()
    {
        // Arrange
        // Act
        var act = () => global::DynaSharp.Testing.Fake.Expressions.ParseKeyCondition("pk = :v", Scope(), new KeySchema("pk", global::DynaSharp.Requests.ScalarAttributeType.String));

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessageContaining("does not support a key condition expression", StringComparison.Ordinal);
    }

    [Test]
    public async Task ParseProjection_AnyText_AnswersUnsupported()
    {
        // Arrange
        // Act
        var act = () => global::DynaSharp.Testing.Fake.Expressions.ParseProjection("a", Scope());

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessageContaining("does not support a projection expression", StringComparison.Ordinal);
    }

    private static PlaceholderScope Scope() => new(null, null);
}
