using System.Text.Json;
using DynaSharp.Operations;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// A <c>BatchWriteItem</c>: up to 25 puts and deletes over several tables, each as <c>PutItem</c> and <c>DeleteItem</c> without a condition do it. The fake writes all of them or,
/// when one is rejected, none, so <c>UnprocessedItems</c> is always empty; a test of what a client does with unprocessed items queues that answer on the handler.
/// </summary>
internal static class BatchWriteItem
{
    /// <summary>The operation.</summary>
    public const string Name = "BatchWriteItem";

    private const int MaxWrites = 25;

    /// <summary>Answers the operation.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void Handle(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        RequestJson.Members(request, Name, "RequestItems", "ReturnConsumedCapacity", "ReturnItemCollectionMetrics");
        var tables = RequestJson.ReadObject(RequestJson.Required(request, "RequestItems"), "RequestItems");
        Batches.ThrowIfLength(tables.Count, "requestItems", MaxWrites);
        var capacity = SingleWrite.Enum(request, "ReturnConsumedCapacity", WireNames.ReturnConsumedCapacities);
        var metrics = SingleWrite.Enum(request, "ReturnItemCollectionMetrics", WireNames.ReturnItemCollectionMetricsValues);
        var writes = Read(db, tables);

        var journal = new WriteJournal();
        var outcomes = new List<(FakeTable Table, WriteOutcome Outcome)>(writes.Count);
        try
        {
            foreach (var (table, key, item) in writes)
            {
                var outcome = item is null ? new WriteOutcome(key, table.Remove(key), null) : new WriteOutcome(key, table.Put(item), null);
                journal.Record(table, key, outcome.OldImage);
                outcomes.Add((table, outcome with { NewImage = table.Get(key) }));
            }
        }
        catch (FakeError)
        {
            journal.Undo();
            throw;
        }

        response.WriteStartObject("UnprocessedItems"u8);
        response.WriteEndObject();
        if (capacity != ReturnConsumedCapacity.None)
        {
            WriteCapacity.WriteAll(response, capacity, outcomes.Select(static write => CapacityShare.OfWrite(write.Table, write.Outcome, factor: 1)));
        }

        WriteReturns.WriteAll(response, metrics, outcomes);
    }

    // {"Forum":[{"PutRequest":{"Item":{...}}},{"DeleteRequest":{"Key":{...}}}]}: one write per element, a delete as an item of null.
    private static List<(FakeTable Table, ItemKey Key, Item? Item)> Read(FakeDatabase db, IReadOnlyList<JsonProperty> tables)
    {
        var writes = new List<(FakeTable, ItemKey, Item?)>();
        var seen = new HashSet<(string, ItemKey)>();
        foreach (var entry in tables)
        {
            var elements = RequestJson.ReadArray(entry.Value, "RequestItems");
            Batches.ThrowIfLength(elements.Count, "requestItems", MaxWrites);
            if (writes.Count + elements.Count > MaxWrites)
            {
                throw FakeError.Validation($"Too many items requested for the {Name} call");
            }

            var table = db.GetTable(entry.Name);
            foreach (var element in elements)
            {
                var (action, body) = Batches.OneOf(element, "RequestItems of " + Name, "Supplied WriteRequest must have exactly one of PutRequest or DeleteRequest", "PutRequest", "DeleteRequest");
                (ItemKey Key, Item? Item) write;
                if (action == "PutRequest")
                {
                    RequestJson.Members(body, "PutRequest of " + Name, "Item");
                    var item = RequestJson.ReadItem(RequestJson.Required(body, "Item"), "Item");
                    write = (table.KeySchema.KeyOf(item), item);
                }
                else
                {
                    RequestJson.Members(body, "DeleteRequest of " + Name, "Key");
                    write = (table.KeySchema.ReadKey(RequestJson.ReadItem(RequestJson.Required(body, "Key"), "Key")), null);
                }

                Batches.ThrowIfSeen(seen, table, write.Key, "Provided list of item keys contains duplicates");
                writes.Add((table, write.Key, write.Item));
            }
        }

        return writes;
    }
}
