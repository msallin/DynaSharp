using System.Diagnostics;
using DynaSharp.Streams;
using DynaSharp.Telemetry;
using DynaSharp.Testing;
using Microsoft.Extensions.Logging;

namespace DynaSharp.UnitTests.Telemetry;

// The spans, metrics and logs of ReadStreamAsync, over a shard p with two records and its child c with one, of a stream that is switched off. The shards are
// read one after the other, so the answers are queued in order. The meter and the activity source are global and tests run in parallel, so every test
// filters by a trace of its own or by a host name of its own.
public sealed class ReadStreamTelemetryTests
{
    private const string Span = "DynamoDB Streams.ReadStream";

    private static readonly Item Key = new() { ["pk"] = "order-secret-3f1c" };

    [Test]
    public async Task ReadStreamAsync_Listener_StartsAnInternalSpanPerShard()
    {
        // Arrange
        using var capture = new ActivityCapture();

        // Act
        await ReadParentAndChildAsync();

        // Assert
        var spans = Shards(capture).Select(static span => span.GetTagItem("dynasharp.stream.shard_id") + ":" + span.Kind).Order(StringComparer.Ordinal);
        await Assert.That(string.Join(",", spans)).IsEqualTo("c:Internal,p:Internal");
    }

    [Test]
    public async Task ReadStreamAsync_Listener_TagsTheSpanOfAShardWithTheShardAndItsRecords()
    {
        // Arrange
        using var capture = new ActivityCapture();

        // Act
        await ReadParentAndChildAsync();

        // Assert
        await Assert.That(ActivityCapture.Tags(Shard(capture, "p"))).IsEqualTo(
            "db.system.name=aws.dynamodb,rpc.service=DynamoDB Streams,cloud.region=eu-central-2,server.address=streams.dynamodb.eu-central-2.amazonaws.com,"
            + "dynasharp.stream.shard_id=p,dynasharp.stream.record_count=2");
    }

    [Test]
    public async Task ReadStreamAsync_Listener_LinksTheSpanOfAChildToTheSpanOfItsParent()
    {
        // Arrange
        using var capture = new ActivityCapture();

        // Act
        await ReadParentAndChildAsync();

        // Assert
        await Assert.That(Shard(capture, "c").Links.Single().Context.SpanId).IsEqualTo(Shard(capture, "p").SpanId);
    }

    [Test]
    public async Task ReadStreamAsync_Listener_LinksTheSpanOfAShardWithoutAParentToNothing()
    {
        // Arrange
        using var capture = new ActivityCapture();

        // Act
        await ReadParentAndChildAsync();

        // Assert
        await Assert.That(Shard(capture, "p").Links).IsEmpty();
    }

    [Test]
    public async Task ReadStreamAsync_EndpointOnAPortOfItsOwn_TagsTheSpanOfAShardWithThePort()
    {
        // Arrange
        using var capture = new ActivityCapture();
        var host = TestClient.UniqueHost();

        // Act
        await ReadParentAndChildAsync(serviceUrl: new Uri($"http://{host}:8000"));

        // Assert
        await Assert.That(Shard(capture, "p").GetTagItem("server.port")).IsEqualTo((object?)8000);
    }

    [Test]
    public async Task ReadStreamAsync_Listener_PutsTheSpanOfEveryShardUnderTheSpanOfTheCaller()
    {
        // Arrange
        using var capture = new ActivityCapture();

        // Act
        await ReadParentAndChildAsync();

        // Assert
        await Assert.That(Shards(capture).Select(static span => span.ParentSpanId).Distinct().Single()).IsEqualTo(capture.ParentSpanId);
    }

    [Test]
    public async Task ReadStreamAsync_Listener_PutsTheCallsOfAShardUnderItsSpan()
    {
        // Arrange
        using var capture = new ActivityCapture();

        // Act
        await ReadParentAndChildAsync();

        // Assert
        var parent = Shard(capture, "p").SpanId;
        var calls = capture.All.Where(span => span.ParentSpanId == parent).Select(static span => span.OperationName).Order(StringComparer.Ordinal);
        await Assert.That(string.Join(",", calls)).IsEqualTo("DynamoDB Streams.DescribeStream,DynamoDB Streams.GetRecords,DynamoDB Streams.GetShardIterator");
    }

