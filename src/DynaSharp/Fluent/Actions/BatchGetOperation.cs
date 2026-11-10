using DynaSharp.Http;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// <c>BatchGetItem</c> as a command runs it: the keys of its table, and those of the gets with a slot, on any table. Each key is written once, to the body of the state, and a request is put
/// together from their bytes.
/// </summary>
/// <typeparam name="T">How the items of the table of the command are read.</typeparam>
internal readonly struct BatchGetOperation<T> : IBatchOperation<BatchGetResult<T>>
    where T : IDynamoDbDocument<T>
{
    /// <summary>The most keys one request may hold, of all tables. A response holds at most 16 MB, and DynamoDB gives back the keys that do not fit.</summary>
    public const int MaxKeysPerRequest = 100;

    public static string Name => BatchGetItemOperation.Name;

    public static bool EndsCommand => false;

    public static int FindEnd(ActionList actions) => Math.Min(actions.First + MaxKeysPerRequest, actions.Count);

    // {"Shop":{"Keys":[{"pk":{"S":"a"}},{"pk":{"S":"b"}}],"ConsistentRead":true,"ProjectionExpression":"#n0","ExpressionAttributeNames":{"#n0":"name"}},"Other":{"Keys":[...]}}.
    // The projection is that of the table of the command; a get with a slot reads the whole item, which its type needs.
    public static void WriteRequestItems(CommandState state, PooledByteBuffer output, int first, int end)
    {
        var actions = state.Actions;
        var body = state.Body.WrittenSpan;
        var separator = false;
        output.Append((byte)'{');
        for (var table = 0; table < actions.TableCount; table++)
        {
            var written = 0;
            for (var i = first; i < end; i++)
            {
                if (actions.TableIndexOf(i) != table)
                {
                    continue;
                }

                if (written++ > 0)
                {
                    output.Append((byte)',');
                }
                else
                {
                    if (separator)
                    {
                        output.Append((byte)',');
                    }

                    output.Append(actions.TableAt(table).RequestItemsStart);
                    output.Append("{\"Keys\":["u8);
                    separator = true;
                }

                output.Append(actions.Entry(i, body));
            }

            if (written > 0)
            {
                EndTable(state, output, actions.TableAt(table));
            }
        }

        output.Append((byte)'}');
    }

    public static ArraySegment<byte> WriteBody(CommandState state, CallBuffers buffers) =>
        BatchDriver.WriteBody<BatchGetOperation<T>, BatchGetResult<T>>(state, buffers.Request);

    // The carry is what this request sent, and it stays until the whole response is read: a response that fails halfway leaves the carry, the items and the slots as
    // they were, so that a batch that gives up reports the keys of this request as unprocessed, and not those of the requests before it.
    public static BatchResponse ReadResponse(ReadOnlySpan<byte> utf8Json, CommandState state)
    {
        var actions = state.Actions;
        var items = (List<T>)actions.Results!;
        var before = items.Count;
        var slots = actions.Slots;
        var responses = new Responses(items, state.Table, slots);
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
            slots?.Discard();
            throw;
        }

        slots?.Commit(utf8Json);
        actions.ReadFailed = false;
        actions.Carry.Truncate(0);
        actions.Carry.Append(utf8Json[unprocessed.Json]);
        return new BatchResponse(unprocessed.Count, envelope.ConsumedCapacity, null);
    }

    public static IReadOnlyList<ConsumedCapacity> GetConsumedCapacity(BatchResponse response) => response.ConsumedCapacity;

    public static BatchGetResult<T> CreateResult(CommandState state, BatchTotals totals) =>
        new((List<T>)state.Actions.Results!, totals.Requests, totals.ConsumedCapacity, state.Actions.Slots);

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

        actions.Slots?.MarkUnprocessed(unsentFrom, actions.Carry.WrittenSpan);
        var items = (List<T>)actions.Results!;
        var message = $"DynamoDB answered {totals.Processed} of {actions.Count} keys in {totals.Requests} requests and left the rest unprocessed.";
        return new BatchGetIncompleteException<T>(message, unprocessed, items, cause) { OperationName = Name, TableName = actions.OnlyTableName, Slots = actions.Slots };
    }

    private static Dictionary<string, KeysAndAttributes> ReadKeys(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new DynamoDbReader(utf8Json);
        return BatchJson.ReadKeysByTable(ref reader);
    }

    private static void EndTable(CommandState state, PooledByteBuffer output, TableModel table)
    {
        output.Append((byte)']');
        if (state.Read.ConsistentRead)
        {
            output.Append(",\"ConsistentRead\":true"u8);
        }

        if (table.Name == state.Table.Name)
        {
            state.Expressions.WriteMembers(output);
        }

        output.Append((byte)'}');
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

    // "Responses":{"Shop":[{"pk":{"S":"a"}},{"pk":{"S":"b"}}],"Other":[...]}. An item of a table that a slot reads goes to the slot whose key it has, and any other item
    // of the table of the command is read as T. A batch asks for nothing else, so an item of another table that no slot asked for is left alone.
    private readonly struct Responses(List<T> items, TableModel table, BatchSlots? slots) : IResponseMember
    {
        public void Read(ref DynamoDbReader reader)
        {
            reader.ReadStartItem();
            while (reader.TryReadAttributeName())
            {
                var name = reader.GetAttributeName();
                var own = name == table.Name;
                var slotTable = slots?.TableNamed(name);
                OperationJson.ReadStartArray(ref reader);
                var first = reader;
                var position = 0;
                try
                {
                    for (; reader.TryReadStartItem(); position++)
                    {
                        if (slotTable is not null && (slots!.TryMatch(slotTable, reader, Extent(reader)) || !own))
                        {
                            reader.Json.Skip();
                            continue;
                        }

                        items.Add(T.ReadAttributes(ref reader));
                    }
                }
                catch (DynamoDbMappingException failure)
                {
                    failure.Key = FailedItem.KeyOf(first, position, table);
                    throw;
                }
            }
        }

        // Where an item stands in the response, found on a copy of the reader, which stays on its opening brace.
        private static Range Extent(DynamoDbReader item)
        {
            var start = (int)item.Json.TokenStartIndex;
            item.Json.Skip();
            return start..(int)item.Json.BytesConsumed;
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
