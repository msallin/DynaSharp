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
        await RunOnTheClockAsync(reading, time, TimeSpan.FromMinutes(5));

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

    // Moves the clock to each wait the reading starts, until it ends or the clock moved by the limit.
    private static async Task RunOnTheClockAsync(Task reading, ManualTimeProvider time, TimeSpan limit)
    {
        var moved = TimeSpan.Zero;
        while (!reading.IsCompleted)
        {
            if (time.NextDueIn is { } dueIn && dueIn < TimeSpan.FromMinutes(10))
            {
                moved += dueIn;
                if (moved > limit)
                {
                    throw new TimeoutException("The reading did not end on the clock.");
                }

                time.Advance(dueIn);
            }
            else
            {
                await Task.Yield();
            }
        }
    }
}
