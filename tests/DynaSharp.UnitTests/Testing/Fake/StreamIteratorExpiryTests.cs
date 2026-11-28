using System.Net;
using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Testing.Fake;

// FakeDynamoDb.ExpireStreamIterators: what 15 minutes on the clock do to the iterators, and nothing else, on the wire and for a reading that renews them.
public sealed class StreamIteratorExpiryTests
{
    private const string Expired = "com.amazonaws.dynamodb.v20120810#ExpiredIteratorException";

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExpireStreamIterators_IteratorHandedOutBefore_AnswersExpiredIterator(bool ofGetRecords)
    {
        // Arrange: an iterator of GetShardIterator, or the next one of a page, on a clock that does not move.
        var fake = FakeStreams.UsersOn(new ManualTimeProvider(Start));
        var iterator = FakeStreams.Iterator(fake, "TRIM_HORIZON");
        if (ofGetRecords)
        {
            iterator = FakeStreams.Page(fake, iterator).GetProperty("NextShardIterator").GetString()!;
        }

        // Act
        fake.ExpireStreamIterators();

        // Assert
        var (status, body) = FakeCalls.Send(fake, "GetRecords", $$"""{"ShardIterator":"{{iterator}}"}""");
        await Assert.That((status, body.GetProperty("__type").GetString())).IsEqualTo((HttpStatusCode.BadRequest, Expired));
    }

    [Test]
    public async Task ExpireStreamIterators_IteratorTakenAfterwards_ReadsTheRecordsOfBefore()
    {
        // Arrange: a record that 15 minutes leave in the shard, and no record is trimmed by the expiry either.
        var fake = FakeStreams.UsersOn(new ManualTimeProvider(Start));
        FakeStreams.Put(fake, "a", 1);

        // Act
        fake.ExpireStreamIterators();

        // Assert
        await Assert.That(FakeStreams.Names(FakeStreams.Records(fake))).IsEquivalentTo(["INSERT"]);
    }

    [Test]
    public async Task ExpireStreamIterators_LatestReadingThatHandedOutNothing_CallsOnGapOnceAndReadsOn()
    {
        // Arrange: the reading idles on an empty shard with an iterator of GetRecords.
        var time = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(time);
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();
        var gaps = 0;
        var gapped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = client.Table("Users", "pk")
            .ReadStreamAsync(streams, ShardIteratorType.Latest, onGap: _ =>
            {
                Interlocked.Increment(ref gaps);
                gapped.TrySetResult();
            })
            .Take(1)
            .ToListAsync()
            .AsTask();
        await UntilAsync(() => time.NextDueIn is not null);

        // Act: the write comes once the reading idles again, at its new position.
        fake.ExpireStreamIterators();
        await gapped.Task;
        await UntilAsync(() => time.NextDueIn is not null);
        FakeStreams.Put(fake, "after", 1);

        // Assert
        await Assert.That(((await reading).Single().Record.DynamoDb!.Keys!["pk"].AsString(), Volatile.Read(ref gaps))).IsEqualTo(("after", 1));
    }

    [Test]
    public async Task ExpireStreamIterators_TrimHorizonReading_RenewsFromTheStartWithoutAGap()
    {
        // Arrange: the reading idles on an empty shard, from its start.
        var time = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(time);
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();
        var gaps = 0;
        var reading = client.Table("Users", "pk")
            .ReadStreamAsync(streams, ShardIteratorType.TrimHorizon, onGap: _ => Interlocked.Increment(ref gaps))
            .Take(1)
            .ToListAsync()
            .AsTask();
        await UntilAsync(() => time.NextDueIn is not null);

        // Act
        fake.ExpireStreamIterators();
        FakeStreams.Put(fake, "after", 1);

        // Assert
        await Assert.That(((await reading).Single().Record.DynamoDb!.Keys!["pk"].AsString(), Volatile.Read(ref gaps))).IsEqualTo(("after", 0));
    }

    [Test]
    public async Task ExpireStreamIterators_LatestReadingThatHandedOutARecord_GoesOnAfterItWithoutAGap()
    {
        // Arrange: the reading has read a and asked for the page after it, so it holds an iterator of GetRecords behind a.
        var time = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(time);
        using var client = fake.CreateClient();
        using var streams = fake.CreateStreamsClient();
        var gaps = 0;
        var reading = client.Table("Users", "pk")
            .ReadStreamAsync(streams, ShardIteratorType.Latest, onGap: _ => Interlocked.Increment(ref gaps))
            .Take(2)
            .ToListAsync()
            .AsTask();
        await UntilAsync(() => time.NextDueIn is not null);
        FakeStreams.Put(fake, "a", 1);
        await UntilAsync(() => fake.Requests.Count(static request => request.Operation == "GetRecords") >= 3);

        // Act
        fake.ExpireStreamIterators();
        FakeStreams.Put(fake, "b", 2);

        // Assert
        var keys = (await reading).Select(static change => change.Record.DynamoDb!.Keys!["pk"].AsString());
        await Assert.That((string.Join(",", keys), Volatile.Read(ref gaps))).IsEqualTo(("a,b", 0));
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
}
