using DynaSharp.Streams;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Http.RetryTestSupport;
using static DynaSharp.UnitTests.StreamScript;

namespace DynaSharp.UnitTests;

/// <summary>
/// <see cref="DynamoDbStreamsClient.ReadStreamAsync"/> over a <see cref="StubHandler"/> whose responder answers each shard by its own requests, so that shards
/// read at the same time get the same answers in any order: the lineage, the start positions, the listing, the failures, cancellation and the arguments.
/// </summary>
public sealed class DynamoDbStreamsClientReadStreamTests
{
    [Test]
    public async Task ReadStreamAsync_SplitShard_ReadsTheParentToItsEndBeforeItsChildren()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Disabled, null, Closed("p"), Closed("c1", "p"), Closed("c2", "p")));
        script.ClosedShard("p", "p-1", "p-2");
        script.Answer("children:p", Description(StreamStatus.Disabled, null, Closed("c1", "p"), Closed("c2", "p")));
        script.ClosedShard("c1", "c1-1");
        script.ClosedShard("c2", "c2-1");
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        var records = await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        var sequence = SequenceNumbers(records);
        await Assert.That((string.Join(",", sequence.Take(2)), string.Join(",", sequence.Skip(2).Order(StringComparer.Ordinal)))).IsEqualTo(("p-1,p-2", "c1-1,c2-1"));
    }

    [Test]
    public async Task ReadStreamAsync_ThreeGenerations_ReadsEachAfterTheOneBefore()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Disabled, null, Closed("g", "c"), Closed("c", "p"), Closed("p")));
        script.ClosedShard("p", "p-1");
        script.Answer("children:p", Description(StreamStatus.Disabled, null, Closed("c", "p")));
        script.ClosedShard("c", "c-1");
        script.Answer("children:c", Description(StreamStatus.Disabled, null, Closed("g", "c")));
        script.ClosedShard("g", "g-1");
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        var records = await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", SequenceNumbers(records))).IsEqualTo("p-1,c-1,g-1");
    }

    [Test]
    public async Task ReadStreamAsync_SeveralShards_NamesTheShardOfEachRecord()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Disabled, null, Closed("a"), Closed("b")));
        script.ClosedShard("a", "a-1", "a-2");
        script.ClosedShard("b", "b-1");
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        var records = await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        var pairs = records.Select(static change => change.ShardId + ":" + change.Record.DynamoDb!.SequenceNumber).Order(StringComparer.Ordinal);
        await Assert.That(string.Join(",", pairs)).IsEqualTo("a:a-1,a:a-2,b:b-1");
    }

    [Test]
    public async Task ReadStreamAsync_TrimHorizon_StartsARootAtTheTrimHorizonAndAChildAtItsFirstSequenceNumber()
    {
        // Arrange: the open child closes while it is read
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("old"), Open("new", "old")));
        script.ClosedShard("old", "old-1");
        script.Answer("children:old", Description(StreamStatus.Disabled, null, Open("new", "old")));
        script.ClosedShard("new", "new-1");
        script.Answer("children:new", Description(StreamStatus.Disabled, null));
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", Positions(script.Handler))).IsEqualTo($"old:TRIM_HORIZON,new:AT_SEQUENCE_NUMBER:{ShardStart}");
    }

    [Test]
    public async Task ReadStreamAsync_Latest_ReadsOnlyTheShardsOpenAtTheStartFromLatest()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("old"), Open("new", "old"), Closed("other")));
        script.ClosedShard("new", "new-1");
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        await streams.ReadStreamAsync(FromTrimHorizon() with { ShardIteratorType = ShardIteratorType.Latest }).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", Positions(script.Handler))).IsEqualTo("new:LATEST");
    }

    [Test]
    public async Task ReadStreamAsync_LatestAndAChildOfAClosedShardListedLate_ReadsTheLateChild()
    {
        // Arrange: a split in progress at the start, whose second child DynamoDB lists only on the second look
        var time = new ManualTimeProvider();
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("p"), Open("c1", "p")));
        script.Answer("children:p", Description(StreamStatus.Enabled, null, Open("c1", "p")), Description(StreamStatus.Enabled, null, Open("c1", "p"), Closed("c2", "p")));
        script.ClosedShard("c1", "c1-1");
        script.ClosedShard("c2", "c2-1");
        using var streams = TestClient.CreateStreams(script.Handler, time, attemptTimeout: Timeout.InfiniteTimeSpan);
        var reading = streams.ReadStreamAsync(FromTrimHorizon() with { ShardIteratorType = ShardIteratorType.Latest }).ToListAsync().AsTask();

        // Act
        await AdvanceThroughDelaysAsync(reading, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That(string.Join(",", Positions(script.Handler).Order(StringComparer.Ordinal))).IsEqualTo($"c1:LATEST,c2:AT_SEQUENCE_NUMBER:{ShardStart}");
    }

    [Test]
    public async Task ReadStreamAsync_LatestAndTheLookForChildrenOfAClosedShardFails_ThrowsTheFailure()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("p")));
        script.Answer("children:p", StubResponse.DynamoDbError("ValidationException", "Failure of the look."));
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        var act = async () => await streams.ReadStreamAsync(FromTrimHorizon() with { ShardIteratorType = ShardIteratorType.Latest }).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<ValidationException>();
    }

    [Test]
    public async Task ReadStreamAsync_LatestAndAClosedShardWithoutAChildYet_ReadsTheChildOnceItIsListed()
    {
        // Arrange: the shard closed just before the start, and DynamoDB lists its child a second later
        var time = new ManualTimeProvider();
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("p")));
        script.Answer("children:p", Description(StreamStatus.Enabled, null), Description(StreamStatus.Disabled, null, Closed("c", "p")));
        script.ClosedShard("c", "c-1");
        using var streams = TestClient.CreateStreams(script.Handler, time, attemptTimeout: Timeout.InfiniteTimeSpan);
        var reading = streams.ReadStreamAsync(FromTrimHorizon() with { ShardIteratorType = ShardIteratorType.Latest }).ToListAsync().AsTask();

        // Act
        await AdvanceThroughDelaysAsync(reading, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That(string.Join(",", SequenceNumbers(await reading))).IsEqualTo("c-1");
    }

    [Test]
    [Arguments(61, 0)]
    [Arguments(59, 1)]
    public async Task ReadStreamAsync_LatestAndAClosedShardWhoseChildOpenedThatLongAgo_LooksForMoreChildrenOnlyWithinAMinute(int secondsAgo, int looks)
    {
        // Arrange: the id of the child tells when it opened, and with it when the split was
        var now = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var child = $"shardId-{now.AddSeconds(-secondsAgo).ToUnixTimeMilliseconds():D20}-0000000a";
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("p"), Open(child, "p")));
        script.Answer("children:p", Description(StreamStatus.Disabled, null, Open(child, "p")));
        script.ClosedShard(child, "c-1");
        using var streams = TestClient.CreateStreams(script.Handler, new ManualTimeProvider(now));

        // Act
        await streams.ReadStreamAsync(FromTrimHorizon() with { ShardIteratorType = ShardIteratorType.Latest }).ToListAsync();

        // Assert
        await Assert.That(script.Handler.Requests.Count(static sent => sent.Operation == "DescribeStream" && sent.BodyJson.TryGetProperty("ShardFilter", out var filter)
            && filter.GetProperty("ShardId").GetString() == "p")).IsEqualTo(looks);
    }

    [Test]
    public async Task ReadStreamAsync_Latest_ReadsAChildOfAShardOpenAtTheStartFromItsFirstSequenceNumber()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Open("open")));
        script.ClosedShard("open", "open-1");
        script.Answer("children:open", Description(StreamStatus.Disabled, null, Closed("child", "open")));
        script.ClosedShard("child", "child-1");
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        await streams.ReadStreamAsync(FromTrimHorizon() with { ShardIteratorType = ShardIteratorType.Latest }).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", Positions(script.Handler))).IsEqualTo($"open:LATEST,child:AT_SEQUENCE_NUMBER:{ShardStart}");
    }

    [Test]
    public async Task ReadStreamAsync_ShardsOnTwoPages_ReadsTheShardsOfBoth()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, "a", Closed("a")));
        script.Answer("list:a", Description(StreamStatus.Disabled, null, Closed("b")));
        script.ClosedShard("a", "a-1");
        script.ClosedShard("b", "b-1");
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        var records = await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", SequenceNumbers(records).Order(StringComparer.Ordinal))).IsEqualTo("a-1,b-1");
    }

    [Test]
    public async Task ReadStreamAsync_ChildrenOnTwoPages_StartsTheChildrenOfBoth()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Disabled, null, Closed("p")));
        script.ClosedShard("p", "p-1");
        script.Answer("children:p", Description(StreamStatus.Enabled, "c1", Closed("c1", "p")));
        script.Answer("children:p:c1", Description(StreamStatus.Disabled, null, Closed("c2", "p")));
        script.ClosedShard("c1", "c1-1");
        script.ClosedShard("c2", "c2-1");
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        var records = await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", SequenceNumbers(records).Order(StringComparer.Ordinal))).IsEqualTo("c1-1,c2-1,p-1");
    }

    [Test]
    public async Task ReadStreamAsync_ServiceIgnoresTheChildFilter_ReadsEveryShardOnce()
    {
        // Arrange: every DescribeStream lists the whole stream, as an emulator that ignores ShardFilter would
        var script = new StreamScript();
        var all = Description(StreamStatus.Disabled, null, Closed("p"), Closed("q"), Closed("c", "p"));
        script.Answer("list:", all);
        script.Answer("children:p", all);
        script.Answer("children:q", all);
        script.Answer("children:c", all);
        script.ClosedShard("p", "p-1");
        script.ClosedShard("q", "q-1");
        script.ClosedShard("c", "c-1");
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", Positions(script.Handler).Order(StringComparer.Ordinal))).IsEqualTo($"c:AT_SEQUENCE_NUMBER:{ShardStart},p:TRIM_HORIZON,q:TRIM_HORIZON");
    }

    [Test]
    public async Task ReadStreamAsync_ParentNotListed_ReadsTheShardFromTheStart()
    {
        // Arrange: the parent expired and is no longer listed
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Disabled, null, Closed("c", "expired")));
        script.ClosedShard("c", "c-1");
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        var records = await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", SequenceNumbers(records))).IsEqualTo("c-1");
    }

    [Test]
    public async Task ReadStreamAsync_ShardWithoutIdOrRange_SkipsTheOneAndReadsTheOtherAsOpen()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", StubResponse.Json("""{"StreamDescription":{"StreamStatus":"DISABLED","Shards":[{"ParentShardId":"x"},{"ShardId":"a"}]}}"""));
        script.ClosedShard("a", "a-1");
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        await streams.ReadStreamAsync(FromTrimHorizon() with { ShardIteratorType = ShardIteratorType.Latest }).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", Positions(script.Handler))).IsEqualTo("a:LATEST");
    }

    [Test]
    [Arguments(StreamStatus.Enabled)]
    [Arguments(StreamStatus.Enabling)]
    public async Task ReadStreamAsync_NoChildrenListedYetWhileTheStreamIsOn_WaitsTheDelayAndAsksAgain(StreamStatus status)
    {
        // Arrange
        var time = new ManualTimeProvider();
        var script = new StreamScript();
        script.Answer("list:", Description(status, null, Closed("p")));
        script.ClosedShard("p", "p-1");
        script.Answer("children:p", Description(status, null), Description(StreamStatus.Disabled, null, Closed("c", "p")));
        script.ClosedShard("c", "c-1");
        using var streams = TestClient.CreateStreams(script.Handler, time, attemptTimeout: Timeout.InfiniteTimeSpan);
        var reading = streams.ReadStreamAsync(FromTrimHorizon() with { EmptyPageDelay = TimeSpan.FromSeconds(2) }).ToListAsync().AsTask();

        // Act
        var delays = await AdvanceThroughDelaysAsync(reading, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That((delays.Single(), string.Join(",", SequenceNumbers(await reading)))).IsEqualTo((TimeSpan.FromSeconds(2), "p-1,c-1"));
    }

    [Test]
    [Arguments(StreamStatus.Disabling)]
    [Arguments(StreamStatus.Disabled)]
    public async Task ReadStreamAsync_NoChildrenOfAStreamBeingSwitchedOff_Ends(StreamStatus status)
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(status, null, Closed("p")));
        script.ClosedShard("p", "p-1");
        script.Answer("children:p", Description(status, null));
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        var records = await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", SequenceNumbers(records))).IsEqualTo("p-1");
    }

    [Test]
    public async Task ReadStreamAsync_DescriptionWithoutStream_EndsAfterOneCall()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", StubResponse.Json("{}"));
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        var records = await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That((records.Count, script.Handler.Requests.Count)).IsEqualTo((0, 1));
    }

    [Test]
    public async Task ReadStreamAsync_ChildrenAnswerWithoutStream_EndsAfterTheParent()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("p")));
        script.ClosedShard("p", "p-1");
        script.Answer("children:p", StubResponse.Json("{}"));
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        var records = await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", SequenceNumbers(records))).IsEqualTo("p-1");
    }

    [Test]
    public async Task ReadStreamAsync_Delay_IsWaitedAfterAnEmptyPageOfAShard()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Disabled, null, Open("o")));
        script.Answer("iterator:o", StubResponse.ShardIterator("o/0"));
        script.Answer("records:o/0", StubResponse.Records("o/1"));
        script.Answer("records:o/1", StubResponse.Records(null, Insert("o-1")));
        using var streams = TestClient.CreateStreams(script.Handler, time, attemptTimeout: Timeout.InfiniteTimeSpan);
        var reading = streams.ReadStreamAsync(FromTrimHorizon() with { EmptyPageDelay = TimeSpan.FromSeconds(3) }).ToListAsync().AsTask();

        // Act
        var delays = await AdvanceThroughDelaysAsync(reading, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That((delays.Single(), string.Join(",", SequenceNumbers(await reading)))).IsEqualTo((TimeSpan.FromSeconds(3), "o-1"));
    }

    [Test]
    public async Task ReadStreamAsync_ShardFailsWhileAnotherIsOpen_ThrowsTheFailure()
    {
        // Arrange
        var script = OpenShardAndFailingShard(out _);
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        var act = async () => await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<ValidationException>();
    }

    [Test]
    public async Task ReadStreamAsync_ShardFailsWhileAnotherIsOpen_StopsTheOpenShardBeforeItThrows()
    {
        // Arrange
        var script = OpenShardAndFailingShard(out var released);
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        try
        {
            await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();
        }
        catch (ValidationException)
        {
        }

        // Assert
        await Assert.That(released.Task.IsCompleted).IsTrue();
    }

    [Test]
    public async Task ReadStreamAsync_SecondFailureAfterTheFirst_ThrowsTheFirst()
    {
        // Arrange: shard a answers with an error only once the failure of b has stopped it
        var script = new StreamScript();
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        script.Answer("list:", Description(StreamStatus.Enabled, null, Open("a"), Open("b")));
        script.Answer("iterator:a", async cancellationToken =>
        {
            holding.SetResult();
            await WhenCancelledAsync(cancellationToken);
            return StubResponse.DynamoDbError("AccessDeniedException", "Second failure.");
        });
        script.Answer("iterator:b", async _ =>
        {
            await holding.Task;
            return StubResponse.DynamoDbError("ValidationException", "First failure.");
        });
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        var act = async () => await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<ValidationException>();
    }

    [Test]
    public async Task ReadStreamAsync_ListingFails_ThrowsTheFailure()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", StubResponse.DynamoDbError("ValidationException", "Invalid StreamArn."));
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        var act = async () => await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<ValidationException>();
    }

    [Test]
    public async Task ReadStreamAsync_Trimmed_ThrowsTrimmedDataAccessException()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Open("o")));
        script.Answer("iterator:o", StubResponse.ShardIterator("o/0"));
        script.Answer("records:o/0", StubResponse.DynamoDbError("TrimmedDataAccessException", "The operation attempted to read past the oldest stream record in a shard."));
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        var act = async () => await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<TrimmedDataAccessException>();
    }

    [Test]
    public async Task ReadStreamAsync_ClosedShardIsGone_ThrowsTrimmedDataAccessException()
    {
        // Arrange: DynamoDB removed the shard, and its records, before it was read
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("gone")));
        script.Answer("iterator:gone", StubResponse.ResourceNotFound());
        script.Answer("children:gone", Description(StreamStatus.Disabled, null, Closed("c", "gone")));
        script.ClosedShard("c", "c-1");
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        var act = async () => await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<TrimmedDataAccessException>().WithMessage("The closed shard gone was removed before it was read, 24 hours after it closed, so its records are lost.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadStreamAsync_ClosedChildIsGone_ThrowsTrimmedDataAccessException()
    {
        // Arrange: the parent was read, and its child was removed before its turn came
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("p"), Closed("c", "p")));
        script.ClosedShard("p", "p-1");
        script.Answer("children:p", Description(StreamStatus.Enabled, null, Closed("c", "p")));
        script.Answer("iterator:c", StubResponse.ResourceNotFound());
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        var act = async () => await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<TrimmedDataAccessException>();
    }

    [Test]
    public async Task ReadStreamAsync_ChildTrimmedBeforeItsTurn_ThrowsTrimmedDataAccessException()
    {
        // Arrange: the first records of the child are gone, which its first sequence number shows and the trim horizon would not
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("p")));
        script.ClosedShard("p", "p-1");
        script.Answer("children:p", Description(StreamStatus.Disabled, null, Open("c", "p")));
        script.Answer("iterator:c", StubResponse.DynamoDbError("TrimmedDataAccessException", "The operation attempted to read past the oldest stream record in a shard."));
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        var act = async () => await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<TrimmedDataAccessException>();
    }

    [Test]
    [Arguments(0, "TRIM_HORIZON")]
    [Arguments((23 * 60) - 1, "TRIM_HORIZON")]
    [Arguments(23 * 60, "AT_SEQUENCE_NUMBER:" + ShardStart)]
    [Arguments(30 * 60, "AT_SEQUENCE_NUMBER:" + ShardStart)]
    public async Task ReadStreamAsync_ChildOpenedThatLongAgo_StartsAtTheTrimHorizonOnlyWhileNothingCanBeTrimmed(int minutesAgo, string position)
    {
        // Arrange: the id of a shard of DynamoDB holds the Unix milliseconds it opened at
        var now = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var child = $"shardId-{now.AddMinutes(-minutesAgo).ToUnixTimeMilliseconds():D20}-0000000a";
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("p")));
        script.ClosedShard("p", "p-1");
        script.Answer("children:p", Description(StreamStatus.Disabled, null, Closed(child, "p")));
        script.ClosedShard(child, "c-1");
        using var streams = TestClient.CreateStreams(script.Handler, new ManualTimeProvider(now));

        // Act
        await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(Positions(script.Handler).Last()).IsEqualTo($"{child}:{position}");
    }

    [Test]
    [Arguments("c")]
    [Arguments("shardXd-00000001767225600000-0000000a")]
    [Arguments("shardId-00000001767225600000x0000000a")]
    [Arguments("shardId-0000000176722560000a-0000000a")]
    [Arguments("shardId-00000999999999999999-0000000a")]
    public async Task ReadStreamAsync_ChildWhoseIdTellsNoTime_StartsAtItsFirstSequenceNumber(string child)
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("p")));
        script.ClosedShard("p", "p-1");
        script.Answer("children:p", Description(StreamStatus.Disabled, null, Closed(child, "p")));
        script.ClosedShard(child, "c-1");
        using var streams = TestClient.CreateStreams(script.Handler, new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        // Act
        await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(Positions(script.Handler).Last()).IsEqualTo($"{child}:AT_SEQUENCE_NUMBER:{ShardStart}");
    }

    [Test]
    [Arguments("""{"ShardId":"c","ParentShardId":"p"}""")]
    [Arguments("""{"ShardId":"c","ParentShardId":"p","SequenceNumberRange":{}}""")]
    [Arguments("""{"ShardId":"c","ParentShardId":"p","SequenceNumberRange":{"StartingSequenceNumber":""}}""")]
    public async Task ReadStreamAsync_ChildWithoutStartingSequenceNumber_ReadsItFromTheTrimHorizon(string child)
    {
        // Arrange: an emulator leaves the range of an empty shard empty
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("p")));
        script.ClosedShard("p", "p-1");
        script.Answer("children:p", StubResponse.Json($$$"""{"StreamDescription":{"StreamStatus":"DISABLED","Shards":[{{{child}}}]}}"""));
        script.ClosedShard("c", "c-1");
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", Positions(script.Handler))).IsEqualTo("p:TRIM_HORIZON,c:TRIM_HORIZON");
    }

    [Test]
    public async Task ReadStreamAsync_SecondChildListedLate_ReadsItToo()
    {
        // Arrange: DynamoDB lists the second child of a split only on the third look
        var time = new ManualTimeProvider();
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("p")));
        script.ClosedShard("p", "p-1");
        script.Answer(
            "children:p",
            Description(StreamStatus.Enabled, null, Closed("c1", "p")),
            Description(StreamStatus.Enabled, null, Closed("c1", "p")),
            Description(StreamStatus.Enabled, null, Closed("c1", "p"), Closed("c2", "p")));
        script.ClosedShard("c1", "c1-1");
        script.ClosedShard("c2", "c2-1");
        using var streams = TestClient.CreateStreams(script.Handler, time, attemptTimeout: Timeout.InfiniteTimeSpan);
        var reading = streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync().AsTask();

        // Act
        await AdvanceThroughDelaysAsync(reading, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That(string.Join(",", SequenceNumbers(await reading).Order(StringComparer.Ordinal))).IsEqualTo("c1-1,c2-1,p-1");
    }

    [Test]
    public async Task ReadStreamAsync_OneChildWhileTheStreamIsOn_LooksForMoreEveryFiveSecondsForAMinute()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("p")));
        script.ClosedShard("p", "p-1");
        script.Answer("children:p", Description(StreamStatus.Enabled, null, Closed("c", "p")));
        script.ClosedShard("c", "c-1");
        using var streams = TestClient.CreateStreams(script.Handler, time, attemptTimeout: Timeout.InfiniteTimeSpan);
        var reading = streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync().AsTask();

        // Act
        var delays = await AdvanceThroughDelaysAsync(reading, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That((delays.Count, delays.Distinct().Single())).IsEqualTo((12, TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task ReadStreamAsync_DelayAboveFiveSeconds_LooksForMoreChildrenAtThatDelay()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("p")));
        script.ClosedShard("p", "p-1");
        script.Answer("children:p", Description(StreamStatus.Enabled, null, Closed("c", "p")));
        script.ClosedShard("c", "c-1");
        using var streams = TestClient.CreateStreams(script.Handler, time, attemptTimeout: Timeout.InfiniteTimeSpan);
        var reading = streams.ReadStreamAsync(FromTrimHorizon() with { EmptyPageDelay = TimeSpan.FromSeconds(7) }).ToListAsync().AsTask();

        // Act
        var delays = await AdvanceThroughDelaysAsync(reading, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That((delays.Count, delays.Distinct().Single())).IsEqualTo((9, TimeSpan.FromSeconds(7)));
    }

    [Test]
    public async Task ReadStreamAsync_NoChildYetAndNoDelay_WaitsASecondBeforeItAsksAgain()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Closed("p")));
        script.ClosedShard("p", "p-1");
        script.Answer("children:p", Description(StreamStatus.Enabled, null), Description(StreamStatus.Disabled, null, Closed("c", "p")));
        script.ClosedShard("c", "c-1");
        using var streams = TestClient.CreateStreams(script.Handler, time, attemptTimeout: Timeout.InfiniteTimeSpan);
        var reading = streams.ReadStreamAsync(FromTrimHorizon() with { EmptyPageDelay = TimeSpan.Zero }).ToListAsync().AsTask();

        // Act
        var delays = await AdvanceThroughDelaysAsync(reading, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That(delays.Single()).IsEqualTo(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task ReadStreamAsync_OpenShardIsGone_ThrowsResourceNotFoundException()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Open("o")));
        script.Answer("iterator:o", StubResponse.ResourceNotFound());
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        var act = async () => await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<ResourceNotFoundException>();
    }

    [Test]
    public async Task ReadStreamAsync_Cancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var script = OpenShardHeldAfterOneRecord(out var holding, out _);
        using var streams = TestClient.CreateStreams(script.Handler);
        using var cancellation = new CancellationTokenSource();
        await using var records = streams.ReadStreamAsync(FromTrimHorizon(), cancellation.Token).GetAsyncEnumerator();
        await records.MoveNextAsync();
        await holding.Task;

        // Act
        await cancellation.CancelAsync();

        // Assert
        await Assert.That(async () => await records.MoveNextAsync()).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task ReadStreamAsync_Cancelled_StopsTheReadersBeforeItThrows()
    {
        // Arrange
        var script = OpenShardHeldAfterOneRecord(out var holding, out var released);
        using var streams = TestClient.CreateStreams(script.Handler);
        using var cancellation = new CancellationTokenSource();
        await using var records = streams.ReadStreamAsync(FromTrimHorizon(), cancellation.Token).GetAsyncEnumerator();
        await records.MoveNextAsync();
        await holding.Task;

        // Act
        await cancellation.CancelAsync();
        try
        {
            await records.MoveNextAsync();
        }
        catch (OperationCanceledException)
        {
        }

        // Assert
        await Assert.That(released.Task.IsCompleted).IsTrue();
    }

    [Test]
    public async Task ReadStreamAsync_CallerStopsAfterTheFirstRecord_StopsTheReadersOnDispose()
    {
        // Arrange
        var script = OpenShardHeldAfterOneRecord(out var holding, out var released);
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        await foreach (var _ in streams.ReadStreamAsync(FromTrimHorizon()))
        {
            await holding.Task;
            break;
        }

        // Assert
        await Assert.That(released.Task.IsCompleted).IsTrue();
    }

    [Test]
    public async Task ReadStreamAsync_TokenOfWithCancellation_IsHonoured()
    {
        // Arrange
        var script = OpenShardHeldAfterOneRecord(out _, out _);
        using var streams = TestClient.CreateStreams(script.Handler);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var act = async () => await streams.ReadStreamAsync(FromTrimHorizon()).WithCancellation(cancellation.Token).GetAsyncEnumerator().MoveNextAsync();

        // Assert
        await Assert.That(act).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task ReadStreamAsync_TokensOfTheCallAndOfWithCancellation_ReadsToTheEnd()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Disabled, null, Closed("a")));
        script.ClosedShard("a", "a-1");
        using var streams = TestClient.CreateStreams(script.Handler);
        using var call = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();

        // Act
        var records = new List<ShardChangeRecord>();
        await foreach (var change in streams.ReadStreamAsync(FromTrimHorizon(), call.Token).WithCancellation(enumeration.Token))
        {
            records.Add(change);
        }

        // Assert
        await Assert.That(string.Join(",", SequenceNumbers(records))).IsEqualTo("a-1");
    }

    [Test]
    public async Task ReadStreamAsync_NotEnumerated_SendsNothing()
    {
        // Arrange
        var script = new StreamScript();
        using var streams = TestClient.CreateStreams(script.Handler);

        // Act
        _ = streams.ReadStreamAsync(FromTrimHorizon());

        // Assert
        await Assert.That(script.Handler.Requests.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ReadStreamAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var streams = TestClient.CreateStreams(new StubHandler());

        // Act
        var act = () => streams.ReadStreamAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    [Arguments(ShardIteratorType.Unknown)]
    [Arguments(ShardIteratorType.AtSequenceNumber)]
    [Arguments(ShardIteratorType.AfterSequenceNumber)]
    [Arguments((ShardIteratorType)99)]
    public async Task ReadStreamAsync_PositionOtherThanTrimHorizonOrLatest_ThrowsArgumentOutOfRangeException(ShardIteratorType type)
    {
        // Arrange
        using var streams = TestClient.CreateStreams(new StubHandler());

        // Act
        var act = () => streams.ReadStreamAsync(FromTrimHorizon() with { ShardIteratorType = type });

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(-1L)]
    [Arguments(15L * 60 * 1000)]
    public async Task ReadStreamAsync_DelayOutOfRange_ThrowsArgumentOutOfRangeException(long milliseconds)
    {
        // Arrange
        using var streams = TestClient.CreateStreams(new StubHandler());

        // Act
        var act = () => streams.ReadStreamAsync(FromTrimHorizon() with { EmptyPageDelay = TimeSpan.FromMilliseconds(milliseconds) });

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(ShardIteratorType.TrimHorizon, 0L)]
    [Arguments(ShardIteratorType.Latest, (15L * 60 * 1000) - 1)]
    public async Task ReadStreamAsync_ValidPositionAndDelay_IsAccepted(ShardIteratorType type, long milliseconds)
    {
        // Arrange
        using var streams = TestClient.CreateStreams(new StubHandler());

        // Act
        var records = streams.ReadStreamAsync(FromTrimHorizon() with { ShardIteratorType = type, EmptyPageDelay = TimeSpan.FromMilliseconds(milliseconds) });

        // Assert
        await Assert.That(records).IsNotNull();
    }

    private static ReadStreamRequest FromTrimHorizon() => new() { StreamArn = TestClient.StreamArn, ShardIteratorType = ShardIteratorType.TrimHorizon };

    private static async Task WhenCancelledAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    // An open shard whose first GetRecords waits until it is cancelled, and a closed shard that fails once the open one waits.
    private static StreamScript OpenShardAndFailingShard(out TaskCompletionSource released)
    {
        var script = new StreamScript();
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        released = done;
        script.Answer("list:", Description(StreamStatus.Enabled, null, Open("open"), Closed("bad")));
        script.Answer("iterator:open", StubResponse.ShardIterator("open/0"));
        script.Answer("records:open/0", Hold(holding, done));
        script.Answer("iterator:bad", async _ =>
        {
            await holding.Task;
            return StubResponse.DynamoDbError("ValidationException", "Failure of another shard.");
        });
        return script;
    }

    // An open shard with one record, whose next GetRecords waits until it is cancelled.
    private static StreamScript OpenShardHeldAfterOneRecord(out TaskCompletionSource holding, out TaskCompletionSource released)
    {
        var script = new StreamScript();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        holding = entered;
        released = done;
        script.Answer("list:", Description(StreamStatus.Enabled, null, Open("open")));
        script.Answer("iterator:open", StubResponse.ShardIterator("open/0"));
        script.Answer("records:open/0", StubResponse.Records("open/1", Insert("open-1")));
        script.Answer("records:open/1", Hold(entered, done));
        return script;
    }

    // Says when the request arrived, waits until it is cancelled, and says that it ended before the cancellation reaches the client.
    private static Func<CancellationToken, Task<StubResponse>> Hold(TaskCompletionSource entered, TaskCompletionSource released) => async cancellationToken =>
    {
        entered.TrySetResult();
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Not reached.");
        }
        finally
        {
            released.TrySetResult();
        }
    };
}
