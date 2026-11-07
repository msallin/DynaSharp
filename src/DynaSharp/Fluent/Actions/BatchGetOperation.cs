using DynaSharp.Http;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary><c>BatchGetItem</c> as a command runs it, on one table. Each key is written once, to the body of the state, and a request is put together from their bytes.</summary>
/// <typeparam name="T">How the items are read.</typeparam>
internal readonly struct BatchGetOperation<T> : IBatchOperation<BatchGetResult<T>>
    where T : IDynamoDbDocument<T>
{
    /// <summary>The most keys one request may hold. A response holds at most 16 MB, and DynamoDB gives back the keys that do not fit.</summary>
    public const int MaxKeysPerRequest = 100;

    public static string Name => BatchGetItemOperation.Name;

    public static bool EndsCommand => false;

    public static int FindEnd(ActionList actions) => Math.Min(actions.First + MaxKeysPerRequest, actions.Count);

    // {"Shop":{"Keys":[{"pk":{"S":"a"}},{"pk":{"S":"b"}}],"ConsistentRead":true,"ProjectionExpression":"#n0","ExpressionAttributeNames":{"#n0":"name"}}}
    public static void WriteRequestItems(CommandState state, PooledByteBuffer output, int first, int end)
    {
        var actions = state.Actions;
        var body = state.Body.WrittenSpan;
        output.Append((byte)'{');
        if (first < end)
        {
            output.Append(state.Table.RequestItemsStart);
            output.Append("{\"Keys\":["u8);
            output.Append(actions.Entry(first, body));
            for (var i = first + 1; i < end; i++)
            {
                output.Append((byte)',');
                output.Append(actions.Entry(i, body));
            }

            output.Append((byte)']');
            if (state.Read.ConsistentRead)
            {
                output.Append(",\"ConsistentRead\":true"u8);
            }

            state.Expressions.WriteMembers(output);
            output.Append((byte)'}');
        }

        output.Append((byte)'}');
    }

    public static ArraySegment<byte> WriteBody(CommandState state, CallBuffers buffers) =>
        BatchDriver.WriteBody<BatchGetOperation<T>, BatchGetResult<T>>(state, buffers.Request);

    // The carry is what this request sent, and it stays until the whole response is read: a response that fails halfway leaves the carry and the items as
    // they were, so that a batch that gives up reports the keys of this request as unprocessed, and not those of the requests before it.
    public static BatchResponse ReadResponse(ReadOnlySpan<byte> utf8Json, CommandState state)
    {
        var actions = state.Actions;
        var items = (List<T>)actions.Results!;
        var before = items.Count;
        var responses = new Responses(items, state.Table);
        var unprocessed = default(Unprocessed);
        BatchGetEnvelope envelope;
        actions.ReadFailed = true;
        try
        {
            var reader = new DynamoDbReader(utf8Json);
            envelope = BatchGetEnvelope.Read(ref reader, ref responses, ref unprocessed);
        }
        catch
        {
            items.RemoveRange(before, items.Count - before);
            throw;
        }

        actions.ReadFailed = false;
        actions.Carry.Truncate(0);
        actions.Carry.Append(utf8Json[unprocessed.Json]);
        return new BatchResponse(unprocessed.Count, envelope.ConsumedCapacity, null);
    }

    public static IReadOnlyList<ConsumedCapacity> GetConsumedCapacity(BatchResponse response) => response.ConsumedCapacity;

    public static BatchGetResult<T> CreateResult(CommandState state, BatchTotals totals) =>
        new((List<T>)state.Actions.Results!, totals.Requests, totals.ConsumedCapacity);

    public static Exception CreateIncomplete(CommandState state, BatchTotals totals, int unsentFrom, Exception? cause)
    {
        var actions = state.Actions;
        using var unsent = new PooledByteBuffer();
        WriteRequestItems(state, unsent, unsentFrom, actions.Count);
        var unprocessed = ReadKeys(unsent.WrittenSpan);
        if (actions.Carry.WrittenCount > 0)
        {
            foreach (var (table, keys) in ReadKeys(actions.Carry.WrittenSpan))
            {
                unprocessed[table] = unprocessed.TryGetValue(table, out var rest) ? rest with { Keys = [.. keys.Keys, .. rest.Keys] } : keys;
            }
        }

        var items = (List<T>)actions.Results!;
        var message = $"DynamoDB answered {totals.Processed} of {actions.Count} keys in {totals.Requests} requests and left the rest unprocessed.";
        return new BatchGetIncompleteException<T>(message, unprocessed, items, cause) { OperationName = Name, TableName = actions.OnlyTableName };
    }

    private static Dictionary<string, KeysAndAttributes> ReadKeys(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new DynamoDbReader(utf8Json);
        return BatchJson.ReadKeysByTable(ref reader);
    }

    // "UnprocessedKeys":{"Shop":{"Keys":[{...},{...}],"ConsistentRead":true}}. The object is the next RequestItems as it stands, with the projection, so it is
    // copied, and walked only to count the keys. The range is empty if there are none.
    private static int FindUnprocessed(ref DynamoDbReader reader, out Range json)
    {
        reader.ReadStartItem();
        var start = (int)reader.Json.TokenStartIndex;
        var count = 0;
        while (reader.TryReadAttributeName())
        {
            reader.ReadStartItem();
            while (reader.TryReadAttributeName())
            {
                if (reader.AttributeNameEquals("Keys"u8))
                {
                    OperationJson.ReadStartArray(ref reader);
                    while (reader.TryReadStartItem())
                    {
                        reader.Json.Skip();
                        count++;
                    }
                }
                else
                {
                    OperationJson.SkipValue(ref reader);
                }
            }
        }

        json = count > 0 ? start..(int)reader.Json.BytesConsumed : default;
        return count;
    }

    // "Responses":{"Shop":[{"pk":{"S":"a"}},{"pk":{"S":"b"}}]}, on the one table of the command.
    private readonly struct Responses(List<T> items, TableModel table) : IResponseMember
    {
        public void Read(ref DynamoDbReader reader)
        {
            reader.ReadStartItem();
            while (reader.TryReadAttributeName())
            {
                OperationJson.ReadStartArray(ref reader);
                var first = reader;
                var before = items.Count;
                try
                {
                    while (reader.TryReadStartItem())
                    {
                        items.Add(T.ReadAttributes(ref reader));
                    }
                }
                catch (DynamoDbMappingException failure)
                {
                    failure.Key = FailedItem.KeyOf(first, items.Count - before, table);
                    throw;
                }
            }
        }
    }

    // What DynamoDB left: how many, and where it stands in the response.
    private struct Unprocessed : IResponseMember
    {
        public int Count { get; private set; }

        public Range Json { get; private set; }

        public void Read(ref DynamoDbReader reader)
        {
            Count = FindUnprocessed(ref reader, out var json);
            Json = json;
        }
    }
}
