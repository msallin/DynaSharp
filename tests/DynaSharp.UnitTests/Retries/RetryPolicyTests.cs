using DynaSharp.Retries;

namespace DynaSharp.UnitTests.Retries;

internal sealed class RetryPolicyTests
{
    [Test]
    [Arguments(FailureKind.Transient, 1, 0.0, 0.0)]
    [Arguments(FailureKind.Transient, 1, 1.0, 25.0)]
    [Arguments(FailureKind.Transient, 1, 0.5, 12.5)]
    [Arguments(FailureKind.Transient, 2, 1.0, 50.0)]
    [Arguments(FailureKind.Transient, 3, 1.0, 100.0)]
    [Arguments(FailureKind.Transient, 9, 1.0, 6_400.0)]
    [Arguments(FailureKind.Transient, 10, 1.0, 12_800.0)]
    [Arguments(FailureKind.Transient, 11, 1.0, 20_000.0)]
    [Arguments(FailureKind.Timeout, 1, 1.0, 25.0)]
    [Arguments(FailureKind.Timeout, 2, 1.0, 50.0)]
    [Arguments(FailureKind.Throttling, 1, 0.0, 0.0)]
    [Arguments(FailureKind.Throttling, 1, 1.0, 50.0)]
    [Arguments(FailureKind.Throttling, 1, 0.5, 25.0)]
    [Arguments(FailureKind.Throttling, 2, 1.0, 100.0)]
    [Arguments(FailureKind.Throttling, 3, 1.0, 200.0)]
    [Arguments(FailureKind.Throttling, 9, 1.0, 12_800.0)]
    [Arguments(FailureKind.Throttling, 10, 1.0, 20_000.0)]
    [Arguments(FailureKind.Throttling, 11, 1.0, 20_000.0)]
    [Arguments(FailureKind.Throttling, int.MaxValue, 1.0, 20_000.0)]
    [Arguments(FailureKind.Transient, int.MaxValue, 0.0, 0.0)]
    public async Task GetDelay_FixedRandom_IsTheShareOfTheCeiling(FailureKind kind, int attempt, double random, double expectedMilliseconds)
    {
        // Arrange (the ceiling is min(20 s, base * 2^(attempt - 1)), the base 50 ms for throttling and 25 ms otherwise)
        var policy = new RetryPolicy(3, new FixedRandom(random));

        // Act
        var delay = policy.GetDelay(kind, attempt);

        // Assert
        await Assert.That(delay).IsEqualTo(TimeSpan.FromMilliseconds(expectedMilliseconds));
    }

    [Test]
    [Arguments(1, 0.0, 0.0)]
    [Arguments(1, 0.5, 5.0)]
    [Arguments(1, 1.0, 10.0)]
    [Arguments(2, 1.0, 20.0)]
    [Arguments(4, 1.0, 80.0)]
    [Arguments(7, 1.0, 640.0)]
    [Arguments(8, 1.0, 1_000.0)]
    [Arguments(9, 1.0, 1_000.0)]
    [Arguments(int.MaxValue, 1.0, 1_000.0)]
    [Arguments(int.MaxValue, 0.0, 0.0)]
    public async Task GetConflictDelay_FixedRandom_IsTheShareOfTheCeiling(int run, double random, double expectedMilliseconds)
    {
        // Arrange (the ceiling is min(1 s, 10 ms * 2^(run - 1)))
        var policy = new RetryPolicy(3, new FixedRandom(random));

        // Act
        var delay = policy.GetConflictDelay(run);

        // Assert
        await Assert.That(delay).IsEqualTo(TimeSpan.FromMilliseconds(expectedMilliseconds));
    }

    [Test]
    public async Task GetConflictDelay_ManyRuns_TakesNothingFromTheQuota()
    {
        // Arrange
        var policy = new RetryPolicy(3, FixedRandom.One);
        var quota = policy.AvailableQuota;

        // Act
        for (var run = 1; run <= 200; run++)
        {
            policy.GetConflictDelay(run);
        }

        // Assert
        await Assert.That(policy.AvailableQuota).IsEqualTo(quota);
    }

    [Test]
    public async Task GetDelay_RealRandomSource_StaysWithinTheCeiling()
    {
        // Arrange (the ceiling of the third retry after throttling is 200 ms)
        var policy = new RetryPolicy(3, new Random(7));

        // Act
        var delays = Enumerable.Range(0, 1_000).Select(_ => policy.GetDelay(FailureKind.Throttling, 3)).ToArray();

        // Assert
        await Assert.That(delays.All(delay => delay >= TimeSpan.Zero && delay <= TimeSpan.FromMilliseconds(200))).IsTrue();
    }

