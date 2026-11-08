using DynaSharp.Streams;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.StreamScript;

namespace DynaSharp.UnitTests;

// A reader must not resume on the context of its caller: a caller that blocks on a step of the enumeration (.Result on a UI thread) would deadlock. Each test
// starts one step of the enumeration under a counting context and makes one call of that step wait, so that the await of it is the first that suspends.
public sealed class DynamoDbStreamsClientContextTests
{
    [Test]
    public async Task ReadShardAsync_FirstIteratorArrivesLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var answers = new Answers();
        var iterator = answers.Later(StubResponse.ShardIterator("it-1"));
        answers.Now(StubResponse.Records(null, Insert("1")));
        using var streams = TestClient.CreateStreams(answers.Handler);
        await using var records = streams.ReadShardAsync(FromTrimHorizon()).GetAsyncEnumerator();
        var context = new CountingSynchronizationContext();
        var step = context.Start(() => records.MoveNextAsync().AsTask());

        // Act
        iterator.SetResult();
        await step;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task ReadShardAsync_PageArrivesLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var answers = new Answers();
        answers.Now(StubResponse.ShardIterator("it-1"));
        var page = answers.Later(StubResponse.Records(null, Insert("1")));
        using var streams = TestClient.CreateStreams(answers.Handler);
        await using var records = streams.ReadShardAsync(FromTrimHorizon()).GetAsyncEnumerator();
        var context = new CountingSynchronizationContext();
        var step = context.Start(() => records.MoveNextAsync().AsTask());

        // Act
        page.SetResult();
        await step;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task ReadShardAsync_RenewedIteratorArrivesLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange: the second page finds the iterator expired
        var answers = new Answers();
        answers.Now(StubResponse.ShardIterator("it-1"));
        answers.Now(StubResponse.Records("it-2", Insert("1")));
        answers.Now(StubResponse.DynamoDbError("ExpiredIteratorException", "Iterator expired."));
        var renewed = answers.Later(StubResponse.ShardIterator("it-3"));
        answers.Now(StubResponse.Records(null, Insert("2")));
        using var streams = TestClient.CreateStreams(answers.Handler);
        await using var records = streams.ReadShardAsync(FromTrimHorizon()).GetAsyncEnumerator();
        await records.MoveNextAsync();
        var context = new CountingSynchronizationContext();
        var step = context.Start(() => records.MoveNextAsync().AsTask());

        // Act
        renewed.SetResult();
        await step;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task ReadShardAsync_WaitAfterAnEmptyPage_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var answers = new Answers();
        answers.Now(StubResponse.ShardIterator("it-1"));
        answers.Now(StubResponse.Records("it-2"));
        answers.Now(StubResponse.Records(null, Insert("1")));
        using var streams = TestClient.CreateStreams(answers.Handler, time, attemptTimeout: Timeout.InfiniteTimeSpan);
        await using var records = streams.ReadShardAsync(FromTrimHorizon() with { EmptyPageDelay = TimeSpan.FromSeconds(1) }).GetAsyncEnumerator();
        var context = new CountingSynchronizationContext();
        var step = context.Start(() => records.MoveNextAsync().AsTask());

        // Act
        time.Advance(TimeSpan.FromSeconds(1));
        await step;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task ReadShardAsync_WaitEndedByAChange_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange: a server that tells of its changes, as the fake does
        var change = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answers = new Answers();
        answers.Now(StubResponse.ShardIterator("it-1"));
        answers.Now(StubResponse.Records("it-2"));
        answers.Now(StubResponse.Records(null, Insert("1")));
        using var streams = TestClient.CreateStreams(answers.Handler, new ManualTimeProvider(), attemptTimeout: Timeout.InfiniteTimeSpan);
        streams.NextStreamChange = () => change.Task;
        await using var records = streams.ReadShardAsync(FromTrimHorizon() with { EmptyPageDelay = TimeSpan.FromSeconds(1) }).GetAsyncEnumerator();
        var context = new CountingSynchronizationContext();
        var step = context.Start(() => records.MoveNextAsync().AsTask());

        // Act
        change.SetResult();
        await step;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task ReadShardAsync_ChangeBeforeTheWait_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange: the change came while the empty page was on its way, so only the stop of the timer can wait
        var answers = new Answers();
        answers.Now(StubResponse.ShardIterator("it-1"));
        answers.Now(StubResponse.Records("it-2"));
        answers.Now(StubResponse.Records(null, Insert("1")));
        using var streams = TestClient.CreateStreams(answers.Handler, new ManualTimeProvider(), attemptTimeout: Timeout.InfiniteTimeSpan);
        streams.NextStreamChange = static () => Task.CompletedTask;
        await using var records = streams.ReadShardAsync(FromTrimHorizon() with { EmptyPageDelay = TimeSpan.FromSeconds(1) }).GetAsyncEnumerator();
        var context = new CountingSynchronizationContext();

        // Act
        await context.Start(() => records.MoveNextAsync().AsTask());

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task ReadStreamAsync_ListingArrivesLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var listed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var script = new StreamScript();
        script.Answer("list:", async _ =>
        {
            await listed.Task.ConfigureAwait(false);
            return Description(StreamStatus.Disabled, null);
        });
        using var streams = TestClient.CreateStreams(script.Handler);
        await using var records = streams.ReadStreamAsync(StreamFromTrimHorizon()).GetAsyncEnumerator();
        var context = new CountingSynchronizationContext();
        var step = context.Start(() => records.MoveNextAsync().AsTask());

        // Act
        listed.SetResult();
        await step;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task ReadStreamAsync_DisposedWhileAShardIsRead_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange: the second page of the shard waits until the reading stops
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Open("open")));
        script.Answer("iterator:open", StubResponse.ShardIterator("open/0"));
        script.Answer("records:open/0", StubResponse.Records("open/1", Insert("open-1")));
        script.Answer("records:open/1", async cancellationToken =>
        {
            holding.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Not reached.");
        });
        using var streams = TestClient.CreateStreams(script.Handler);
        var records = streams.ReadStreamAsync(StreamFromTrimHorizon()).GetAsyncEnumerator();
        await records.MoveNextAsync();
        await holding.Task;
        var context = new CountingSynchronizationContext();

        // Act
        await context.Start(() => records.DisposeAsync().AsTask());

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    private static ReadShardRequest FromTrimHorizon() => new() { StreamArn = TestClient.StreamArn, ShardId = "shardId-1", ShardIteratorType = ShardIteratorType.TrimHorizon };

    private static ReadStreamRequest StreamFromTrimHorizon() => new() { StreamArn = TestClient.StreamArn, ShardIteratorType = ShardIteratorType.TrimHorizon };

    // The answers of a shard in the order of its calls: one at once, or one once the test releases it.
    private sealed class Answers
    {
        private readonly Queue<Func<Task<StubResponse>>> _answers = new();

        public Answers()
        {
            Handler = new StubHandler((_, _) => _answers.Dequeue()());
        }

        public StubHandler Handler { get; }

        public void Now(StubResponse response) => _answers.Enqueue(() => Task.FromResult(response));

        public TaskCompletionSource Later(StubResponse response)
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _answers.Enqueue(async () =>
            {
                await release.Task.ConfigureAwait(false);
                return response;
            });
            return release;
        }
    }
}
