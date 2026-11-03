namespace DynaSharp.IntegrationTests.Support;

/// <summary>What a test with a failed cleanup reports, which needs no backend: its own failure if it had one, the failures of the cleanup if it passed.</summary>
public sealed class CleanupFailuresTests
{
    [Test]
    public async Task ToThrow_TestFailed_KeepsTheFailureOfTheTest()
    {
        // Arrange
        var body = new InvalidOperationException("The assertion of the test.");
        Exception[] cleanup = [new InvalidOperationException("The table t could not be deleted.")];

        // Act
        var thrown = CleanupFailures.ToThrow(body, cleanup);

        // Assert
        await Assert.That(thrown).IsNull();
    }

    [Test]
    public async Task ToThrow_TestPassed_FailsWithEveryCleanupFailureInOrder()
    {
        // Arrange
        Exception[] cleanup = [new InvalidOperationException("The table a could not be deleted."), new InvalidOperationException("The table b could not be deleted.")];

        // Act
        var thrown = CleanupFailures.ToThrow(null, cleanup);

        // Assert
        await Assert.That(thrown!.InnerExceptions.SequenceEqual(cleanup)).IsTrue();
    }

    [Test]
    public async Task ToThrow_TestPassedAndCleanupDidToo_ThrowsNothing()
    {
        // Act
        var thrown = CleanupFailures.ToThrow(null, []);

        // Assert
        await Assert.That(thrown).IsNull();
    }
}
