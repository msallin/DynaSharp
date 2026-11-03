using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// What the fake does not implement here answers UnsupportedByFakeException, which names it.
public sealed class WriteReturnsTests
{
    [Test]
    public async Task Check_AnyValues_AnswersUnsupported()
    {
        // Arrange
        // Act
        var act = () => WriteReturns.Check("PutItem", ReturnValue.AllOld, ReturnItemCollectionMetrics.None);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessageContaining("does not support ReturnValues and ReturnItemCollectionMetrics in PutItem", StringComparison.Ordinal);
    }

    [Test]
    public async Task Write_AnyImages_AnswersUnsupported()
    {
        // Arrange
        // Act
        var act = () => WriteReturns.Write(null!, Table(), ReturnValue.AllOld, ReturnItemCollectionMetrics.None, null, null);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessageContaining("does not support ReturnValues and ReturnItemCollectionMetrics", StringComparison.Ordinal);
    }

    private static FakeTable Table() => new("Orders", new KeySchema("pk", global::DynaSharp.Requests.ScalarAttributeType.String), [], DateTimeOffset.UnixEpoch);
}
