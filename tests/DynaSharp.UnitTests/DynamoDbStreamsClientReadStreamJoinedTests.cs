using System.Net;
using DynaSharp.Streams;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.StreamScript;

namespace DynaSharp.UnitTests;

/// <summary>
/// <see cref="ReadStreamRequest.OnJoined"/>: once, when every shard the reading starts with holds an iterator, and not before, not again and not after a failure.
/// </summary>
public sealed class DynamoDbStreamsClientReadStreamJoinedTests
{
    [Test]
    public async Task ReadStreamAsync_Latest_CallsItOnceEveryOpenShardHoldsAnIterator()
    {
        // Arrange: a reads a page while b waits for its iterator
        var aRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bPositioned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Enabled, null, Open("a"), Open("b"), Closed("c")));
        script.Answer("iterator:a", StubResponse.ShardIterator("a/0"));
        script.Answer("records:a/0", _ =>
        {
            aRead.TrySetResult();
            return Task.FromResult(StubResponse.Records(null, Insert("a-1")));
        });
        script.Answer("iterator:b", async _ =>
        {
            await bPositioned.Task;
            return StubResponse.ShardIterator("b/0");
        });
        script.Answer("records:b/0", StubResponse.Records(null, Insert("b-1")));
        using var streams = TestClient.CreateStreams(script.Handler);
        var calls = 0;
        var reading = streams.ReadStreamAsync(Latest() with { OnJoined = () => Interlocked.Increment(ref calls) }).ToListAsync().AsTask();
        await aRead.Task;
        var beforeB = Volatile.Read(ref calls);

        // Act
        bPositioned.SetResult();
        await reading;

        // Assert
        await Assert.That((beforeB, calls)).IsEqualTo((0, 1));
    }

    [Test]
    public async Task ReadStreamAsync_TrimHorizon_CallsItOnceTheShardsWithoutAListedParentHoldIterators()
    {
        // Arrange: the child is read after its parent and does not count. By the call the reader of p may already have read on into the child, so the
        // call is asserted to come once p holds its iterator, not to come before the child holds one.
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Disabled, null, Closed("p"), Closed("c", "p")));
        script.ClosedShard("p", "p-1");
        script.Answer("children:p", Description(StreamStatus.Disabled, null, Closed("c", "p")));
        script.ClosedShard("c", "c-1");
        using var streams = TestClient.CreateStreams(script.Handler);
        var firstPositionAtTheCall = new List<string?>();
        var request = FromTrimHorizon() with { OnJoined = () => firstPositionAtTheCall.Add(Positions(script.Handler).FirstOrDefault()) };

        // Act
        await streams.ReadStreamAsync(request).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", firstPositionAtTheCall)).IsEqualTo("p:TRIM_HORIZON");
    }

    [Test]
    public async Task ReadStreamAsync_NoShardToStartWith_CallsItOnceTheStreamIsListed()
    {
        // Arrange: under Latest a closed shard has nothing to read from now on
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Disabled, null, Closed("p")));
        using var streams = TestClient.CreateStreams(script.Handler);
        var calls = 0;

        // Act
        await streams.ReadStreamAsync(Latest() with { OnJoined = () => calls++ }).ToListAsync();

        // Assert
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task ReadStreamAsync_ShardWithoutAnIterator_CountsAsHoldingItsPosition()
    {
        // Arrange: GetShardIterator answers without one, so the shard has nothing to read
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Disabled, null, Open("o")));
        script.Answer("iterator:o", StubResponse.Json("{}"));
        using var streams = TestClient.CreateStreams(script.Handler);
        var calls = 0;

        // Act
        await streams.ReadStreamAsync(Latest() with { OnJoined = () => calls++ }).ToListAsync();

        // Assert
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task ReadStreamAsync_FirstIteratorFailsTransiently_CallsItOnceTheIteratorCame()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Disabled, null, Open("o")));
        script.Answer("iterator:o", StubResponse.DynamoDbError("InternalServerError", "Failure of the service.", HttpStatusCode.InternalServerError), StubResponse.ShardIterator("o/0"));
        script.Answer("records:o/0", StubResponse.Records(null, Insert("o-1")));
        using var streams = TestClient.CreateStreams(script.Handler, random: FixedRandom.Zero);
        var iteratorsAtTheCall = new List<int>();
        var request = Latest() with { OnJoined = () => iteratorsAtTheCall.Add(Positions(script.Handler).Count()) };

        // Act
        await streams.ReadStreamAsync(request).ToListAsync();

        // Assert
        await Assert.That(string.Join(",", iteratorsAtTheCall)).IsEqualTo("2");
    }

    [Test]
    public async Task ReadStreamAsync_ListingFails_NeverCallsIt()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", StubResponse.DynamoDbError("ValidationException", "Failure of the listing."));
        using var streams = TestClient.CreateStreams(script.Handler);
        var calls = 0;

        // Act
        await TestClient.CaptureAsync<ValidationException>(() => streams.ReadStreamAsync(Latest() with { OnJoined = () => calls++ }).ToListAsync().AsTask());

        // Assert
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    public async Task ReadStreamAsync_ShardFailsBeforeItHoldsAnIterator_NeverCallsIt()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Disabled, null, Open("o")));
        script.Answer("iterator:o", StubResponse.DynamoDbError("AccessDeniedException", "Not allowed."));
        using var streams = TestClient.CreateStreams(script.Handler);
        var calls = 0;

        // Act
        await TestClient.CaptureAsync<AccessDeniedException>(() => streams.ReadStreamAsync(Latest() with { OnJoined = () => calls++ }).ToListAsync().AsTask());

        // Assert
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    public async Task ReadStreamAsync_CallbackThrows_ThrowsItsException()
    {
        // Arrange
        var script = new StreamScript();
        script.Answer("list:", Description(StreamStatus.Disabled, null, Open("o")));
        script.ClosedShard("o", "o-1");
        using var streams = TestClient.CreateStreams(script.Handler);
        var request = Latest() with { OnJoined = static () => throw new InvalidOperationException("Failure of the callback.") };

        // Act
        var act = async () => await streams.ReadStreamAsync(request).ToListAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    private static ReadStreamRequest FromTrimHorizon() => new() { StreamArn = TestClient.StreamArn, ShardIteratorType = ShardIteratorType.TrimHorizon };

    private static ReadStreamRequest Latest() => FromTrimHorizon() with { ShardIteratorType = ShardIteratorType.Latest };
}
