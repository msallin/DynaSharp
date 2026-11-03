using System.Text.Json.Nodes;
using DynaSharp.Streams;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests;

/// <summary>
/// <see cref="DynamoDbStreamsClient.ReadShardAsync"/> over a <see cref="StubHandler"/>: the start position, the paging, the wait after an empty
/// page, the renewal of an expired iterator, the end of a closed shard, cancellation and the errors it leaves to the caller.
/// </summary>
public sealed class DynamoDbStreamsClientReadShardTests
{
    private const string ShardId = "shardId-1";

    private static readonly Item Key = new() { ["pk"] = "order-1" };

    [Test]
    public async Task ReadShardAsync_ClosedShard_ReturnsEveryRecordInOrderAndEnds()
    {
        // Arrange
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.ShardIterator("it-1"));
        stub.Enqueue(StubResponse.Records("it-2", Insert("1"), Insert("2")));
        stub.Enqueue(StubResponse.Records(null, Insert("3")));

        // Act
        var records = await streams.ReadShardAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(records.Select(static record => record.DynamoDb!.SequenceNumber!)).IsEquivalentTo(["1", "2", "3"]);
    }

    [Test]
    public async Task ReadShardAsync_ClosedShard_FollowsTheIteratorOfEachPage()
    {
        // Arrange
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.ShardIterator("it-1"));
        stub.Enqueue(StubResponse.Records("it-2", Insert("1")));
        stub.Enqueue(StubResponse.Records(null));

        // Act
        await streams.ReadShardAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(stub.Requests.Skip(1).Select(static sent => Body(sent)["ShardIterator"]!.GetValue<string>())).IsEquivalentTo(["it-1", "it-2"]);
    }

    [Test]
    public async Task ReadShardAsync_AfterSequenceNumber_AsksForAnIteratorAtThatPosition()
    {
        // Arrange
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.ShardIterator("it-1"));
        stub.Enqueue(StubResponse.Records(null));
        var request = FromTrimHorizon() with { ShardIteratorType = ShardIteratorType.AfterSequenceNumber, SequenceNumber = "42" };

        // Act
        await streams.ReadShardAsync(request).ToListAsync();

        // Assert
        await Assert.That(Position(stub.Requests[0])).IsEqualTo((TestClient.StreamArn, ShardId, "AFTER_SEQUENCE_NUMBER", "42"));
    }

    [Test]
    public async Task ReadShardAsync_Limit_IsSentWithEveryGetRecords()
    {
        // Arrange
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.ShardIterator("it-1"));
        stub.Enqueue(StubResponse.Records("it-2", Insert("1")));
        stub.Enqueue(StubResponse.Records(null));

        // Act
        await streams.ReadShardAsync(FromTrimHorizon() with { Limit = 25 }).ToListAsync();

        // Assert
        await Assert.That(stub.Requests.Skip(1).Select(static sent => Body(sent)["Limit"]!.GetValue<int>())).IsEquivalentTo([25, 25]);
    }

    [Test]
    public async Task ReadShardAsync_EmptyPageOfAnOpenShard_WaitsTheDelayBeforeTheNextCall()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub, time);
        stub.Enqueue(StubResponse.ShardIterator("it-1"));
        stub.Enqueue(StubResponse.Records("it-2"));
        stub.Enqueue(StubResponse.Records(null, Insert("1")));
        var reading = streams.ReadShardAsync(FromTrimHorizon() with { EmptyPageDelay = TimeSpan.FromSeconds(3) }).ToListAsync().AsTask();

        // Act
        var delays = await AdvanceThroughDelaysAsync(reading, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That(delays).IsEquivalentTo([TimeSpan.FromSeconds(3)]);
    }

    [Test]
    public async Task ReadShardAsync_PagesWithRecordsAndTheLastEmptyPage_NeverWait()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub, time);
        stub.Enqueue(StubResponse.ShardIterator("it-1"));
        stub.Enqueue(StubResponse.Records("it-2", Insert("1")));
        stub.Enqueue(StubResponse.Records(null));
        var reading = streams.ReadShardAsync(FromTrimHorizon()).ToListAsync().AsTask();

        // Act
        var delays = await AdvanceThroughDelaysAsync(reading, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That(delays).IsEmpty();
    }

    [Test]
    public async Task ReadShardAsync_IteratorExpiresMidShard_RenewsAfterTheLastRecordReturned()
    {
        // Arrange
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.ShardIterator("it-1"));
        stub.Enqueue(StubResponse.Records("it-2", Insert("6"), Insert("7")));
        stub.Enqueue(Expired());
        stub.Enqueue(StubResponse.ShardIterator("it-3"));
        stub.Enqueue(StubResponse.Records(null));

        // Act
        await streams.ReadShardAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(Position(stub.Requests[3])).IsEqualTo((TestClient.StreamArn, ShardId, "AFTER_SEQUENCE_NUMBER", "7"));
    }

    [Test]
    public async Task ReadShardAsync_IteratorExpiresMidShard_ReturnsEveryRecordOnce()
    {
        // Arrange
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.ShardIterator("it-1"));
        stub.Enqueue(StubResponse.Records("it-2", Insert("6")));
        stub.Enqueue(Expired());
        stub.Enqueue(StubResponse.ShardIterator("it-3"));
        stub.Enqueue(StubResponse.Records(null, Insert("7")));

        // Act
        var records = await streams.ReadShardAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(records.Select(static record => record.DynamoDb!.SequenceNumber!)).IsEquivalentTo(["6", "7"]);
    }

    [Test]
    public async Task ReadShardAsync_IteratorExpiresBeforeAnyRecord_RenewsAtThePositionOfTheRequest()
    {
        // Arrange
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.ShardIterator("it-1"));
        stub.Enqueue(StubResponse.Records("it-2"));
        stub.Enqueue(Expired());
        stub.Enqueue(StubResponse.ShardIterator("it-3"));
        stub.Enqueue(StubResponse.Records(null));
        var request = FromTrimHorizon() with { ShardIteratorType = ShardIteratorType.AtSequenceNumber, SequenceNumber = "5", EmptyPageDelay = TimeSpan.Zero };

        // Act
        await streams.ReadShardAsync(request).ToListAsync();

        // Assert
        await Assert.That(Position(stub.Requests[3])).IsEqualTo((TestClient.StreamArn, ShardId, "AT_SEQUENCE_NUMBER", "5"));
    }

    [Test]
    public async Task ReadShardAsync_RecordsWithoutSequenceNumber_DoNotMoveThePosition()
    {
        // Arrange: the second record has no dynamodb member, the third one no SequenceNumber
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.ShardIterator("it-1"));
        stub.Enqueue(StubResponse.Json("""
            {"NextShardIterator":"it-2","Records":[
             {"eventID":"1","eventName":"INSERT","dynamodb":{"SequenceNumber":"7"}},
             {"eventID":"2","eventName":"MODIFY"},
             {"eventID":"3","eventName":"MODIFY","dynamodb":{}}]}
            """));
        stub.Enqueue(Expired());
        stub.Enqueue(StubResponse.ShardIterator("it-3"));
        stub.Enqueue(StubResponse.Records(null));

        // Act
        await streams.ReadShardAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(Position(stub.Requests[3]).SequenceNumber).IsEqualTo("7");
    }

    [Test]
    public async Task ReadShardAsync_NewIteratorExpired_ThrowsExpiredIteratorException()
    {
        // Arrange
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.ShardIterator("it-1"));
        stub.Enqueue(Expired());

        // Act
        var act = async () => await streams.ReadShardAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<ExpiredIteratorException>();
    }

    [Test]
    public async Task ReadShardAsync_TrimmedWhileReading_ThrowsTrimmedDataAccessException()
    {
        // Arrange
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.ShardIterator("it-1"));
        stub.Enqueue(StubResponse.Records("it-2", Insert("1")));
        stub.Enqueue(StubResponse.DynamoDbError("TrimmedDataAccessException", "The operation attempted to read past the oldest stream record in a shard."));

        // Act
        var act = async () => await streams.ReadShardAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<TrimmedDataAccessException>();
    }

    [Test]
    public async Task ReadShardAsync_StartIsTrimmed_ThrowsTrimmedDataAccessException()
    {
        // Arrange
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.DynamoDbError("TrimmedDataAccessException", "The operation attempted to read past the oldest stream record in a shard."));
        var request = FromTrimHorizon() with { ShardIteratorType = ShardIteratorType.AfterSequenceNumber, SequenceNumber = "1" };

        // Act
        var act = async () => await streams.ReadShardAsync(request).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<TrimmedDataAccessException>();
    }

    [Test]
    public async Task ReadShardAsync_NoIterator_ReturnsNoRecordsAfterOneCall()
    {
        // Arrange
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.Json("{}"));

        // Act
        var records = await streams.ReadShardAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That((records.Count, stub.Requests.Count)).IsEqualTo((0, 1));
    }

    [Test]
    public async Task ReadShardAsync_CallerStopsAfterTheFirstRecord_SendsNoFurtherRequest()
    {
        // Arrange
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.ShardIterator("it-1"));
        stub.Enqueue(StubResponse.Records("it-2", Insert("1"), Insert("2")));

        // Act
        await foreach (var _ in streams.ReadShardAsync(FromTrimHorizon()))
        {
            break;
        }

        // Assert
        await Assert.That(stub.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ReadShardAsync_RenewalWithoutIterator_EndsAfterTheRecordsReturned()
    {
        // Arrange
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.ShardIterator("it-1"));
        stub.Enqueue(StubResponse.Records("it-2", Insert("1")));
        stub.Enqueue(Expired());
        stub.Enqueue(StubResponse.Json("{}"));

        // Act
        var records = await streams.ReadShardAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That((records.Count, stub.Requests.Count)).IsEqualTo((1, 4));
    }

    [Test]
    public async Task ReadShardAsync_NotEnumerated_SendsNothing()
    {
        // Arrange
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub);

        // Act
        _ = streams.ReadShardAsync(FromTrimHorizon());

        // Assert
        await Assert.That(stub.Requests.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ReadShardAsync_CancelledWhileWaiting_ThrowsOperationCanceledException()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub, time);
        using var cancellation = new CancellationTokenSource();
        stub.Enqueue(StubResponse.ShardIterator("it-1"));
        stub.Enqueue(StubResponse.Records("it-2"));
        var delay = TimeSpan.FromMinutes(1);
        var reading = streams.ReadShardAsync(FromTrimHorizon() with { EmptyPageDelay = delay }, cancellation.Token).ToListAsync().AsTask();
        while (time.NextDueIn != delay && !reading.IsCompleted)
        {
            await Task.Yield();
        }

        // Act
        await cancellation.CancelAsync();

        // Assert
        await Assert.That(async () => await reading).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task ReadShardAsync_TokenOfWithCancellation_IsHonoured()
    {
        // Arrange
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var act = async () => await streams.ReadShardAsync(FromTrimHorizon()).WithCancellation(cancellation.Token).GetAsyncEnumerator().MoveNextAsync();

        // Assert
        await Assert.That(act).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task ReadShardAsync_TokensOfTheCallAndOfWithCancellation_ReadsToTheEnd()
    {
        // Arrange
        using var stub = new StubHandler();
        using var streams = TestClient.CreateStreams(stub);
        using var call = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        stub.Enqueue(StubResponse.ShardIterator("it-1"));
        stub.Enqueue(StubResponse.Records(null, Insert("1")));

        // Act
        var count = 0;
        await foreach (var _ in streams.ReadShardAsync(FromTrimHorizon(), call.Token).WithCancellation(enumeration.Token))
        {
            count++;
        }

        // Assert
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task ReadShardAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var streams = TestClient.CreateStreams(new StubHandler());

        // Act
        var act = () => streams.ReadShardAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    [Arguments(ShardIteratorType.Unknown)]
    [Arguments((ShardIteratorType)99)]
    public async Task ReadShardAsync_UndefinedPosition_ThrowsArgumentOutOfRangeException(ShardIteratorType type)
    {
        // Arrange
        using var streams = TestClient.CreateStreams(new StubHandler());

        // Act
        var act = () => streams.ReadShardAsync(FromTrimHorizon() with { ShardIteratorType = type });

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(ShardIteratorType.AtSequenceNumber, null)]
    [Arguments(ShardIteratorType.AfterSequenceNumber, null)]
    [Arguments(ShardIteratorType.TrimHorizon, "1")]
    [Arguments(ShardIteratorType.Latest, "1")]
    public async Task ReadShardAsync_SequenceNumberNotMatchingThePosition_ThrowsArgumentException(ShardIteratorType type, string? sequenceNumber)
    {
        // Arrange
        using var streams = TestClient.CreateStreams(new StubHandler());

        // Act
        var act = () => streams.ReadShardAsync(FromTrimHorizon() with { ShardIteratorType = type, SequenceNumber = sequenceNumber });

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>();
    }

    [Test]
    [Arguments(-1L)]
    [Arguments(15L * 60 * 1000)]
    [Arguments(long.MaxValue / TimeSpan.TicksPerMillisecond)]
    public async Task ReadShardAsync_DelayOutOfRange_ThrowsArgumentOutOfRangeException(long milliseconds)
    {
        // Arrange
        using var streams = TestClient.CreateStreams(new StubHandler());

        // Act
        var act = () => streams.ReadShardAsync(FromTrimHorizon() with { EmptyPageDelay = TimeSpan.FromMilliseconds(milliseconds) });

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(0L)]
    [Arguments((15L * 60 * 1000) - 1)]
    public async Task ReadShardAsync_DelayInRange_IsAccepted(long milliseconds)
    {
        // Arrange
        using var streams = TestClient.CreateStreams(new StubHandler());

        // Act
        var records = streams.ReadShardAsync(FromTrimHorizon() with { EmptyPageDelay = TimeSpan.FromMilliseconds(milliseconds) });

        // Assert
        await Assert.That(records).IsNotNull();
    }

    private static ReadShardRequest FromTrimHorizon() => new() { StreamArn = TestClient.StreamArn, ShardId = ShardId, ShardIteratorType = ShardIteratorType.TrimHorizon };

    private static ChangeRecord Insert(string sequenceNumber) => StubResponse.Change(OperationType.Insert, Key, newImage: Key, sequenceNumber: sequenceNumber);

    private static StubResponse Expired() => StubResponse.DynamoDbError("ExpiredIteratorException", "Iterator expired. The iterator was created at time Mon Nov 02 12:00:00 UTC 2026.");

    private static JsonNode Body(RecordedRequest sent) => JsonNode.Parse(sent.BodyText)!;

    private static (string? StreamArn, string? ShardId, string? Type, string? SequenceNumber) Position(RecordedRequest sent)
    {
        var body = Body(sent);
        return (body["StreamArn"]?.GetValue<string>(), body["ShardId"]?.GetValue<string>(), body["ShardIteratorType"]?.GetValue<string>(), body["SequenceNumber"]?.GetValue<string>());
    }
}
