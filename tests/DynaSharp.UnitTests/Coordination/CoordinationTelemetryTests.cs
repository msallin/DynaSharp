using System.Diagnostics;
using System.Diagnostics.Metrics;
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

    [Test]
    public async Task ContextOf_NoSpan_IsTheDefault()
    {
        // Arrange
        Activity? none = null;

        // Act
        var context = CoordinationTelemetry.ContextOf(none);

        // Assert
        await Assert.That(context == default).IsTrue();
    }

    [Test]
    public async Task LinksTo_NoSpan_IsNone()
    {
        // Arrange
        ActivityContext none = default;

        // Act
        var links = CoordinationTelemetry.LinksTo(none);

        // Assert
        await Assert.That(links).IsNull();
    }

    [Test]
    public async Task Meter_LockInstruments_HaveTheirUnitsAndDescriptions()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", TimeSpan.FromSeconds(30));
        var instruments = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Meter.Name == "DynaSharp" && instrument.Name.StartsWith("dynasharp.lock.", StringComparison.Ordinal))
            {
                lock (instruments)
                {
                    instruments.Add($"{instrument.Name} {instrument.Unit} {instrument.Description}");
                }
            }
        };

        // Act
        listener.Start();

        // Assert
        await Assert.That(instruments.Order(StringComparer.Ordinal)).IsEquivalentTo(
        [
            "dynasharp.lock.acquisitions {lease} Number of leases a LockTable acquired, also by taking over a lock whose holder stopped renewing it.",
            "dynasharp.lock.contention_retries {retry} Number of times RunAsLeaderAsync contended again after an acquire failed with a transient error, with error.type.",
            "dynasharp.lock.losses {lease} Number of leases lost before they were released: taken over, deleted, run out, or the table gone.",
            "dynasharp.lock.renewals {renewal} Number of renewals of leases, by the heartbeat or by RenewAsync, with error.type on the failed ones.",
            "dynasharp.lock.wakeups {wakeup} Number of times a lease released in this process woke an acquire of the same LockTable that waited for the lock, before its next poll.",
        ]);
    }
}
