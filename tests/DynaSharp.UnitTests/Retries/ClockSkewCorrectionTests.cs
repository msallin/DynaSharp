using DynaSharp.Retries;

namespace DynaSharp.UnitTests.Retries;

public sealed class ClockSkewCorrectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 34, 56, TimeSpan.Zero);

    [Test]
    [Arguments("RequestTimeTooSkewed")]
    [Arguments("RequestExpired")]
    [Arguments("RequestInTheFuture")]
    [Arguments("InvalidSignatureException")]
    [Arguments("SignatureDoesNotMatch")]
    [Arguments("AuthFailure")]
    public async Task IsClockSkewCode_CodeThatTheAwsSdksTreatAsSkew_ReturnsTrue(string code)
    {
        // Arrange (the case)

        // Act
        var result = ClockSkewCorrection.IsClockSkewCode(code);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    [Arguments("ThrottlingException")]
    [Arguments("ValidationException")]
    [Arguments("UnrecognizedClientException")]
    [Arguments("AccessDeniedException")]
    [Arguments("ExpiredTokenException")]
    [Arguments("InvalidSignature")]
    [Arguments("requestexpired")]
    [Arguments("UnrecognizedResponse")]
    [Arguments("")]
    public async Task IsClockSkewCode_AnyOtherCode_ReturnsFalse(string code)
    {
        // Arrange (the case)

        // Act
        var result = ClockSkewCorrection.IsClockSkewCode(code);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task GetUtcNow_NothingLearned_IsTheLocalClock()
    {
        // Arrange
        var clock = new ClockSkewCorrection(new ManualTimeProvider(Now));

        // Act
        var signingTime = clock.GetUtcNow();

        // Assert
        await Assert.That(signingTime).IsEqualTo(Now);
    }

    [Test]
    public async Task GetTimestamp_AfterCorrection_IsTheTimestampOfTheLocalClock()
    {
        // Arrange (a correction moves the wall clock, and must not show up as elapsed time)
        var time = new ManualTimeProvider(Now);
        time.Advance(TimeSpan.FromSeconds(3));
        var clock = new ClockSkewCorrection(time);
        clock.TryCorrect(Now, Now.AddMinutes(15), out _);

        // Act
        var timestamp = clock.GetTimestamp();

        // Assert
        await Assert.That((timestamp, clock.TimestampFrequency)).IsEqualTo((time.GetTimestamp(), time.TimestampFrequency));
    }

    [Test]
    public async Task CreateTimer_AfterCorrection_FiresOnTheLocalClock()
    {
        // Arrange (a correction of 15 minutes must not bring a timer of one minute forward)
        var time = new ManualTimeProvider(Now);
        var clock = new ClockSkewCorrection(time);
        clock.TryCorrect(Now, Now.AddMinutes(15), out _);
        var fired = 0;

        // Act
        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);
        time.Advance(TimeSpan.FromSeconds(59));
        var beforeDue = fired;
        time.Advance(TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That((beforeDue, fired)).IsEqualTo((0, 1));
    }

    [Test]
    [Arguments(5)]
    [Arguments(-5)]
    [Arguments(15)]
    [Arguments(-90)]
    public async Task TryCorrect_ServerFurtherAwayThanTheTolerance_LearnsTheOffset(int minutes)
    {
        // Arrange
        var time = new ManualTimeProvider(Now);
        var clock = new ClockSkewCorrection(time);
        var serverTime = Now.AddMinutes(minutes);

        // Act
        var corrected = clock.TryCorrect(Now, serverTime, out var offset);

        // Assert
        await Assert.That((corrected, offset, clock.GetUtcNow())).IsEqualTo((true, TimeSpan.FromMinutes(minutes), serverTime));
    }

    [Test]
    [Arguments(0)]
    [Arguments(3)]
    [Arguments(-3)]
    [Arguments(4)]
    [Arguments(-4)]
    public async Task TryCorrect_ServerWithinTheTolerance_LearnsNothing(int minutes)
    {
        // Arrange (exactly four minutes still counts as in sync)
        var clock = new ClockSkewCorrection(new ManualTimeProvider(Now));

        // Act
        var corrected = clock.TryCorrect(Now, Now.AddMinutes(minutes), out var offset);

        // Assert
        await Assert.That((corrected, offset, clock.GetUtcNow())).IsEqualTo((false, TimeSpan.Zero, Now));
    }

    [Test]
    [Arguments(1)]
    [Arguments(-1)]
    public async Task TryCorrect_ServerOneTickBeyondTheTolerance_LearnsTheOffset(int direction)
    {
        // Arrange
        var clock = new ClockSkewCorrection(new ManualTimeProvider(Now));
        var serverTime = Now.AddMinutes(4 * direction).AddTicks(direction);

        // Act
        var corrected = clock.TryCorrect(Now, serverTime, out _);

        // Assert
        await Assert.That(corrected).IsTrue();
    }

    [Test]
    public async Task GetUtcNow_AfterCorrection_FollowsTheLocalClock()
    {
        // Arrange
        var time = new ManualTimeProvider(Now);
        var clock = new ClockSkewCorrection(time);
        clock.TryCorrect(Now, Now.AddMinutes(15), out _);

        // Act
        time.Advance(TimeSpan.FromSeconds(30));

        // Assert
        await Assert.That(clock.GetUtcNow()).IsEqualTo(Now.AddMinutes(15).AddSeconds(30));
    }

    [Test]
    public async Task TryCorrect_SecondResponse_ReplacesTheOffsetInsteadOfAddingToIt()
    {
        // Arrange (the server is 15 minutes ahead; the local clock then drifts to 20 minutes behind it)
        var time = new ManualTimeProvider(Now);
        var clock = new ClockSkewCorrection(time);
        clock.TryCorrect(Now, Now.AddMinutes(15), out _);
        var signedAt = clock.GetUtcNow();

        // Act
        clock.TryCorrect(signedAt, Now.AddMinutes(20), out var offset);

        // Assert
        await Assert.That((offset, clock.GetUtcNow())).IsEqualTo((TimeSpan.FromMinutes(20), Now.AddMinutes(20)));
    }

    [Test]
    public async Task TryCorrect_ClockFixedAfterACorrection_LearnsThatThereIsNoOffsetAnyMore()
    {
        // Arrange (a +15 minute correction was learned; since then the local clock was set right, so the requests are 15 minutes ahead)
        var time = new ManualTimeProvider(Now);
        var clock = new ClockSkewCorrection(time);
        clock.TryCorrect(Now, Now.AddMinutes(15), out _);
        var signedAt = clock.GetUtcNow();

        // Act
        var corrected = clock.TryCorrect(signedAt, Now, out var offset);

        // Assert
        await Assert.That((corrected, offset, clock.GetUtcNow())).IsEqualTo((true, TimeSpan.Zero, Now));
    }

    [Test]
    public async Task GetUtcNow_CorrectionPastTheLastRepresentableTime_StopsAtTheEnd()
    {
        // Arrange (a Date header in the last second of year 9999, and a clock that moves on)
        var time = new ManualTimeProvider(Now);
        var clock = new ClockSkewCorrection(time);
        clock.TryCorrect(Now, DateTimeOffset.MaxValue.AddSeconds(-1), out _);
        time.Advance(TimeSpan.FromDays(1));

        // Act
        var signingTime = clock.GetUtcNow();

        // Assert
        await Assert.That(signingTime).IsEqualTo(new DateTimeOffset(DateTime.MaxValue.Ticks, TimeSpan.Zero));
    }

    [Test]
    public async Task GetUtcNow_CorrectionBeforeTheFirstRepresentableTime_StopsAtTheStart()
    {
        // Arrange (a Date header in the first second of year 1, and a clock that is set back)
        var time = new ManualTimeProvider(Now);
        var clock = new ClockSkewCorrection(time);
        clock.TryCorrect(Now, DateTimeOffset.MinValue.AddSeconds(1), out _);
        time.Advance(TimeSpan.FromDays(-1));

        // Act
        var signingTime = clock.GetUtcNow();

        // Assert
        await Assert.That(signingTime).IsEqualTo(new DateTimeOffset(DateTime.MinValue.Ticks, TimeSpan.Zero));
    }
}
