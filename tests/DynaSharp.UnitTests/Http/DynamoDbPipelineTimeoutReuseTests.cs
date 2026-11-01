using DynaSharp.Credentials;
using DynaSharp.Requests;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Http;

// The pipeline reuses the cancellation token source of an attempt. These tests all run on the system clock: the base class library resets
// only sources whose timer it made itself, so with any other TimeProvider no source is ever reused. The credentials lookup receives the
// token of the attempt itself, which lets a test see which source an attempt ran on without reflection.
public sealed class DynamoDbPipelineTimeoutReuseTests
{
    [Test]
    public async Task ExecuteAsync_SecondCallAfterTheFirst_RunsOnTheSameTimeoutSource()
    {
        // Arrange
        var credentials = new RecordingCredentialsProvider();
        using var handler = new StubHandler(_ => Success());
        using var client = TestClient.Create(handler, credentials: credentials);
        await client.GetItemAsync(TestClient.GetItem());

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(credentials.Tokens[1]).IsEqualTo(credentials.Tokens[0]);
    }

    [Test]
    public async Task ExecuteAsync_RetryAfterAFailedAttempt_RunsOnTheSameTimeoutSource()
    {
        // Arrange
        var credentials = new RecordingCredentialsProvider();
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(Success());
        using var client = TestClient.Create(handler, credentials: credentials, maxAttempts: 2, random: FixedRandom.Zero);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(credentials.Tokens[1]).IsEqualTo(credentials.Tokens[0]);
    }

    [Test]
    public async Task ExecuteAsync_RetryAfterAFailedAttempt_FindsTheSourceNotCancelled()
    {
        // Arrange
        var credentials = new RecordingCredentialsProvider();
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(Success());
        using var client = TestClient.Create(handler, credentials: credentials, maxAttempts: 2, random: FixedRandom.Zero);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(credentials.CancelledOnArrival).IsEquivalentTo([false, false]);
    }

    [Test]
    public async Task ExecuteAsync_CallerCancelsTheFirstCall_SecondCallRunsOnAnotherSource()
    {
        // Arrange
        var credentials = new RecordingCredentialsProvider();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var handler = new StubHandler(async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                arrived.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return Success();
        });
        using var client = TestClient.Create(handler, credentials: credentials);
        using var cancellation = new CancellationTokenSource();
        var first = client.GetItemAsync(TestClient.GetItem(), cancellation.Token);
        await arrived.Task;
        await cancellation.CancelAsync();
        await TestClient.CaptureAsync<OperationCanceledException>(() => first);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(credentials.Tokens[1]).IsNotEqualTo(credentials.Tokens[0]);
    }

    [Test]
    public async Task ExecuteAsync_CallerCancelsTheFirstCall_SecondCallFindsItsSourceNotCancelled()
    {
        // Arrange
        var credentials = new RecordingCredentialsProvider();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var handler = new StubHandler(async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                arrived.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return Success();
        });
        using var client = TestClient.Create(handler, credentials: credentials);
        using var cancellation = new CancellationTokenSource();
        var first = client.GetItemAsync(TestClient.GetItem(), cancellation.Token);
        await arrived.Task;
        await cancellation.CancelAsync();
        await TestClient.CaptureAsync<OperationCanceledException>(() => first);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(credentials.CancelledOnArrival[1]).IsFalse();
    }

    [Test]
    public async Task ExecuteAsync_CallerCancelsAfterTheCallCompleted_DoesNotCancelTheCallThatRunsOnItsSource()
    {
        // Arrange (the first call completes and hands its source back; the second runs on that source and waits in the handler)
        var credentials = new RecordingCredentialsProvider();
        var release = new TaskCompletionSource();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var handler = new StubHandler(async (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 2)
            {
                arrived.SetResult();
                await release.Task;
            }

            return Success();
        });
        using var client = TestClient.Create(handler, credentials: credentials);
        using var firstCaller = new CancellationTokenSource();
        await client.GetItemAsync(TestClient.GetItem(), firstCaller.Token);
        var second = client.GetItemAsync(TestClient.GetItem());
        await arrived.Task;

        // Act
        await firstCaller.CancelAsync();
        release.SetResult();

        // Assert
        await Assert.That(async () => await second).ThrowsNothing();
    }

    [Test]
    public async Task Dispose_AfterACall_DisposesTheIdleTimeoutSource()
    {
        // Arrange
        var credentials = new RecordingCredentialsProvider();
        using var handler = new StubHandler(_ => Success());
        var client = TestClient.Create(handler, credentials: credentials);
        await client.GetItemAsync(TestClient.GetItem());

        // Act
        client.Dispose();

        // Assert
        await Assert.That(IsDisposed(credentials.Tokens[0])).IsTrue();
    }

    [Test]
    public async Task ExecuteAsync_CallFinishesAfterTheClientIsDisposed_DisposesItsTimeoutSource()
    {
        // Arrange
        var credentials = new RecordingCredentialsProvider();
        var release = new TaskCompletionSource();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, _) =>
        {
            arrived.SetResult();
            await release.Task;
            return Success();
        });
        var client = TestClient.Create(handler, credentials: credentials);
        var call = client.GetItemAsync(TestClient.GetItem());
        await arrived.Task;
        client.Dispose();

        // Act
        release.SetResult();
        await call;

        // Assert
        await Assert.That(IsDisposed(credentials.Tokens[0])).IsTrue();
    }

    [Test]
    public async Task ExecuteAsync_MoreCallsAtOnceThanThePoolHolds_DisposesTheSurplusSource()
    {
        // Arrange
        const int PoolCapacity = 64;
        var credentials = new RecordingCredentialsProvider();
        var release = new TaskCompletionSource();
        using var arrivals = new CountdownEvent(PoolCapacity + 1);
        var allArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, _) =>
        {
            if (arrivals.Signal())
            {
                allArrived.SetResult();
            }

            await release.Task;
            return Success();
        });
        using var client = TestClient.Create(handler, credentials: credentials);
        var calls = Enumerable.Range(0, PoolCapacity + 1).Select(_ => client.GetItemAsync(TestClient.GetItem())).ToArray();
        await allArrived.Task;

        // Act
        release.SetResult();
        await Task.WhenAll(calls);

        // Assert
        await Assert.That(credentials.Tokens.Count(IsDisposed)).IsEqualTo(1);
    }

    private static bool IsDisposed(CancellationToken token)
    {
        try
        {
            _ = token.WaitHandle;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    // Hands out the same credentials and notes the token of every lookup, and whether it was cancelled by then.
    private sealed class RecordingCredentialsProvider : ICredentialsProvider
    {
        private readonly Lock _gate = new();
        private readonly List<CancellationToken> _tokens = [];
        private readonly List<bool> _cancelledOnArrival = [];

        public IReadOnlyList<CancellationToken> Tokens
        {
            get
            {
                lock (_gate)
                {
                    return [.. _tokens];
                }
            }
        }

        public IReadOnlyList<bool> CancelledOnArrival
        {
            get
            {
                lock (_gate)
                {
                    return [.. _cancelledOnArrival];
                }
            }
        }

        public ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _tokens.Add(cancellationToken);
                _cancelledOnArrival.Add(cancellationToken.IsCancellationRequested);
            }

            return new ValueTask<AwsCredentials>(TestClient.Credentials);
        }
    }
}
