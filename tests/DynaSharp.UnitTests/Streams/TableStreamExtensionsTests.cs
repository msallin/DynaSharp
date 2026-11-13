using DynaSharp.Requests;
using DynaSharp.Streams;
using DynaSharp.Telemetry;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Fluent.Mix;
using DynaSharp.UnitTests.Telemetry;
using DynaSharp.UnitTests.Testing.Fake;
using static DynaSharp.UnitTests.Streams.TypedChanges;

namespace DynaSharp.UnitTests.Streams;

/// <summary>
/// <see cref="TableStreamExtensions.ReadStreamAsync{T}"/> over a <see cref="FakeDynamoDb"/>, whose table <c>Users</c> has a stream: the stream that <c>DescribeTable</c> names
/// at the start of each enumeration, a table without one, and the arguments.
/// </summary>
public sealed class TableStreamExtensionsTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ReadStreamAsync_TableWithAStream_ReadsTheStreamTheTableNames()
    {
        // Arrange
        var fake = FakeStreams.UsersOn(new ManualTimeProvider(Start));
        FakeStreams.Put(fake, "k", 1);
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();

        // Act
        var records = await client.Table("Users", "pk").ReadStreamAsync(streams, ShardIteratorType.TrimHorizon).Take(1).ToListAsync();

        // Assert
        await Assert.That(records.Single().Record.DynamoDb!.Keys!["pk"].AsString()).IsEqualTo("k");
    }

    [Test]
    public async Task ReadStreamAsync_StreamSwitchedOffAfterTheStart_EndsWithTheStream()
    {
        // Arrange
        using var handler = SwitchedOffAfterTheStart();
        using var client = TestClient.Create(handler);
        using var streams = TestClient.CreateStreams(handler);

        // Act
        var records = await client.Table("Users", "pk").ReadStreamAsync(streams, ShardIteratorType.TrimHorizon).ToListAsync();

        // Assert
        await Assert.That((records.Single().ShardId, handler.Requests[1].BodyJson.GetProperty("StreamArn").GetString())).IsEqualTo(("shard-1", TestClient.StreamArn));
    }

    [Test]
    public async Task ReadStreamAsync_TokensOfTheCallAndOfWithCancellation_ReadsToTheEnd()
    {
        // Arrange
        using var handler = SwitchedOffAfterTheStart();
        using var client = TestClient.Create(handler);
        using var streams = TestClient.CreateStreams(handler);
        using var call = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();

        // Act
        var records = new List<ShardChangeRecord>();
        await foreach (var change in client.Table("Users", "pk").ReadStreamAsync(streams, ShardIteratorType.TrimHorizon, cancellationToken: call.Token).WithCancellation(enumeration.Token))
        {
            records.Add(change);
        }

        // Assert
        await Assert.That(records.Single().ShardId).IsEqualTo("shard-1");
    }

    [Test]
    public async Task ReadStreamAsync_StreamGoneAfterTheTableNamedIt_ThrowsResourceNotFoundException()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json($$$"""{"Table":{"TableName":"Users","StreamSpecification":{"StreamEnabled":true,"StreamViewType":"KEYS_ONLY"},"LatestStreamArn":"{{{TestClient.StreamArn}}}"}}"""));
        handler.Enqueue(StubResponse.ResourceNotFound());
        using var client = TestClient.Create(handler);
        using var streams = TestClient.CreateStreams(handler);

        // Act
        var act = async () => await client.Table("Users", "pk").ReadStreamAsync(streams, ShardIteratorType.TrimHorizon).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<ResourceNotFoundException>();
    }

    [Test]
    public async Task ReadStreamAsync_StreamTurnedOffAndOnBetweenTwoEnumerations_ReadsTheNewStreamTheSecondTime()
    {
        // Arrange: the old stream keeps the first value for 24 hours, and only the new one has the second
        var time = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(time);
        FakeStreams.Put(fake, "k", 1);
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();
        var reading = client.Table("Users", "pk").ReadStreamAsync(streams, ShardIteratorType.TrimHorizon);
        var first = await reading.Take(1).ToListAsync();
        SetStream(fake, enabled: false);
        time.Advance(TimeSpan.FromSeconds(1));
        SetStream(fake, enabled: true);
        FakeStreams.Put(fake, "k", 2);

        // Act
        var second = await reading.Take(1).ToListAsync();

        // Assert
        await Assert.That((Value(first.Single()), Value(second.Single()))).IsEqualTo((1m, 2m));
    }

    [Test]
    public async Task ReadStreamAsync_Joined_ReadsAWriteAfterTheCall()
    {
        // Arrange: the item is written once the reader holds its position, and the clock moves the waits after empty pages
        var time = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(time);
        FakeStreams.Put(fake, "before", 1);
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = client.Table("Users", "pk").ReadStreamAsync(streams, ShardIteratorType.Latest, onJoined: () => joined.TrySetResult()).Take(1).ToListAsync().AsTask();
        await joined.Task;
        FakeStreams.Put(fake, "after", 1);

        // Act
        while (!reading.IsCompleted)
        {
            if (time.NextDueIn is { } dueIn)
            {
                time.Advance(dueIn);
            }
            else
            {
                await Task.Yield();
            }
        }

        // Assert
        await Assert.That((await reading).Single().Record.DynamoDb!.Keys!["pk"].AsString()).IsEqualTo("after");
    }

    [Test]
    public async Task ReadStreamAsync_IteratorFromLatestExpiresBeforeAnyRecord_ReportsTheGapAndReadsOnFromLatest()
    {
        // Arrange: the reader idles on an empty shard, and the clock jumps past the life of the iterator before the next page. No write comes in between,
        // because the fake ends the wait of an idle reader at a write, which would read it before the clock moves.
        var time = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(time);
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gap = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = client.Table("Users", "pk")
            .ReadStreamAsync(streams, ShardIteratorType.Latest, onJoined: () => joined.TrySetResult(), onGap: shard => gap.TrySetResult(shard))
            .Take(1)
            .ToListAsync()
            .AsTask();
        await joined.Task;
        await UntilAsync(() => time.NextDueIn is not null);
        time.Advance(TimeSpan.FromMinutes(16));

        // Act
        await AdvanceUntilAsync(time, gap.Task);
        FakeStreams.Put(fake, "after", 1);
        await AdvanceUntilAsync(time, reading);

        // Assert
        await Assert.That(((await gap.Task).Length > 0, (await reading).Single().Record.DynamoDb!.Keys!["pk"].AsString())).IsEqualTo((true, "after"));
    }

    [Test]
    public async Task ReadStreamAsync_EmptyPageDelay_IsWaitedAfterAnEmptyPage()
    {
        // Arrange
        var time = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(time);
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();
        using var cancellation = new CancellationTokenSource();
        var reading = client.Table("Users", "pk").ReadStreamAsync(streams, ShardIteratorType.Latest, TimeSpan.FromSeconds(3), cancellationToken: cancellation.Token).ToListAsync().AsTask();
        while (time.ArmedTimers == 0)
        {
            await Task.Yield();
        }

        // Act
        var delay = time.NextDueIn;
        await cancellation.CancelAsync();

        // Assert
        await Assert.That((delay, await TestClient.CaptureAsync<OperationCanceledException>(() => reading) is not null)).IsEqualTo(((TimeSpan?)TimeSpan.FromSeconds(3), true));
    }

    [Test]
    public async Task ReadStreamAsync_TableWithoutAStream_ThrowsStreamUnavailableException()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();

        // Act
        var act = async () => await client.Table("Users", "pk").ReadStreamAsync(streams, ShardIteratorType.TrimHorizon).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<StreamUnavailableException>().WithMessage(
            "The table Users has no enabled stream. Turn one on with Stream(...) of CreateTable(), or with StreamSpecification of UpdateTableAsync.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadStreamAsync_StreamTurnedOff_ThrowsStreamUnavailableException()
    {
        // Arrange: DescribeTable still names the stream that was switched off
        var fake = FakeStreams.UsersOn(new ManualTimeProvider(Start));
        SetStream(fake, enabled: false);
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();

        // Act
        var act = async () => await client.Table("Users", "pk").ReadStreamAsync(streams, ShardIteratorType.TrimHorizon).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<StreamUnavailableException>();
    }

    [Test]
    [Arguments("""{"Table":{"TableName":"Users","StreamSpecification":{"StreamEnabled":false},"LatestStreamArn":"arn:aws:dynamodb:eu-central-2:123456789012:table/Users/stream/x"}}""")]
    [Arguments("""{"Table":{"TableName":"Users","StreamSpecification":{"StreamEnabled":true,"StreamViewType":"NEW_IMAGE"}}}""")]
    [Arguments("""{"Table":{"TableName":"Users"}}""")]
    public async Task ReadStreamAsync_DescriptionWithoutAnEnabledAndNamedStream_ThrowsWithoutReadingAStream(string description)
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(description));
        using var client = TestClient.Create(handler);
        using var streams = TestClient.CreateStreams(handler);

        // Act
        await TestClient.CaptureAsync<InvalidOperationException>(() => client.Table("Users", "pk").ReadStreamAsync(streams, ShardIteratorType.TrimHorizon).ToListAsync().AsTask());

        // Assert
        await Assert.That(string.Join(",", handler.Requests.Select(static sent => sent.Operation))).IsEqualTo("DescribeTable");
    }

    [Test]
    public async Task ReadStreamAsync_NotEnumerated_SendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        using var streams = TestClient.CreateStreams(handler);

        // Act
        _ = client.Table("Users", "pk").ReadStreamAsync(streams, ShardIteratorType.TrimHorizon);

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ReadStreamAsync_NullTable_ThrowsArgumentNullException()
    {
        // Arrange
        using var streams = TestClient.CreateStreams(new StubHandler());

        // Act
        var act = () => TableStreamExtensions.ReadStreamAsync<Item>(null!, streams, ShardIteratorType.TrimHorizon);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("table");
    }

    [Test]
    public async Task ReadStreamAsync_NullStreamsClient_ThrowsArgumentNullException()
    {
        // Arrange
        using var client = TestClient.Create(new StubHandler());

        // Act
        var act = () => client.Table("Users", "pk").ReadStreamAsync(null!, ShardIteratorType.TrimHorizon);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("streams");
    }

    [Test]
    [Arguments(ShardIteratorType.AfterSequenceNumber, 0L)]
    [Arguments(ShardIteratorType.Latest, -1L)]
    [Arguments(ShardIteratorType.TrimHorizon, 15 * 60 * 1000L)]
    public async Task ReadStreamAsync_PositionOrDelayOutOfRange_ThrowsArgumentOutOfRangeException(ShardIteratorType type, long milliseconds)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        using var streams = TestClient.CreateStreams(handler);

        // Act
        var act = () => client.Table("Users", "pk").ReadStreamAsync(streams, type, TimeSpan.FromMilliseconds(milliseconds));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ReadChangesAsync_OneType_SkipsTheChangesOfOtherEntities()
    {
        // Arrange
        var fake = Shop("Shop");
        fake.Put("Shop", Item.From(BetaValue() with { Sk = "2" }));
        fake.Put("Shop", Item.From(AlphaValue()));
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();

        // Act
        var changes = await client.Table("Shop", "pk", "sk").Of<Alpha>().ReadChangesAsync(streams, ShardIteratorType.TrimHorizon).Take(1).ToListAsync();

        // Assert
        await Assert.That((changes.Single().Kind, changes.Single().After!.Name, changes.Single().Keys!["sk"].AsString())).IsEqualTo((OperationType.Insert, "first", "1"));
    }

    [Test]
    public async Task ReadChangesAsync_ModifyOfAnItemThatStaysItsType_IsOneChange()
    {
        // Arrange
        var fake = Shop("Shop");
        fake.Put("Shop", Item.From(AlphaValue()));
        fake.Put("Shop", Item.From(AlphaValue() with { Name = "second" }));
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();

        // Act
        var changes = await client.Table("Shop", "pk", "sk").Of<Alpha>().ReadChangesAsync(streams, ShardIteratorType.TrimHorizon).Take(2).ToListAsync();

        // Assert
        await Assert.That((changes[1].Kind, changes[1].Before!.Name, changes[1].After!.Name)).IsEqualTo((OperationType.Modify, "first", "second"));
    }

    [Test]
    public async Task ReadChangesAsync_TableAnswersLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange: the description of the table, which names the stream, comes once the test lets it
        var described = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answers = new Queue<StubResponse>(
        [
            StubResponse.Json($$$"""{"Table":{"TableName":"Shop","StreamSpecification":{"StreamEnabled":true,"StreamViewType":"NEW_AND_OLD_IMAGES"},"LatestStreamArn":"{{{TestClient.StreamArn}}}"}}"""),
            StubResponse.StreamDescription("Shop", StreamStatus.Disabled),
        ]);
        using var handler = new StubHandler(async (_, _) =>
        {
            await described.Task.ConfigureAwait(false);
            lock (answers)
            {
                return answers.Dequeue();
            }
        });
        using var client = TestClient.Create(handler);
        using var streams = TestClient.CreateStreams(handler);
        await using var changes = client.Table("Shop", "pk", "sk").Of<Alpha>().ReadChangesAsync(streams, ShardIteratorType.TrimHorizon).GetAsyncEnumerator();
        var context = new CountingSynchronizationContext();
        var step = context.Start(() => changes.MoveNextAsync().AsTask());

        // Act
        described.SetResult();
        await step;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task ReadChangesAsync_StreamSwitchedOffAfterTheStart_EndsWithTheStream()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json($$$"""{"Table":{"TableName":"Shop","StreamSpecification":{"StreamEnabled":true,"StreamViewType":"NEW_AND_OLD_IMAGES"},"LatestStreamArn":"{{{TestClient.StreamArn}}}"}}"""));
        handler.Enqueue(StubResponse.StreamDescription("Shop", StreamStatus.Disabled, StubResponse.Shard("shard-1", endingSequenceNumber: "9")));
        handler.Enqueue(StubResponse.ShardIterator("iterator-1"));
        handler.Enqueue(StubResponse.Records(null, StubResponse.Inserted(AlphaValue()), StubResponse.Inserted(BetaValue() with { Sk = "2" })));
        handler.Enqueue(StubResponse.StreamDescription("Shop", StreamStatus.Disabled));
        using var client = TestClient.Create(handler);
        using var streams = TestClient.CreateStreams(handler);

        using var call = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();

        // Act: the token of the call and that of WithCancellation, both of which end the reading
        var changes = new List<Change<Alpha>>();
        await foreach (var change in client.Table("Shop", "pk", "sk").Of<Alpha>().ReadChangesAsync(streams, ShardIteratorType.TrimHorizon, cancellationToken: call.Token).WithCancellation(enumeration.Token))
        {
            changes.Add(change);
        }

        // Assert
        await Assert.That(changes.Single().After!.Name).IsEqualTo("first");
    }

    [Test]
    public async Task ReadChangesAsync_CancelledWhileWaiting_ThrowsOperationCanceledException()
    {
        // Arrange
        var time = new ManualTimeProvider(Start);
        var fake = Shop("Shop");
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient(options => options with { TimeProvider = time });
        using var cancellation = new CancellationTokenSource();
        var reading = client.Table("Shop", "pk", "sk").Of<Alpha>().ReadChangesAsync(streams, ShardIteratorType.Latest, cancellationToken: cancellation.Token).ToListAsync().AsTask();
        while (time.ArmedTimers == 0)
        {
            await Task.Yield();
        }

        // Act
        await cancellation.CancelAsync();

        // Assert
        await Assert.That(await TestClient.CaptureAsync<OperationCanceledException>(() => reading)).IsNotNull();
    }

    [Test]
    [Arguments(StreamViewType.NewImage)]
    [Arguments(StreamViewType.OldImage)]
    [Arguments(StreamViewType.KeysOnly)]
    public async Task ReadChangesAsync_ViewWithoutBothImages_ThrowsStreamUnavailableException(StreamViewType view)
    {
        // Arrange
        var fake = Shop("Shop", view);
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();

        // Act
        var act = async () => await client.Table("Shop", "pk", "sk").ReadChangesAsync<Alpha, Beta>(streams, ShardIteratorType.TrimHorizon).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<StreamUnavailableException>().WithMessage(
            $"The stream of the table Shop has the view {view}, and a typed read needs NewAndOldImages: without the old image a removal has nothing to be typed by, and without the new image an insert has not. Read the records with ReadStreamAsync, or switch the stream to NewAndOldImages, which starts a new stream.",
            StringComparison.Ordinal);
    }

    [Test]
    [Arguments(StreamViewType.NewImage)]
    [Arguments(StreamViewType.KeysOnly)]
    public async Task ReadChangesAsync_ViewWithoutBothImages_NamesTheTableTheViewAndTheOneItNeeds(StreamViewType view)
    {
        // Arrange
        var fake = Shop("Shop", view);
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();

        // Act
        var failure = await TestClient.CaptureAsync<StreamUnavailableException>(async () => await client.Table("Shop", "pk", "sk").ReadChangesAsync<Alpha, Beta>(streams, ShardIteratorType.TrimHorizon).ToListAsync());

        // Assert
        await Assert.That((failure.TableName, failure.ViewType, failure.RequiredViewType)).IsEqualTo(("Shop", (StreamViewType?)view, (StreamViewType?)StreamViewType.NewAndOldImages));
    }

    [Test]
    public async Task ReadStreamAsync_TableWithoutAStream_SaysTheStreamIsOffAndNeedsNoView()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();

        // Act
        var failure = await TestClient.CaptureAsync<StreamUnavailableException>(async () => await client.Table("Users", "pk").ReadStreamAsync(streams, ShardIteratorType.TrimHorizon).ToListAsync());

        // Assert
        await Assert.That((failure.TableName, failure.ViewType, failure.RequiredViewType)).IsEqualTo(("Users", (StreamViewType?)null, (StreamViewType?)null));
    }

    [Test]
    public async Task ReadChangesAsync_TableWithoutAStream_SaysTheStreamIsOffAndTheViewItNeeds()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();

        // Act
        var failure = await TestClient.CaptureAsync<StreamUnavailableException>(async () => await client.Table("Users", "pk").ReadChangesAsync<Alpha, Beta>(streams, ShardIteratorType.TrimHorizon).ToListAsync());

        // Assert
        await Assert.That((failure.ViewType, failure.RequiredViewType, failure is InvalidOperationException)).IsEqualTo(((StreamViewType?)null, (StreamViewType?)StreamViewType.NewAndOldImages, true));
    }

    [Test]
    public async Task StreamUnavailableException_NullTableName_Throws()
    {
        // Act
        var failure = TestClient.Catch(() => _ = new StreamUnavailableException("m", null!, null, null));

        // Assert
        await Assert.That(((ArgumentNullException)failure).ParamName).IsEqualTo("tableName");
    }

    [Test]
    public async Task ReadChangesAsync_TypeChangedAtTheKey_GivesTheRemovalThenTheInsert()
    {
        // Arrange
        var fake = Shop("Shop");
        fake.Put("Shop", Item.From(AlphaValue()));
        fake.Put("Shop", Item.From(BetaValue()));
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();

        // Act
        var changes = await client.Table("Shop", "pk", "sk").ReadChangesAsync<Alpha, Beta>(streams, ShardIteratorType.TrimHorizon).Take(3).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", changes.Select(static change => change.Match(static alpha => $"A{alpha.Kind}", static beta => $"B{beta.Kind}"))))
            .IsEqualTo("AInsert,ARemove,BInsert");
    }

    [Test]
    public async Task ReadChangesAsync_ThreeAndFourTypes_GiveEachType()
    {
        // Arrange
        var fake = Shop("Shop");
        fake.Put("Shop", Item.From(AlphaValue()));
        fake.Put("Shop", Item.From(BetaValue() with { Sk = "2" }));
        fake.Put("Shop", Item.From(new Gamma { Pk = "a", Sk = "3" }));
        fake.Put("Shop", Item.From(new Delta { Pk = "a", Sk = "4" }));
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var three = await shop.ReadChangesAsync<Beta, Gamma, Delta>(streams, ShardIteratorType.TrimHorizon).Take(3).ToListAsync();
        var four = await shop.ReadChangesAsync<Alpha, Beta, Gamma, Delta>(streams, ShardIteratorType.TrimHorizon).Take(4).ToListAsync();

        // Assert
        await Assert.That((
            string.Join(",", three.Select(static change => change.Match(static _ => "B", static _ => "G", static _ => "D"))),
            string.Join(",", four.Select(static change => change.Match(static _ => "A", static _ => "B", static _ => "G", static _ => "D"))))).IsEqualTo(("B,G,D", "A,B,G,D"));
    }

    [Test]
    public async Task ReadChangesAsync_ImageThatCannotBeRead_ThrowsDynamoDbMappingException()
    {
        // Arrange
        var fake = Shop("Shop");
        fake.Put("Shop", BrokenBeta);
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table("Shop", "pk", "sk").Of<Beta>().ReadChangesAsync(streams, ShardIteratorType.TrimHorizon).Take(1).ToListAsync());

        // Assert
        await Assert.That((failure.AttributeName, failure.Key)).IsEqualTo(("Amount", RecordKeys));
    }

    [Test]
    public async Task ReadChangesAsync_OnMappingFailure_SkipsEachChangeThatCannotBeRead()
    {
        // Arrange: an insert, a modify of two broken images, and a modify from a broken image to a good one fail; the next insert is read
        var fake = Shop("Shop");
        fake.Put("Shop", BrokenBeta);
        fake.Put("Shop", new Item(BrokenBeta) { ["note"] = "again" });
        fake.Put("Shop", Item.From(BetaValue()));
        fake.Put("Shop", Item.From(BetaValue(8) with { Sk = "2" }));
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();
        var failures = new List<DynamoDbMappingException>();

        // Act
        var changes = await client.Table("Shop", "pk", "sk").Of<Beta>().ReadChangesAsync(streams, ShardIteratorType.TrimHorizon, onMappingFailure: (failure, _) => failures.Add(failure)).Take(1).ToListAsync();

        // Assert
        await Assert.That((changes.Single().After!.Amount, failures.Count)).IsEqualTo((8, 3));
    }

    [Test]
    public async Task ReadChangesAsync_OnMappingFailureOfTheOldType_GivesTheInsertOfTheNewType()
    {
        // Arrange
        var fake = Shop("Shop");
        fake.Put("Shop", BrokenBeta);
        fake.Put("Shop", Item.From(AlphaValue()));
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();
        var failures = new List<DynamoDbMappingException>();

        // Act
        var changes = await client.Table("Shop", "pk", "sk").ReadChangesAsync<Alpha, Beta>(streams, ShardIteratorType.TrimHorizon, onMappingFailure: (failure, _) => failures.Add(failure)).Take(1).ToListAsync();

        // Assert
        await Assert.That((changes.Single().TryGetValue(out Change<Alpha>? alpha), alpha!.Kind, failures.Count)).IsEqualTo((true, OperationType.Insert, 2));
    }

    [Test]
    public async Task ReadChangesAsync_OnMappingFailure_GetsTheShardAndTheSequenceNumberOfTheRecord()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json($$$"""{"Table":{"TableName":"Shop","StreamSpecification":{"StreamEnabled":true,"StreamViewType":"NEW_AND_OLD_IMAGES"},"LatestStreamArn":"{{{TestClient.StreamArn}}}"}}"""));
        handler.Enqueue(StubResponse.StreamDescription("Shop", StreamStatus.Disabled, StubResponse.Shard("shard-7", endingSequenceNumber: "9")));
        handler.Enqueue(StubResponse.ShardIterator("iterator-1"));
        handler.Enqueue(StubResponse.Records(null, new ChangeRecord { EventName = OperationType.Insert, DynamoDb = new StreamRecord { Keys = RecordKeys, NewImage = BrokenBeta, SequenceNumber = "42" } }));
        handler.Enqueue(StubResponse.StreamDescription("Shop", StreamStatus.Disabled));
        using var client = TestClient.Create(handler);
        using var streams = TestClient.CreateStreams(handler);
        var skipped = new List<(string ShardId, string? SequenceNumber, OperationType EventName)>();

        // Act
        await client.Table("Shop", "pk", "sk").ReadChangesAsync<Alpha, Beta>(
            streams, ShardIteratorType.TrimHorizon, onMappingFailure: (_, change) => skipped.Add((change.ShardId, change.SequenceNumber, change.Record.EventName))).ToListAsync();

        // Assert
        await Assert.That(skipped.Single()).IsEqualTo(("shard-7", (string?)"42", OperationType.Insert));
    }

    [Test]
    public async Task ReadChangesAsync_TableOpenedForAnEntity_ReadsOneTypeWithoutTypeArgumentsAndSeveralWithThem()
    {
        // Arrange
        var fake = Shop("Shop");
        fake.Put("Shop", Item.From(AlphaValue()));
        fake.Put("Shop", Item.From(BetaValue() with { Sk = "2" }));
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();
        var alphas = client.Table("Shop", "pk", "sk").Of<Alpha>();

        // Act: the one-type reading infers its type from the table, and a mixed reading names its types, the type of the table among them or not
        var one = Typed<Change<Alpha>>(alphas.ReadChangesAsync(streams, ShardIteratorType.TrimHorizon));
        var two = Typed<MixedChange<Alpha, Beta>>(alphas.ReadChangesAsync<Alpha, Beta>(streams, ShardIteratorType.TrimHorizon));
        var other = Typed<MixedChange<Beta, Gamma>>(alphas.ReadChangesAsync<Beta, Gamma>(streams, ShardIteratorType.TrimHorizon));
        var read = (
            (await one.Take(1).ToListAsync()).Single().After!.Name,
            string.Join(",", (await two.Take(2).ToListAsync()).Select(static change => change.Match(static _ => "A", static _ => "B"))),
            (await other.Take(1).ToListAsync()).Single().TryGetValue(out Change<Beta>? beta) && beta.After!.Amount == 7);

        // Assert
        await Assert.That(read).IsEqualTo(("first", "A,B", true));
    }

    [Test]
    public async Task ReadChangesAsync_TableOpenedForAnEntity_ReadsThreeAndFourTypes()
    {
        // Arrange
        var fake = Shop("Shop");
        fake.Put("Shop", Item.From(AlphaValue()));
        fake.Put("Shop", Item.From(BetaValue() with { Sk = "2" }));
        fake.Put("Shop", Item.From(new Gamma { Pk = "a", Sk = "3" }));
        fake.Put("Shop", Item.From(new Delta { Pk = "a", Sk = "4" }));
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();
        var gammas = client.Table("Shop", "pk", "sk").Of<Gamma>();

        // Act
        var three = await gammas.ReadChangesAsync<Alpha, Beta, Gamma>(streams, ShardIteratorType.TrimHorizon).Take(3).ToListAsync();
        var four = await gammas.ReadChangesAsync<Alpha, Beta, Gamma, Delta>(streams, ShardIteratorType.TrimHorizon).Take(4).ToListAsync();

        // Assert
        await Assert.That((
            string.Join(",", three.Select(static change => change.Match(static _ => "A", static _ => "B", static _ => "G"))),
            string.Join(",", four.Select(static change => change.Match(static _ => "A", static _ => "B", static _ => "G", static _ => "D"))))).IsEqualTo(("A,B,G", "A,B,G,D"));
    }

    [Test]
    public async Task ReadChangesAsync_SkippedChanges_CountOneEachWithTheCallOfTheRecordAndTheTable()
    {
        // Arrange
        var table = TestClient.UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.SkippedItemsMetric);
        var fake = Shop(table);
        fake.Put(table, BrokenBeta);
        fake.Put(table, Item.From(BetaValue() with { Sk = "2" }));
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();

        // Act
        await client.Table(table, "pk", "sk").Of<Beta>().ReadChangesAsync(streams, ShardIteratorType.TrimHorizon, onMappingFailure: static (_, _) => { }).Take(1).ToListAsync();

        // Assert
        await Assert.That((counters.All.Count, counters.All[0].Unit, counters.All[0].TagText)).IsEqualTo((
            1,
            "{item}",
            $"db.collection.name={table},db.operation.name=GetRecords,server.address=streams.dynamodb.us-east-1.amazonaws.com"));
    }

    [Test]
    public async Task ReadChangesAsync_TypesThatCannotBeToldApart_ThrowsArgumentExceptionAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        using var streams = TestClient.CreateStreams(handler);

        // Act
        var act = () => client.Table("Shop", "pk", "sk").ReadChangesAsync<Alpha, AlphaTwin>(streams, ShardIteratorType.TrimHorizon);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task ReadChangesAsync_ThreeOrFourTypesThatCannotBeToldApart_ThrowArgumentExceptionAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        using var streams = TestClient.CreateStreams(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failures = new[]
        {
            Thrown(() => shop.ReadChangesAsync<Alpha, Beta, AlphaTwin>(streams, ShardIteratorType.TrimHorizon)),
            Thrown(() => shop.ReadChangesAsync<Alpha, Beta, Gamma, AlphaTwin>(streams, ShardIteratorType.TrimHorizon)),
        };

        // Assert
        await Assert.That(failures.All(static failure => failure is not null)).IsTrue();
    }

    [Test]
    public async Task ReadChangesAsync_NullTableOrStreams_ThrowsArgumentNullException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        using var streams = TestClient.CreateStreams(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var names = new[]
        {
            Capture(() => TableStreamExtensions.ReadChangesAsync<Alpha>(null!, streams, ShardIteratorType.TrimHorizon)),
            Capture(() => shop.Of<Alpha>().ReadChangesAsync(null!, ShardIteratorType.TrimHorizon)),
            Capture(() => shop.ReadChangesAsync<Alpha, Beta>(null!, ShardIteratorType.TrimHorizon)),
            Capture(() => shop.ReadChangesAsync<Alpha, Beta, Gamma>(null!, ShardIteratorType.TrimHorizon)),
            Capture(() => shop.ReadChangesAsync<Alpha, Beta, Gamma, Delta>(null!, ShardIteratorType.TrimHorizon)),
        };

        // Assert
        await Assert.That(string.Join(",", names)).IsEqualTo("table,streams,streams,streams,streams");
    }

    [Test]
    public async Task ReadChangesAsync_PositionOutOfRange_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        using var streams = TestClient.CreateStreams(handler);

        // Act
        var act = () => client.Table("Shop", "pk", "sk").Of<Alpha>().ReadChangesAsync(streams, ShardIteratorType.AtSequenceNumber);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    // Pins at compile time which reading a call binds to.
    // Moves the clock through the waits that are due until the task completes, and fails the test after ten seconds of real time instead of spinning.
    private static async Task AdvanceUntilAsync(ManualTimeProvider time, Task done)
    {
        await UntilAsync(() =>
        {
            if (time.NextDueIn is { } dueIn)
            {
                time.Advance(dueIn);
            }

            return done.IsCompleted;
        });
        await done;
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (!condition())
        {
            if (TimeProvider.System.GetElapsedTime(started) > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException("The condition did not hold within ten seconds.");
            }

            await Task.Yield();
        }
    }

    private static IAsyncEnumerable<TChange> Typed<TChange>(IAsyncEnumerable<TChange> changes) => changes;

    private static ArgumentException? Thrown(Action act)
    {
        try
        {
            act();
            return null;
        }
        catch (ArgumentException failure)
        {
            return failure;
        }
    }

    private static string? Capture(Action act)
    {
        try
        {
            act();
            return null;
        }
        catch (ArgumentNullException failure)
        {
            return failure.ParamName;
        }
    }

    // DescribeTable finds the stream on, and DescribeStream finds it switched off, with its one shard read to its end.
    private static StubHandler SwitchedOffAfterTheStart()
    {
        var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json($$$"""{"Table":{"TableName":"Users","StreamSpecification":{"StreamEnabled":true,"StreamViewType":"KEYS_ONLY"},"LatestStreamArn":"{{{TestClient.StreamArn}}}"}}"""));
        handler.Enqueue(StubResponse.StreamDescription("Users", StreamStatus.Disabled, StubResponse.Shard("shard-1", endingSequenceNumber: "9")));
        handler.Enqueue(StubResponse.ShardIterator("iterator-1"));
        handler.Enqueue(StubResponse.Records(null, StubResponse.Change(OperationType.Insert, new Item { ["pk"] = "k" })));
        handler.Enqueue(StubResponse.StreamDescription("Users", StreamStatus.Disabled));
        return handler;
    }

    private static void SetStream(FakeDynamoDb fake, bool enabled)
    {
        var specification = enabled ? """{"StreamEnabled":true,"StreamViewType":"NEW_AND_OLD_IMAGES"}""" : """{"StreamEnabled":false}""";
        FakeCalls.Ok(fake, "UpdateTable", $$"""{"TableName":"Users","StreamSpecification":{{specification}}}""");
    }

    private static decimal Value(ShardChangeRecord change) => change.Record.DynamoDb!.NewImage!["v"].AsNumber<decimal>();
}
