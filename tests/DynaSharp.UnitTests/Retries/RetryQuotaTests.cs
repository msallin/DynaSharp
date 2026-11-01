using DynaSharp.Retries;

namespace DynaSharp.UnitTests.Retries;

public sealed class RetryQuotaTests
{
    [Test]
    public async Task Constructor_NewQuota_HoldsFiveHundredTokens()
    {
        // Arrange (nothing to arrange)

        // Act
        var quota = new RetryQuota();

        // Assert
        await Assert.That(quota.Available).IsEqualTo(500);
    }

    [Test]
    public async Task TryAcquire_EnoughTokens_TakesThem()
    {
        // Arrange
        var quota = new RetryQuota();

        // Act
        var acquired = quota.TryAcquire(5);

        // Assert
        await Assert.That((acquired, quota.Available)).IsEqualTo((true, 495));
    }

    [Test]
    public async Task TryAcquire_ExactlyTheTokensLeft_TakesThemAll()
    {
        // Arrange
        var quota = new RetryQuota();

        // Act
        var acquired = quota.TryAcquire(500);

        // Assert
        await Assert.That((acquired, quota.Available)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task TryAcquire_OneTokenMoreThanLeft_TakesNothing()
    {
        // Arrange
        var quota = new RetryQuota();

        // Act
        var acquired = quota.TryAcquire(501);

        // Assert
        await Assert.That((acquired, quota.Available)).IsEqualTo((false, 500));
    }

    [Test]
    public async Task TryAcquire_EmptyQuota_TakesNothing()
    {
        // Arrange
        var quota = new RetryQuota();
        quota.TryAcquire(500);

        // Act
        var acquired = quota.TryAcquire(1);

        // Assert
        await Assert.That((acquired, quota.Available)).IsEqualTo((false, 0));
    }

    [Test]
    public async Task Release_AfterAcquire_GivesTheTokensBack()
    {
        // Arrange
        var quota = new RetryQuota();
        quota.TryAcquire(10);

        // Act
        quota.Release(10);

        // Assert
        await Assert.That(quota.Available).IsEqualTo(500);
    }

    [Test]
    public async Task Release_LessThanWasTaken_RefillsPartly()
    {
        // Arrange
        var quota = new RetryQuota();
        quota.TryAcquire(10);

        // Act
        quota.Release(5);

        // Assert
        await Assert.That(quota.Available).IsEqualTo(495);
    }

    [Test]
    public async Task Release_MoreThanWasTaken_StopsAtTheCapacity()
    {
        // Arrange
        var quota = new RetryQuota();
        quota.TryAcquire(5);

        // Act
        quota.Release(10);

        // Assert
        await Assert.That(quota.Available).IsEqualTo(500);
    }

    [Test]
    public async Task Release_FullQuota_StaysAtTheCapacity()
    {
        // Arrange
        var quota = new RetryQuota();

        // Act
        quota.Release(1);

        // Assert
        await Assert.That(quota.Available).IsEqualTo(500);
    }

    [Test]
    public async Task Release_ToExactlyTheCapacity_LeavesNothingAbove()
    {
        // Arrange
        var quota = new RetryQuota();
        quota.TryAcquire(5);

        // Act
        quota.Release(5);

        // Assert
        await Assert.That(quota.Available).IsEqualTo(500);
    }

    [Test]
    public async Task TryAcquire_ManyThreadsForMoreThanTheQuota_GrantsExactlyTheTokensThatExist()
    {
        // Arrange (200 threads ask for 5 tokens each: 1000 in all, against 500)
        var quota = new RetryQuota();
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 200)
            .Select(_ => OnOwnThread(() =>
            {
                start.Wait();
                return quota.TryAcquire(5);
            }))
            .ToArray();

        // Act
        start.Set();
        var granted = (await Task.WhenAll(tasks)).Count(result => result);

        // Assert
        await Assert.That((granted, quota.Available)).IsEqualTo((100, 0));
    }

    [Test]
    public async Task TryAcquireAndRelease_ManyThreadsInParallel_EndAtTheCapacity()
    {
        // Arrange (16 threads acquire and release the same amount many times; every acquire succeeds because 16 * 5 < 500)
        var quota = new RetryQuota();
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 16)
            .Select(_ => OnOwnThread(() =>
            {
                start.Wait();
                var failed = 0;
                for (var i = 0; i < 50_000; i++)
                {
                    if (quota.TryAcquire(5))
                    {
                        quota.Release(5);
                    }
                    else
                    {
                        failed++;
                    }
                }

                return failed;
            }))
            .ToArray();

        // Act
        start.Set();
        var failed = (await Task.WhenAll(tasks)).Sum();

        // Assert
        await Assert.That((failed, quota.Available)).IsEqualTo((0, 500));
    }

    [Test]
    public async Task Release_ManyThreadsOnAnEmptyQuota_NeverFillsItAboveTheCapacity()
    {
        // Arrange (a drained quota; 200 threads give back 1000 tokens against room for 500)
        var quota = new RetryQuota();
        quota.TryAcquire(500);
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 200)
            .Select(_ => OnOwnThread(() =>
            {
                start.Wait();
                quota.Release(5);
                return 0;
            }))
            .ToArray();

        // Act
        start.Set();
        await Task.WhenAll(tasks);

        // Assert
        await Assert.That(quota.Available).IsEqualTo(500);
    }

    // A thread of its own per worker, so that they really run at the same time and wait for the start signal.
    private static Task<T> OnOwnThread<T>(Func<T> work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
