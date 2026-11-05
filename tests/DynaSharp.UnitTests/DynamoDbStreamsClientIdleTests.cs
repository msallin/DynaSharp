using System.Diagnostics;
using DynaSharp.Streams;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Testing.Fake;

namespace DynaSharp.UnitTests;

/// <summary>
/// The waits of a reader of <see cref="DynamoDbStreamsClient"/> on an idle stream of a <see cref="FakeDynamoDb"/>, whose client ends them at the next change of a stream: on a clock
/// that the test never moves a change is all that can end them.
/// </summary>
public sealed class DynamoDbStreamsClientIdleTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ReadShardAsync_WriteWhileTheReaderWaitsOnAnEmptyPage_ReadsItWithoutMovingTheClock()
    {
        // Arrange
        var time = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(time);
        using var streams = fake.CreateStreamsClient();
        await using var records = streams.ReadShardAsync(Latest()).GetAsyncEnumerator();
        var next = records.MoveNextAsync().AsTask();
        await WaitingAsync(time);

        // Act
        FakeStreams.Put(fake, "a", 1);
        await next;

        // Assert
        await Assert.That(records.Current.DynamoDb!.Keys!["pk"].AsString()).IsEqualTo("a");
    }

    [Test]
    public async Task ReadShardAsync_WaitEndedByAChange_LeavesNoTimerArmed()
    {
        // Arrange
        var time = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(time);
        using var streams = fake.CreateStreamsClient();
        await using var records = streams.ReadShardAsync(Latest()).GetAsyncEnumerator();
        var next = records.MoveNextAsync().AsTask();
        await WaitingAsync(time);

        // Act
        FakeStreams.Put(fake, "a", 1);
        await next;

        // Assert
        await Assert.That(time.ArmedTimers).IsEqualTo(0);
    }

    [Test]
    public async Task ReadShardAsync_NoChange_WaitsTheWholeDelayOnTheClockAndAsksAgain()
    {
        // Arrange: the reading waits on a clock that stops, and is left to the collector
        var time = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(time);
        using var streams = fake.CreateStreamsClient();
        _ = streams.ReadShardAsync(Latest() with { EmptyPageDelay = TimeSpan.FromSeconds(2) }).ToListAsync().AsTask();
        await WaitingAsync(time);

        // Act
        time.Advance(TimeSpan.FromSeconds(2));
        while (time.TimersCreated < 2 || time.ArmedTimers == 0)
        {
            await Task.Yield();
        }

        // Assert
        await Assert.That(time.NextDueIn).IsEqualTo(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task ReadShardAsync_CancelledWhileItWaitsOnTheFake_ThrowsOperationCanceledException()
    {
        // Arrange
        var time = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(time);
        using var streams = fake.CreateStreamsClient();
        using var cancellation = new CancellationTokenSource();
        await using var records = streams.ReadShardAsync(Latest(), cancellation.Token).GetAsyncEnumerator();
        var next = records.MoveNextAsync().AsTask();
        await WaitingAsync(time);

        // Act
        await cancellation.CancelAsync();

        // Assert
        await Assert.That(async () => await next).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task ReadStreamAsync_LatestAfterASplit_EndsAtTheDisableWithoutMovingTheClock()
    {
        // Arrange: the parent closed at the start is watched for more children, every five seconds, and the children wait on empty pages
        var time = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(time);
        fake.SplitStreamShards("Users");
        using var streams = fake.CreateStreamsClient();
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new ReadStreamRequest { StreamArn = FakeStreams.UsersArn, ShardIteratorType = ShardIteratorType.Latest, OnJoined = () => joined.TrySetResult() };
        var reading = streams.ReadStreamAsync(request).ToListAsync().AsTask();
        await joined.Task;
        FakeStreams.Put(fake, "a", 1);

        // Act
        fake.DisableStream("Users");

        // Assert
        await Assert.That((await reading).Count).IsEqualTo(1);
    }

    [Test]
    public async Task ReadStreamAsync_WriteOnTheSystemClock_IsReadWellWithinTheEmptyPageDelay()
    {
        // Arrange: the default EmptyPageDelay of a second, on the clock of the system
        var fake = FakeStreams.UsersOn(TimeProvider.System);
        var arn = fake.DescribeTable("Users").LatestStreamArn!;
        using var streams = fake.CreateStreamsClient();
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new ReadStreamRequest { StreamArn = arn, ShardIteratorType = ShardIteratorType.Latest, OnJoined = () => joined.TrySetResult() };
        await using var records = streams.ReadStreamAsync(request).GetAsyncEnumerator();
        var next = records.MoveNextAsync().AsTask();
        await joined.Task;
        var clock = Stopwatch.StartNew();

        // Act
        FakeStreams.Put(fake, "a", 1);
        await next;

        // Assert
        await Assert.That(clock.Elapsed).IsLessThan(request.EmptyPageDelay);
    }

    private static ReadShardRequest Latest() => new() { StreamArn = FakeStreams.UsersArn, ShardId = FakeStreams.FirstShard, ShardIteratorType = ShardIteratorType.Latest };

    // Until the reader waits after an empty page.
    private static async Task WaitingAsync(ManualTimeProvider time)
    {
        while (time.ArmedTimers == 0)
        {
            await Task.Yield();
        }
    }
}
