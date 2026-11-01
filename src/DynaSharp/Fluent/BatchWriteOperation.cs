using DynaSharp.Http;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// <c>BatchWriteItem</c> as a command runs it. Each write is written once, to the body of the state, and a request is put together from the
/// bytes of its writes, grouped by table.
/// </summary>
internal readonly struct BatchWriteOperation : IBatchOperation<BatchWriteResult>
{
    /// <summary>The most writes one request may hold.</summary>
    public const int MaxWritesPerRequest = 25;

    /// <summary>
    /// The most bytes the writes of a request may take: the 16 MB DynamoDB accepts, without what the request needs around them. 25 table names of 1 KB,
    /// as an ARN can be, and the options fit into what is left out.
    /// </summary>
    public const int MaxWriteBytesPerRequest = (16 * 1024 * 1024) - (64 * 1024);

    public static string Name => BatchWriteItemOperation.Name;

    public static bool EndsCommand => false;

    /// <summary>Writes a put.</summary>
    /// <typeparam name="T">The type of the item.</typeparam>
    /// <param name="state">The state of the command.</param>
    /// <param name="table">The table.</param>
    /// <param name="item">The item.</param>
    public static void AddPut<T>(CommandState state, TableModel table, T item)
        where T : IDynamoDbDocument<T>
    {
        // {"PutRequest":{"Item":{"pk":{"S":"a"}}}}
        WriteRules.NoteBatchWrite(state, table);
        var body = state.Body;
        state.Actions.Add(table, body.WrittenCount);
        body.Append("{\"PutRequest\":{\"Item\":"u8);
        var json = state.BodyJson;
        json.WriteStartObject();
        T.WriteAttributes(new DynamoDbWriter(json), item);
        json.WriteEndObject();
        CommandState.EndWrite(json);
        body.Append("}}"u8);
    }

    /// <summary>Writes a delete of a key of attribute values.</summary>
    /// <param name="state">The state of the command.</param>
    /// <param name="table">The table.</param>
    /// <param name="partitionKey">The partition key.</param>
    /// <param name="sortKey">The sort key, or the default value if there is none.</param>
    public static void AddDelete(CommandState state, TableModel table, AttributeValue partitionKey, AttributeValue sortKey)
    {
        // {"DeleteRequest":{"Key":{"pk":{"S":"a"}}}}
        table.CheckKey(partitionKey, sortKey);
        WriteRules.NoteBatchWrite(state, table);
        var body = state.Body;
        state.Actions.Add(table, body.WrittenCount);
        body.Append("{\"DeleteRequest\":{\"Key\":"u8);
        state.WriteKeyObject(table, partitionKey, sortKey);
        body.Append("}}"u8);
    }

    /// <summary>Writes a delete of a key that writes itself.</summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <param name="state">The state of the command.</param>
    /// <param name="table">The table.</param>
    /// <param name="key">The key.</param>
    public static void AddDelete<TKey>(CommandState state, TableModel table, TKey key)
        where TKey : IKey
    {
        WriteRules.NoteBatchWrite(state, table);
        var body = state.Body;
        state.Actions.Add(table, body.WrittenCount);
        body.Append("{\"DeleteRequest\":{\"Key\":"u8);
        var json = state.BodyJson;
        json.WriteStartObject();
        key.WriteKey(new DynamoDbWriter(json));
        state.EndKey();
        body.Append("}}"u8);
    }

    public static int FindEnd(ActionList actions)
    {
        var first = actions.First;
        var last = Math.Min(first + MaxWritesPerRequest, actions.Count);

        // The first write always goes, also one above the limit: DynamoDB is the one to say that it cannot take it. The commas count too.
        var end = Math.Min(first + 1, last);
        while (end < last && actions.LengthOf(first, end + 1) + (end - first) <= MaxWriteBytesPerRequest)
        {
            end++;
        }

        return end;
    }

    // {"Shop":[{"PutRequest":{...}},{"DeleteRequest":{...}}],"Audit":[{"PutRequest":{...}}]}
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
                    output.Append((byte)'[');
                    separator = true;
                }

                output.Append(actions.Entry(i, body));
            }

            if (written > 0)
            {
                output.Append((byte)']');
            }
        }

        output.Append((byte)'}');
    }

    public static ArraySegment<byte> WriteBody(CommandState state, CallBuffers buffers) =>
        BatchDriver.WriteBody<BatchWriteOperation, BatchWriteResult>(state, buffers.Request);

    public static BatchResponse ReadResponse(ReadOnlySpan<byte> utf8Json, CommandState state)
    {
        var carry = state.Actions.Carry;
        carry.Truncate(0);
        var unprocessed = 0;
        IReadOnlyList<ConsumedCapacity> capacity = [];
        Dictionary<string, IReadOnlyList<ItemCollectionMetrics>>? metrics = null;
        var reader = new DynamoDbReader(utf8Json);
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("UnprocessedItems"u8))
            {
                unprocessed = CopyUnprocessed(ref reader, utf8Json, carry);
            }
            else if (reader.AttributeNameEquals("ConsumedCapacity"u8))
            {
                capacity = OperationJson.ReadConsumedCapacityList(ref reader);
            }
            else if (reader.AttributeNameEquals("ItemCollectionMetrics"u8))
            {
                metrics = OperationJson.ReadItemCollectionMetricsByTable(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new BatchResponse(unprocessed, capacity, metrics);
    }

    public static IReadOnlyList<ConsumedCapacity> GetConsumedCapacity(BatchResponse response) => response.ConsumedCapacity;

    public static BatchWriteResult CreateResult(CommandState state, BatchTotals totals) =>
        new(totals.Processed, totals.Requests, totals.ConsumedCapacity, totals.ItemCollectionMetrics);

    public static Exception CreateIncomplete(CommandState state, BatchTotals totals, int unsentFrom, Exception? cause)
    {
        var actions = state.Actions;
        using var unsent = new PooledByteBuffer();
        WriteRequestItems(state, unsent, unsentFrom, actions.Count);
        var unprocessed = ReadWrites(unsent.WrittenSpan);
        if (actions.Carry.WrittenCount > 0)
        {
            foreach (var (table, writes) in ReadWrites(actions.Carry.WrittenSpan))
            {
                unprocessed[table] = unprocessed.TryGetValue(table, out var rest) ? [.. writes, .. rest] : writes;
            }
        }

        var message = $"DynamoDB took {totals.Processed} of {actions.Count} writes in {totals.Requests} requests and left the rest unprocessed.";
        return new BatchIncompleteException(message, unprocessed, totals.Processed, cause) { OperationName = Name, TableName = actions.OnlyTableName };
    }

    private static Dictionary<string, IReadOnlyList<WriteRequest>> ReadWrites(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new DynamoDbReader(utf8Json);
        return BatchJson.ReadWritesByTable(ref reader);
    }

    // "UnprocessedItems":{"Shop":[{"PutRequest":{...}},{"DeleteRequest":{...}}]}. The object is the next RequestItems as it stands, so it is copied, and walked
    // only to count the writes.
    private static int CopyUnprocessed(ref DynamoDbReader reader, ReadOnlySpan<byte> utf8Json, PooledByteBuffer carry)
    {
        reader.ReadStartItem();
        var start = (int)reader.Json.TokenStartIndex;
        var count = 0;
        while (reader.TryReadAttributeName())
        {
            OperationJson.ReadStartArray(ref reader);
            while (reader.TryReadStartItem())
            {
                reader.Json.Skip();
                count++;
            }
        }

        if (count > 0)
        {
            carry.Append(utf8Json[start..(int)reader.Json.BytesConsumed]);
        }

        return count;
    }
}
