namespace DynaSharp.Testing;

/// <content>Planned failures: the next requests of an operation get an answer of the test instead of the fake's, through every handler of the fake.</content>
public sealed partial class FakeDynamoDb
{
    // Guarded by the gate of the database, as everything else of the fake is.
    private readonly List<PlannedFailure> _failures = [];

    /// <summary>
    /// Makes the next requests of an operation fail: <c>fake.FailNext("GetRecords", StubResponse.DynamoDbError("ThrottlingException", "Rate exceeded"))</c>. The request gets
    /// <paramref name="failure"/> and the fake never sees it, as DynamoDB never sees a request it throttles, so a failed write writes nothing; the requests after it are
    /// answered by the fake as before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It holds for every handler of this fake, those made before the call too, so for the clients of <see cref="CreateClient"/> and <see cref="CreateStreamsClient"/> alike, and for
    /// <see cref="Answer(RecordedRequest)"/> and <see cref="Answer(string, string)"/>. A response queued with <see cref="StubHandler.Enqueue"/> on a handler still comes first: the handler
    /// asks the fake only when its queue is empty. Requests of several threads, such as the shard readers of <c>ReadStreamAsync</c>, take the failures one at a time under the lock of the fake,
    /// so each failure is taken once.
    /// </para>
    /// <para>
    /// Failures are taken in the order planned: a request takes the first one of its operation that <paramref name="when"/> accepts. A client repeats a throttling error or a server error
    /// as it does on DynamoDB, so a failure planned for more requests than the client has attempts surfaces, and <c>ReadStreamAsync</c> reads a shard on after it. <see cref="Reset"/>
    /// drops the failures not taken, and <see cref="ThrowIfFailuresLeft"/> tells a test that the code sent fewer requests than it planned for. The direct API, such as
    /// <see cref="Put(string, Item)"/>, is no request and takes none.
    /// </para>
    /// </remarks>
    /// <param name="operation">The operation, <c>GetRecords</c>, or its whole target, <c>DynamoDBStreams_20120810.GetRecords</c>.</param>
    /// <param name="failure">The answer, such as <see cref="StubResponse.DynamoDbError"/>. A response of another operation fails the request, as a queued one does on a <see cref="StubHandler"/>.</param>
    /// <param name="times">How many requests get it, one by default.</param>
    /// <param name="when">
    /// Which requests of the operation count, such as those of one table, <c>request => request.BodyJson.GetProperty("TableName").GetString() == "Orders"</c>, or <see langword="null"/>
    /// for all. It runs under the lock of the fake, so it must not call the fake.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> or <paramref name="failure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="operation"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="times"/> is less than one.</exception>
    public void FailNext(string operation, StubResponse failure, int times = 1, Func<RecordedRequest, bool>? when = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(operation);
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentOutOfRangeException.ThrowIfLessThan(times, 1);
        var name = RecordedRequest.ParseOperation(operation)!;
        lock (_db.Gate)
        {
            _failures.Add(new PlannedFailure(name, failure, when) { Left = times });
        }
    }

    /// <summary>Throws when a failure planned with <see cref="FailNext"/> was not taken, naming the operation and how many are left.</summary>
    /// <exception cref="InvalidOperationException">A failure is left: the code under test sent fewer requests than the test planned failures for.</exception>
    public void ThrowIfFailuresLeft()
    {
        string? message = null;
        lock (_db.Gate)
        {
            if (_failures.Count > 0)
            {
                var left = new List<string>(_failures.Count);
                foreach (var planned in _failures)
                {
                    left.Add($"{planned.Left} of {planned.Operation}");
                }

                message = $"FakeDynamoDb has planned failures that no request took: {string.Join(", ", left)}. The code under test sent fewer requests than the test planned failures for.";
            }
        }

        if (message is not null)
        {
            throw new InvalidOperationException(message);
        }
    }

    // The first planned failure that fits the request, taken, or null for the fake to answer.
    private StubResponse? TakeFailure(RecordedRequest request)
    {
        lock (_db.Gate)
        {
            for (var i = 0; i < _failures.Count; i++)
            {
                var planned = _failures[i];
                if (string.Equals(planned.Operation, request.Operation, StringComparison.Ordinal) && (planned.When is null || planned.When(request)))
                {
                    if (--planned.Left == 0)
                    {
                        _failures.RemoveAt(i);
                    }

                    return planned.Response;
                }
            }
        }

        return null;
    }

    private sealed class PlannedFailure(string operation, StubResponse response, Func<RecordedRequest, bool>? when)
    {
        public string Operation { get; } = operation;

        public StubResponse Response { get; } = response;

        public Func<RecordedRequest, bool>? When { get; } = when;

        public int Left { get; set; }
    }
}
