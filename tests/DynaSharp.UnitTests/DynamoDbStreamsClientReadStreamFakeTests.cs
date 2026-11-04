using DynaSharp.Streams;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Testing.Fake;

namespace DynaSharp.UnitTests;

/// <summary>
/// <see cref="DynamoDbStreamsClient.ReadStreamAsync"/> over a <see cref="FakeDynamoDb"/> whose shards split, on a clock the test moves: the lineage, a child listed late and
/// a child trimmed before its turn, end to end through the wire.
/// </summary>
public sealed class DynamoDbStreamsClientReadStreamFakeTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] Keys = ["k0", "k1", "k2", "k3", "k4", "k5", "k6", "k7"];

    [Test]
    public async Task ReadStreamAsync_ShardSplitInTwo_ReturnsTheChangesOfEachItemInOrder()
    {
        // Arrange: every item changes before and after the split, and the stream is then switched off, so the reading ends
        var time = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(time);
        PutAll(fake, 1);
        fake.SplitStreamShards("Users");
        PutAll(fake, 2);
        Disable(fake);
        using var streams = fake.CreateStreamsClient(options => options with { TimeProvider = time });

        // Act
        var records = await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(Changes(records)).IsEqualTo(string.Join(",", Keys.Select(static key => key + ":1>2")));
    }

    [Test]
    public async Task ReadStreamAsync_ShardSplitInTwo_ReadsBothChildren()
    {
        // Arrange
        var time = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(time);
        fake.SplitStreamShards("Users");
        PutAll(fake, 1);
        Disable(fake);
        using var streams = fake.CreateStreamsClient(options => options with { TimeProvider = time });

        // Act
        var records = await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(records.Select(static change => change.ShardId).Distinct().Count()).IsEqualTo(2);
    }

    [Test]
    public async Task ReadStreamAsync_SecondChildListedLate_ReadsItsRecords()
    {
        // Arrange: the stream stays on, so the reading is stopped after every record has come
        var time = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(time);
        PutAll(fake, 1);
        fake.SplitStreamShards("Users", secondChildListedAfter: TimeSpan.FromSeconds(3));
        PutAll(fake, 2);
        using var streams = fake.CreateStreamsClient(options => options with { TimeProvider = time });
        var reading = streams.ReadStreamAsync(FromTrimHorizon()).Take(2 * Keys.Length).ToListAsync().AsTask();

        // Act
        await RunOnTheClockAsync(reading, time, TimeSpan.FromMinutes(5), waiting: 2);

        // Assert
        await Assert.That(Changes(await reading)).IsEqualTo(string.Join(",", Keys.Select(static key => key + ":1>2")));
    }

    [Test]
    [Arguments(0, 2)]
    [Arguments(30, 1)]
    public async Task ReadStreamAsync_LatestDuringASplitWhoseSecondChildIsListedLate_ReadsTheWritesOfBothChildren(int secondChildListedAfter, int listedAtTheStart)
    {
        // Arrange: the split happens before the start, and the items are written once the readers of the children listed at the start hold their iterators
        var time = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(time);
        fake.SplitStreamShards("Users", secondChildListedAfter: TimeSpan.FromSeconds(secondChildListedAfter));
        var positioned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var iterators = 0;
        using var handler = new StubHandler((request, cancellationToken) =>
        {
            var answer = fake.Answer(request);
            if (request.Operation == "GetShardIterator" && Interlocked.Increment(ref iterators) == listedAtTheStart)
            {
                positioned.TrySetResult();
            }

            return Task.FromResult(answer);
        });
        using var streams = StubClient.CreateStreams(handler, options => options with { TimeProvider = time });
        var latest = FromTrimHorizon() with { ShardIteratorType = ShardIteratorType.Latest };
        var reading = streams.ReadStreamAsync(latest).Take(2 * Keys.Length).ToListAsync().AsTask();
        await positioned.Task;
        PutAll(fake, 1);
        PutAll(fake, 2);

        // Act
        await RunOnTheClockAsync(reading, time, TimeSpan.FromMinutes(2), waiting: 2);

        // Assert
        await Assert.That(Changes(await reading)).IsEqualTo(string.Join(",", Keys.Select(static key => key + ":1>2")));
    }

    [Test]
    public async Task CreateStreamsClient_NoOptions_WaitsOnTheClockOfTheFake()
    {
        // Arrange
        var time = new ManualTimeProvider(Start);
        var fake = new FakeDynamoDb(time);

        // Act
        using var streams = fake.CreateStreamsClient();

        // Assert
        await Assert.That(streams.Options.TimeProvider).IsSameReferenceAs(time);
    }

    [Test]
    public async Task CreateStreamsClient_OptionsWithAClock_KeepThatClock()
    {
        // Arrange
        var fake = new FakeDynamoDb(new ManualTimeProvider(Start));
        var other = new ManualTimeProvider(Start);

        // Act
        using var streams = fake.CreateStreamsClient(options => options with { TimeProvider = other });

        // Assert
        await Assert.That(streams.Options.TimeProvider).IsSameReferenceAs(other);
    }

    [Test]
    public async Task ReadStreamAsync_ChildTrimmedWhileItsParentWasRead_ThrowsTrimmedDataAccessException()
    {
        // Arrange: a minute before the records are 24 hours old the reading starts, and the look for the children of the parent takes two minutes
        var time = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(time);
        FakeStreams.Put(fake, "parent", 1);
        fake.SplitStreamShards("Users", children: 1);
        FakeStreams.Put(fake, "child", 1);
        time.Advance(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1));
        using var handler = new StubHandler((request, cancellationToken) =>
        {
            var answer = fake.Answer(request);
            if (request.Operation == "DescribeStream" && request.BodyJson.TryGetProperty("ShardFilter", out _))
            {
                time.Advance(TimeSpan.FromMinutes(2));
            }

            return Task.FromResult(answer);
        });
        using var streams = StubClient.CreateStreams(handler, options => options with { TimeProvider = time });

        // Act
        var act = async () => await streams.ReadStreamAsync(FromTrimHorizon()).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<TrimmedDataAccessException>();
    }

    private static ReadStreamRequest FromTrimHorizon() => new() { StreamArn = FakeStreams.UsersArn, ShardIteratorType = ShardIteratorType.TrimHorizon };

    private static void PutAll(FakeDynamoDb fake, int v)
    {
        foreach (var key in Keys)
        {
            FakeStreams.Put(fake, key, v);
        }
    }

    private static void Disable(FakeDynamoDb fake) =>
        FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Users","StreamSpecification":{"StreamEnabled":false}}""");

    // Per item, in key order, the values of its changes in the order they came: k0:1>2 for an insert of 1 and a change to 2.
    private static string Changes(IEnumerable<ShardChangeRecord> records) => string.Join(
        ",",
        records
            .GroupBy(static change => change.Record.DynamoDb!.Keys!["pk"].AsString())
            .OrderBy(static item => item.Key, StringComparer.Ordinal)
            .Select(static item => item.Key + ":" + string.Join(">", item.Select(static change => change.Record.DynamoDb!.NewImage!["v"].AsNumber()))));

    // Moves the clock to the next wait of the reading, but only once as many waits as the test names are armed: the reader of an open shard waits again at
    // once, on the thread that moved the clock, and would otherwise run the clock away from a reader that the pool has not run yet.
    private static async Task RunOnTheClockAsync(Task reading, ManualTimeProvider time, TimeSpan limit, int waiting)
    {
        var moved = TimeSpan.Zero;
        while (!reading.IsCompleted)
        {
            if (time.ArmedTimers >= waiting && time.NextDueIn is { } dueIn)
            {
                moved += dueIn;
                if (moved > limit)
                {
                    throw new TimeoutException("The reading did not end on the clock.");
                }

                time.Advance(dueIn < TimeSpan.Zero ? TimeSpan.Zero : dueIn);
            }
            else
            {
                await Task.Yield();
            }
        }
    }
}