    [Test]
    public async Task GetDelay_RealRandomSource_SpreadsOverTheWholeRange()
    {
        // Arrange (full jitter: the delays are not clustered at the ceiling)
        var policy = new RetryPolicy(3, new Random(7));

        // Act
        var delays = Enumerable.Range(0, 1_000).Select(_ => policy.GetDelay(FailureKind.Throttling, 3)).ToArray();

        // Assert
        await Assert.That((delays.Min() < TimeSpan.FromMilliseconds(20), delays.Max() > TimeSpan.FromMilliseconds(180))).IsEqualTo((true, true));
    }

    [Test]
    [Arguments(FailureKind.Transient, 5)]
    [Arguments(FailureKind.Throttling, 5)]
    [Arguments(FailureKind.Timeout, 10)]
    public async Task Decide_RetryableFailure_RetriesAndTakesTheCostFromTheQuota(FailureKind kind, int cost)
    {
        // Arrange
        var policy = new RetryPolicy(3, FixedRandom.Zero);

        // Act
        var decision = policy.Decide(kind, resendAtOnce: false, attempt: 1, lastRetryCost: 0);

        // Assert
        await Assert.That((decision.Verdict, decision.LastRetryCost, policy.AvailableQuota)).IsEqualTo((RetryVerdict.Retry, cost, 500 - cost));
    }

    [Test]
    public async Task Decide_RetryableFailure_CarriesTheDelayOfTheAttempt()
    {
        // Arrange
        var policy = new RetryPolicy(3, FixedRandom.One);

        // Act
        var decision = policy.Decide(FailureKind.Throttling, resendAtOnce: false, attempt: 2, lastRetryCost: 5);

        // Assert
        await Assert.That(decision.Delay).IsEqualTo(TimeSpan.FromMilliseconds(100));
    }

    [Test]
    [Arguments(FailureKind.Transient)]
    [Arguments(FailureKind.Throttling)]
    [Arguments(FailureKind.Timeout)]
    public async Task Decide_LastAttempt_GivesUpWithAttemptsExhausted(FailureKind kind)
    {
        // Arrange
        var policy = new RetryPolicy(3, FixedRandom.Zero);

        // Act
        var decision = policy.Decide(kind, resendAtOnce: false, attempt: 3, lastRetryCost: 5);

        // Assert
        await Assert.That((decision.Verdict, decision.ShouldRetry, policy.AvailableQuota)).IsEqualTo((RetryVerdict.AttemptsExhausted, false, 500));
    }

    [Test]
    public async Task Decide_OneAttemptBeforeTheLast_StillRetries()
    {
        // Arrange
        var policy = new RetryPolicy(3, FixedRandom.Zero);

        // Act
        var decision = policy.Decide(FailureKind.Transient, resendAtOnce: false, attempt: 2, lastRetryCost: 5);

        // Assert
        await Assert.That(decision.Verdict).IsEqualTo(RetryVerdict.Retry);
    }

    [Test]
    public async Task Decide_OnlyOneAttemptAllowed_SurfacesWithoutClaimingExhaustion()
    {
        // Arrange (a client with retries switched off has not used up anything, so it must not warn about exhausted retries)
        var policy = new RetryPolicy(1, FixedRandom.Zero);

        // Act
        var decision = policy.Decide(FailureKind.Throttling, resendAtOnce: false, attempt: 1, lastRetryCost: 0);

        // Assert
        await Assert.That(decision.Verdict).IsEqualTo(RetryVerdict.Surface);
    }

    [Test]
    public async Task Decide_MaxAttemptsIsIntMaxValue_Retries()
    {
        // Arrange
        var policy = new RetryPolicy(int.MaxValue, FixedRandom.Zero);

        // Act
        var decision = policy.Decide(FailureKind.Transient, resendAtOnce: false, attempt: 1_000_000, lastRetryCost: 5);

        // Assert
        await Assert.That(decision.Verdict).IsEqualTo(RetryVerdict.Retry);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(100)]
    public async Task Decide_PermanentFailure_SurfacesAtAnyAttempt(int attempt)
    {
        // Arrange
        var policy = new RetryPolicy(int.MaxValue, FixedRandom.Zero);

        // Act
        var decision = policy.Decide(FailureKind.Permanent, resendAtOnce: false, attempt, lastRetryCost: 0);

        // Assert
        await Assert.That((decision.Verdict, decision.ShouldRetry, policy.AvailableQuota)).IsEqualTo((RetryVerdict.Surface, false, 500));
    }

    [Test]
    public async Task Decide_QuotaHoldsTooLittle_GivesUpWithQuotaExhausted()
    {
        // Arrange
        var policy = new RetryPolicy(100, FixedRandom.Zero);
        for (var i = 0; i < 100; i++)
        {
            policy.Decide(FailureKind.Transient, resendAtOnce: false, attempt: 1, lastRetryCost: 0);
        }

        // Act
        var decision = policy.Decide(FailureKind.Transient, resendAtOnce: false, attempt: 1, lastRetryCost: 5);

        // Assert
        await Assert.That((decision.Verdict, policy.AvailableQuota)).IsEqualTo((RetryVerdict.QuotaExhausted, 0));
    }

