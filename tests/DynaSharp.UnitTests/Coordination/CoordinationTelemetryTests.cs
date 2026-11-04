using DynaSharp.Coordination;

namespace DynaSharp.UnitTests.Coordination;

// The test platform listens to every activity source, so a lock never meets a null span in the other tests. These pass one.
public sealed class CoordinationTelemetryTests
{
    [Test]
    public async Task Acquire_NoSpan_RecordsOnlyTheMetric()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", TimeSpan.FromSeconds(30));

        // Act
        var act = () =>
        {
            CoordinationTelemetry.Acquire(null, lease, takeOver: false);
            CoordinationTelemetry.Acquire(null, null, takeOver: false);
        };

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task Fail_NoSpan_DoesNothing()
    {
        // Arrange
        var exception = new InvalidOperationException("broken");

        // Act
        var act = () => CoordinationTelemetry.Fail(null, exception);

        // Assert
        await Assert.That(act).ThrowsNothing();
    }
}
