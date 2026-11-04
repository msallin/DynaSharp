using System.Text.Json;
using DynaSharp.Streams;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests;

/// <summary>
/// Answers the requests of a stream by what they ask for, not by their order: <c>list:</c> and <c>list:&lt;start&gt;</c> for the listing,
/// <c>children:&lt;shard&gt;</c> and <c>children:&lt;shard&gt;:&lt;start&gt;</c> for the children of a shard, <c>iterator:&lt;shard&gt;</c> and <c>records:&lt;iterator&gt;</c>.
/// Several answers to one key are given in turn, the last one again. Children that no answer names are none, of a disabled stream.
/// </summary>
internal sealed class StreamScript
{
    /// <summary>The starting sequence number <see cref="StubResponse.Shard"/> gives every shard.</summary>
    public const string ShardStart = "100000000000000000000";

    private static readonly Item Key = new() { ["pk"] = "order-1" };

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Queue<Func<CancellationToken, Task<StubResponse>>>> _answers = [];

    public StreamScript()
    {
        Handler = new StubHandler(AnswerAsync);
    }

    public StubHandler Handler { get; }

    public static Shard Closed(string shardId, string? parentShardId = null) => StubResponse.Shard(shardId, parentShardId, endingSequenceNumber: "999");

    public static Shard Open(string shardId, string? parentShardId = null) => StubResponse.Shard(shardId, parentShardId);

    public static StubResponse Description(StreamStatus status, string? lastEvaluatedShardId, params Shard[] shards) => StubResponse.StreamDescription(new StreamDescription
    {
        StreamArn = TestClient.StreamArn,
        StreamStatus = status,
        Shards = shards,
        LastEvaluatedShardId = lastEvaluatedShardId,
    });

    public static ChangeRecord Insert(string sequenceNumber) => StubResponse.Change(OperationType.Insert, Key, newImage: Key, sequenceNumber: sequenceNumber);

    public static List<string> SequenceNumbers(IEnumerable<ShardChangeRecord> records) => [.. records.Select(static change => change.Record.DynamoDb!.SequenceNumber!)];

    // The shards and positions of the GetShardIterator calls, in the order sent, with the sequence number of a position that has one.
    public static IEnumerable<string> Positions(StubHandler handler) => handler.Requests
        .Where(static sent => sent.Operation == "GetShardIterator")
        .Select(static sent => sent.BodyJson.GetProperty("ShardId").GetString() + ":" + sent.BodyJson.GetProperty("ShardIteratorType").GetString()
            + (sent.BodyJson.TryGetProperty("SequenceNumber", out var number) ? ":" + number.GetString() : string.Empty));

    public void Answer(string key, params StubResponse[] responses)
    {
        foreach (var response in responses)
        {
            Answer(key, _ => Task.FromResult(response));
        }
    }

    public void Answer(string key, Func<CancellationToken, Task<StubResponse>> answer)
    {
        lock (_gate)
        {
            if (!_answers.TryGetValue(key, out var queue))
            {
                _answers[key] = queue = new();
            }

            queue.Enqueue(answer);
        }
    }

    // A closed shard read to its end in one page.
    public void ClosedShard(string shardId, params string[] sequenceNumbers)
    {
        Answer("iterator:" + shardId, StubResponse.ShardIterator(shardId + "/0"));
        Answer("records:" + shardId + "/0", StubResponse.Records(null, [.. sequenceNumbers.Select(Insert)]));
    }

    private static string DescribeKey(JsonElement body)
    {
        var start = body.TryGetProperty("ExclusiveStartShardId", out var exclusive) ? exclusive.GetString() : null;
        if (body.TryGetProperty("ShardFilter", out var filter))
        {
            var children = "children:" + filter.GetProperty("ShardId").GetString();
            return start is null ? children : children + ":" + start;
        }

        return "list:" + start;
    }

    private Task<StubResponse> AnswerAsync(RecordedRequest request, CancellationToken cancellationToken)
    {
        var body = request.BodyJson;
        var key = request.Operation switch
        {
            "DescribeStream" => DescribeKey(body),
            "GetShardIterator" => "iterator:" + body.GetProperty("ShardId").GetString(),
            _ => "records:" + body.GetProperty("ShardIterator").GetString(),
        };
        Func<CancellationToken, Task<StubResponse>>? answer = null;
        lock (_gate)
        {
            if (_answers.TryGetValue(key, out var queue))
            {
                answer = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            }
        }

        if (answer is not null)
        {
            return answer(cancellationToken);
        }

        return key.StartsWith("children:", StringComparison.Ordinal)
            ? Task.FromResult(Description(StreamStatus.Disabled, null))
            : throw new InvalidOperationException($"The script has no answer for {key}.");
    }
}