    [Test]
    public async Task ReadStreamAsync_ShardFails_MarksItsSpanAsFailedWithTheErrorType()
    {
        // Arrange
        using var capture = new ActivityCapture();
        using var handler = new StubHandler();
        handler.Enqueue(Description(StubResponse.Shard("gone", endingSequenceNumber: "999")));
        handler.Enqueue(StubResponse.ResourceNotFound());
        using var streams = TestClient.CreateStreams(handler);

        // Act
        await TestClient.CaptureAsync<TrimmedDataAccessException>(() => streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync().AsTask());

        // Assert
        var span = Shard(capture, "gone");
        await Assert.That((span.Status, span.GetTagItem("error.type"))).IsEqualTo((ActivityStatusCode.Error, (object?)"TrimmedDataAccessException"));
    }

    [Test]
    public async Task ReadStreamAsync_ShardStoppedByTheCaller_EndsItsSpanWithoutAFailure()
    {
        // Arrange
        using var capture = new ActivityCapture();
        using var handler = new StubHandler(static async (request, cancellationToken) => request.Operation switch
        {
            "DescribeStream" => StubResponse.StreamDescription(new StreamDescription { StreamStatus = StreamStatus.Enabled, Shards = [StubResponse.Shard("open")] }),
            "GetShardIterator" => StubResponse.ShardIterator("open/0"),
            _ when request.BodyJson.GetProperty("ShardIterator").GetString() == "open/0" => StubResponse.Records("open/1", Insert("open-1")),

            // The next page waits until the reader is stopped.
            _ => await UntilCancelledAsync(cancellationToken),
        });
        using var streams = TestClient.CreateStreams(handler);

        // Act
        await foreach (var _ in streams.ReadStreamAsync(FromTrimHorizon()))
        {
            break;
        }

        // Assert
        await Assert.That(Shard(capture, "open").Status).IsEqualTo(ActivityStatusCode.Unset);
    }

