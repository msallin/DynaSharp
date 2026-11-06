using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DynaSharp.Http;
using DynaSharp.Telemetry;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;
using DynaSharp.UnitTests.Telemetry;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Http;

// The seam of the pipeline for a layer that builds its own calls. SegmentCall has the shape of a command of the table layer: its body
// is already written, in a segment that belongs to a state the call gives back when it is released, and it reads its response
// itself. Nothing here goes through IOperation.
public sealed class WireCallTests
{
    [Test]
    public async Task SendAsync_CallWithItsOwnBody_SendsThatBody()
    {
        // Arrange
        using var handler = new StubHandler(_ => Success());
        using var client = TestClient.Create(handler);
        var state = new SegmentState("""{"TableName":"Users","Marker":"own-body"}""");

        // Act
        await client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None);

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).IsEqualTo("""{"TableName":"Users","Marker":"own-body"}""");
    }

    [Test]
    public async Task SendAsync_CallWithItsOwnBody_RentsNoArrayForTheRequest()
    {
        // Arrange (the only array is the one of the response)
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => Success());
        using var client = TestClient.Create(handler, pool: pool);
        var state = new SegmentState("""{"TableName":"Users"}""");

        // Act
        await client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None);

        // Assert
        await Assert.That(pool.RentedLengths.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_CallThatReadsItsResponse_ReturnsWhatTheCallMade()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"pk":{"S":"user-1"}}}"""));
        using var client = TestClient.Create(handler);
        var state = new SegmentState("{}");

        // Act
        var result = await client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None);

        // Assert
        await Assert.That(result).IsEqualTo("""read:{"Item":{"pk":{"S":"user-1"}}}""");
    }

    [Test]
    public async Task SendAsync_CallThatIsRetried_SendsTheSameBytesAgain()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(ServerError());
        handler.Enqueue(Success());
        using var client = CreateClient(handler);
        var state = new SegmentState("""{"Marker":"retried"}""");

        // Act
        await client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None);

        // Assert
        await Assert.That(handler.Requests.Select(request => request.BodyText).ToArray()).IsEquivalentTo(["""{"Marker":"retried"}""", """{"Marker":"retried"}""", """{"Marker":"retried"}"""]);
    }

    [Test]
    public async Task SendAsync_CallThatIsRetried_IsReleasedOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Success());
        using var client = CreateClient(handler);
        var state = new SegmentState("{}");

        // Act
        await client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None);

        // Assert
        await Assert.That(state.Releases).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_CallThatSucceeds_IsReleasedOnce()
    {
        // Arrange
        using var handler = new StubHandler(_ => Success());
        using var client = TestClient.Create(handler);
        var state = new SegmentState("{}");

        // Act
        await client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None);

        // Assert
        await Assert.That(state.Releases).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_CallThatFails_IsReleasedOnce()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "No table."));
        using var client = TestClient.Create(handler);
        var state = new SegmentState("{}");
        await TestClient.CaptureAsync<ResourceNotFoundException>(() => client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None).AsTask());

        // Act
        var releases = state.Releases;

        // Assert
        await Assert.That(releases).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_CallThatIsCancelled_IsReleasedOnce()
    {
        // Arrange
        using var handler = new StubHandler(_ => Success());
        using var client = TestClient.Create(handler);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var state = new SegmentState("{}");
        await TestClient.CaptureAsync<OperationCanceledException>(() => client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), cancellation.Token).AsTask());

        // Act
        var releases = state.Releases;

        // Assert
        await Assert.That(releases).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_CallWhoseBodyCannotBeProvided_ThrowsTheFailure()
    {
        // Arrange
        using var handler = new StubHandler(_ => Success());
        using var client = TestClient.Create(handler);
        var state = new SegmentState("{}") { FailToWrite = true };

        // Act
        var act = async () => await client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task SendAsync_CallWhoseBodyCannotBeProvided_IsReleasedOnce()
    {
        // Arrange
        using var handler = new StubHandler(_ => Success());
        using var client = TestClient.Create(handler);
        var state = new SegmentState("{}") { FailToWrite = true };
        await TestClient.CaptureAsync<InvalidOperationException>(() => client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None).AsTask());

        // Act
        var releases = state.Releases;

        // Assert
        await Assert.That(releases).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_CallThatCannotReadTheResponse_ThrowsUnrecognizedResponseAndIsReleasedOnce()
    {
        // Arrange (the call reads JSON, and the body is not)
        using var handler = new StubHandler(_ => StubResponse.Json("not json"));
        using var client = TestClient.Create(handler);
        var state = new SegmentState("{}") { ParseResponse = true };
        await TestClient.CaptureAsync<DynamoDbException>(() => client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None).AsTask());

        // Act
        var releases = state.Releases;

        // Assert
        await Assert.That(releases).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_CallThatCannotReadTheResponse_ExceptionNamesTheOperationOfTheCall()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("not json"));
        using var client = TestClient.Create(handler);
        var state = new SegmentState("{}") { ParseResponse = true };

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None).AsTask());

        // Assert
        await Assert.That(exception.OperationName).IsEqualTo("CustomOperation");
    }

    [Test]
    public async Task SendAsync_DisposedClient_ThrowsObjectDisposedExceptionAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler(_ => Success());
        var client = TestClient.Create(handler);
        client.Dispose();
        var state = new SegmentState("{}");

        // Act
        var act = async () => await client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<ObjectDisposedException>();
    }

    [Test]
    public async Task SendAsync_DisposedClient_ReleasesTheCall()
    {
        // Arrange
        using var handler = new StubHandler(_ => Success());
        var client = TestClient.Create(handler);
        client.Dispose();
        var state = new SegmentState("{}");
        try
        {
            await client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            // The expected failure, which another test looks at.
        }

        // Act
        var releases = state.Releases;

        // Assert
        await Assert.That(releases).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_Call_SendsTheTargetOfTheCall()
    {
        // Arrange
        using var handler = new StubHandler(_ => Success());
        using var client = TestClient.Create(handler);
        var state = new SegmentState("{}");

        // Act
        await client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None);

        // Assert
        await Assert.That(handler.Requests.Single().Headers["X-Amz-Target"]).IsEqualTo("DynamoDB_20120810.CustomOperation");
    }

    [Test]
    public async Task SendAsync_CallWithATable_ReportsTheNamesAndTagsOfTheCallInTheSpan()
    {
        // Arrange
        using var handler = new StubHandler(_ => Success());
        var state = new SegmentState("{}");

        // Act
        var activity = await ActivityCapture.RunAsync(handler, client => client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None).AsTask());

        // Assert
        await Assert.That(ActivityCapture.Tags(activity)).Contains("db.operation.name=CustomOperation").And.Contains("db.collection.name=Things").And.Contains("test.request=yes").And.Contains("test.response=yes");
    }

    [Test]
    public async Task SendAsync_CallWithATable_NamesTheSpanAfterTheCall()
    {
        // Arrange
        using var handler = new StubHandler(_ => Success());
        var state = new SegmentState("{}");

        // Act
        var activity = await ActivityCapture.RunAsync(handler, client => client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None).AsTask());

        // Assert
        await Assert.That(activity.OperationName).IsEqualTo("DynamoDB.CustomOperation");
    }

    [Test]
    public async Task SendAsync_CallWithATable_RecordsTheDurationUnderTheNamesOfTheCall()
    {
        // Arrange
        using var handler = new StubHandler(_ => Success());
        var host = TestClient.UniqueHost();
        using var capture = MeasurementCapture<double>.ForServer(host, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler, serviceUrl: new Uri($"https://{host}"));
        var state = new SegmentState("{}");

        // Act
        await client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None);

        // Assert
        var tags = capture.Single().Tags;
        await Assert.That((tags["db.operation.name"], tags["db.collection.name"])).IsEqualTo(("CustomOperation", "Things"));
    }

    [Test]
    public async Task SendAsync_ManyConcurrentCallsThatTakeAndGiveBackPooledStates_EveryCallSendsAndGetsItsOwnBytes()
    {
        // Arrange (a state goes back to the pool when its call is released; one that was released early would be written by the next call
        // while the retry of the first still sends it)
        const int Calls = 2000;
        var pool = new BoundedPool<SegmentState>(8);
        var mismatches = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var attempts = new System.Collections.Concurrent.ConcurrentDictionary<int, int>();
        using var handler = new StubHandler(async (request, _) =>
        {
            using var document = JsonDocument.Parse(request.Body);
            var index = document.RootElement.GetProperty("Index").GetInt32();
            var attempt = attempts.AddOrUpdate(index, 1, static (_, count) => count + 1);
            await Task.Yield();
            return index % 3 == 0 && attempt == 1 ? Throttled() : StubResponse.Json($"{{\"Index\":{index}}}");
        });
        using var client = TestClient.Create(handler, maxAttempts: 3, random: FixedRandom.Zero);

        // Act
        await Parallel.ForEachAsync(Enumerable.Range(0, Calls), new ParallelOptions { MaxDegreeOfParallelism = 16 }, async (index, _) =>
        {
            var state = pool.Rent() ?? new SegmentState();
            state.Reset($"{{\"Index\":{index},\"Pad\":\"{new string('p', index % 700)}\"}}");
            state.Pool = pool;
            var result = await client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None);
            if (result != $"read:{{\"Index\":{index}}}")
            {
                mismatches.Enqueue($"call {index} got {result}");
            }
        });

        // Assert
        await Assert.That(mismatches).IsEmpty();
    }

    [Test]
    public async Task SendAsync_CallWithItsOwnAttempts_OverridesTheClient()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        using var client = CreateClient(handler, maxAttempts: 3);
        var state = new SegmentState("{}") { MaxAttempts = 1 };

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(async () => await client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_CallOfAClientThatAlwaysFails_UsesTheAttemptsOfTheCall()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => Throttled());
        using var client = CreateClient(handler, time, maxAttempts: 1);
        var state = new SegmentState("{}") { MaxAttempts = 2 };

        // Act
        await AdvanceThroughDelaysAsync(FailAsync(client, state), time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(2);

        static async Task FailAsync(DynamoDbClient client, SegmentState state) =>
            await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(async () => await client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None));
    }

    [Test]
    public async Task SendAsync_ValidationFailure_LetsTheCallDescribeIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "No."));
        using var client = TestClient.Create(handler);
        var state = new SegmentState("{}");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None));

        // Assert
        await Assert.That(ReferenceEquals(state.Failure, failure)).IsTrue();
    }

    [Test]
    public async Task SendAsync_FailedCondition_LetsTheCallDescribeIt()
    {
        // Arrange
        // A call of its own is no write, so the error comes in the shape that fits any operation.
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ConditionalCheckFailedException", "The conditional request failed"));
        using var client = TestClient.Create(handler);
        var state = new SegmentState("{}");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None));

        // Assert
        await Assert.That(ReferenceEquals(state.Failure, failure)).IsTrue();
    }

    [Test]
    public async Task SendAsync_OtherFailureOfTheService_LetsTheCallDescribeIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "No."));
        using var client = TestClient.Create(handler);
        var state = new SegmentState("{}");

        // Act
        var failure = await TestClient.CaptureAsync<ResourceNotFoundException>(async () => await client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None));

        // Assert
        await Assert.That(ReferenceEquals(state.Failure, failure)).IsTrue();
    }

    [Test]
    public async Task SendAsync_FailureThatIsNotOfTheService_DescribesNothing()
    {
        // Arrange
        using var handler = new StubHandler(static _ => throw new HttpRequestException("The connection dropped."));
        using var client = TestClient.Create(handler);
        var state = new SegmentState("{}");

        // Act
        await TestClient.CaptureAsync<HttpRequestException>(async () => await client.Core.SendAsync<SegmentCall, string>(new SegmentCall(state), CancellationToken.None));

        // Assert
        await Assert.That(state.Failure).IsNull();
    }

    private readonly struct SegmentCall(SegmentState state) : IWireCall<string>
    {
        public string OperationName => "CustomOperation";

        public string ServiceName => "DynamoDB";

        public string SpanName => "DynamoDB.CustomOperation";

        public string Target => "DynamoDB_20120810.CustomOperation";

        public int MaxAttempts => state.MaxAttempts;

        public string? GetTableName() => "Things";

        // The body is not copied: the buffers of the call stay untouched.
        public ArraySegment<byte> WriteBody(CallBuffers buffers) => state.Body;

        public bool TryReadFailure(ReadOnlySpan<byte> utf8Json, out string response)
        {
            response = string.Empty;
            return false;
        }

        public string ReadResponse(ReadOnlySpan<byte> utf8Json)
        {
            if (state.ParseResponse)
            {
                _ = JsonDocument.Parse(utf8Json.ToArray());
            }

            return "read:" + Encoding.UTF8.GetString(utf8Json);
        }

        public void SetActivityTags(Activity activity) => activity.SetTag("test.request", "yes");

        public void SetResponseTags(Activity activity, string response) => activity.SetTag("test.response", "yes");

        public IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(string response) => null;

        public void DescribeFailure(DynamoDbException failure) => state.Failure = failure;

        public void Release() => state.Release();
    }

    // What a command of the table layer holds: a body in a segment, and the way back into a pool.
    private sealed class SegmentState
    {
        private byte[] _body;

        public SegmentState(string json = "{}")
        {
            _body = Encoding.UTF8.GetBytes(json);
        }

        public bool FailToWrite { get; init; }

        public bool ParseResponse { get; init; }

        public int MaxAttempts { get; init; }

        public DynamoDbException? Failure { get; set; }

        public BoundedPool<SegmentState>? Pool { get; set; }

        public int Releases { get; private set; }

        public ArraySegment<byte> Body => FailToWrite ? throw new InvalidOperationException("The command cannot be written.") : _body;

        public void Reset(string json) => _body = Encoding.UTF8.GetBytes(json);

        public void Release()
        {
            Releases++;
            Pool?.Return(this);
        }
    }
}
