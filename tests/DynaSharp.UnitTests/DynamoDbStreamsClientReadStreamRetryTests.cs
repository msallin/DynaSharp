using System.Net;
using DynaSharp.Streams;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Http.RetryTestSupport;
using static DynaSharp.UnitTests.StreamScript;

namespace DynaSharp.UnitTests;

/// <summary>
/// How <see cref="DynamoDbStreamsClient.ReadStreamAsync"/> reads a shard again after a transient failure: from where it stopped, with the same iterator while it lives, for
/// fifteen minutes an outage, and not for any other failure. The clients make one attempt per call, so every failure reaches the reader.
/// </summary>
public sealed class DynamoDbStreamsClientReadStreamRetryTests
{
    [Test]
    public async Task ReadStreamAsync_GetRecordsFailsTransiently_SendsTheSameIteratorAgainAndReturnsEveryRecordOnce()
    {
        // Arrange
        var script = OneShard();
        script.Answer("records:o/0", StubResponse.Records("o/1", Insert("o-1")));
        script.Answer("records:o/1", ServerError(), StubResponse.Records(null, Insert("o-2")));
        using var streams = TestClient.CreateStreams(script.Handler, random: FixedRandom.Zero);

        // Act
        var records = await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That((string.Join(",", SequenceNumbers(records)), string.Join(",", IteratorsRead(script.Handler)))).IsEqualTo(("o-1,o-2", "o/0,o/1,o/1"));
    }

    [Test]
    [Arguments("InternalServerError", HttpStatusCode.InternalServerError)]
    [Arguments("LimitExceededException", HttpStatusCode.BadRequest)]
    [Arguments("ThrottlingException", HttpStatusCode.BadRequest)]
    [Arguments("ServiceUnavailable", HttpStatusCode.ServiceUnavailable)]
    public async Task ReadStreamAsync_TransientError_ReadsTheShardOn(string errorCode, HttpStatusCode status)
    {
        // Arrange
        var script = OneShard();
        script.Answer("records:o/0", StubResponse.DynamoDbError(errorCode, "Failure of the service.", status), StubResponse.Records(null, Insert("o-1")));
        using var streams = TestClient.CreateStreams(script.Handler, random: FixedRandom.Zero);

        // Act
        var records = await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", SequenceNumbers(records))).IsEqualTo("o-1");
    }

    [Test]
    public async Task ReadStreamAsync_NetworkFailure_ReadsTheShardOn()
    {
        // Arrange
        var script = OneShard();
        script.Answer("records:o/0", _ => Task.FromException<StubResponse>(new HttpRequestException("The connection was reset.")));
        script.Answer("records:o/0", StubResponse.Records(null, Insert("o-1")));
        using var streams = TestClient.CreateStreams(script.Handler, random: FixedRandom.Zero);

        // Act
        var records = await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", SequenceNumbers(records))).IsEqualTo("o-1");
    }

    [Test]
    [Arguments("AccessDeniedException")]
    [Arguments("ValidationException")]
    [Arguments("TrimmedDataAccessException")]
    [Arguments("ResourceNotFoundException")]
    public async Task ReadStreamAsync_ErrorThatIsNotTransient_ThrowsItWithoutReadingAgain(string errorCode)
    {
        // Arrange
        var script = OneShard();
        script.Answer("records:o/0", StubResponse.DynamoDbError(errorCode, "Failure of the request."));
        using var streams = TestClient.CreateStreams(script.Handler, random: FixedRandom.Zero);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbException>(() => streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync().AsTask());

        // Assert
        await Assert.That((failure.ErrorCode, IteratorsRead(script.Handler).Count())).IsEqualTo((errorCode, 1));
    }