    [Test]
    public async Task Decide_FiveTokensLeft_GivesUpOnATimeoutButStillRetriesATransientFailure()
    {
        // Arrange (495 tokens taken by 99 retries: 5 are enough for a retry, but a timeout costs 10)
        var policy = new RetryPolicy(10, FixedRandom.Zero);
        for (var i = 0; i < 99; i++)
        {
            policy.Decide(FailureKind.Transient, resendAtOnce: false, attempt: 1, lastRetryCost: 0);
        }

        // Act
        var timeout = policy.Decide(FailureKind.Timeout, resendAtOnce: false, attempt: 1, lastRetryCost: 0);
        var transient = policy.Decide(FailureKind.Transient, resendAtOnce: false, attempt: 1, lastRetryCost: 0);

        // Assert
        await Assert.That((timeout.Verdict, transient.Verdict)).IsEqualTo((RetryVerdict.QuotaExhausted, RetryVerdict.Retry));
    }

    [Test]
    [Arguments(FailureKind.Permanent)]
    [Arguments(FailureKind.Transient)]
    [Arguments(FailureKind.Throttling)]
    [Arguments(FailureKind.Timeout)]
    public async Task Decide_ClockCorrected_ResendsWithoutWaitingOrQuota(FailureKind kind)
    {
        // Arrange
        var policy = new RetryPolicy(3, FixedRandom.One);

        // Act
        var decision = policy.Decide(kind, resendAtOnce: true, attempt: 1, lastRetryCost: 0);

        // Assert
        await Assert.That((decision.Verdict, decision.ShouldRetry, decision.Delay, policy.AvailableQuota)).IsEqualTo((RetryVerdict.Resend, true, TimeSpan.Zero, 500));
    }

    [Test]
    public async Task Decide_ClockCorrectedAfterARetry_KeepsTheCostOfThatRetry()
    {
        // Arrange
        var policy = new RetryPolicy(3, FixedRandom.One);

        // Act
        var decision = policy.Decide(FailureKind.Permanent, resendAtOnce: true, attempt: 2, lastRetryCost: 10);

        // Assert
        await Assert.That(decision.LastRetryCost).IsEqualTo(10);
    }

    [Test]
    public async Task Decide_ClockCorrectedOnTheLastAttempt_GivesUp()
    {
        // Arrange (the resend counts as an attempt, so there is none left for it)
        var policy = new RetryPolicy(2, FixedRandom.One);

        // Act
        var decision = policy.Decide(FailureKind.Permanent, resendAtOnce: true, attempt: 2, lastRetryCost: 0);

        // Assert
        await Assert.That(decision.Verdict).IsEqualTo(RetryVerdict.AttemptsExhausted);
    }

    [Test]
    public async Task Decide_ClockCorrectedWithRetriesSwitchedOff_Surfaces()
    {
        // Arrange
        var policy = new RetryPolicy(1, FixedRandom.One);

        // Act
        var decision = policy.Decide(FailureKind.Permanent, resendAtOnce: true, attempt: 1, lastRetryCost: 0);

        // Assert
        await Assert.That(decision.Verdict).IsEqualTo(RetryVerdict.Surface);
    }

    [Test]
    public async Task OnSuccess_NoRetryAndAFullQuota_ChangesNothing()
    {
        // Arrange
        var policy = new RetryPolicy(3, FixedRandom.Zero);

        // Act
        policy.OnSuccess(0);

        // Assert
        await Assert.That(policy.AvailableQuota).IsEqualTo(500);
    }

    [Test]
    public async Task OnSuccess_NoRetry_GivesOneTokenBack()
    {
        // Arrange
        var policy = new RetryPolicy(3, FixedRandom.Zero);
        policy.Decide(FailureKind.Transient, resendAtOnce: false, attempt: 1, lastRetryCost: 0);
        policy.Decide(FailureKind.Transient, resendAtOnce: false, attempt: 1, lastRetryCost: 0);

        // Act
        policy.OnSuccess(0);

        // Assert
        await Assert.That(policy.AvailableQuota).IsEqualTo(491);
    }

    [Test]
    [Arguments(FailureKind.Transient)]
    [Arguments(FailureKind.Timeout)]
    public async Task OnSuccess_AfterTwoRetries_GivesBackOnlyWhatTheLastRetryTook(FailureKind lastFailure)
    {
        // Arrange (the first retry took 10 tokens, the second 5 or 10, so 15 or 20 are out)
        var policy = new RetryPolicy(3, FixedRandom.Zero);
        policy.Decide(FailureKind.Timeout, resendAtOnce: false, attempt: 1, lastRetryCost: 0);
        var last = policy.Decide(lastFailure, resendAtOnce: false, attempt: 2, lastRetryCost: 10);

        // Act
        policy.OnSuccess(last.LastRetryCost);

        // Assert
        await Assert.That(policy.AvailableQuota).IsEqualTo(490);
    }
}
