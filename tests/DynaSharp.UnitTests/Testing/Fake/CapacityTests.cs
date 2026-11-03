using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// What the fake does not implement here answers UnsupportedByFakeException, which names it.
public sealed class CapacityTests
{
    [Test]
    public async Task ReadUnits_AnySize_AnswersUnsupported()
    {
        // Arrange
        // Act
        var act = () => Capacity.ReadUnits(1, consistent: true);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessageContaining("does not support the capacity units of a read", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteUnits_AnySize_AnswersUnsupported()
    {
        // Arrange
        // Act
        var act = () => Capacity.WriteUnits(1, 2);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessageContaining("does not support the capacity units of a write", StringComparison.Ordinal);
    }

    [Test]
    public async Task IndexWriteUnits_AnyChange_AnswersUnsupported()
    {
        // Arrange
        // Act
        var act = () => Capacity.IndexWriteUnits(Table(), null, null);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessageContaining("does not support the capacity units of an index", StringComparison.Ordinal);
    }

    [Test]
    public async Task Write_AnyUnits_AnswersUnsupported()
    {
        // Arrange
        // Act
        var act = () => Capacity.Write(null!, ReturnConsumedCapacity.Total, "Orders", 1, isRead: true, [new IndexUnits("ByX", true, 1)]);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessageContaining("does not support ReturnConsumedCapacity", StringComparison.Ordinal);
    }

    private static FakeTable Table() => new("Orders", new KeySchema("pk", global::DynaSharp.Requests.ScalarAttributeType.String), [], DateTimeOffset.UnixEpoch);
}