    [Test]
    public async Task ReadStreamAsync_GetShardIteratorFailsTransiently_AsksForTheSamePositionAgain()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Open("o")));
        script.Answer("iterator:o", ServerError(), StubResponse.ShardIterator("o/0"));
        script.Answer("records:o/0", StubResponse.Records(null, Insert("o-1")));
        using var streams = TestClient.CreateStreams(script.Handler, random: FixedRandom.Zero);

        // Act
        await streams.ReadStreamAsync(Latest()).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", Positions(script.Handler))).IsEqualTo("o:LATEST,o:LATEST");
    }

    [Test]
    public async Task ReadStreamAsync_IteratorExpiresAfterATransientFailure_GoesOnAfterTheLastRecord()
    {
        // Arrange: the wait outlasts the iterator, which the second GetRecords shows
        var script = OneShard(StubResponse.ShardIterator("o/0"), StubResponse.ShardIterator("o/5"));
        script.Answer("records:o/0", StubResponse.Records("o/1", Insert("o-1")));
        script.Answer("records:o/1", ServerError(), Expired());
        script.Answer("records:o/5", StubResponse.Records(null, Insert("o-2")));
        using var streams = TestClient.CreateStreams(script.Handler, random: FixedRandom.Zero);

        // Act
        var records = await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That((string.Join(",", SequenceNumbers(records)), string.Join(",", Positions(script.Handler))))
            .IsEqualTo(("o-1,o-2", "o:TRIM_HORIZON,o:AFTER_SEQUENCE_NUMBER:o-1"));
    }

    [Test]
    public async Task ReadStreamAsync_IteratorExpiresAfterATransientFailureBeforeAnyRecord_StartsAtTheStartPositionAgain()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Open("o")));
        script.Answer("iterator:o", StubResponse.ShardIterator("o/0"), StubResponse.ShardIterator("o/5"));
        script.Answer("records:o/0", ServerError(), Expired());
        script.Answer("records:o/5", StubResponse.Records(null, Insert("o-1")));
        using var streams = TestClient.CreateStreams(script.Handler, random: FixedRandom.Zero);

        // Act
        await streams.ReadStreamAsync(Latest()).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", Positions(script.Handler))).IsEqualTo("o:LATEST,o:LATEST");
    }

    [Test]
    public async Task ReadStreamAsync_PositionTrimmedWhenTheIteratorIsRenewedAfterATransientFailure_ThrowsTrimmedDataAccessException()
    {
        // Arrange
        var script = OneShard(StubResponse.ShardIterator("o/0"), StubResponse.DynamoDbError("TrimmedDataAccessException", "The records are gone."));
        script.Answer("records:o/0", StubResponse.Records("o/1", Insert("o-1")));
        script.Answer("records:o/1", ServerError(), Expired());
        using var streams = TestClient.CreateStreams(script.Handler, random: FixedRandom.Zero);

        // Act
        var act = async () => await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<TrimmedDataAccessException>();
    }

    [Test]
    public async Task ReadStreamAsync_LookForChildrenFailsTransiently_LooksAgainAndReadsTheChild()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("p")));
        script.ClosedShard("p", "p-1");
        script.Answer("children:p", ServerError(), Description(StreamStatus.Disabled, null, Closed("c", "p")));
        script.ClosedShard("c", "c-1");
        using var streams = TestClient.CreateStreams(script.Handler, random: FixedRandom.Zero);

        // Act
        var records = await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", SequenceNumbers(records))).IsEqualTo("p-1,c-1");
    }

    [Test]
    public async Task ReadStreamAsync_LookForChildrenFailsTransiently_WaitsTheBackoffBeforeItLooksAgain()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("p")));
        script.ClosedShard("p", "p-1");
        script.Answer("children:p", ServerError(), Description(StreamStatus.Disabled, null));
        using var streams = TestClient.CreateStreams(script.Handler, time, attemptTimeout: Timeout.InfiniteTimeSpan, random: FixedRandom.One);
        var reading = streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync().AsTask();

        // Act
        var delays = await AdvanceThroughDelaysAsync(reading, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That(delays).IsEquivalentTo([TimeSpan.FromSeconds(1)]);
    }

    [Test]
    public async Task ReadStreamAsync_LookForChildrenAnswersAfterATransientFailure_LogsThatTheShardIsReadAgain()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("p")));
        script.ClosedShard("p", "p-1");
        script.Answer("children:p", ServerError(), Description(StreamStatus.Disabled, null));
        using var streams = TestClient.CreateStreams(script.Handler, loggerFactory: logs, random: FixedRandom.Zero);

        // Act
        await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(logs.Entries.Count(static entry => entry.EventId.Id == 13)).IsEqualTo(1);
    }

    [Test]
    public async Task ReadStreamAsync_LookForChildrenOfAShardClosedAtTheStartFailsTransiently_LooksAgainAndReadsTheChild()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("p")));
        script.Answer("children:p", ServerError(), Description(StreamStatus.Disabled, null, Closed("c", "p")));
        script.ClosedShard("c", "c-1");
        using var streams = TestClient.CreateStreams(script.Handler, random: FixedRandom.Zero);

        // Act
        var records = await streams.ReadStreamAsync(Latest()).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", SequenceNumbers(records))).IsEqualTo("c-1");
    }

    [Test]
    public async Task ReadStreamAsync_ListingFailsTransiently_ThrowsWithoutListingAgain()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", ServerError());
        using var streams = TestClient.CreateStreams(script.Handler, random: FixedRandom.Zero);

        // Act
        await TestClient.CaptureAsync<InternalServerErrorException>(() => streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync().AsTask());

        // Assert
        await Assert.That(script.Handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ReadStreamAsync_FailureThatLastsFifteenMinutes_ThrowsItOnceTheyArePast()
    {
        // Arrange: the longest waits, 1, 2, 4, 8 and 16 seconds, then 30 seconds each
        var time = new ManualTimeProvider();
        var script = OneShard();
        script.Answer("records:o/0", ServerError());
        using var streams = TestClient.CreateStreams(script.Handler, time, attemptTimeout: Timeout.InfiniteTimeSpan, random: FixedRandom.One);
        var reading = streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync().AsTask();

        // Act
        var delays = await AdvanceThroughDelaysAsync(reading, time, TestClient.DefaultAttemptTimeout);

        // Assert: the last retry starts within the fifteen minutes, and the failure after it surfaces
        var waited = TimeSpan.FromTicks(delays.Sum(static delay => delay.Ticks));
        await Assert.That((waited >= ShardRun.RetryWindow, waited - delays[^1] < ShardRun.RetryWindow, reading.Exception?.InnerException?.GetType()))
            .IsEqualTo((true, true, typeof(InternalServerErrorException)));
    }

    [Test]
    public async Task ReadStreamAsync_FailureExactlyFifteenMinutesIntoAnOutage_ThrowsIt()
    {
        // Arrange: the second attempt fails at the end of the window, and a third would get the record
        var time = new ManualTimeProvider();
        var script = OneShard();
        script.Answer("records:o/0", _ => Task.FromResult(ServerError()));
        script.Answer("records:o/0", _ =>
        {
            time.Advance(ShardRun.RetryWindow);
            return Task.FromResult(ServerError());
        });
        script.Answer("records:o/0", StubResponse.Records(null, Insert("o-1")));
        using var streams = TestClient.CreateStreams(script.Handler, time, attemptTimeout: Timeout.InfiniteTimeSpan, random: FixedRandom.Zero);

        // Act
        var act = async () => await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<InternalServerErrorException>();
    }

    [Test]
    public async Task ReadStreamAsync_SecondOutageAfterARecord_GetsFifteenMinutesOfItsOwn()
    {
        // Arrange: each outage takes 26 retries, 661 seconds, and both together more than fifteen minutes
        var time = new ManualTimeProvider();
        var script = OneShard();
        foreach (var page in new[] { "o/0", "o/1" })
        {
            for (var i = 0; i < 26; i++)
            {
                script.Answer("records:" + page, ServerError());
            }
        }

        script.Answer("records:o/0", StubResponse.Records("o/1", Insert("o-1")));
        script.Answer("records:o/1", StubResponse.Records(null, Insert("o-2")));
        using var streams = TestClient.CreateStreams(script.Handler, time, attemptTimeout: Timeout.InfiniteTimeSpan, random: FixedRandom.One);
        var reading = streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync().AsTask();

        // Act
        await AdvanceThroughDelaysAsync(reading, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That(string.Join(",", SequenceNumbers(await reading))).IsEqualTo("o-1,o-2");
    }

    [Test]
    public async Task ReadStreamAsync_CancelledWhileItWaitsToReadAgain_ThrowsOperationCanceledException()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var script = OneShard();
        script.Answer("records:o/0", ServerError());
        using var streams = TestClient.CreateStreams(script.Handler, time, attemptTimeout: Timeout.InfiniteTimeSpan, random: FixedRandom.One);
        using var cancellation = new CancellationTokenSource();
        var reading = streams.ReadStreamAsync(FromTrimHorizon(), cancellation.Token).ToListAsync().AsTask();
        while (time.ArmedTimers == 0)
        {
            await Task.Yield();
        }

        // Act
        await cancellation.CancelAsync();

        // Assert
        await Assert.That(async () => await reading).Throws<OperationCanceledException>();
    }

    private static ReadStreamRequest FromTrimHorizon() => new() { StreamArn = TestClient.StreamArn, ShardIteratorType = ShardIteratorType.TrimHorizon };

    private static ReadStreamRequest Latest() => FromTrimHorizon() with { ShardIteratorType = ShardIteratorType.Latest };

    private static StubResponse ServerError() => StubResponse.DynamoDbError("InternalServerError", "Failure of the service.", HttpStatusCode.InternalServerError);

    private static StubResponse Expired() => StubResponse.DynamoDbError("ExpiredIteratorException", "Iterator expired.");

    // An open shard o of a stream that is switched off, so the reading ends once o has been read to its end; the pages are the test's, and the iterators too if it gives them.
    private static StreamScript OneShard(params StubResponse[] iterators)
    {
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Disabled, null, Open("o")));
        script.Answer("iterator:o", iterators.Length > 0 ? iterators : [StubResponse.ShardIterator("o/0")]);
        return script;
    }

    // The iterators of the GetRecords calls, in the order sent.
    private static IEnumerable<string> IteratorsRead(StubHandler handler) => handler.Requests
        .Where(static sent => sent.Operation == "GetRecords")
        .Select(static sent => sent.BodyJson.GetProperty("ShardIterator").GetString()!);
}
