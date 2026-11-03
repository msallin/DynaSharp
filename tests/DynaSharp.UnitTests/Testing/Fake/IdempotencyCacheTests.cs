using System.Text.Json;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// The window of the API reference, TransactWriteItems: a client token is valid for 10 minutes after the first request that uses it.
public sealed class IdempotencyCacheTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task IsReplay_NewToken_IsFalse()
    {
        // Arrange
        var cache = new IdempotencyCache();

        // Act
        var replay = cache.IsReplay("t", JsonElement.Parse("""{"a":1}"""), Start);

        // Assert
        await Assert.That(replay).IsFalse();
    }

    [Test]
    public async Task IsReplay_SameRequestWithMembersInAnotherOrder_IsTrue()
    {
        // Arrange
        var cache = new IdempotencyCache();
        cache.Remember("t", JsonElement.Parse("""{"a":1,"b":[2]}"""), Start);

        // Act
        var replay = cache.IsReplay("t", JsonElement.Parse("""{"b":[2],"a":1}"""), Start.AddMinutes(9));

        // Assert
        await Assert.That(replay).IsTrue();
    }

    [Test]
    public async Task IsReplay_AnotherRequestWithinTheWindow_AnswersIdempotentParameterMismatch()
    {
        // Arrange
        var cache = new IdempotencyCache();
        cache.Remember("t", JsonElement.Parse("""{"a":1}"""), Start);

        // Act
        var act = () => cache.IsReplay("t", JsonElement.Parse("""{"a":2}"""), Start);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("The request uses the same client token as a previous, but non-identical request.", StringComparison.Ordinal);
    }

    [Test]
    public async Task IsReplay_AfterTheWindow_IsFalseAndForgetsTheToken()
    {
        // Arrange
        var cache = new IdempotencyCache();
        cache.Remember("t", JsonElement.Parse("""{"a":1}"""), Start);
        _ = cache.IsReplay("t", JsonElement.Parse("""{"a":2}"""), Start + IdempotencyCache.Window);

        // Act
        var replay = cache.IsReplay("t", JsonElement.Parse("""{"a":1}"""), Start);

        // Assert
        await Assert.That(replay).IsFalse();
    }

    [Test]
    public async Task Clear_RememberedToken_IsNewAgain()
    {
        // Arrange
        var cache = new IdempotencyCache();
        cache.Remember("t", JsonElement.Parse("""{"a":1}"""), Start);

        // Act
        cache.Clear();

        // Assert
        await Assert.That(cache.IsReplay("t", JsonElement.Parse("""{"a":2}"""), Start)).IsFalse();
    }
}