    [Test]
    public async Task ReadStreamAsync_AnotherShardFailsWhileThisOneWaitsForRoomInTheBuffer_EndsItsSpanWithoutAFailure()
    {
        // Arrange (shard "full" fills the buffer of 16 records, the consumer holds the first, and its next record waits for room; then shard "bad" fails)
        using var capture = new ActivityCapture();
        var bufferFull = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            var shardId = request.Operation == "GetShardIterator" ? request.BodyJson.GetProperty("ShardId").GetString() : null;
            var iterator = request.Operation == "GetRecords" ? request.BodyJson.GetProperty("ShardIterator").GetString() : null;
            switch (request.Operation, shardId, iterator)
            {
                case ("DescribeStream", _, _):
                    return StubResponse.StreamDescription(new StreamDescription { StreamStatus = StreamStatus.Enabled, Shards = [StubResponse.Shard("full"), StubResponse.Shard("bad")] });
                case (_, "full", _):
                    return StubResponse.ShardIterator("full/0");
                case (_, "bad", _):
                    // More round trips for "bad" than "full" has left, so the record of "full" waits for room when "bad" fails.
                    await bufferFull.Task.WaitAsync(cancellationToken);
                    return StubResponse.ShardIterator("bad/0");
                case (_, _, "full/0"):
                    return StubResponse.Records("full/1", [.. Enumerable.Range(1, 17).Select(static number => Insert($"full-{number}"))]);
                case (_, _, "full/1"):
                    bufferFull.SetResult();
                    return StubResponse.Records("full/2", Insert("full-18"));
                case (_, _, ['b', 'a', 'd', '/', var page]) when page < '5':
                    return StubResponse.Records($"bad/{(char)(page + 1)}");
                case (_, _, "bad/5"):
                    return StubResponse.DynamoDbError("AccessDeniedException", "Not allowed.");
                default:
                    return await UntilCancelledAsync(cancellationToken);
            }
        });
        using var streams = TestClient.CreateStreams(handler);
        var request = new ReadStreamRequest { StreamArn = TestClient.StreamArn, ShardIteratorType = ShardIteratorType.TrimHorizon, EmptyPageDelay = TimeSpan.Zero };
        var records = streams.ReadStreamAsync(request).GetAsyncEnumerator();
        await records.MoveNextAsync();

        // Act
        await capture.StoppedAsync(static span => Equals(span.GetTagItem("dynasharp.stream.shard_id"), "bad"));
        await TestClient.CaptureAsync<AccessDeniedException>(async () => await records.MoveNextAsync());
        await records.DisposeAsync();

        // Assert
        await Assert.That(Shard(capture, "full").Status).IsEqualTo(ActivityStatusCode.Unset);
    }

    [Test]
    public async Task ReadStreamAsync_Listener_CountsTheRecordsHandedOut()
    {
        // Arrange
        var host = TestClient.UniqueHost();
        using var counters = MeasurementCapture<long>.ForServer(host, TelemetryNames.StreamRecordsMetric);

        // Act
        await ReadParentAndChildAsync(serviceUrl: new Uri($"http://{host}:8000"));

        // Assert
        await Assert.That((counters.All.Sum(static measurement => measurement.Value), counters.All[0].TagText)).IsEqualTo((3L, $"server.address={host},server.port=8000"));
    }

    [Test]
    public async Task ReadStreamAsync_Listener_CountsEachShardUpWhileItIsReadAndDownAfter()
    {
        // Arrange
        var host = TestClient.UniqueHost();
        using var counters = MeasurementCapture<long>.ForServer(host, TelemetryNames.StreamShardsMetric);

        // Act
        await ReadParentAndChildAsync(serviceUrl: new Uri($"http://{host}:8000"));

        // Assert
        var values = counters.All.Select(static measurement => measurement.Value).ToList();
        await Assert.That((values.Count(static value => value == 1), values.Sum())).IsEqualTo((2, 0L));
    }

    [Test]
    public async Task ReadStreamAsync_Instruments_HaveTheirUnits()
    {
        // Arrange
        var host = TestClient.UniqueHost();
        using var counters = MeasurementCapture<long>.ForServer(host, TelemetryNames.StreamShardsMetric, TelemetryNames.StreamRecordsMetric);

        // Act
        await ReadParentAndChildAsync(serviceUrl: new Uri($"http://{host}:8000"));

        // Assert
        var units = counters.All.Select(static measurement => measurement.Instrument + "=" + measurement.Unit).Distinct().Order(StringComparer.Ordinal);
        await Assert.That(string.Join(",", units)).IsEqualTo("dynasharp.client.stream.records={record},dynasharp.client.stream.shards={shard}");
    }

    [Test]
    public async Task ReadStreamAsync_ShardReadToItsEnd_LogsItsChildrenAtDebugLevel()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();

        // Act
        await ReadParentAndChildAsync(loggerFactory: logs);

        // Assert
        var entries = logs.Entries.Where(static entry => entry.EventId.Id == 10).Select(static entry => $"{entry.Level} {entry.Category} {entry.Message}").Order(StringComparer.Ordinal);
        await Assert.That(string.Join("\n", entries)).IsEqualTo(
            "Debug DynaSharp.Streams.DynamoDbStreamsClient DynamoDB Streams shard c was read to its end. Child shards to read next: 0.\n"
            + "Debug DynaSharp.Streams.DynamoDbStreamsClient DynamoDB Streams shard p was read to its end. Child shards to read next: 1.");
    }

    [Test]
    public async Task ReadStreamAsync_ChildListedOnALaterLook_LogsItAtDebugLevel()
    {
        // Arrange: the child of p is listed on the second look, a second after p was read to its end
        var logs = new CapturingLoggerFactory();
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.StreamDescription(new StreamDescription { StreamStatus = StreamStatus.Enabled, Shards = [StubResponse.Shard("p", endingSequenceNumber: "999")] }));
        handler.Enqueue(StubResponse.ShardIterator("p/0"));
        handler.Enqueue(StubResponse.Records(null, Insert("p-1")));
        handler.Enqueue(StubResponse.StreamDescription(new StreamDescription { StreamStatus = StreamStatus.Enabled }));
        handler.Enqueue(Description(StubResponse.Shard("c", "p", endingSequenceNumber: "999")));
        handler.Enqueue(StubResponse.ShardIterator("c/0"));
        handler.Enqueue(StubResponse.Records(null, Insert("c-1")));
        handler.Enqueue(Description());
        using var streams = TestClient.CreateStreams(handler, time, attemptTimeout: Timeout.InfiniteTimeSpan, loggerFactory: logs);
        var reading = streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync().AsTask();

        // Act
        await Http.RetryTestSupport.AdvanceThroughDelaysAsync(reading, time, TestClient.DefaultAttemptTimeout);
        await reading;

        // Assert
        var entry = logs.Entries.Single(static entry => entry.EventId.Id == 11);
        await Assert.That($"{entry.Level} {entry.Message}").IsEqualTo("Debug DynamoDB Streams listed the child shard c of shard p only 00:00:01 after the first look for its children.");
    }

    [Test]
    public async Task ReadStreamAsync_ShardReadAgainAfterATransientFailure_TagsItsSpanWithTheRetries()
    {
        // Arrange
        using var capture = new ActivityCapture();

        // Act
        await ReadAfterTwoFailuresAsync();

        // Assert
        await Assert.That(Shard(capture, "p").GetTagItem("dynasharp.stream.retry_count")).IsEqualTo(2);
    }

    [Test]
    public async Task ReadStreamAsync_ShardReadAgainAfterATransientFailure_CountsEachRetryWithItsErrorType()
    {
        // Arrange
        var host = TestClient.UniqueHost();
        using var counters = MeasurementCapture<long>.ForServer(host, TelemetryNames.StreamRetriesMetric);

        // Act
        await ReadAfterTwoFailuresAsync(serviceUrl: new Uri($"http://{host}:8000"));

        // Assert
        await Assert.That((counters.All.Sum(static measurement => measurement.Value), counters.All[0].TagText, counters.All[0].Unit))
            .IsEqualTo((2L, $"error.type=InternalServerError,server.address={host},server.port=8000", "{retry}"));
    }

    [Test]
    public async Task ReadStreamAsync_ShardReadAgainAfterATransientFailure_LogsTheOutageOnceAndItsEnd()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();

        // Act
        await ReadAfterTwoFailuresAsync(loggerFactory: logs);

        // Assert
        var entries = logs.Entries.Where(static entry => entry.EventId.Id is 12 or 13).Select(static entry => $"{entry.Level} {entry.Message}");
        await Assert.That(string.Join("\n", entries)).IsEqualTo(
            "Warning DynamoDB Streams shard p failed with InternalServerError on the last attempt of a call. ReadStreamAsync reads it on from where it stopped, with a backoff, "
            + "for up to 00:15:00, and then passes the error on.\n"
            + "Information DynamoDB Streams shard p is read again, 00:00:00 after it failed and after 2 retries.");
    }

    [Test]
    public async Task ReadShardAsync_GapOfAShardReadFromLatest_CountsItUnderTheServer()
    {
        // Arrange
        var host = TestClient.UniqueHost();
        using var counters = MeasurementCapture<long>.ForServer(host, TelemetryNames.StreamGapsMetric);

        // Act
        await ReadLatestThatExpiresAsync(serviceUrl: new Uri($"http://{host}:8000"));

        // Assert
        await Assert.That((counters.All.Single().Value, counters.All[0].TagText, counters.All[0].Unit)).IsEqualTo((1L, $"server.address={host},server.port=8000", "{gap}"));
    }

    [Test]
    public async Task ReadShardAsync_GapOfAShardReadFromLatest_LogsAWarningNamingTheShard()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();

        // Act
        await ReadLatestThatExpiresAsync(loggerFactory: logs);

        // Assert
        var entry = logs.Entries.Single(static entry => entry.EventId.Id == 15);
        await Assert.That((entry.EventId.Name, entry.Level, entry.Message)).IsEqualTo((
            "StreamGap",
            LogLevel.Warning,
            "DynamoDB Streams shard p was read from Latest, and its iterator expired before it handed out a record, so it is read from Latest again: what was written in between is skipped."));
    }

    [Test]
    public async Task ReadStreamAsync_Listener_ReportsNoKeyAndNoStream()
    {
        // Arrange
        using var capture = new ActivityCapture();
        var logs = new CapturingLoggerFactory();

        // Act
        await ReadParentAndChildAsync(loggerFactory: logs);

        // Assert
        var reported = string.Join("\n", Shards(capture).Select(ActivityCapture.Tags).Concat(logs.Entries.Select(static entry => entry.Message)));
        await Assert.That(reported.Contains("secret", StringComparison.Ordinal) || reported.Contains(TestClient.StreamArn, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ReadStreamAsync_NobodyListensAndNoSpanIsCurrent_ReadsEveryShard()
    {
        // Arrange: the test host listens to every source it can see, so only a disposed source starts no spans
        var source = new ActivitySource("DynaSharp.Test." + Guid.NewGuid().ToString("N"));
        source.Dispose();
        Activity.Current = null;

        // Act
        var records = await ReadParentAndChildAsync(activitySource: source);

        // Assert
        await Assert.That(string.Join(",", records.Select(static change => change.Record.DynamoDb!.SequenceNumber))).IsEqualTo("p-1,p-2,c-1");
    }

    private static async Task<List<ShardChangeRecord>> ReadParentAndChildAsync(Uri? serviceUrl = null, ILoggerFactory? loggerFactory = null, ActivitySource? activitySource = null)
    {
        using var handler = new StubHandler();
        handler.Enqueue(Description(StubResponse.Shard("p", endingSequenceNumber: "999"), StubResponse.Shard("c", "p", endingSequenceNumber: "999")));
        handler.Enqueue(StubResponse.ShardIterator("p/0"));
        handler.Enqueue(StubResponse.Records(null, Insert("p-1"), Insert("p-2")));
        handler.Enqueue(Description(StubResponse.Shard("c", "p", endingSequenceNumber: "999")));
        handler.Enqueue(StubResponse.ShardIterator("c/0"));
        handler.Enqueue(StubResponse.Records(null, Insert("c-1")));
        handler.Enqueue(Description());
        using var streams = TestClient.CreateStreams(handler, serviceUrl: serviceUrl, loggerFactory: loggerFactory, activitySource: activitySource);
        var records = await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();
        handler.ThrowIfResponsesLeft();
        return records;
    }

    // A closed shard p whose first GetRecords fails twice, on a clock that stands still, with no jitter, so the retries wait nothing.
    private static async Task ReadAfterTwoFailuresAsync(Uri? serviceUrl = null, ILoggerFactory? loggerFactory = null)
    {
        using var handler = new StubHandler();
        handler.Enqueue(Description(StubResponse.Shard("p", endingSequenceNumber: "999")));
        handler.Enqueue(StubResponse.ShardIterator("p/0"));
        handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Failure of the service.", System.Net.HttpStatusCode.InternalServerError));
        handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Failure of the service.", System.Net.HttpStatusCode.InternalServerError));
        handler.Enqueue(StubResponse.Records(null, Insert("p-1")));
        handler.Enqueue(Description());
        using var streams = TestClient.CreateStreams(handler, new ManualTimeProvider(), serviceUrl: serviceUrl, loggerFactory: loggerFactory, random: FixedRandom.Zero);
        await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();
        handler.ThrowIfResponsesLeft();
    }

    private static async Task ReadLatestThatExpiresAsync(Uri? serviceUrl = null, ILoggerFactory? loggerFactory = null)
    {
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.ShardIterator("p/0"));
        handler.Enqueue(StubResponse.Records("p/1"));
        handler.Enqueue(StubResponse.DynamoDbError("ExpiredIteratorException", "Iterator expired."));
        handler.Enqueue(StubResponse.ShardIterator("p/2"));
        handler.Enqueue(StubResponse.Records(null));
        using var streams = TestClient.CreateStreams(handler, new ManualTimeProvider(), serviceUrl: serviceUrl, loggerFactory: loggerFactory);
        var request = new ReadShardRequest { StreamArn = TestClient.StreamArn, ShardId = "p", ShardIteratorType = ShardIteratorType.Latest, EmptyPageDelay = TimeSpan.Zero };
        await streams.ReadShardAsync(request).ToListAsync();
        handler.ThrowIfResponsesLeft();
    }

    private static async Task<StubResponse> UntilCancelledAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("Not reached.");
    }

    private static ReadStreamRequest FromTrimHorizon() => new() { StreamArn = TestClient.StreamArn, ShardIteratorType = ShardIteratorType.TrimHorizon };

    private static StubResponse Description(params Shard[] shards) =>
        StubResponse.StreamDescription(new StreamDescription { StreamArn = TestClient.StreamArn, StreamStatus = StreamStatus.Disabled, Shards = shards });

    private static ChangeRecord Insert(string sequenceNumber) => StubResponse.Change(OperationType.Insert, Key, newImage: Key, sequenceNumber: sequenceNumber);

    private static List<Activity> Shards(ActivityCapture capture) => [.. capture.All.Where(static span => span.OperationName == Span)];

    private static Activity Shard(ActivityCapture capture, string shardId) =>
        Shards(capture).Single(span => Equals(span.GetTagItem("dynasharp.stream.shard_id"), shardId));
}
