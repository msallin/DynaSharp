using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// What the fake does not implement here answers UnsupportedByFakeException, which names it.
public sealed class PageBuilderTests
{
    [Test]
    public async Task Build_AnyItems_AnswersUnsupported()
    {
        // Arrange
        // Act
        var act = () => PageBuilder.Build([], new PageOptions(null, null, null, SelectKind.Unspecified, Table(), null));

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessageContaining("does not support a page of a read", StringComparison.Ordinal);
    }

    private static FakeTable Table() => new("Orders", new KeySchema("pk", global::DynaSharp.Requests.ScalarAttributeType.String), [], DateTimeOffset.UnixEpoch);
}
