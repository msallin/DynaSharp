using DynaSharp.Credentials;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynaSharp.UnitTests.Credentials;

public sealed class RefreshingCredentialsCacheTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromTicks(1);

    [Test]
    public async Task GetAsync_FirstCall_FetchesOnceAndReturnsTheCredentials()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var credentials = Credentials("first", time.GetUtcNow().AddHours(6));
        var calls = 0;
        var cache = Create(time, () => Fetched(ref calls, credentials));

        // Act
        var outcome = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That((outcome.Credentials, calls)).IsEqualTo((credentials, 1));
    }

    [Test]
    public async Task GetAsync_SecondCallWellBeforeExpiry_ReturnsTheSameInstanceWithoutFetching()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => Fetched(ref calls, Credentials("a", time.GetUtcNow().AddHours(6))));
        var first = await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromHours(5));

        // Act
        var second = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That((ReferenceEquals(first.Credentials, second.Credentials), calls)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task GetAsync_ExactlyFifteenMinutesLeft_DoesNotRefresh()
    {
        // Arrange (botocore refreshes when less than 15 minutes are left)
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => Fetched(ref calls, Credentials("a", time.GetUtcNow().AddHours(1))));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(45));

        // Act
        await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task GetAsync_OneTickLessThanFifteenMinutesLeft_StartsARefreshAndReturnsTheCurrentCredentials()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => Fetched(ref calls, Credentials($"key{calls}", time.GetUtcNow().AddHours(1))));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(45) + Tick);

        // Act
        var outcome = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That((outcome.Credentials!.AccessKeyId, calls)).IsEqualTo(("key0", 2));
    }

    [Test]
    public async Task GetAsync_AfterARefresh_ReturnsTheNewInstanceUntilItIsDueToo()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => Fetched(ref calls, Credentials($"key{calls}", time.GetUtcNow().AddHours(1))));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(50));

        // The stub completes at once, so the refresh this call starts is done when it returns.
        await cache.GetAsync(CancellationToken.None);
        var refreshed = await cache.GetAsync(CancellationToken.None);

        // Act
        var again = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That((ReferenceEquals(refreshed.Credentials, again.Credentials), calls)).IsEqualTo((true, 2));
    }

    [Test]
    public async Task GetAsync_CredentialsWithoutExpiration_AreNeverRefreshed()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => Fetched(ref calls, Credentials("a", null)));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromDays(400));

        // Act
        await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    [Arguments("unavailable")]
    [Arguments("bug")]
    [Arguments("reason")]
    [Arguments("expired")]
    public async Task GetAsync_AdvisoryRefreshFails_KeepsServingTheCurrentCredentials(string failure)
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => calls++ == 0 ? Found(Credentials("old", time.GetUtcNow().AddMinutes(20))) : Fail(failure, time));
        var first = await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(6));

        // Act
        var outcome = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(outcome.Credentials).IsSameReferenceAs(first.Credentials);
    }

    [Test]
    [Arguments("unavailable")]
    [Arguments("bug")]
    [Arguments("reason")]
    [Arguments("expired")]
    public async Task GetAsync_AdvisoryRefreshFails_LogsAWarning(string failure)
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var logs = new CapturingLoggerFactory();
        var calls = 0;
        var cache = Create(time, () => calls++ == 0 ? Found(Credentials("old", time.GetUtcNow().AddMinutes(20))) : Fail(failure, time), logs.CreateLogger("test"));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(6));

        // Act
        await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(logs.Entries.Select(entry => (entry.EventId.Id, entry.Level)).Single()).IsEqualTo((21, LogLevel.Warning));
    }

    [Test]
    public async Task GetAsync_AdvisoryRefreshFails_WarningNamesTheSourceAndTheReasonButNoSecret()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var logs = new CapturingLoggerFactory();
        var calls = 0;
        var cache = Create(
            time,
            () => calls++ == 0
                ? Found(Credentials("old", time.GetUtcNow().AddMinutes(20)))
                : throw new CredentialsUnavailableException("the endpoint returned HTTP 503"),
            logs.CreateLogger("test"));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(6));

        // Act
        await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(logs.Entries.Single().Message).IsEqualTo("Refreshing the credentials from the test source failed in the advisory refresh period: the endpoint returned HTTP 503. The current credentials stay in use.");
    }

    [Test]
    public async Task GetAsync_OneTickBeforeTheRetryInterval_DoesNotFetchAgain()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => calls++ == 0 ? Found(Credentials("old", time.GetUtcNow().AddMinutes(20))) : Fail("unavailable", time));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(6));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(RefreshingCredentialsCache.AdvisoryRetryInterval - TimeSpan.FromTicks(1));

        // Act
        await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task GetAsync_FreshCredentialsAlreadyInTheAdvisoryPeriod_WaitTheRetryIntervalBeforeTheNextFetch()
    {
        // Arrange (a session of 15 minutes, the least AssumeRole grants, arrives inside the advisory period)
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => Fetched(ref calls, Credentials($"key{calls}", time.GetUtcNow().AddMinutes(15) - Tick)));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(RefreshingCredentialsCache.AdvisoryRetryInterval - Tick);

        // Act
        await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task GetAsync_FreshCredentialsAlreadyInTheAdvisoryPeriodAfterTheRetryInterval_FetchesAgain()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => Fetched(ref calls, Credentials($"key{calls}", time.GetUtcNow().AddMinutes(15) - Tick)));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(RefreshingCredentialsCache.AdvisoryRetryInterval);

        // Act
        await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task GetAsync_AfterTheRetryInterval_FetchesAgain()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => calls++ == 0 ? Found(Credentials("old", time.GetUtcNow().AddMinutes(20))) : Fail("unavailable", time));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(6));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(RefreshingCredentialsCache.AdvisoryRetryInterval);

        // Act
        await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(calls).IsEqualTo(3);
    }

    [Test]
    public async Task GetAsync_MandatoryPeriodDuringTheRetryInterval_FetchesAtOnce()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => calls++ switch
        {
            0 => Found(Credentials("old", time.GetUtcNow().AddMinutes(20))),
            1 => Fail("unavailable", time),
            _ => Found(Credentials("new", time.GetUtcNow().AddHours(6))),
        });
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(10));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(20));

        // Act
        var outcome = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(outcome.Credentials!.AccessKeyId).IsEqualTo("new");
    }

    [Test]
    public async Task GetAsync_ExactlyTenMinutesLeftAndRefreshFails_IsStillAdvisory()
    {
        // Arrange (the mandatory period starts with less than 10 minutes left)
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => calls++ == 0 ? Found(Credentials("old", time.GetUtcNow().AddMinutes(20))) : Fail("unavailable", time));
        var first = await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(10));

        // Act
        var outcome = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(outcome.Credentials).IsSameReferenceAs(first.Credentials);
    }

    [Test]
    [Arguments("unavailable")]
    [Arguments("reason")]
    [Arguments("expired")]
    public async Task GetAsync_OneTickLessThanTenMinutesLeftAndRefreshFails_ThrowsCredentialsUnavailableException(string failure)
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => calls++ == 0 ? Found(Credentials("old", time.GetUtcNow().AddMinutes(20))) : Fail(failure, time));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(10) + Tick);

        // Act
        var act = async () => await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>();
    }

    [Test]
    public async Task GetAsync_MandatoryRefreshFailsWithReason_ThrowsAnExceptionWithTheReason()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => calls++ == 0 ? Found(Credentials("old", time.GetUtcNow().AddMinutes(20))) : Fail("reason", time));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(11));

        // Act
        var act = async () => await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessage("Refreshing the credentials from the test source failed: the source has no credentials.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetAsync_MandatoryRefreshThrows_ThrowsTheOriginalExceptionInstance()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var original = new InvalidOperationException("bug in the fetch");
        var calls = 0;
        var cache = Create(time, () => calls++ == 0 ? Found(Credentials("old", time.GetUtcNow().AddMinutes(20))) : throw original);
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(11));

        // Act
        var exception = await TestClient.CaptureAsync<InvalidOperationException>(async () => await cache.GetAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception).IsSameReferenceAs(original);
    }

    [Test]
    public async Task GetAsync_AdvisoryRefreshFailsWithAMessageEndingInAPeriod_WarningHasNoDoublePeriod()
    {
        // Arrange (the warning adds its own period after the reason)
        var time = new ManualTimeProvider();
        using var logs = new CapturingLoggerFactory();
        var calls = 0;
        var cache = Create(
            time,
            () => calls++ == 0
                ? Found(Credentials("old", time.GetUtcNow().AddMinutes(20)))
                : throw new CredentialsUnavailableException("the endpoint returned HTTP 503."),
            logs.CreateLogger("test"));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(6));

        // Act
        await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(logs.Entries.Single().Message).IsEqualTo("Refreshing the credentials from the test source failed in the advisory refresh period: the endpoint returned HTTP 503. The current credentials stay in use.");
    }

    [Test]
    public async Task GetAsync_MandatoryRefreshFails_LogsAWarningOfTheMandatoryPeriod()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var logs = new CapturingLoggerFactory();
        var calls = 0;
        var cache = Create(time, () => calls++ == 0 ? Found(Credentials("old", time.GetUtcNow().AddMinutes(20))) : Fail("unavailable", time), logs.CreateLogger("test"));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(11));

        // Act
        await Assert.That(async () => await cache.GetAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>();

        // Assert
        await Assert.That(logs.Entries.Select(entry => (entry.EventId.Id, entry.Level)).Single()).IsEqualTo((22, LogLevel.Warning));
    }

    [Test]
    public async Task GetAsync_AdvisoryFetchEndsInTheMandatoryPeriod_LogsTheMandatoryWarning()
    {
        // Arrange (a caller of the advisory period starts the fetch; by the time it fails, the credentials are in the mandatory period)
        var time = new ManualTimeProvider();
        using var logs = new CapturingLoggerFactory();
        var fetch = new GatedFetch();
        var cache = Create(time, fetch.FetchAsync, logs.CreateLogger("test"));
        var firstCall = cache.GetAsync(CancellationToken.None);
        fetch.Complete(0, CredentialsOutcome.Found(Credentials("old", time.GetUtcNow().AddMinutes(20))));
        await firstCall;
        time.Advance(TimeSpan.FromMinutes(6));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(5));
        var waiter = cache.GetAsync(CancellationToken.None);

        // Act
        fetch.Complete(1, CredentialsOutcome.DoesNotApply("the source has no credentials"));
        await Assert.That(async () => await waiter).ThrowsExactly<CredentialsUnavailableException>();

        // Assert
        await Assert.That(logs.Entries.Select(entry => (entry.EventId.Id, entry.Level)).Single()).IsEqualTo((22, LogLevel.Warning));
    }

    [Test]
    public async Task GetAsync_MandatoryFetchStartedInTheAdvisoryPeriod_WaiterGetsTheFailure()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var fetch = new GatedFetch();
        var cache = Create(time, fetch.FetchAsync);
        var firstCall = cache.GetAsync(CancellationToken.None);
        fetch.Complete(0, CredentialsOutcome.Found(Credentials("old", time.GetUtcNow().AddMinutes(20))));
        await firstCall;
        time.Advance(TimeSpan.FromMinutes(6));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(5));
        var waiter = cache.GetAsync(CancellationToken.None);

        // Act
        fetch.Complete(1, CredentialsOutcome.DoesNotApply("the source has no credentials"));

        // Assert
        await Assert.That(async () => await waiter).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("the source has no credentials", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetAsync_LoggerThrowsWhenAMandatoryRefreshFails_ThrowsTheFailureOfTheRefresh()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => calls++ == 0 ? Found(Credentials("old", time.GetUtcNow().AddMinutes(20))) : Fail("unavailable", time), new ThrowingLogger("logger broke"));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(11));

        // Act
        var act = async () => await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>();
    }

    [Test]
    public async Task GetAsync_LoggerThrowsWhenARefreshFails_TheNextRefreshStillRuns()
    {
        // Arrange (the flight ended although the logger threw, so a later call starts a new one and gets the new credentials)
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(
            time,
            () => calls++ switch
            {
                0 => Found(Credentials("old", time.GetUtcNow().AddMinutes(20))),
                1 => Fail("unavailable", time),
                _ => Found(Credentials("new", time.GetUtcNow().AddHours(1))),
            },
            new ThrowingLogger("logger broke"));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(11));
        await Assert.That(async () => await cache.GetAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>();

        // Act
        var outcome = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(outcome.Credentials!.AccessKeyId).IsEqualTo("new");
    }

    [Test]
    public async Task GetAsync_LoggerThrowsWhenAnAdvisoryRefreshFails_NoExceptionGoesUnobserved()
    {
        // Arrange (the refresh runs on its own and nobody awaits it, so an exception that escaped it would surface as an unobserved task
        // exception; the marker keeps the test from seeing the exceptions of other tests)
        const string marker = "logger broke 7c1e5a";
        var unobserved = new List<string>();
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            lock (unobserved)
            {
                unobserved.AddRange(args.Exception.Flatten().InnerExceptions.Select(exception => exception.Message).Where(message => message == marker));
            }
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            // Act
            await FailAdvisoryRefreshWithAThrowingLoggerAsync(marker);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        // Assert
        await Assert.That(unobserved).IsEmpty();
    }

    [Test]
    public async Task GetAsync_MandatoryRefreshFailed_TheNextCallTriesAgainAndCanSucceed()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(
            time,
            () => ++calls switch
            {
                1 => Found(Credentials("old", time.GetUtcNow().AddMinutes(20))),
                2 => Fail("unavailable", time),
                _ => Found(Credentials("new", time.GetUtcNow().AddHours(6))),
            });
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(11));
        await Assert.That(async () => await cache.GetAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>();

        // Act
        var outcome = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(outcome.Credentials!.AccessKeyId).IsEqualTo("new");
    }

    [Test]
    public async Task GetAsync_CredentialsExpired_WaitsForTheRefresh()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => Fetched(ref calls, Credentials($"key{calls}", time.GetUtcNow().AddMinutes(20))));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromHours(3));

        // Act
        var outcome = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(outcome.Credentials!.AccessKeyId).IsEqualTo("key1");
    }

    [Test]
    public async Task GetAsync_FirstFetchHasNoCredentials_ReturnsTheReasonAndDoesNotLog()
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var cache = Create(new ManualTimeProvider(), () => ValueTask.FromResult(CredentialsOutcome.DoesNotApply("nothing here")), logs.CreateLogger("test"));

        // Act
        var outcome = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That((outcome.Credentials, outcome.Reason, logs.Entries.Count)).IsEqualTo(((AwsCredentials?)null, "nothing here", 0));
    }

    [Test]
    public async Task GetAsync_FirstFetchThrows_ThrowsAndDoesNotLog()
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var cache = Create(new ManualTimeProvider(), () => throw new CredentialsUnavailableException("broken"), logs.CreateLogger("test"));

        // Act
        await Assert.That(async () => await cache.GetAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>().WithMessage("broken", StringComparison.Ordinal);

        // Assert
        await Assert.That(logs.Entries).IsEmpty();
    }

    [Test]
    public async Task GetAsync_FirstFetchFailedThenWorks_TheFailureIsNotCached()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => calls++ == 0 ? throw new CredentialsUnavailableException("broken") : Found(Credentials("a", time.GetUtcNow().AddHours(1))));
        await Assert.That(async () => await cache.GetAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>();

        // Act
        var outcome = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(outcome.Credentials!.AccessKeyId).IsEqualTo("a");
    }

    [Test]
    public async Task GetAsync_FirstFetchReturnsExpiredCredentials_ReturnsABrokenOutcomeThatMentionsTheClock()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var cache = Create(time, () => Found(Credentials("a", time.GetUtcNow() - Tick)));

        // Act
        var outcome = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That((outcome.Kind, outcome.Reason)).IsEqualTo((CredentialsOutcomeKind.Broken, "it returned credentials that are already expired (check the system clock)"));
    }

    [Test]
    public async Task GetAsync_FirstFetchDoesNotApply_KeepsThatKind()
    {
        // Arrange
        var cache = Create(new ManualTimeProvider(), () => ValueTask.FromResult(CredentialsOutcome.DoesNotApply("nothing here")));

        // Act
        var outcome = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(outcome.Kind).IsEqualTo(CredentialsOutcomeKind.DoesNotApply);
    }

    [Test]
    public async Task GetAsync_FetchReturnsCredentialsThatExpireExactlyNow_AcceptsThem()
    {
        // Arrange (botocore calls credentials expired only when less than zero seconds remain)
        var time = new ManualTimeProvider();
        var credentials = Credentials("a", time.GetUtcNow());
        var cache = Create(time, () => Found(credentials));

        // Act
        var outcome = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(outcome.Credentials).IsSameReferenceAs(credentials);
    }

    [Test]
    public async Task GetAsync_ManyCallersAtTheSameTime_FetchOnceAndAllGetTheSameInstance()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var fetch = new GatedFetch();
        var cache = Create(time, fetch.FetchAsync);
        var callers = Enumerable.Range(0, 8).Select(_ => cache.GetAsync(CancellationToken.None)).ToArray();
        var credentials = Credentials("shared", time.GetUtcNow().AddHours(6));

        // Act
        fetch.Complete(0, CredentialsOutcome.Found(credentials));
        var results = new List<AwsCredentials?>();
        foreach (var caller in callers)
        {
            results.Add((await caller).Credentials);
        }

        // Assert
        await Assert.That((results.Count(r => ReferenceEquals(r, credentials)), fetch.Calls)).IsEqualTo((8, 1));
    }

    [Test]
    public async Task GetAsync_ManyThreadsAtTheSameTime_FetchOnce()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => Fetched(ref calls, Credentials("a", time.GetUtcNow().AddHours(6))));
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(
                async () =>
                {
                    start.Wait();
                    return (await cache.GetAsync(CancellationToken.None)).Credentials;
                }))
            .ToArray();

        // Act
        start.Set();
        var results = await Task.WhenAll(tasks);

        // Assert
        await Assert.That((results.Distinct().Count(), Volatile.Read(ref calls))).IsEqualTo((1, 1));
    }

    [Test]
    public async Task GetAsync_AdvisoryRefreshInProgress_OtherCallersReturnTheCurrentCredentialsAtOnce()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var fetch = new GatedFetch();
        var cache = Create(time, fetch.FetchAsync);
        var firstCall = cache.GetAsync(CancellationToken.None);
        var old = Credentials("old", time.GetUtcNow().AddMinutes(20));
        fetch.Complete(0, CredentialsOutcome.Found(old));
        await firstCall;
        time.Advance(TimeSpan.FromMinutes(6));
        var refresher = cache.GetAsync(CancellationToken.None);

        // Act
        var other = cache.GetAsync(CancellationToken.None);
        var completedAtOnce = other.IsCompleted;

        // Assert
        await Assert.That((completedAtOnce, (await other).Credentials, fetch.Calls)).IsEqualTo((true, old, 2));
        fetch.Complete(1, CredentialsOutcome.Found(Credentials("new", time.GetUtcNow().AddHours(6))));
        await refresher;
    }

    [Test]
    public async Task GetAsync_AdvisoryRefreshInProgress_TheCallerThatStartedItDoesNotWait()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var fetch = new GatedFetch();
        var cache = Create(time, fetch.FetchAsync);
        var firstCall = cache.GetAsync(CancellationToken.None);
        var old = Credentials("old", time.GetUtcNow().AddMinutes(20));
        fetch.Complete(0, CredentialsOutcome.Found(old));
        await firstCall;
        time.Advance(TimeSpan.FromMinutes(6));

        // Act
        var refresher = cache.GetAsync(CancellationToken.None);
        var completedAtOnce = refresher.IsCompleted;

        // Assert
        await Assert.That((completedAtOnce, (await refresher).Credentials)).IsEqualTo((true, old));
        fetch.Complete(1, CredentialsOutcome.Found(Credentials("new", time.GetUtcNow().AddHours(6))));
    }

    [Test]
    public async Task GetAsync_AfterAnAdvisoryRefreshCompleted_ReturnsTheNewCredentials()
    {
        // Arrange (the stub completes at once, so the refresh that the second call starts is done when it returns)
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => Fetched(ref calls, Credentials($"key{calls}", time.GetUtcNow().AddHours(1))));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(50));
        await cache.GetAsync(CancellationToken.None);

        // Act
        var outcome = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(outcome.Credentials!.AccessKeyId).IsEqualTo("key1");
    }

    [Test]
    public async Task GetAsync_MandatoryRefreshInProgress_OtherCallersWaitForItAndShareTheResult()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var fetch = new GatedFetch();
        var cache = Create(time, fetch.FetchAsync);
        var firstCall = cache.GetAsync(CancellationToken.None);
        fetch.Complete(0, CredentialsOutcome.Found(Credentials("old", time.GetUtcNow().AddMinutes(20))));
        await firstCall;
        time.Advance(TimeSpan.FromMinutes(11));
        var refresher = cache.GetAsync(CancellationToken.None);
        var waiter = cache.GetAsync(CancellationToken.None);
        var waitedAtOnce = waiter.IsCompleted;

        // Act
        fetch.Complete(1, CredentialsOutcome.Found(Credentials("new", time.GetUtcNow().AddHours(6))));
        var shared = await waiter;

        // Assert
        await Assert.That((waitedAtOnce, shared.Credentials!.AccessKeyId, fetch.Calls)).IsEqualTo((false, "new", 2));
        await refresher;
    }

    [Test]
    public async Task GetAsync_OneWaiterCancels_ItStopsWaitingWithoutCancellingTheFetchForTheOthers()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var fetch = new GatedFetch();
        var cache = Create(time, fetch.FetchAsync);
        using var cancelFirst = new CancellationTokenSource();
        var first = cache.GetAsync(cancelFirst.Token);
        var second = cache.GetAsync(CancellationToken.None);
        var credentials = Credentials("a", time.GetUtcNow().AddHours(6));

        // Act
        await cancelFirst.CancelAsync();
        await Assert.That(async () => await first).Throws<OperationCanceledException>();
        fetch.Complete(0, CredentialsOutcome.Found(credentials));
        var outcome = await second;

        // Assert
        await Assert.That((outcome.Credentials, fetch.Calls)).IsEqualTo((credentials, 1));
    }

    [Test]
    public async Task GetAsync_TheCallerThatStartedTheFetchCancels_TheFetchStillCompletesForTheOthersAndIsCached()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var fetch = new GatedFetch();
        var cache = Create(time, fetch.FetchAsync);
        using var cancelStarter = new CancellationTokenSource();
        var starter = cache.GetAsync(cancelStarter.Token);
        var other = cache.GetAsync(CancellationToken.None);
        await cancelStarter.CancelAsync();
        await Assert.That(async () => await starter).Throws<OperationCanceledException>();
        fetch.Complete(0, CredentialsOutcome.Found(Credentials("a", time.GetUtcNow().AddHours(6))));
        await other;

        // Act
        var later = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That((later.Credentials!.AccessKeyId, fetch.Calls)).IsEqualTo(("a", 1));
    }

    [Test]
    public async Task GetAsync_CancelledBeforeTheCall_ThrowsOperationCanceledException()
    {
        // Arrange
        var cache = Create(new ManualTimeProvider(), () => Found(Credentials("a", null)));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        var act = async () => await cache.GetAsync(cancelled.Token);

        // Assert
        await Assert.That(act).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task GetAsync_CancelledBeforeTheCall_DoesNotStartAFetch()
    {
        // Arrange
        var calls = 0;
        var cache = Create(new ManualTimeProvider(), () => Fetched(ref calls, Credentials("a", null)));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.That(async () => await cache.GetAsync(cancelled.Token)).Throws<OperationCanceledException>();

        // Act
        var fetches = calls;

        // Assert
        await Assert.That(fetches).IsEqualTo(0);
    }

    [Test]
    public async Task GetAsync_CancelledTokenButCredentialsAreValid_ReturnsThem()
    {
        // Arrange (the fast path costs nothing, so it does not look at the token)
        var cache = Create(new ManualTimeProvider(), () => Found(Credentials("a", null)));
        await cache.GetAsync(CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        var outcome = await cache.GetAsync(cancelled.Token);

        // Assert
        await Assert.That(outcome.Credentials!.AccessKeyId).IsEqualTo("a");
    }

    [Test]
    public async Task GetAsync_AnotherCallerRefreshesAfterTheFastPathLooked_UsesThoseCredentialsWithoutAnotherFetch()
    {
        // Arrange (the hook runs a complete refresh between the cheap check and the lock, which no schedule of threads can force)
        var inner = new ManualTimeProvider();
        var hooked = new HookedTimeProvider(inner);
        var calls = 0;
        var cache = new RefreshingCredentialsCache(
            "test source",
            hooked,
            NullLogger.Instance,
            () => Fetched(ref calls, Credentials($"key{calls}", inner.GetUtcNow().AddMinutes(20))));
        await cache.GetAsync(CancellationToken.None);
        inner.Advance(TimeSpan.FromMinutes(6));
        hooked.OnGetUtcNow = callNumber =>
        {
            if (callNumber == 2)
            {
                cache.GetAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            }
        };
        hooked.Reset();

        // Act
        var outcome = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That((outcome.Credentials!.AccessKeyId, calls)).IsEqualTo(("key1", 2));
    }

    [Test]
    public async Task Drop_TheCurrentCredentials_TheNextCallFetches()
    {
        // Arrange (well before the expiry: the service knows better)
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => Fetched(ref calls, Credentials($"key{calls}", time.GetUtcNow().AddHours(6))));
        var current = await cache.GetAsync(CancellationToken.None);
        cache.Drop(current.Credentials!);

        // Act
        var outcome = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That((outcome.Credentials!.AccessKeyId, calls)).IsEqualTo(("key1", 2));
    }

    [Test]
    public async Task Drop_CredentialsThatWereAlreadyReplaced_KeepsTheNewerOnesWithoutFetching()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => Fetched(ref calls, Credentials($"key{calls}", time.GetUtcNow().AddHours(6))));
        var old = await cache.GetAsync(CancellationToken.None);
        cache.Drop(old.Credentials!);
        var newer = await cache.GetAsync(CancellationToken.None);

        // Act
        cache.Drop(old.Credentials!);
        var outcome = await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That((ReferenceEquals(outcome.Credentials, newer.Credentials), calls)).IsEqualTo((true, 2));
    }

    [Test]
    public async Task Drop_ManyCallersAfterwards_FetchOnceAndAllGetTheSameInstance()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var fetch = new GatedFetch();
        var cache = Create(time, fetch.FetchAsync);
        var firstCall = cache.GetAsync(CancellationToken.None);
        var old = Credentials("old", time.GetUtcNow().AddHours(6));
        fetch.Complete(0, CredentialsOutcome.Found(old));
        await firstCall;
        cache.Drop(old);
        var callers = Enumerable.Range(0, 8).Select(_ => cache.GetAsync(CancellationToken.None)).ToArray();
        var fresh = Credentials("new", time.GetUtcNow().AddHours(6));

        // Act
        fetch.Complete(1, CredentialsOutcome.Found(fresh));
        var results = new List<AwsCredentials?>();
        foreach (var caller in callers)
        {
            results.Add((await caller).Credentials);
        }

        // Assert
        await Assert.That((results.Count(r => ReferenceEquals(r, fresh)), fetch.Calls)).IsEqualTo((8, 2));
    }

    [Test]
    public async Task Drop_WhileAnAdvisoryRefreshRuns_CallersWaitForThatRefresh()
    {
        // Arrange (the refresh is already there, so a second fetch would be for nothing, but its result is not there yet)
        var time = new ManualTimeProvider();
        var fetch = new GatedFetch();
        var cache = Create(time, fetch.FetchAsync);
        var firstCall = cache.GetAsync(CancellationToken.None);
        var old = Credentials("old", time.GetUtcNow().AddMinutes(20));
        fetch.Complete(0, CredentialsOutcome.Found(old));
        await firstCall;
        time.Advance(TimeSpan.FromMinutes(6));
        await cache.GetAsync(CancellationToken.None);

        // Act
        cache.Drop(old);
        var waiter = cache.GetAsync(CancellationToken.None);
        var waited = !waiter.IsCompleted;
        fetch.Complete(1, CredentialsOutcome.Found(Credentials("new", time.GetUtcNow().AddHours(6))));
        var outcome = await waiter;

        // Assert
        await Assert.That((waited, outcome.Credentials!.AccessKeyId, fetch.Calls)).IsEqualTo((true, "new", 2));
    }

    [Test]
    public async Task Drop_TheCredentialsDuringARefreshThatFails_AreGoneAndTheFailureIsLogged()
    {
        // Arrange (the refresh of the advisory period fails after the service rejected what it was meant to replace)
        var time = new ManualTimeProvider();
        using var logs = new CapturingLoggerFactory();
        var fetch = new GatedFetch();
        var cache = Create(time, fetch.FetchAsync, logs.CreateLogger("test"));
        var firstCall = cache.GetAsync(CancellationToken.None);
        var old = Credentials("old", time.GetUtcNow().AddMinutes(20));
        fetch.Complete(0, CredentialsOutcome.Found(old));
        await firstCall;
        time.Advance(TimeSpan.FromMinutes(6));
        await cache.GetAsync(CancellationToken.None);
        cache.Drop(old);
        var waiter = cache.GetAsync(CancellationToken.None);

        // Act
        fetch.Complete(1, CredentialsOutcome.DoesNotApply("the source has no credentials"));
        var outcome = await waiter;

        // Assert
        await Assert.That((outcome.Credentials, outcome.Reason, logs.Entries[^1].EventId.Id)).IsEqualTo(((AwsCredentials?)null, "the source has no credentials", 22));
    }

    [Test]
    public async Task Drop_FetchThrowsAfterwards_ThrowsAndLogsAWarningOfTheMandatoryPeriod()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var logs = new CapturingLoggerFactory();
        var calls = 0;
        var cache = Create(time, () => calls++ == 0 ? Found(Credentials("old", time.GetUtcNow().AddHours(6))) : Fail("unavailable", time), logs.CreateLogger("test"));
        var current = await cache.GetAsync(CancellationToken.None);
        cache.Drop(current.Credentials!);

        // Act
        await Assert.That(async () => await cache.GetAsync(CancellationToken.None)).ThrowsExactly<CredentialsUnavailableException>();

        // Assert
        await Assert.That(string.Join(',', logs.Entries.Select(entry => entry.EventId.Id))).IsEqualTo("23,22");
    }

    [Test]
    public async Task Drop_TheCurrentCredentials_LogsOnceWithoutSecrets()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var logs = new CapturingLoggerFactory();
        var cache = Create(time, () => Found(Credentials("key", time.GetUtcNow().AddHours(6))), logs.CreateLogger("test"));
        var current = await cache.GetAsync(CancellationToken.None);

        // Act
        cache.Drop(current.Credentials!);
        cache.Drop(current.Credentials!);

        // Assert
        await Assert.That(logs.Entries.Select(entry => (entry.EventId.Id, entry.Level, entry.Message)).Single())
            .IsEqualTo((23, LogLevel.Warning, "DynamoDB rejected the credentials from the test source as expired. They are dropped and fetched again."));
    }

    [Test]
    public async Task Drop_NothingFetchedYet_LogsNothingAndTheFirstCallFetches()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var logs = new CapturingLoggerFactory();
        var calls = 0;
        var cache = Create(time, () => Fetched(ref calls, Credentials("key", time.GetUtcNow().AddHours(6))), logs.CreateLogger("test"));

        // Act
        cache.Drop(Credentials("somewhere else", null));
        await cache.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That((logs.Entries.Count, calls)).IsEqualTo((0, 1));
    }

    // Its own method, so that nothing here keeps the task of the refresh alive when the caller collects garbage.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task FailAdvisoryRefreshWithAThrowingLoggerAsync(string message)
    {
        var time = new ManualTimeProvider();
        var calls = 0;
        var cache = Create(time, () => calls++ == 0 ? Found(Credentials("old", time.GetUtcNow().AddMinutes(20))) : Fail("unavailable", time), new ThrowingLogger(message));
        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(6));
        await cache.GetAsync(CancellationToken.None);
    }

    private static RefreshingCredentialsCache Create(ManualTimeProvider time, Func<ValueTask<CredentialsOutcome>> fetch, ILogger? logger = null) =>
        new("test source", time, logger ?? NullLogger.Instance, fetch);

    private static AwsCredentials Credentials(string accessKeyId, DateTimeOffset? expiration) => new(accessKeyId, "secret-" + accessKeyId, "token-" + accessKeyId, expiration);

    private static ValueTask<CredentialsOutcome> Found(AwsCredentials credentials) => ValueTask.FromResult(CredentialsOutcome.Found(credentials));

    private static ValueTask<CredentialsOutcome> Fetched(ref int calls, AwsCredentials credentials)
    {
        Interlocked.Increment(ref calls);
        return Found(credentials);
    }

    private static ValueTask<CredentialsOutcome> Fail(string failure, ManualTimeProvider time) => failure switch
    {
        "unavailable" => throw new CredentialsUnavailableException("the endpoint returned HTTP 503"),
        "bug" => throw new InvalidOperationException("bug in the fetch"),
        "reason" => ValueTask.FromResult(CredentialsOutcome.DoesNotApply("the source has no credentials")),
        _ => Found(Credentials("expired", time.GetUtcNow() - Tick)),
    };

    /// <summary>A fetch that completes when the test says so. Each call gets its own gate.</summary>
    private sealed class GatedFetch
    {
        private readonly Lock _gate = new();
        private readonly List<TaskCompletionSource<CredentialsOutcome>> _calls = [];

        public int Calls
        {
            get
            {
                lock (_gate)
                {
                    return _calls.Count;
                }
            }
        }

        public ValueTask<CredentialsOutcome> FetchAsync()
        {
            var call = new TaskCompletionSource<CredentialsOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                _calls.Add(call);
            }

            return new ValueTask<CredentialsOutcome>(call.Task);
        }

        public void Complete(int call, CredentialsOutcome outcome)
        {
            lock (_gate)
            {
                _calls[call].SetResult(outcome);
            }
        }
    }

    /// <summary>A logger that fails on every message.</summary>
    private sealed class ThrowingLogger(string message) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            throw new InvalidOperationException(message);
    }

    /// <summary>A clock that calls a hook whenever it is read, to run code between two steps of the code under test.</summary>
    private sealed class HookedTimeProvider(ManualTimeProvider inner) : TimeProvider
    {
        private int _reads;

        public Action<int>? OnGetUtcNow { get; set; }

        public void Reset() => _reads = 0;

        public override DateTimeOffset GetUtcNow()
        {
            OnGetUtcNow?.Invoke(++_reads);
            return inner.GetUtcNow();
        }
    }
}
